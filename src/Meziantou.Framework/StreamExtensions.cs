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
            var length = stream.Length - stream.Position;
            if (length == 0)
                return [];

            var buffer = new byte[length];
            var actualLength = TryReadAll(stream, buffer, 0, buffer.Length);
            Array.Resize(ref buffer, actualLength);
            return buffer;
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
            var length = stream.Length - stream.Position;
            if (length == 0)
                return [];

            var buffer = new byte[length];
            var actualLength = await TryReadAllAsync(stream, buffer, cancellationToken).ConfigureAwait(false);
            Array.Resize(ref buffer, actualLength);
            return buffer;
        }

        using var memoryStream = new MemoryStream();
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
