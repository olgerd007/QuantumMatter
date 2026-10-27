using System.Buffers.Binary;
using System.Security.Cryptography;

namespace QuantumMatter.CryptographyLab.Sessions;

// Ordered, bounded records. Mutable and not thread-safe; any rejected input aborts the channel.
internal sealed class SessionChannel : IDisposable
{
    private readonly AesGcm _send;
    private readonly AesGcm _receive;
    private readonly byte[] _sendIv;
    private readonly byte[] _receiveIv;
    private readonly byte[] _hash;
    private readonly byte _direction;
    private ulong _nextSend, _nextReceive;
    private bool _disposed;

    internal SessionChannel(SessionSecrets material, byte[] hash, bool server)
    {
        material.ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(hash);
        if (hash.Length != 32) { throw new ArgumentException("Expected a SHA-256 transcript hash.", nameof(hash)); }
        _send = new AesGcm(server ? material.ServerKey : material.ClientKey, 16);
        bool complete = false;
        AesGcm? receive = null;
        try
        {
            receive = new AesGcm(server ? material.ClientKey : material.ServerKey, 16);
            _sendIv = (byte[])(server ? material.ServerIv : material.ClientIv).Clone();
            _receiveIv = (byte[])(server ? material.ClientIv : material.ServerIv).Clone();
            _hash = (byte[])hash.Clone();
            _direction = server ? (byte)1 : (byte)0;
            _receive = receive;
            complete = true;
        }
        finally { if (!complete) { receive?.Dispose(); _send.Dispose(); } }
    }

    internal byte[] Protect(ReadOnlySpan<byte> plaintext, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (plaintext.Length > SessionWire.MaximumPlaintextBytes) { throw new ArgumentOutOfRangeException(nameof(plaintext)); }
        cancellationToken.ThrowIfCancellationRequested();
        if (_nextSend >= SessionWire.MaximumRecords)
        {
            Dispose();
            throw new SessionRejectedException(SessionError.RecordLimit);
        }

        byte[] record = new byte[SessionWire.RecordHeaderBytes + plaintext.Length + 16];
        SessionWire.WriteHeader(record, 4);
        record[8] = _direction;
        BinaryPrimitives.WriteUInt64BigEndian(record.AsSpan(9), _nextSend);
        BinaryPrimitives.WriteInt32BigEndian(record.AsSpan(17), plaintext.Length);
        Span<byte> nonce = stackalloc byte[12], associated = stackalloc byte[SessionWire.RecordHeaderBytes + 32];
        Nonce(_sendIv, _nextSend, nonce);
        record.AsSpan(0, SessionWire.RecordHeaderBytes).CopyTo(associated);
        _hash.CopyTo(associated[SessionWire.RecordHeaderBytes..]);
        // This bounded encryption and counter commit are noncancellable once begun.
        try { _send.Encrypt(nonce, plaintext, record.AsSpan(SessionWire.RecordHeaderBytes, plaintext.Length), record.AsSpan(record.Length - 16), associated); }
        catch (CryptographicException) { Dispose(); throw; }
        _nextSend++;
        return record;
    }

    internal byte[] Unprotect(ReadOnlySpan<byte> record, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        byte[]? plaintext = null;
        bool released = false;
        try
        {
            if (record.Length < SessionWire.RecordHeaderBytes + 16 || record.Length > SessionWire.RecordHeaderBytes + SessionWire.MaximumPlaintextBytes + 16)
            {
                throw new SessionRejectedException(SessionError.MalformedMessage);
            }

            SessionWire.ValidateHeader(record, 4);
            int length = BinaryPrimitives.ReadInt32BigEndian(record[17..]);
            if (length < 0 || length > SessionWire.MaximumPlaintextBytes || record.Length != SessionWire.RecordHeaderBytes + length + 16)
            {
                throw new SessionRejectedException(SessionError.MalformedMessage);
            }

            if (record[8] != 1 - _direction || BinaryPrimitives.ReadUInt64BigEndian(record[9..]) != _nextReceive)
            {
                throw new SessionRejectedException(SessionError.SequenceMismatch);
            }

            if (_nextReceive >= SessionWire.MaximumRecords) { throw new SessionRejectedException(SessionError.RecordLimit); }
            Span<byte> nonce = stackalloc byte[12], associated = stackalloc byte[SessionWire.RecordHeaderBytes + 32];
            Nonce(_receiveIv, _nextReceive, nonce);
            record[..SessionWire.RecordHeaderBytes].CopyTo(associated);
            _hash.CopyTo(associated[SessionWire.RecordHeaderBytes..]);
            plaintext = new byte[length];
            _receive.Decrypt(nonce, record.Slice(SessionWire.RecordHeaderBytes, length), record[^16..], plaintext, associated);
            _nextReceive++;
            released = true;
            return plaintext;
        }
        catch (AuthenticationTagMismatchException)
        {
            Dispose();
            throw new SessionRejectedException(SessionError.AuthenticationFailed);
        }
        catch (SessionRejectedException) { Dispose(); throw; }
        catch (CryptographicException) { Dispose(); throw; }
        finally
        {
            if (!released && plaintext is not null) { CryptographicOperations.ZeroMemory(plaintext); }
        }
    }

    private static void Nonce(byte[] iv, ulong sequence, Span<byte> nonce)
    {
        iv.CopyTo(nonce);
        BinaryPrimitives.WriteUInt64BigEndian(nonce[4..], BinaryPrimitives.ReadUInt64BigEndian(nonce[4..]) ^ sequence);
    }

    public void Dispose()
    {
        if (_disposed) { return; }
        _disposed = true;
        _send.Dispose();
        _receive.Dispose();
        CryptographicOperations.ZeroMemory(_sendIv);
        CryptographicOperations.ZeroMemory(_receiveIv);
    }
}
