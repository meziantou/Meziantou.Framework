using System.Buffers.Binary;
using System.Text;

namespace Meziantou.Framework.WebTransport.Http3;

// The capsules of the CONNECT stream (RFC 9297, section 3.2), sent in the payload of DATA frames
internal readonly record struct Capsule(long Type, ReadOnlyMemory<byte> Value)
{
    public const long Datagram = 0x00;
    public const long CloseWebTransportSession = 0x2843;
    public const long DrainWebTransportSession = 0x78AE;

    public const int MaxCloseReasonLength = 1024;

    public static byte[] Encode(long type, ReadOnlySpan<byte> value)
    {
        var result = new byte[VariableLengthInteger.GetEncodedLength(type) + VariableLengthInteger.GetEncodedLength(value.Length) + value.Length];
        var position = VariableLengthInteger.Write(result, type);
        position += VariableLengthInteger.Write(result.AsSpan(position), value.Length);
        value.CopyTo(result.AsSpan(position));
        return result;
    }

    // CLOSE_WEBTRANSPORT_SESSION: a 32-bit error code and a UTF-8 reason of at most 1024 bytes
    public static byte[] EncodeClose(uint errorCode, string reason)
    {
        var reasonBytes = Encoding.UTF8.GetBytes(reason);
        if (reasonBytes.Length > MaxCloseReasonLength)
            throw new ArgumentException($"The reason must be at most {MaxCloseReasonLength} bytes in UTF-8.", nameof(reason));

        var value = new byte[4 + reasonBytes.Length];
        BinaryPrimitives.WriteUInt32BigEndian(value, errorCode);
        reasonBytes.CopyTo(value, 4);
        return Encode(CloseWebTransportSession, value);
    }

    public static (uint ErrorCode, string Reason) DecodeClose(ReadOnlySpan<byte> value)
    {
        if (value.Length < 4 || value.Length > 4 + MaxCloseReasonLength)
            throw new Http3ProtocolException(Http3ErrorCode.MessageError, "Invalid CLOSE_WEBTRANSPORT_SESSION capsule.");

        string reason;
        try
        {
            reason = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(value[4..]);
        }
        catch (DecoderFallbackException ex)
        {
            throw new Http3ProtocolException("The reason of the CLOSE_WEBTRANSPORT_SESSION capsule is not valid UTF-8.", ex);
        }

        return (BinaryPrimitives.ReadUInt32BigEndian(value), reason);
    }

    // Reads the complete capsules at the start of the buffer, and returns the number of bytes they use
    public static int ReadCapsules(ReadOnlySpan<byte> buffer, List<Capsule> capsules, int maxCapsuleSize)
    {
        var position = 0;
        while (true)
        {
            var remaining = buffer[position..];
            if (!VariableLengthInteger.TryRead(remaining, out var type, out var typeLength))
                return position;

            if (!VariableLengthInteger.TryRead(remaining[typeLength..], out var length, out var lengthLength))
                return position;

            if (length > maxCapsuleSize)
                throw new Http3ProtocolException(Http3ErrorCode.ExcessiveLoad, "The capsule is too large.");

            var headerLength = typeLength + lengthLength;
            if (remaining.Length - headerLength < length)
                return position;

            capsules.Add(new Capsule(type, remaining.Slice(headerLength, (int)length).ToArray()));
            position += headerLength + (int)length;
        }
    }
}
