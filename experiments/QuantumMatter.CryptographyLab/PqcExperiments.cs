using System.Diagnostics;
using System.Security.Cryptography;

namespace QuantumMatter.CryptographyLab;

internal static class PqcExperiments
{
    private static readonly byte[] Message = Enumerable.Range(0, 1024).Select(index => (byte)index).ToArray();

    internal static LabReport Run(int repetitions, int warmup, bool includeSlhDsa = false,
        CancellationToken cancellationToken = default)
    {
        if (repetitions is < 1 or > 1000) { throw new ArgumentOutOfRangeException(nameof(repetitions)); }
        if (warmup is < 0 or > 100) { throw new ArgumentOutOfRangeException(nameof(warmup)); }
        cancellationToken.ThrowIfCancellationRequested();
        var rows = new List<PrimitiveResult>();
        foreach (MLKemAlgorithm algorithm in new[] { MLKemAlgorithm.MLKem512, MLKemAlgorithm.MLKem768, MLKemAlgorithm.MLKem1024 })
        {
            cancellationToken.ThrowIfCancellationRequested();
            rows.Add(MLKem.IsSupported ? RunKem(algorithm, repetitions, warmup, cancellationToken) : Unsupported(algorithm.Name));
        }

        foreach (MLDsaAlgorithm algorithm in new[] { MLDsaAlgorithm.MLDsa44, MLDsaAlgorithm.MLDsa65, MLDsaAlgorithm.MLDsa87 })
        {
            cancellationToken.ThrowIfCancellationRequested();
            rows.Add(MLDsa.IsSupported ? RunSignature(algorithm, repetitions, warmup, cancellationToken) : Unsupported(algorithm.Name));
        }

        if (includeSlhDsa) { rows.Add(SlhDsaExperiment.Run(repetitions, warmup, cancellationToken)); }
        cancellationToken.ThrowIfCancellationRequested();
        return new LabReport(TimeProvider.System.GetUtcNow(), "net10.0",
            ThisConfiguration, Message.Length, repetitions, warmup,
            "Anonymous single-thread provider measurements; host hardware and installation details are not collected. Allocations cover managed current-thread bytes only. Raw times exclude warmup; no timing-based security inference. ML-KEM/ML-DSA also check selected pinned NIST ACVP vectors; this is not full ACVP certification or cross-provider interoperability validation.",
            rows.AsReadOnly());
    }

#if DEBUG
    private const string ThisConfiguration = "Debug";
#else
    private const string ThisConfiguration = "Release";
#endif

    private static PrimitiveResult RunKem(MLKemAlgorithm algorithm, int repetitions, int warmup, CancellationToken token)
    {
        NistKnownAnswers.ValidateKem(algorithm, token);
        using MLKem privateKey = MLKem.GenerateKey(algorithm);
        byte[] exportedPublic = privateKey.ExportEncapsulationKey();
        using MLKem publicKey = MLKem.ImportEncapsulationKey(algorithm, exportedPublic);
        byte[] exportedPrivate = privateKey.ExportDecapsulationKey();
        try
        {
            using MLKem imported = MLKem.ImportDecapsulationKey(algorithm, exportedPrivate);
            publicKey.Encapsulate(out byte[] ciphertext, out byte[] secret);
            byte[] recovered = new byte[algorithm.SharedSecretSizeInBytes];
            byte[] changedCiphertext = (byte[])ciphertext.Clone();
            changedCiphertext[0] ^= 1;
            byte[] rejectedSecret = new byte[algorithm.SharedSecretSizeInBytes];
            try
            {
                imported.Decapsulate(ciphertext, recovered);
                privateKey.Decapsulate(changedCiphertext, rejectedSecret);
                Require(CryptographicOperations.FixedTimeEquals(secret, recovered), "KEM imported-key round trip failed.");
                Require(!CryptographicOperations.FixedTimeEquals(secret, rejectedSecret), "Changed ciphertext retained its secret.");
                CheckAuthenticatedEncryption(secret, recovered, rejectedSecret);
                bool lengthRejected = false;
                try { privateKey.Decapsulate(ciphertext.AsSpan(1), recovered); }
                catch (ArgumentException) { lengthRejected = true; }
                Require(lengthRejected, "Malformed KEM ciphertext length was accepted.");

                var measurements = new[]
                {
                    Measure("key-generation", () => { using var key = MLKem.GenerateKey(algorithm); }, repetitions, warmup, token),
                    Measure("encapsulation", () => publicKey.Encapsulate(ciphertext, secret), repetitions, warmup, token),
                    Measure("decapsulation", () => privateKey.Decapsulate(ciphertext, recovered), repetitions, warmup, token)
                };
                Require(CryptographicOperations.FixedTimeEquals(secret, recovered), "Measured KEM operations disagreed.");
                return new PrimitiveResult(algorithm.Name, "passed", privateKey.GetType().FullName,
                    exportedPublic.Length, exportedPrivate.Length, ciphertext.Length,
                    "Two pinned NIST ACVP decapsulation cases passed (valid/implicit-rejection). Raw keys/ciphertexts; imported-key round trip, malformed length and AEAD checks passed; not an authenticated peer protocol.",
                    Array.AsReadOnly(measurements));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(secret);
                CryptographicOperations.ZeroMemory(recovered);
                CryptographicOperations.ZeroMemory(rejectedSecret);
            }
        }
        finally { CryptographicOperations.ZeroMemory(exportedPrivate); }
    }

