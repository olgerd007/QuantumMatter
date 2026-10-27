#if NET10_0_OR_GREATER
using System.Buffers.Binary;
using System.Security.Cryptography;
using QuantumMatter.CryptographyLab;
using QuantumMatter.CryptographyLab.Sessions;
using Xunit;

namespace QuantumMatter.Tests;

public sealed class PqcSessionTests
{
    private static bool Supported => MLKem.IsSupported && MLDsa.IsSupported && AesGcm.IsSupported;
    private const string Identity = "lab.server";

    [Fact]
    public void Session_experiment_requires_real_providers_and_rejects_every_reported_attack()
    {
        SessionExperimentResult result = AuthenticatedSessionExperiment.Run();
        Assert.Equal(Supported ? "passed" : "unsupported", result.Status);
        Assert.Equal(Supported ? 2 : 0, result.SuccessfulDirections);
        Assert.Equal(Supported ? 17 : 0, result.RejectedAttacks.Count);
        Assert.Equal(result.RejectedAttacks.Count, result.RejectedAttacks.Select(row => row.Attack).Distinct().Count());
    }

    [Fact]
    public void Fixed_suite_sizes_and_parser_limits_are_explicit()
    {
        Assert.Equal(SessionWire.KeyBytes, MLKemAlgorithm.MLKem768.EncapsulationKeySizeInBytes);
        Assert.Equal(SessionWire.CiphertextBytes, MLKemAlgorithm.MLKem768.CiphertextSizeInBytes);
        Assert.Equal(SessionWire.SignatureBytes, MLDsaAlgorithm.MLDsa65.SignatureSizeInBytes);
        Assert.Throws<ArgumentException>(() => SessionWire.Identity(""));
        Assert.Throws<ArgumentException>(() => SessionWire.Identity(new string('a', 65)));
        Assert.Throws<ArgumentException>(() => SessionWire.Identity("server\0other"));
        Assert.Throws<ArgumentException>(() => SessionWire.Identity("sérver"));
        Assert.Throws<OperationCanceledException>(() => AuthenticatedSessionExperiment.Run(new CancellationToken(true)));
    }

    [Fact]
    public void Canonical_parser_matches_independent_framing_predicate_for_seeded_malformed_inputs()
    {
        var random = new Random(7281);
        for (int trial = 0; trial < 3000; trial++)
        {
            byte[] message;
            if (trial % 3 == 0)
            {
                message = new byte[random.Next(SessionWire.MaximumHandshakeBytes + 2)];
                random.NextBytes(message);
            }
            else
            {
                // Independent encoding of a one-character identity plus a client payload.
                message = new byte[8 + 1 + 1 + 32 + 1184];
                "QM18"u8.CopyTo(message);
                message[4] = 1; message[5] = 1; message[7] = 1; message[8] = 1; message[9] = (byte)'a';
                if (trial % 3 == 1) { message[random.Next(message.Length)] ^= (byte)(1 << random.Next(8)); }
            }

            bool accepted;
            try { SessionWire.ValidateHandshake(message, 1, 32 + 1184); accepted = true; }
            catch (SessionRejectedException) { accepted = false; }
            Assert.Equal(IndependentValidHello(message), accepted);
        }

        static bool IndependentValidHello(byte[] message)
        {
            if (message.Length < 9 || message.Length > 8192 || message[0] != 'Q' || message[1] != 'M'
                || message[2] != '1' || message[3] != '8' || message[4] != 1 || message[5] != 1
                || message[6] != 0 || message[7] != 1) { return false; }
            int length = message[8];
            if (length < 1 || length > 64 || message.Length != 9 + length + 1216) { return false; }
            return message.Skip(9).Take(length).All(value =>
                value is >= (byte)'a' and <= (byte)'z' or >= (byte)'A' and <= (byte)'Z'
                    or >= (byte)'0' and <= (byte)'9' or (byte)'.' or (byte)'-' or (byte)'_');
        }
    }

