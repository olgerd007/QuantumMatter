using System.Security.Cryptography;
using System.Text.Json;

namespace QuantumMatter.CryptographyLab;

internal static class NistKnownAnswers
{
    internal static void ValidateKem(MLKemAlgorithm algorithm, CancellationToken token)
    {
        using JsonDocument document = ReadVectors();
        int checkedCases = 0;
        foreach (JsonElement vector in document.RootElement.GetProperty("cases").EnumerateArray())
        {
            if (vector.GetProperty("algorithm").GetString() != algorithm.Name) { continue; }
            token.ThrowIfCancellationRequested();
            // All private values in this resource are published synthetic NIST fixtures.
            byte[] privateBytes = Hex(vector, "dk");
            byte[] secret = new byte[algorithm.SharedSecretSizeInBytes];
            try
            {
                using MLKem key = MLKem.ImportDecapsulationKey(algorithm, privateBytes);
                key.Decapsulate(Hex(vector, "ciphertext"), secret);
                PqcExperiments.Require(CryptographicOperations.FixedTimeEquals(secret, Hex(vector, "secret")),
                    $"NIST ML-KEM known-answer case {vector.GetProperty("tcId").GetInt32()} failed.");
                checkedCases++;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(privateBytes);
                CryptographicOperations.ZeroMemory(secret);
            }
        }

        PqcExperiments.Require(checkedCases == 2, "Expected two pinned NIST KEM cases per parameter set.");
    }

    internal static void ValidateSignature(MLDsaAlgorithm algorithm, CancellationToken token)
    {
        using JsonDocument document = ReadVectors();
        int checkedCases = 0;
        foreach (JsonElement vector in document.RootElement.GetProperty("cases").EnumerateArray())
        {
            if (vector.GetProperty("algorithm").GetString() != algorithm.Name) { continue; }
            token.ThrowIfCancellationRequested();
            using MLDsa key = MLDsa.ImportMLDsaPublicKey(algorithm, Hex(vector, "publicKey"));
            bool valid = key.VerifyData(Hex(vector, "message").AsSpan(), Hex(vector, "signature"), Hex(vector, "context"));
            PqcExperiments.Require(valid == vector.GetProperty("valid").GetBoolean(),
                $"NIST ML-DSA known-answer case {vector.GetProperty("tcId").GetInt32()} failed.");
            checkedCases++;
        }

        PqcExperiments.Require(checkedCases == 2, "Expected two pinned NIST signature cases per parameter set.");
    }

    private static JsonDocument ReadVectors()
    {
        using Stream source = typeof(NistKnownAnswers).Assembly.GetManifestResourceStream(
            "QuantumMatter.CryptographyLab.Vectors.NistAcvp.json") ?? throw new InvalidOperationException("Missing NIST vector resource.");
        return JsonDocument.Parse(source);
    }

    private static byte[] Hex(JsonElement vector, string name) => Convert.FromHexString(vector.GetProperty(name).GetString()!);
}
