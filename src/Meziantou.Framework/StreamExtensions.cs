namespace Meziantou.Framework;

public static class StreamExtensions
{
    public static int TryReadAll(this Stream stream, byte[] buffer, int offset, int count)
    {
        return TryReadAll(stream, buffer.AsSpan(offset, count));
    }

    public static int TryReadAll(this Stream stream, Span<byte> buffer)
    {
        var totalRead = 0;
        while (!buffer.IsEmpty)
        {
            var read = stream.Read(buffer);
            if (read == 0)
                return totalRead;

            totalRead += read;
            buffer = buffer[read..];
        }

        return totalRead;
    }

    public static Task<int> TryReadAllAsync(this Stream stream, byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
    {
        return TryReadAllAsync(stream, buffer.AsMemory(offset, count), cancellationToken);
    }

    public static async Task<int> TryReadAllAsync(this Stream stream, Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var totalRead = 0;
        while (!buffer.IsEmpty)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
                return totalRead;

            totalRead += read;
            buffer = buffer[read..];
        }

        return totalRead;
    }

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