    private static PrimitiveResult RunSignature(MLDsaAlgorithm algorithm, int repetitions, int warmup, CancellationToken token)
    {
        NistKnownAnswers.ValidateSignature(algorithm, token);
        using MLDsa privateKey = MLDsa.GenerateKey(algorithm);
        byte[] publicBytes = privateKey.ExportMLDsaPublicKey();
        using MLDsa publicKey = MLDsa.ImportMLDsaPublicKey(algorithm, publicBytes);
        byte[] privateBytes = privateKey.ExportMLDsaPrivateKey();
        try
        {
            using MLDsa imported = MLDsa.ImportMLDsaPrivateKey(algorithm, privateBytes);
            byte[] signature = imported.SignData(Message, []);
            Require(publicKey.VerifyData(Message.AsSpan(), signature, []), "Imported signature round trip failed.");
            byte[] changedMessage = (byte[])Message.Clone();
            changedMessage[0] ^= 1;
            Require(!publicKey.VerifyData(changedMessage.AsSpan(), signature, []), "Changed message verified.");
            signature[0] ^= 1;
            Require(!publicKey.VerifyData(Message.AsSpan(), signature, []), "Changed signature verified.");
            privateKey.SignData(Message, signature, []);
            var measurements = new[]
            {
                Measure("key-generation", () => { using var key = MLDsa.GenerateKey(algorithm); }, repetitions, warmup, token),
                Measure("sign", () => privateKey.SignData(Message, signature, []), repetitions, warmup, token),
                Measure("verify", () => Require(publicKey.VerifyData(Message.AsSpan(), signature, []), "Signature verification failed."), repetitions, warmup, token)
            };
            return new PrimitiveResult(algorithm.Name, "passed", privateKey.GetType().FullName,
                publicBytes.Length, privateBytes.Length, signature.Length,
                "Two pinned NIST ACVP verification cases passed (valid/invalid). Raw keys/signature; imported keys and message/signature tampering checked. This is not full ACVP certification.",
                Array.AsReadOnly(measurements));
        }
        finally { CryptographicOperations.ZeroMemory(privateBytes); }
    }

    private static void CheckAuthenticatedEncryption(byte[] secret, byte[] recovered, byte[] wrongSecret)
    {
        byte[] key = HKDF.DeriveKey(HashAlgorithmName.SHA256, secret, 32, info: "QuantumMatter v1.7 lab"u8.ToArray());
        byte[] recoveredKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, recovered, 32, info: "QuantumMatter v1.7 lab"u8.ToArray());
        byte[] wrongKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, wrongSecret, 32, info: "QuantumMatter v1.7 lab"u8.ToArray());
        byte[] plaintext = new byte[Message.Length];
        try
        {
            byte[] nonce = RandomNumberGenerator.GetBytes(12), tag = new byte[16], ciphertext = new byte[Message.Length];
            using var sender = new AesGcm(key, 16);
            using var receiver = new AesGcm(recoveredKey, 16);
            using var wrongReceiver = new AesGcm(wrongKey, 16);
            sender.Encrypt(nonce, Message, ciphertext, tag);
            receiver.Decrypt(nonce, ciphertext, tag, plaintext);
            Require(plaintext.AsSpan().SequenceEqual(Message), "AEAD round trip failed.");
            bool rejected = false;
            try { wrongReceiver.Decrypt(nonce, ciphertext, tag, plaintext); }
            catch (AuthenticationTagMismatchException) { rejected = true; }
            Require(rejected, "AEAD accepted a rejected KEM secret.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(recoveredKey);
            CryptographicOperations.ZeroMemory(wrongKey);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    internal static PrimitiveResult Unsupported(string name) => new(name, "unsupported", null, null, null, null,
        "The current native crypto provider does not support this primitive. No tests or timings were executed.", Array.Empty<OperationMeasurement>());

    internal static OperationMeasurement Measure(string operation, Action action, int repetitions, int warmup, CancellationToken token)
    {
        for (int index = 0; index < warmup; index++) { token.ThrowIfCancellationRequested(); action(); }
        var elapsed = new double[repetitions];
        var allocations = new long[repetitions];
        for (int index = 0; index < repetitions; index++)
        {
            token.ThrowIfCancellationRequested();
            long before = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            action();
            elapsed[index] = Stopwatch.GetElapsedTime(start).TotalMicroseconds;
            allocations[index] = GC.GetAllocatedBytesForCurrentThread() - before;
        }

        double[] sorted = elapsed.Order().ToArray();
        return new OperationMeasurement(operation, Array.AsReadOnly(elapsed), Array.AsReadOnly(allocations),
            sorted[(int)Math.Ceiling(0.5 * repetitions) - 1], sorted[(int)Math.Ceiling(0.95 * repetitions) - 1]);
    }

    internal static void Require(bool condition, string message)
    {
        if (!condition) { throw new InvalidOperationException(message); }
    }
}

internal sealed record LabReport(DateTimeOffset TimestampUtc, string TargetFramework,
    string Configuration, int MessageBytes, int Repetitions, int Warmup,
    string Methodology, IReadOnlyList<PrimitiveResult> Primitives);

internal sealed record PrimitiveResult(string Algorithm, string Status, string? ProviderType, int? PublicKeyBytes,
    int? PrivateKeyBytes, int? CiphertextOrSignatureBytes, string Notes, IReadOnlyList<OperationMeasurement> Measurements);

internal sealed record OperationMeasurement(string Operation, IReadOnlyList<double> Microseconds,
    IReadOnlyList<long> ManagedAllocatedBytes, double P50Microseconds, double P95Microseconds);
