namespace Meziantou.Framework.Fonts;

/// <summary>Reads a font file from a stream to its end, within <see cref="FontResourceLimits.MaxEncodedBytes"/>.</summary>
internal static class FontStreamReader
{
    private const int ChunkLength = 64 * 1024;

    /// <summary>Reads the stream to its end.</summary>
    public static byte[] ReadAll(Stream stream, FontResourceLimits limits)
    {
        var buffer = new byte[GetInitialLength(stream, limits)];
        var length = 0;
        Span<byte> next = stackalloc byte[1];
        while (true)
        {
            if (length == buffer.Length)
            {
                // The buffer is full, which is the end of a seekable stream: one more byte says whether to grow it.
                if (stream.Read(next) == 0)
                    break;

                buffer = Grow(buffer, limits);
                buffer[length++] = next[0];
                EnsureEncodedBytes(limits, length);
                continue;
            }

            var read = stream.Read(buffer.AsSpan(length));
            if (read == 0)
                break;

            length += read;
            EnsureEncodedBytes(limits, length);
        }

        return Trim(buffer, length);
    }

    /// <summary>Reads the stream to its end, asynchronously.</summary>
    public static async Task<byte[]> ReadAllAsync(Stream stream, FontResourceLimits limits, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var buffer = new byte[GetInitialLength(stream, limits)];
        var length = 0;
        byte[]? next = null;
        while (true)
        {
            if (length == buffer.Length)
            {
                // The buffer is full, which is the end of a seekable stream: one more byte says whether to grow it.
                next ??= new byte[1];
                if (await stream.ReadAsync(next, cancellationToken).ConfigureAwait(false) == 0)
                    break;

                buffer = Grow(buffer, limits);
                buffer[length++] = next[0];
                EnsureEncodedBytes(limits, length);
                continue;
            }

            var read = await stream.ReadAsync(buffer.AsMemory(length), cancellationToken).ConfigureAwait(false);
            if (read == 0)
                break;

            length += read;
            EnsureEncodedBytes(limits, length);
        }

        return Trim(buffer, length);
    }

    /// <summary>Checks the number of bytes read from a font file.</summary>
    public static void EnsureEncodedBytes(FontResourceLimits limits, long length)
    {
        if (length > limits.MaxEncodedBytes)
            throw new FontResourceLimitException(FontResourceLimitKind.EncodedBytes, limits.MaxEncodedBytes, length);
    }

    private static int GetInitialLength(Stream stream, FontResourceLimits limits)
    {
        // The remaining length of a seekable stream avoids the copies of a growing buffer. It is only a hint: the
        // limits are enforced on the bytes actually read.
        if (stream.CanSeek)
        {
            var remaining = stream.Length - stream.Position;
            if (remaining > limits.MaxEncodedBytes)
                throw new FontResourceLimitException(FontResourceLimitKind.EncodedBytes, limits.MaxEncodedBytes, remaining);

            if (remaining is > 0 and < int.MaxValue)
                return (int)remaining;
        }

        return ChunkLength;
    }

    private static byte[] Grow(byte[] buffer, FontResourceLimits limits)
    {
        var length = Math.Min((long)buffer.Length * 2, Math.Min(limits.MaxEncodedBytes + 1, Array.MaxLength));
        if (length <= buffer.Length)
            throw new FontResourceLimitException(FontResourceLimitKind.EncodedBytes, limits.MaxEncodedBytes, (long)buffer.Length + 1);

        var result = new byte[length];
        buffer.CopyTo(result, 0);
        return result;
    }

    private static byte[] Trim(byte[] buffer, int length) => length == buffer.Length ? buffer : buffer.AsSpan(0, length).ToArray();
}
