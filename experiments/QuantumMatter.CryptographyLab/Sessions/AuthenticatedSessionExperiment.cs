using System.Security.Cryptography;

namespace QuantumMatter.CryptographyLab.Sessions;

internal static class AuthenticatedSessionExperiment
{
    private const string Identity = "lab.server";

    internal static SessionExperimentResult Run(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!MLKem.IsSupported || !MLDsa.IsSupported || !AesGcm.IsSupported)
        {
            return new("unsupported", SessionWire.Suite, "Requires native ML-KEM, ML-DSA and AES-GCM support.", 0, []);
        }

        NistKnownAnswers.ValidateKem(MLKemAlgorithm.MLKem768, cancellationToken);
        NistKnownAnswers.ValidateSignature(MLDsaAlgorithm.MLDsa65, cancellationToken);
        using MLDsa signer = MLDsa.GenerateKey(MLDsaAlgorithm.MLDsa65);
        byte[] pin = signer.ExportMLDsaPublicKey();
        using (var client = new ClientHandshake(Identity, pin, cancellationToken))
        using (var server = ServerHandshake.Accept(client.Hello, Identity, signer, cancellationToken))
        using (SessionChannel clientChannel = client.Complete(server.Response, out byte[] finished, cancellationToken))
        using (SessionChannel serverChannel = server.Complete(finished, cancellationToken))
        {
            byte[] message = "authenticated lab record"u8.ToArray();
            byte[] decrypted = serverChannel.Unprotect(clientChannel.Protect(message, cancellationToken), cancellationToken);
            try { PqcExperiments.Require(decrypted.AsSpan().SequenceEqual(message), "Authenticated client record mismatch."); }
            finally { CryptographicOperations.ZeroMemory(decrypted); }
            decrypted = clientChannel.Unprotect(serverChannel.Protect(message, cancellationToken), cancellationToken);
            try { PqcExperiments.Require(decrypted.AsSpan().SequenceEqual(message), "Authenticated server record mismatch."); }
            finally { CryptographicOperations.ZeroMemory(decrypted); }
        }

        var checks = new List<SessionAttackResult>();
        foreach (string attack in new[] { "wrong-signing-key", "ciphertext-change", "signature-change", "finished-change", "downgrade", "wrong-identity", "truncated-response", "cross-session-response" })
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var client = new ClientHandshake(Identity, pin, cancellationToken);
            using MLDsa attacker = MLDsa.GenerateKey(MLDsaAlgorithm.MLDsa65);
            using var server = ServerHandshake.Accept(client.Hello, Identity, attack == "wrong-signing-key" ? attacker : signer, cancellationToken);
            byte[] response = server.Response;
            int body = SessionWire.HeaderBytes + 1 + response[SessionWire.HeaderBytes];
            switch (attack)
            {
                case "ciphertext-change": response[body + SessionWire.RandomBytes] ^= 1; break;
                case "signature-change": response[body + SessionWire.RandomBytes + SessionWire.CiphertextBytes] ^= 1; break;
                case "finished-change": response[^1] ^= 1; break;
                case "downgrade": response[7] = 0; break;
                case "wrong-identity": response[SessionWire.HeaderBytes + 1] ^= 1; break;
                case "truncated-response": response = response[..^1]; break;
                case "cross-session-response":
                    using (var otherClient = new ClientHandshake(Identity, pin, cancellationToken))
                    using (var otherServer = ServerHandshake.Accept(otherClient.Hello, Identity, signer, cancellationToken))
                    { response = otherServer.Response; }
                    break;
            }

            Reject(attack, () => { using var invalid = client.Complete(response, out _, cancellationToken); });
        }

        foreach (string attack in new[] { "client-finished-change", "finished-reflection", "finished-replay", "record-replay", "record-change", "record-reflection", "record-reordering", "oversized-record", "record-limit" })
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var client = new ClientHandshake(Identity, pin, cancellationToken);
            using var server = ServerHandshake.Accept(client.Hello, Identity, signer, cancellationToken);
            byte[] response = server.Response;
            using SessionChannel clientChannel = client.Complete(response, out byte[] finished, cancellationToken);
            if (attack == "client-finished-change")
            {
                finished[^1] ^= 1;
                Reject(attack, () => { using var invalid = server.Complete(finished, cancellationToken); });
                continue;
            }

            if (attack == "finished-reflection")
            {
                response.AsSpan(response.Length - SessionWire.TagBytes).CopyTo(finished.AsSpan(SessionWire.HeaderBytes));
                Reject(attack, () => { using var invalid = server.Complete(finished, cancellationToken); });
                continue;
            }

            using SessionChannel serverChannel = server.Complete(finished, cancellationToken);
            byte[] record = clientChannel.Protect("test"u8, cancellationToken);
            switch (attack)
            {
                case "finished-replay": Reject(attack, () => { using var invalid = server.Complete(finished, cancellationToken); }); break;
                case "record-replay":
                    CryptographicOperations.ZeroMemory(serverChannel.Unprotect(record, cancellationToken));
                    Reject(attack, () => { serverChannel.Unprotect(record, cancellationToken); }); break;
                case "record-change":
                    record[^1] ^= 1;
                    Reject(attack, () => { serverChannel.Unprotect(record, cancellationToken); }); break;
                case "record-reflection": Reject(attack, () => { clientChannel.Unprotect(record, cancellationToken); }); break;
                case "record-reordering":
                    byte[] second = clientChannel.Protect("second"u8, cancellationToken);
                    Reject(attack, () => { serverChannel.Unprotect(second, cancellationToken); }); break;
                case "oversized-record":
                    byte[] oversized = new byte[SessionWire.MaximumPlaintextBytes + SessionWire.RecordHeaderBytes + 17];
                    Reject(attack, () => { serverChannel.Unprotect(oversized, cancellationToken); }); break;
                case "record-limit":
                    for (int index = 1; index < SessionWire.MaximumRecords; index++) { clientChannel.Protect([], cancellationToken); }
                    Reject(attack, () => { clientChannel.Protect([], cancellationToken); }); break;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new("passed", SessionWire.Suite,
            "Educational concretization of NIST SP 800-227 section 5.2.3. Server authenticated by a provisioned ML-DSA pin; client anonymous. Lab framing/confirmation/record extensions are not a reviewed production protocol or TLS implementation.",
            2, checks.AsReadOnly());

        void Reject(string attack, Action action)
        {
            try { action(); }
            catch (SessionRejectedException error) { checks.Add(new(attack, error.Error.ToString())); return; }
            throw new InvalidOperationException($"Adversarial check unexpectedly accepted: {attack}.");
        }
    }
}

internal sealed record SessionExperimentResult(string Status, string Suite, string Assumptions,
    int SuccessfulDirections, IReadOnlyList<SessionAttackResult> RejectedAttacks);

internal sealed record SessionAttackResult(string Attack, string Error);
