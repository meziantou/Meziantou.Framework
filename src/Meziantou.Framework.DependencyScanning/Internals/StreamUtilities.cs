namespace Meziantou.Framework.DependencyScanning.Internals;

internal static class StreamUtilities
{
    public static StreamReader CreateReader(Stream stream, Encoding encoding)
    {
        return new StreamReader(stream, encoding, leaveOpen: true);
    }

    public static async ValueTask<StreamReader> CreateReaderAsync(Stream stream, CancellationToken token)
    {
        var encoding = await GetEncodingAsync(stream, token).ConfigureAwait(false);
        stream.Seek(0, SeekOrigin.Begin);

        return CreateReader(stream, encoding);
    }

    public static StreamWriter CreateWriter(Stream stream, Encoding encoding)
    {
        return new StreamWriter(stream, encoding, leaveOpen: true);
    }

    internal static async ValueTask<Encoding> GetEncodingAsync(Stream stream, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);

        // Read the BOM
        var bom = new byte[4];
        var readCount = await ReadUntilCountOrEndAsync(stream, bom.AsMemory(), cancellationToken).ConfigureAwait(false);
        var buffer = bom.AsSpan(0, readCount);

        // Analyze the BOM
        if (buffer.Length >= 3 && buffer[0] == 0x2b && buffer[1] == 0x2f && buffer[2] == 0x76)
#pragma warning disable SYSLIB0001 // Type or member is obsolete
            return Encoding.UTF7;
#pragma warning restore SYSLIB0001

        if (buffer.Length >= 3 && buffer[0] == 0xef && buffer[1] == 0xbb && buffer[2] == 0xbf)
            return Encoding.UTF8;

        if (buffer.Length >= 2 && buffer[0] == 0xff && buffer[1] == 0xfe)
            return Encoding.Unicode; //UTF-16LE

        if (buffer.Length >= 2 && buffer[0] == 0xfe && buffer[1] == 0xff)
            return Encoding.BigEndianUnicode; //UTF-16BE

        if (buffer.Length >= 4 && buffer[0] == 0 && buffer[1] == 0 && buffer[2] == 0xfe && buffer[3] == 0xff)
            return Encoding.UTF32;

        return Encoding.Default;
    }

    private static async ValueTask<int> ReadUntilCountOrEndAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.Slice(totalRead), cancellationToken).ConfigureAwait(false);
            if (read == 0)
                return totalRead;

            totalRead += read;
        }

        return totalRead;
    }
}
