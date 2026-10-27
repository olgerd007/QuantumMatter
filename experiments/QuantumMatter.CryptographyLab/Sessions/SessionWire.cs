using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace QuantumMatter.CryptographyLab.Sessions;

internal enum SessionError
{
    MalformedMessage,
    UnsupportedSuite,
    IdentityMismatch,
    AuthenticationFailed,
    UnexpectedState,
    SequenceMismatch,
    RecordLimit
}

internal sealed class SessionRejectedException(SessionError error) : CryptographicException($"Session rejected: {error}.")
{
    internal SessionError Error { get; } = error;
}

// Fixed-size, canonical lab framing; this is not a TLS wire format.
internal static class SessionWire
{
    internal const int HeaderBytes = 8;
    internal const int RandomBytes = 32;
    internal const int KeyBytes = 1184;
    internal const int CiphertextBytes = 1088;
    internal const int SignatureBytes = 3309;
    internal const int TagBytes = 32;
    internal const int MaximumHandshakeBytes = 8192;
    internal const int RecordHeaderBytes = 21;
    internal const int MaximumPlaintextBytes = 16 * 1024;
    internal const int MaximumRecords = 1024;
    internal const string Suite = "ML-KEM-768/ML-DSA-65/HKDF-SHA256/HMAC-SHA256/AES-256-GCM";

    internal static byte[] Identity(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name.Length is < 1 or > 64 || name.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('.' or '-' or '_')))
        {
            throw new ArgumentException("Use 1-64 ASCII letters, digits, dots, underscores or hyphens.", nameof(name));
        }

        return Encoding.ASCII.GetBytes(name);
    }

    internal static byte[] Create(byte type, ReadOnlySpan<byte> identity, int payloadBytes)
    {
        byte[] message = new byte[HeaderBytes + 1 + identity.Length + payloadBytes];
        WriteHeader(message, type);
        message[HeaderBytes] = (byte)identity.Length;
        identity.CopyTo(message.AsSpan(HeaderBytes + 1));
        return message;
    }

    internal static int ValidateHandshake(ReadOnlySpan<byte> message, byte type, int payloadBytes)
    {
        if (message.Length > MaximumHandshakeBytes || message.Length < HeaderBytes + 1)
        {
            throw new SessionRejectedException(SessionError.MalformedMessage);
        }

        ValidateHeader(message, type);
        int length = message[HeaderBytes];
        if (length is < 1 or > 64 || message.Length != HeaderBytes + 1 + length + payloadBytes)
        {
            throw new SessionRejectedException(SessionError.MalformedMessage);
        }

        foreach (byte character in message.Slice(HeaderBytes + 1, length))
        {
            if (!char.IsAsciiLetterOrDigit((char)character) && character is not ((byte)'.' or (byte)'-' or (byte)'_'))
            {
                throw new SessionRejectedException(SessionError.MalformedMessage);
            }
        }

        return HeaderBytes + 1 + length;
    }

    internal static void CheckIdentity(ReadOnlySpan<byte> message, ReadOnlySpan<byte> expected)
    {
        int length = message[HeaderBytes];
        if (!message.Slice(HeaderBytes + 1, length).SequenceEqual(expected))
        {
            throw new SessionRejectedException(SessionError.IdentityMismatch);
        }
    }

    internal static void WriteHeader(Span<byte> destination, byte type)
    {
        "QM18"u8.CopyTo(destination);
        destination[4] = 1; // Lab framing version; the fixed suite has identifier 1.
        destination[5] = type;
        BinaryPrimitives.WriteUInt16BigEndian(destination[6..], 1);
    }

    internal static void ValidateHeader(ReadOnlySpan<byte> message, byte type)
    {
        if (message.Length < HeaderBytes || !message[..4].SequenceEqual("QM18"u8) || message[5] != type)
        {
            throw new SessionRejectedException(SessionError.MalformedMessage);
        }

        if (message[4] != 1 || BinaryPrimitives.ReadUInt16BigEndian(message[6..]) != 1)
        {
            throw new SessionRejectedException(SessionError.UnsupportedSuite);
        }
    }

    internal static byte[] Transcript(ReadOnlySpan<byte> hello, ReadOnlySpan<byte> responseBody, ReadOnlySpan<byte> pin)
    {
        // The provisioned verification key replaces NIST's illustrative certificate.
        // Include its raw encoding as well as all transmitted unsigned fields.
        ReadOnlySpan<byte> domain = "QuantumMatter-v1.8-NIST-5.2.3-LAB"u8;
        byte[] transcript = new byte[domain.Length + 12 + hello.Length + responseBody.Length + pin.Length];
        domain.CopyTo(transcript);
        int offset = domain.Length;
        foreach (ReadOnlyMemory<byte> field in new[] { hello.ToArray().AsMemory(), responseBody.ToArray().AsMemory(), pin.ToArray().AsMemory() })
        {
            BinaryPrimitives.WriteInt32BigEndian(transcript.AsSpan(offset), field.Length);
            field.Span.CopyTo(transcript.AsSpan(offset + 4));
            offset += 4 + field.Length;
        }

        return transcript;
    }

    internal static byte[] TranscriptHash(ReadOnlySpan<byte> transcript, ReadOnlySpan<byte> signature)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(transcript);
        hash.AppendData(signature);
        return hash.GetHashAndReset();
    }
}
