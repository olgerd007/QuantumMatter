using System.Security.Cryptography;

namespace QuantumMatter.CryptographyLab.Sessions;

// One attempt per object; all ephemeral/provider keys are owned and disposed here.
internal sealed class ClientHandshake : IDisposable
{
    private readonly MLKem _ephemeral;
    private readonly MLDsa _trustedSigner;
    private readonly byte[] _pin;
    private readonly byte[] _identity;
    private readonly byte[] _hello;
    private bool _used;

    internal ClientHandshake(string expectedServer, byte[] trustedSigningKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trustedSigningKey);
        if (trustedSigningKey.Length != MLDsaAlgorithm.MLDsa65.PublicKeySizeInBytes)
        {
            throw new ArgumentException("The provisioned pin must be an ML-DSA-65 public key.", nameof(trustedSigningKey));
        }
        _identity = SessionWire.Identity(expectedServer);
        cancellationToken.ThrowIfCancellationRequested();
        _pin = (byte[])trustedSigningKey.Clone();
        _trustedSigner = MLDsa.ImportMLDsaPublicKey(MLDsaAlgorithm.MLDsa65, _pin);
        MLKem? ephemeral = null;
        bool complete = false;
        try
        {
            ephemeral = MLKem.GenerateKey(MLKemAlgorithm.MLKem768);
            _hello = SessionWire.Create(1, _identity, SessionWire.RandomBytes + SessionWire.KeyBytes);
            int offset = _hello.Length - SessionWire.RandomBytes - SessionWire.KeyBytes;
            RandomNumberGenerator.Fill(_hello.AsSpan(offset, SessionWire.RandomBytes));
            ephemeral.ExportEncapsulationKey(_hello.AsSpan(offset + SessionWire.RandomBytes));
            cancellationToken.ThrowIfCancellationRequested();
            _ephemeral = ephemeral;
            complete = true;
        }
        finally
        {
            if (!complete) { ephemeral?.Dispose(); _trustedSigner.Dispose(); }
        }
    }

    internal byte[] Hello => !_used ? (byte[])_hello.Clone() : throw new SessionRejectedException(SessionError.UnexpectedState);

    internal SessionChannel Complete(ReadOnlySpan<byte> response, out byte[] finished,
        CancellationToken cancellationToken = default)
    {
        if (_used) { throw new SessionRejectedException(SessionError.UnexpectedState); }
        _used = true;
        finished = [];
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            int offset = SessionWire.ValidateHandshake(response, 2,
                SessionWire.RandomBytes + SessionWire.CiphertextBytes + SessionWire.SignatureBytes + SessionWire.TagBytes);
            SessionWire.CheckIdentity(response, _identity);
            int signatureOffset = offset + SessionWire.RandomBytes + SessionWire.CiphertextBytes;
            ReadOnlySpan<byte> signature = response.Slice(signatureOffset, SessionWire.SignatureBytes);
            byte[] transcript = SessionWire.Transcript(_hello, response[..signatureOffset], _pin);
            if (!_trustedSigner.VerifyData(transcript.AsSpan(), signature, "QM18-server"u8))
            {
                throw new SessionRejectedException(SessionError.AuthenticationFailed);
            }

            cancellationToken.ThrowIfCancellationRequested();
            byte[] secret = new byte[32];
            try
            {
                _ephemeral.Decapsulate(response.Slice(offset + SessionWire.RandomBytes, SessionWire.CiphertextBytes), secret);
                byte[] hash = SessionWire.TranscriptHash(transcript, signature);
                using var material = new SessionSecrets(secret, hash);
                byte[] serverFinished = material.ServerFinished(hash);
                if (!CryptographicOperations.FixedTimeEquals(serverFinished, response[^SessionWire.TagBytes..]))
                {
                    throw new SessionRejectedException(SessionError.AuthenticationFailed);
                }

                cancellationToken.ThrowIfCancellationRequested();
                byte[] confirmation = new byte[SessionWire.HeaderBytes + SessionWire.TagBytes];
                SessionWire.WriteHeader(confirmation, 3);
                material.ClientFinished(hash, serverFinished).CopyTo(confirmation, SessionWire.HeaderBytes);
                var channel = new SessionChannel(material, hash, server: false);
                finished = confirmation;
                return channel;
            }
            finally { CryptographicOperations.ZeroMemory(secret); }
        }
        finally { Dispose(); }
    }

    public void Dispose()
    {
        _used = true;
        _ephemeral.Dispose();
        _trustedSigner.Dispose();
    }
}
