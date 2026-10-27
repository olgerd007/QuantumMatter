using System.Security.Cryptography;

namespace QuantumMatter.CryptographyLab.Sessions;

internal sealed class ServerHandshake : IDisposable
{
    private readonly SessionSecrets _material;
    private readonly byte[] _hash;
    private readonly byte[] _response;
    private bool _used;

    private ServerHandshake(SessionSecrets material, byte[] hash, byte[] response)
    {
        _material = material;
        _hash = hash;
        _response = response;
    }

    // The signing key is borrowed only for Accept; ownership stays with the caller.
    internal static ServerHandshake Accept(ReadOnlySpan<byte> hello, string identity, MLDsa signingKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(signingKey);
        if (signingKey.Algorithm != MLDsaAlgorithm.MLDsa65) { throw new ArgumentException("The fixed lab suite requires ML-DSA-65.", nameof(signingKey)); }
        byte[] expected = SessionWire.Identity(identity);
        cancellationToken.ThrowIfCancellationRequested();
        int offset = SessionWire.ValidateHandshake(hello, 1, SessionWire.RandomBytes + SessionWire.KeyBytes);
        SessionWire.CheckIdentity(hello, expected);
        using MLKem publicKey = MLKem.ImportEncapsulationKey(MLKemAlgorithm.MLKem768, hello[(offset + SessionWire.RandomBytes)..]);
        byte[] ciphertext = new byte[SessionWire.CiphertextBytes], secret = new byte[32];
        SessionSecrets? material = null;
        bool transferred = false;
        try
        {
            publicKey.Encapsulate(ciphertext, secret);
            byte[] response = SessionWire.Create(2, expected,
                SessionWire.RandomBytes + SessionWire.CiphertextBytes + SessionWire.SignatureBytes + SessionWire.TagBytes);
            int responseOffset = SessionWire.HeaderBytes + 1 + expected.Length;
            RandomNumberGenerator.Fill(response.AsSpan(responseOffset, SessionWire.RandomBytes));
            ciphertext.CopyTo(response, responseOffset + SessionWire.RandomBytes);
            int signatureOffset = responseOffset + SessionWire.RandomBytes + SessionWire.CiphertextBytes;
            byte[] transcript = SessionWire.Transcript(hello, response.AsSpan(0, signatureOffset), signingKey.ExportMLDsaPublicKey());
            cancellationToken.ThrowIfCancellationRequested();
            signingKey.SignData(transcript, response.AsSpan(signatureOffset, SessionWire.SignatureBytes), "QM18-server"u8);
            byte[] hash = SessionWire.TranscriptHash(transcript, response.AsSpan(signatureOffset, SessionWire.SignatureBytes));
            material = new SessionSecrets(secret, hash);
            material.ServerFinished(hash).CopyTo(response, response.Length - SessionWire.TagBytes);
            cancellationToken.ThrowIfCancellationRequested();
            var result = new ServerHandshake(material, hash, response);
            transferred = true;
            return result;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
            if (!transferred) { material?.Dispose(); }
        }
    }

    internal byte[] Response => !_used ? (byte[])_response.Clone() : throw new SessionRejectedException(SessionError.UnexpectedState);

    internal SessionChannel Complete(ReadOnlySpan<byte> finished, CancellationToken cancellationToken = default)
    {
        if (_used) { throw new SessionRejectedException(SessionError.UnexpectedState); }
        _used = true;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (finished.Length != SessionWire.HeaderBytes + SessionWire.TagBytes)
            {
                throw new SessionRejectedException(SessionError.MalformedMessage);
            }

            SessionWire.ValidateHeader(finished, 3);
            byte[] expected = _material.ClientFinished(_hash, _response[^SessionWire.TagBytes..]);
            if (!CryptographicOperations.FixedTimeEquals(expected, finished[SessionWire.HeaderBytes..]))
            {
                throw new SessionRejectedException(SessionError.AuthenticationFailed);
            }

            return new SessionChannel(_material, _hash, server: true);
        }
        finally { Dispose(); }
    }

    public void Dispose()
    {
        _used = true;
        _material.Dispose();
    }
}