    [Fact]
    public void Handshake_binds_every_transcript_field_and_cannot_be_retried_after_rejection()
    {
        if (!Supported) { Assert.Equal("unsupported", AuthenticatedSessionExperiment.Run().Status); return; }
        using MLDsa signer = MLDsa.GenerateKey(MLDsaAlgorithm.MLDsa65);
        byte[] pin = signer.ExportMLDsaPublicKey();
        // Independently choose offsets for identity, nonce, ciphertext, signature and KC tag.
        foreach (int offset in new[] { 9, 10 + Identity.Length, 9 + Identity.Length + 32,
            9 + Identity.Length + 32 + 1088, 9 + Identity.Length + 32 + 1088 + 3309 })
        {
            using var client = new ClientHandshake(Identity, pin);
            using var server = ServerHandshake.Accept(client.Hello, Identity, signer);
            byte[] response = server.Response;
            response[offset] ^= 1;
            Assert.Throws<SessionRejectedException>(() => client.Complete(response, out _));
            Assert.Equal(SessionError.UnexpectedState,
                Assert.Throws<SessionRejectedException>(() => client.Complete(server.Response, out _)).Error);
        }

        using var actualClient = new ClientHandshake(Identity, pin);
        using var otherClient = new ClientHandshake(Identity, pin);
        using var substituted = ServerHandshake.Accept(otherClient.Hello, Identity, signer);
        Assert.Equal(SessionError.AuthenticationFailed,
            Assert.Throws<SessionRejectedException>(() => actualClient.Complete(substituted.Response, out _)).Error);

        // A validly encoded, changed client nonce also binds the signed response to the wrong hello.
        using var client2 = new ClientHandshake(Identity, pin);
        byte[] alteredHello = client2.Hello;
        alteredHello[9 + Identity.Length] ^= 1;
        using var changed = ServerHandshake.Accept(alteredHello, Identity, signer);
        Assert.Equal(SessionError.AuthenticationFailed,
            Assert.Throws<SessionRejectedException>(() => client2.Complete(changed.Response, out _)).Error);

        using var victim = new ClientHandshake(Identity, pin);
        using var interceptor = new ClientHandshake(Identity, pin);
        byte[] replacedKey = victim.Hello;
        interceptor.Hello.AsSpan(^SessionWire.KeyBytes).CopyTo(replacedKey.AsSpan(replacedKey.Length - SessionWire.KeyBytes));
        using var keySubstituted = ServerHandshake.Accept(replacedKey, Identity, signer);
        Assert.Equal(SessionError.AuthenticationFailed,
            Assert.Throws<SessionRejectedException>(() => victim.Complete(keySubstituted.Response, out _)).Error);
    }

    [Fact]
    public void Trusted_pin_is_snapshotted_and_signing_key_ownership_is_preserved()
    {
        if (!Supported) { Assert.Equal("unsupported", AuthenticatedSessionExperiment.Run().Status); return; }
        using MLDsa signer = MLDsa.GenerateKey(MLDsaAlgorithm.MLDsa65);
        byte[] pin = signer.ExportMLDsaPublicKey();
        using var client = new ClientHandshake(Identity, pin);
        Array.Clear(pin);
        byte[] hello = client.Hello;
        using var server = ServerHandshake.Accept(hello, Identity, signer);
        Array.Clear(hello);
        using SessionChannel channel = client.Complete(server.Response, out byte[] finished);
        using SessionChannel peer = server.Complete(finished);
        byte[] plaintext = Enumerable.Range(0, 257).Select(value => (byte)value).ToArray();
        Assert.Equal(plaintext, peer.Unprotect(channel.Protect(plaintext)));
        Assert.Equal(plaintext, channel.Unprotect(peer.Protect(plaintext)));
        Assert.Throws<SessionRejectedException>(() => server.Complete(finished));
        Assert.NotEmpty(signer.SignData(plaintext, []));
    }

    [Fact]
    public void Cancelled_and_malformed_handshake_attempts_abort_without_a_channel()
    {
        if (!Supported) { Assert.Equal("unsupported", AuthenticatedSessionExperiment.Run().Status); return; }
        using MLDsa signer = MLDsa.GenerateKey(MLDsaAlgorithm.MLDsa65);
        byte[] pin = signer.ExportMLDsaPublicKey();
        Assert.Throws<OperationCanceledException>(() => new ClientHandshake(Identity, pin, new CancellationToken(true)));
        using var client = new ClientHandshake(Identity, pin);
        using var server = ServerHandshake.Accept(client.Hello, Identity, signer);
        Assert.Throws<OperationCanceledException>(() => client.Complete(server.Response, out _, new CancellationToken(true)));
        Assert.Throws<SessionRejectedException>(() => client.Complete(server.Response, out _));
        Assert.Throws<OperationCanceledException>(() => server.Complete(new byte[40], new CancellationToken(true)));
        Assert.Throws<SessionRejectedException>(() => server.Complete(new byte[40]));
        Assert.Throws<SessionRejectedException>(() => ServerHandshake.Accept(new byte[SessionWire.MaximumHandshakeBytes + 1], Identity, signer));

        using var pendingClient = new ClientHandshake(Identity, signer.ExportMLDsaPublicKey());
        using var pendingServer = ServerHandshake.Accept(pendingClient.Hello, Identity, signer);
        byte[] response = pendingServer.Response;
        foreach (int length in new[] { 0, 7, 8, 9, response.Length - 1, response.Length + 1 })
        {
            using var attempt = new ClientHandshake(Identity, signer.ExportMLDsaPublicKey());
            byte[] malformed = new byte[length];
            response.AsSpan(0, Math.Min(response.Length, length)).CopyTo(malformed);
            Assert.Throws<SessionRejectedException>(() => attempt.Complete(malformed, out _));
        }
    }

