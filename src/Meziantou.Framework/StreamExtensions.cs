namespace Meziantou.Framework;

/// <summary>
/// Provides extension methods for <see cref="Stream"/>.
/// </summary>
public static class StreamExtensions
{
    /// <summary>Attempts to read all requested bytes from the stream into the buffer.</summary>
    public static int TryReadAll(this Stream stream, byte[] buffer, int offset, int count)
    {
        return TryReadAll(stream, buffer.AsSpan(offset, count));
    }

    /// <summary>Attempts to read all requested bytes from the stream into the buffer span.</summary>
    public static int TryReadAll(this Stream stream, Span<byte> buffer)
    {
        var totalRead = 0;
        while (!buffer.IsEmpty)
        {
            var read = stream.Read(buffer);
            if (read is 0)
                return totalRead;

            totalRead += read;
            buffer = buffer[read..];
        }

        return totalRead;
    }

    /// <summary>Asynchronously attempts to read all requested bytes from the stream into the buffer.</summary>
    public static Task<int> TryReadAllAsync(this Stream stream, byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
    {
        return TryReadAllAsync(stream, buffer.AsMemory(offset, count), cancellationToken);
    }

    /// <summary>Asynchronously attempts to read all requested bytes from the stream into the buffer memory.</summary>
    public static async Task<int> TryReadAllAsync(this Stream stream, Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var totalRead = 0;
        while (!buffer.IsEmpty)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read is 0)
                return totalRead;

            totalRead += read;
            buffer = buffer[read..];
        }

        return totalRead;
    }

    /// <summary>Reads all remaining bytes from the stream.</summary>
    public static byte[] ReadToEnd(this Stream stream)
    {
        if (stream.CanSeek)
        {
            var length = GetRemainingLength(stream);
            if (length == 0)
            {
                // The stream may still have data, for instance when the length is not known yet
                return ReadToEndUsingMemoryStream(stream, []);
            }

            var buffer = new byte[length];
            var actualLength = TryReadAll(stream, buffer, 0, buffer.Length);
            if (actualLength < buffer.Length)
            {
                Array.Resize(ref buffer, actualLength);
                return buffer;
            }

            // The stream may have grown while reading (e.g. a file being appended), so the length is
            // only a hint and the rest of the data must be read until the end of the stream
            return ReadToEndUsingMemoryStream(stream, buffer);
        }

        using var memoryStream = new MemoryStream();
        stream.CopyTo(memoryStream);
        return memoryStream.ToArray();
    }

    /// <summary>Asynchronously reads all remaining bytes from the stream.</summary>
    public static async Task<byte[]> ReadToEndAsync(this Stream stream, CancellationToken cancellationToken = default)
    {
        if (stream.CanSeek)
        {
            var length = GetRemainingLength(stream);
            if (length == 0)
                return await ReadToEndUsingMemoryStreamAsync(stream, [], cancellationToken).ConfigureAwait(false);

            var buffer = new byte[length];
            var actualLength = await TryReadAllAsync(stream, buffer, cancellationToken).ConfigureAwait(false);
            if (actualLength < buffer.Length)
            {
                Array.Resize(ref buffer, actualLength);
                return buffer;
            }

            // See ReadToEnd
            return await ReadToEndUsingMemoryStreamAsync(stream, buffer, cancellationToken).ConfigureAwait(false);
        }

        using var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream, cancellationToken).ConfigureAwait(false);
        return memoryStream.ToArray();
    }

    private static int GetRemainingLength(Stream stream)
    {
        // The position can be past the end of the stream, in which case nothing remains
        var length = stream.Length - stream.Position;
        return (int)Math.Clamp(length, 0, Array.MaxLength);
    }

    private static byte[] ReadToEndUsingMemoryStream(Stream stream, byte[] prefix)
    {
        // Reading one byte first avoids a MemoryStream and a copy in the common case where the stream is at its end
        var nextByte = stream.ReadByte();
        if (nextByte < 0)
            return prefix;

        using var memoryStream = new MemoryStream();
        memoryStream.Write(prefix);
        memoryStream.WriteByte((byte)nextByte);
        stream.CopyTo(memoryStream);
        return memoryStream.ToArray();
    }

    private static async Task<byte[]> ReadToEndUsingMemoryStreamAsync(Stream stream, byte[] prefix, CancellationToken cancellationToken)
    {
        var nextByte = new byte[1];
        var read = await stream.ReadAsync(nextByte, cancellationToken).ConfigureAwait(false);
        if (read is 0)
            return prefix;

        using var memoryStream = new MemoryStream();
        memoryStream.Write(prefix);
        memoryStream.Write(nextByte);
        await stream.CopyToAsync(memoryStream, cancellationToken).ConfigureAwait(false);
        return memoryStream.ToArray();
    }

    /// <summary>Asynchronously copies the stream to a new <see cref="MemoryStream"/> and returns it positioned at the beginning.</summary>
    public static async Task<MemoryStream> ToMemoryStreamAsync(this Stream stream, CancellationToken cancellationToken = default)
    {
        var ms = new MemoryStream();
        try
        {
            await stream.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
            ms.Seek(0, SeekOrigin.Begin);
            return ms;
        }
        catch
        {
            await ms.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
