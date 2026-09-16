namespace Meziantou.Framework.DependencyScanning.Internals;

internal static class StreamUtilities
{
    private static readonly Encoding BigEndianUTF32 = new UTF32Encoding(bigEndian: true, byteOrderMark: true);

    public static StreamReader CreateReader(Stream stream, Encoding encoding)
    {
        return new StreamReader(stream, encoding, leaveOpen: true);
    }

    /// <summary>Creates a reader for scanning a file.</summary>
    /// <remarks>
    /// Reading is tolerant: a file that is not valid in the detected encoding is decoded with replacement characters,
    /// so a scan never fails because of it. Use <see cref="ReadForUpdateAsync"/> to read a file that will be rewritten.
    /// </remarks>
    public static async ValueTask<StreamReader> CreateReaderAsync(Stream stream, CancellationToken token)
    {
        var encoding = await GetEncodingAsync(stream, token).ConfigureAwait(false);
        stream.Seek(0, SeekOrigin.Begin);

        return CreateReader(stream, encoding);
    }

    internal static async ValueTask<Encoding> GetEncodingAsync(Stream stream, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);

        // Read the BOM
        var bom = new byte[4];
        var readCount = await ReadUntilCountOrEndAsync(stream, bom, cancellationToken).ConfigureAwait(false);

        return DetectBom(bom.AsSpan(0, readCount)) switch
        {
            BomKind.Utf8 => Encoding.UTF8,
            BomKind.Utf16LittleEndian => Encoding.Unicode,
            BomKind.Utf16BigEndian => Encoding.BigEndianUnicode,
            BomKind.Utf32LittleEndian => Encoding.UTF32,
            BomKind.Utf32BigEndian => BigEndianUTF32,
            _ => Encoding.Default,
        };
    }

    /// <summary>Reads a whole file that is about to be rewritten, refusing to decode anything that would not survive the round trip.</summary>
    /// <param name="stream">The stream to read from its start.</param>
    /// <param name="isXml">When the file has no BOM, honor the encoding its XML declaration states.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <exception cref="DependencyScannerException">The content is not valid in its encoding, or the encoding is not supported.</exception>
    public static async Task<FileText> ReadForUpdateAsync(Stream stream, bool isXml, CancellationToken cancellationToken)
    {
        stream.Seek(0, SeekOrigin.Begin);
        using var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream, cancellationToken).ConfigureAwait(false);
        var bytes = memoryStream.GetBuffer().AsMemory(0, (int)memoryStream.Length);

        var (encoding, preambleLength) = DetectBom(bytes.Span) switch
        {
            BomKind.Utf8 => (CreateStrictUtf8Encoding(), 3),
            BomKind.Utf16LittleEndian => (new UnicodeEncoding(bigEndian: false, byteOrderMark: false, throwOnInvalidBytes: true), 2),
            BomKind.Utf16BigEndian => (new UnicodeEncoding(bigEndian: true, byteOrderMark: false, throwOnInvalidBytes: true), 2),
            BomKind.Utf32LittleEndian => (new UTF32Encoding(bigEndian: false, byteOrderMark: false, throwOnInvalidCharacters: true), 4),
            BomKind.Utf32BigEndian => ((Encoding)new UTF32Encoding(bigEndian: true, byteOrderMark: false, throwOnInvalidCharacters: true), 4),
            _ => ((isXml ? GetXmlDeclaredEncoding(bytes.Span) : null) ?? CreateStrictUtf8Encoding(), 0),
        };

        string text;
        try
        {
            text = encoding.GetString(bytes.Span[preambleLength..]);
        }
        catch (DecoderFallbackException ex)
        {
            throw new DependencyScannerException($"The file is not valid '{encoding.WebName}' text. Updating it would corrupt the bytes that cannot be decoded.", ex);
        }

        return new FileText(text, encoding, bytes[..preambleLength].ToArray());
    }

    /// <summary>Replaces the content of a file read by <see cref="ReadForUpdateAsync"/>, keeping its encoding and its BOM.</summary>
    /// <remarks>
    /// Everything that can fail or be cancelled happens before the file is touched. The new content is then written in a
    /// single operation that cannot be cancelled, so the file is never left empty or half-written by a cancellation.
    /// An I/O error while writing, such as a full disk, can still leave the file partially written.
    /// </remarks>
    /// <exception cref="DependencyScannerException">The new content cannot be represented in the file's encoding.</exception>
    public static async Task WriteForUpdateAsync(Stream stream, FileText original, string text, CancellationToken cancellationToken)
    {
        byte[] buffer;
        try
        {
            var byteCount = original.Encoding.GetByteCount(text);
            buffer = new byte[original.Preamble.Length + byteCount];
            original.Preamble.CopyTo(buffer, 0);
            original.Encoding.GetBytes(text, buffer.AsSpan(original.Preamble.Length));
        }
        catch (EncoderFallbackException ex)
        {
            throw new DependencyScannerException($"The new content cannot be written as '{original.Encoding.WebName}'.", ex);
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Overwrite in place and truncate afterward: when the content does not grow, no new space has to be allocated
        stream.Seek(0, SeekOrigin.Begin);
        await stream.WriteAsync(buffer, CancellationToken.None).ConfigureAwait(false);
        stream.SetLength(buffer.Length);
        await stream.FlushAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private static UTF8Encoding CreateStrictUtf8Encoding() => new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static Encoding? GetXmlDeclaredEncoding(ReadOnlySpan<byte> bytes)
    {
        var name = XmlUtilities.GetDeclaredEncodingName(bytes);
        if (name is null)
            return null;

        Encoding encoding;
        try
        {
            encoding = Encoding.GetEncoding(name, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        }
        catch (ArgumentException ex)
        {
            throw new DependencyScannerException($"The encoding '{name}' declared by the XML file is not supported.", ex);
        }

        // The declaration was read as ASCII, so the bytes cannot be UTF-16 or UTF-32. An XML reader rejects such a file as well.
        if (encoding is UnicodeEncoding or UTF32Encoding)
            throw new DependencyScannerException($"The XML file declares the encoding '{name}' but has no byte order mark.");

        return encoding;
    }

    private static BomKind DetectBom(ReadOnlySpan<byte> buffer)
    {
        if (buffer is [0xef, 0xbb, 0xbf, ..])
            return BomKind.Utf8;

        // The UTF-32 BOMs must be tested before the UTF-16 ones: the UTF-32LE BOM starts with the UTF-16LE BOM
        if (buffer is [0xff, 0xfe, 0x00, 0x00, ..])
            return BomKind.Utf32LittleEndian;

        if (buffer is [0x00, 0x00, 0xfe, 0xff, ..])
            return BomKind.Utf32BigEndian;

        if (buffer is [0xff, 0xfe, ..])
            return BomKind.Utf16LittleEndian;

        if (buffer is [0xfe, 0xff, ..])
            return BomKind.Utf16BigEndian;

        // UTF-7 is deliberately not detected. "+/v" is ordinary ASCII text, and a UTF-7 file only contains ASCII bytes,
        // so reading it as UTF-8 still round-trips it byte for byte.
        return BomKind.None;
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

    /// <summary>The decoded content of a file, and what is needed to write it back the same way.</summary>
    internal sealed record FileText(string Text, Encoding Encoding, byte[] Preamble);

    private enum BomKind
    {
        None,
        Utf8,
        Utf16LittleEndian,
        Utf16BigEndian,
        Utf32LittleEndian,
        Utf32BigEndian,
    }
}
