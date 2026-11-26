using System.Buffers.Binary;

namespace Meziantou.Framework.WebTransport.Http3;

// The variable-length integers of QUIC (RFC 9000, section 16): the 2 most significant bits of the first byte give the
// length (1, 2, 4 or 8 bytes) of a big-endian value of 6, 14, 30 or 62 bits.
internal static class VariableLengthInteger
{
    public const long MaxValue = (1L << 62) - 1;

    public static int GetLength(byte firstByte) => 1 << (firstByte >> 6);

    public static int GetEncodedLength(long value) => value switch
    {
        < 0 => throw new ArgumentOutOfRangeException(nameof(value)),
        <= 63 => 1,
        <= 16383 => 2,
        <= 1073741823 => 4,
        <= MaxValue => 8,
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    public static bool TryRead(ReadOnlySpan<byte> source, out long value, out int bytesRead)
    {
        if (source.IsEmpty)
        {
            value = 0;
            bytesRead = 0;
            return false;
        }

        var length = GetLength(source[0]);
        if (source.Length < length)
        {
            value = 0;
            bytesRead = 0;
            return false;
        }

        value = length switch
        {
            1 => source[0] & 0x3F,
            2 => BinaryPrimitives.ReadUInt16BigEndian(source) & 0x3FFF,
            4 => BinaryPrimitives.ReadUInt32BigEndian(source) & 0x3FFF_FFFF,
            _ => (long)(BinaryPrimitives.ReadUInt64BigEndian(source) & 0x3FFF_FFFF_FFFF_FFFF),
        };
        bytesRead = length;
        return true;
    }

    public static int Write(Span<byte> destination, long value)
    {
        var length = GetEncodedLength(value);
        switch (length)
        {
            case 1:
                destination[0] = (byte)value;
                break;
            case 2:
                BinaryPrimitives.WriteUInt16BigEndian(destination, (ushort)(value | 0x4000));
                break;
            case 4:
                BinaryPrimitives.WriteUInt32BigEndian(destination, (uint)value | 0x8000_0000);
                break;
            default:
                BinaryPrimitives.WriteUInt64BigEndian(destination, (ulong)value | 0xC000_0000_0000_0000);
                break;
        }

        return length;
    }

    public static void Write(List<byte> destination, long value)
    {
        Span<byte> buffer = stackalloc byte[8];
        var length = Write(buffer, value);
        foreach (var b in buffer[..length])
        {
            destination.Add(b);
        }
    }

    // Reads a variable-length integer from a stream, one byte at a time, so the stream position stays at the end of the
    // integer. Returns null at the end of the stream before the first byte.
    public static async ValueTask<long?> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[8];
        var read = await stream.ReadAsync(buffer.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);
        if (read == 0)
            return null;

        var length = GetLength(buffer[0]);
        if (length > 1)
        {
            await stream.ReadExactlyAsync(buffer.AsMemory(1, length - 1), cancellationToken).ConfigureAwait(false);
        }

        TryRead(buffer, out var value, out _);
        return value;
    }
}
