using System.Security.Cryptography;

namespace QuantumMatter.CryptographyLab;

// .NET 10 marks SLH-DSA experimental. This explicit, opt-in lab is the only
// diagnostic opt-in; no runtime library or global warning policy is changed.
#pragma warning disable SYSLIB5006
internal static class SlhDsaExperiment
{
    internal static PrimitiveResult Run(int repetitions, int warmup, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        SlhDsaAlgorithm algorithm = SlhDsaAlgorithm.SlhDsaSha2_128f;
        if (!SlhDsa.IsSupported) { return PqcExperiments.Unsupported(algorithm.Name); }
        using SlhDsa privateKey = SlhDsa.GenerateKey(algorithm);
        byte[] publicBytes = privateKey.ExportSlhDsaPublicKey();
        byte[] privateBytes = privateKey.ExportSlhDsaPrivateKey();
        try
        {
            using SlhDsa publicKey = SlhDsa.ImportSlhDsaPublicKey(algorithm, publicBytes);
            using SlhDsa imported = SlhDsa.ImportSlhDsaPrivateKey(algorithm, privateBytes);
            byte[] message = "QuantumMatter optional experimental SLH-DSA provider study"u8.ToArray();
            byte[] signature = imported.SignData(message, []);
            PqcExperiments.Require(publicKey.VerifyData(message.AsSpan(), signature, []), "SLH-DSA round trip failed.");
            message[0] ^= 1;
            PqcExperiments.Require(!publicKey.VerifyData(message.AsSpan(), signature, []), "SLH-DSA accepted a changed message.");
            message[0] ^= 1;
            signature[0] ^= 1;
            PqcExperiments.Require(!publicKey.VerifyData(message.AsSpan(), signature, []), "SLH-DSA accepted a changed signature.");
            privateKey.SignData(message, signature, []);
            var measurements = new[]
            {
                PqcExperiments.Measure("key-generation", () => { using var key = SlhDsa.GenerateKey(algorithm); }, repetitions, warmup, token),
                PqcExperiments.Measure("sign", () => privateKey.SignData(message, signature, []), repetitions, warmup, token),
                PqcExperiments.Measure("verify", () => PqcExperiments.Require(publicKey.VerifyData(message.AsSpan(), signature, []), "SLH-DSA verification failed."), repetitions, warmup, token)
            };
            return new PrimitiveResult(algorithm.Name, "passed", privateKey.GetType().FullName,
                publicBytes.Length, privateBytes.Length, signature.Length,
                $"Explicit experimental .NET 10 API opt-in; message bytes={message.Length}; raw keys/signature; imported-key and tampering checks. Not a conformance certification.",
                Array.AsReadOnly(measurements));
        }
        finally { CryptographicOperations.ZeroMemory(privateBytes); }
    }
}
#pragma warning restore SYSLIB5006
