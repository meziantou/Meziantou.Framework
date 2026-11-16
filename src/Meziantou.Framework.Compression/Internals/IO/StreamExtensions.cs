namespace Meziantou.Framework.Compression.Internals.IO;

internal static class StreamExtensions
{
    /// <summary>Fills <paramref name="buffer"/>, or throws an <see cref="InvalidDataException"/> when the stream ends first.</summary>
    public static void ReadExactlyOrThrow(this Stream stream, Span<byte> buffer)
    {
        if (stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false) != buffer.Length)
            throw new InvalidDataException("Unexpected end of data.");
    }

    /// <summary>Fills <paramref name="buffer"/>, or throws an <see cref="InvalidDataException"/> when the stream ends first.</summary>
    public static async ValueTask ReadExactlyOrThrowAsync(this Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        if (await stream.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false) != buffer.Length)
            throw new InvalidDataException("Unexpected end of data.");
    }

    /// <summary>Reads a byte, or throws an <see cref="InvalidDataException"/> when the stream ends.</summary>
    public static byte ReadByteOrThrow(this Stream stream)
    {
        var value = stream.ReadByte();
        if (value < 0)
            throw new InvalidDataException("Unexpected end of data.");

        return (byte)value;
    }

    /// <summary>Reads and discards <paramref name="count"/> bytes, or throws an <see cref="InvalidDataException"/> when the stream ends first.</summary>
    public static void Skip(this Stream stream, long count)
    {
        if (stream.CanSeek)
        {
            if (stream.Position + count > stream.Length)
                throw new InvalidDataException("Unexpected end of data.");

            stream.Position += count;
            return;
        }

        var buffer = new byte[(int)Math.Min(81920, Math.Max(count, 1))];
        while (count > 0)
        {
            var read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, count));
            if (read is 0)
                throw new InvalidDataException("Unexpected end of data.");

            count -= read;
        }
    }
}