    [Fact]
    public void Records_authenticate_header_context_and_payload_and_abort_on_any_invalid_record()
    {
        foreach (string attack in new[] { "version", "suite", "direction", "sequence", "negative-length", "payload", "tag", "trailing", "truncated", "other-session" })
        {
            using var pair = Channels();
            byte[] record = pair.Client.Protect("not released on failure"u8);
            switch (attack)
            {
                case "version": record[4]++; break;
                case "suite": record[7]++; break;
                case "direction": record[8] = 1; break;
                case "sequence": record[16]++; break;
                case "negative-length": BinaryPrimitives.WriteInt32BigEndian(record.AsSpan(17), -1); break;
                case "payload": record[21] ^= 1; break;
                case "tag": record[^1] ^= 1; break;
                case "trailing": record = [.. record, 0]; break;
                case "truncated": record = record[..^1]; break;
                case "other-session":
                    using (var other = Channels(1)) { record = other.Client.Protect("another session"u8); }
                    break;
            }

            byte[]? released = null;
            Assert.Throws<SessionRejectedException>(() => released = pair.Server.Unprotect(record));
            Assert.Null(released);
            Assert.Throws<ObjectDisposedException>(() => pair.Server.Unprotect(record));
        }
    }

    [Fact]
    public void Empty_maximum_and_ordered_records_work_and_replay_or_exhaustion_aborts()
    {
        using var pair = Channels();
        Assert.Empty(pair.Server.Unprotect(pair.Client.Protect([])));
        byte[] message = Enumerable.Repeat((byte)19, SessionWire.MaximumPlaintextBytes).ToArray();
        Assert.Equal(message, pair.Client.Unprotect(pair.Server.Protect(message)));
        byte[] first = pair.Client.Protect(message), second = pair.Client.Protect(message);
        Assert.False(first.AsSpan(21, message.Length).SequenceEqual(second.AsSpan(21, message.Length)));
        Assert.Equal(message, pair.Server.Unprotect(first));
        Assert.Equal(message, pair.Server.Unprotect(second));
        Assert.Equal(SessionError.SequenceMismatch, Assert.Throws<SessionRejectedException>(() => pair.Server.Unprotect(first)).Error);
        Assert.Throws<ArgumentOutOfRangeException>(() => pair.Client.Protect(new byte[SessionWire.MaximumPlaintextBytes + 1]));
        for (int index = 3; index < SessionWire.MaximumRecords; index++) { pair.Client.Protect([]); }
        Assert.Equal(SessionError.RecordLimit, Assert.Throws<SessionRejectedException>(() => pair.Client.Protect([])).Error);
        Assert.Throws<ObjectDisposedException>(() => pair.Client.Protect([]));
    }

    [Fact]
    public void Cancelled_record_operation_leaves_counters_unchanged_and_dispose_wipes_owned_material()
    {
        using var pair = Channels();
        Assert.Throws<OperationCanceledException>(() => pair.Client.Protect([], new CancellationToken(true)));
        byte[] record = pair.Client.Protect("valid"u8);
        Assert.Equal(0UL, BinaryPrimitives.ReadUInt64BigEndian(record.AsSpan(9)));
        Assert.Throws<OperationCanceledException>(() => pair.Server.Unprotect(record, new CancellationToken(true)));
        Assert.Equal("valid"u8.ToArray(), pair.Server.Unprotect(record));
        using var material = new SessionSecrets(new byte[32], new byte[32]);
        byte[][] buffers = [material.ClientKey, material.ServerKey, material.ClientIv, material.ServerIv];
        material.Dispose();
        Assert.All(buffers, buffer => Assert.All(buffer, value => Assert.Equal(0, value)));
        Assert.Throws<ObjectDisposedException>(() => material.ServerFinished(new byte[32]));
        Assert.Throws<ObjectDisposedException>(() => new SessionChannel(material, new byte[32], false));
    }

    // Synthetic input to the record layer permits deterministic, provider-independent
    // framing tests; handshake tests separately exercise actual KEM/signature providers.
    private static ChannelPair Channels(byte context = 0)
    {
        byte[] secret = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();
        byte[] hash = SHA256.HashData([.. "public synthetic session fixture"u8, context]);
        using var material = new SessionSecrets(secret, hash);
        var pair = new ChannelPair(new SessionChannel(material, hash, false), new SessionChannel(material, hash, true));
        CryptographicOperations.ZeroMemory(secret);
        return pair;
    }

    private sealed record ChannelPair(SessionChannel Client, SessionChannel Server) : IDisposable
    {
        public void Dispose() { Client.Dispose(); Server.Dispose(); }
    }
}
#endif
