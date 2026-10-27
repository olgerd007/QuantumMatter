using System.Security.Cryptography;
using System.Text;

namespace QuantumMatter.CryptographyLab.Sessions;

internal sealed class SessionSecrets : IDisposable
{
    private readonly List<byte[]> _buffers = [];
    private bool _disposed;
    internal byte[] ClientKey { get; }
    internal byte[] ServerKey { get; }
    internal byte[] ClientIv { get; }
    internal byte[] ServerIv { get; }
    private byte[] ClientConfirmation { get; }
    private byte[] ServerConfirmation { get; }

    internal SessionSecrets(byte[] sharedSecret, byte[] transcriptHash)
    {
        ArgumentNullException.ThrowIfNull(sharedSecret);
        ArgumentNullException.ThrowIfNull(transcriptHash);
        if (sharedSecret.Length != 32 || transcriptHash.Length != 32)
        {
            throw new ArgumentException("The fixed lab suite requires 32-byte secrets and transcript hashes.");
        }
        bool complete = false;
        try
        {
            ClientKey = Derive("client-traffic", 32);
            ServerKey = Derive("server-traffic", 32);
            ClientIv = Derive("client-iv", 12);
            ServerIv = Derive("server-iv", 12);
            ClientConfirmation = Derive("client-confirmation", 32);
            ServerConfirmation = Derive("server-confirmation", 32);
            complete = true;
        }
        finally { if (!complete) { Dispose(); } }

        byte[] Derive(string label, int size)
        {
            byte[] buffer = HKDF.DeriveKey(HashAlgorithmName.SHA256, sharedSecret, size, transcriptHash,
                Encoding.ASCII.GetBytes("QuantumMatter-v1.8/" + label));
            _buffers.Add(buffer);
            return buffer;
        }
    }

    internal byte[] ServerFinished(byte[] hash)
    {
        ThrowIfDisposed();
        if (hash.Length != 32) { throw new ArgumentException("Expected a SHA-256 transcript hash.", nameof(hash)); }
        return HMACSHA256.HashData(ServerConfirmation, hash);
    }

    internal byte[] ClientFinished(byte[] hash, byte[] serverFinished)
    {
        ThrowIfDisposed();
        if (hash.Length != 32 || serverFinished.Length != 32) { throw new ArgumentException("Expected fixed-size confirmation context."); }
        Span<byte> context = stackalloc byte[64];
        hash.CopyTo(context);
        serverFinished.CopyTo(context[32..]);
        return HMACSHA256.HashData(ClientConfirmation, context);
    }

    public void Dispose()
    {
        if (_disposed) { return; }
        _disposed = true;
        foreach (byte[] buffer in _buffers) { CryptographicOperations.ZeroMemory(buffer); }
        _buffers.Clear();
    }

    internal void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
