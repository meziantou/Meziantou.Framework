using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using Meziantou.Framework.Hashing;

namespace Meziantou.Framework.Fonts;

/// <summary>The extended metadata block and the private data block of a WOFF or WOFF2 file.</summary>
/// <remarks>
/// <para>
/// W3C Recommendations "WOFF File Format 1.0" (sections 7 and 8) and "WOFF File Format 2.0" (sections 6 and 7). The
/// metadata is an XML document in UTF-8, compressed with zlib in a WOFF file and with Brotli in a WOFF2 file; the
/// private data is opaque to the font.
/// </para>
/// <para>
/// The blocks are copied from the file when it is read, still compressed, and the metadata is expanded only when it is
/// asked for: they are not needed to use the font, so a block that lies outside a WOFF file does not prevent the font
/// from loading, and is reported when it is read.
/// </para>
/// </remarks>
internal sealed class WoffExtendedData
{
    private readonly byte[]? _metadata;
    private readonly long _metadataLength;
    private readonly bool _isBrotli;
    private readonly string? _metadataError;
    private readonly byte[]? _privateData;
    private readonly string? _privateDataError;

    private WoffExtendedData(ReadOnlySpan<byte> file, BlockLocation metadata, long metadataLength, BlockLocation privateData, bool isBrotli)
    {
        _isBrotli = isBrotli;
        if (metadata.Length != 0)
        {
            if (!metadata.IsInside(file.Length))
            {
                _metadataError = "the metadata block of the WOFF file lies outside the file.";
            }
            else if (metadataLength == 0)
            {
                _metadataError = "the metadata block of the WOFF file declares an empty document.";
            }
            else
            {
                _metadata = file.Slice((int)metadata.Offset, (int)metadata.Length).ToArray();
                _metadataLength = metadataLength;
            }
        }

        if (privateData.Length != 0)
        {
            if (privateData.IsInside(file.Length))
            {
                _privateData = file.Slice((int)privateData.Offset, (int)privateData.Length).ToArray();
            }
            else
            {
                _privateDataError = "the private data block of the WOFF file lies outside the file.";
            }
        }
    }

    /// <summary>Reads the location of the blocks from the header of a WOFF file, at offset 24.</summary>
    public static WoffExtendedData? ReadWoff(ReadOnlySpan<byte> file, ReadOnlySpan<byte> header) => Create(file, header[24..], isBrotli: false);

    /// <summary>Reads the location of the blocks from the header of a WOFF2 file, at offset 28.</summary>
    public static WoffExtendedData? ReadWoff2(ReadOnlySpan<byte> file, ReadOnlySpan<byte> header) => Create(file, header[28..], isBrotli: true);

    /// <summary>Reads the five fields of the header that locate the blocks: metaOffset, metaLength, metaOrigLength, privOffset and privLength.</summary>
    private static WoffExtendedData? Create(ReadOnlySpan<byte> file, ReadOnlySpan<byte> fields, bool isBrotli)
    {
        var metadata = new BlockLocation(BinaryPrimitives.ReadUInt32BigEndian(fields), BinaryPrimitives.ReadUInt32BigEndian(fields[4..]));
        var metadataLength = BinaryPrimitives.ReadUInt32BigEndian(fields[8..]);
        var privateData = new BlockLocation(BinaryPrimitives.ReadUInt32BigEndian(fields[12..]), BinaryPrimitives.ReadUInt32BigEndian(fields[16..]));

        // A block is absent when its length is zero; the specifications also set its offset to zero.
        if (metadata.Length == 0 && privateData.Length == 0)
            return null;

        return new WoffExtendedData(file, metadata, metadataLength, privateData, isBrotli);
    }

    /// <summary>Expands the metadata document.</summary>
    public string? GetMetadata(FontResourceLimits limits)
    {
        if (_metadataError is not null)
            throw FontErrors.Invalid(_metadataError);

        if (_metadata is null)
            return null;

        if (_metadataLength > limits.MaxFontBytes)
            throw new FontResourceLimitException(FontResourceLimitKind.FontBytes, limits.MaxFontBytes, _metadataLength);

        if (_metadataLength > Array.MaxLength)
            throw new FontResourceLimitException(FontResourceLimitKind.FontBytes, Array.MaxLength, _metadataLength);

        var document = new byte[_metadataLength];
        if (_isBrotli)
        {
            Unbrotli(_metadata, document);
        }
        else
        {
            Inflate(_metadata, document);
        }

        // The document is UTF-8; a byte order mark is not part of its text.
        ReadOnlySpan<byte> text = document;
        if (text.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            text = text[3..];
        }

        return Encoding.UTF8.GetString(text);
    }

    /// <summary>Gets the private data block.</summary>
    public ReadOnlyMemory<byte> GetPrivateData()
    {
        if (_privateDataError is not null)
            throw FontErrors.Invalid(_privateDataError);

        return _privateData;
    }

    private static void Inflate(byte[] compressed, Span<byte> destination)
    {
        try
        {
            using var input = new MemoryStream(compressed, writable: false);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            ReadExactly(zlib, destination);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException)
        {
            throw new InvalidFontException("The font is invalid: the metadata block of the WOFF file has malformed compressed data.", exception);
        }

        // ZLibStream reports a stream cut before its checksum as a clean end (RFC 1950).
        if (compressed.Length < 6 || Adler32.Compute(destination) != BinaryPrimitives.ReadUInt32BigEndian(compressed.AsSpan(compressed.Length - 4)))
            throw FontErrors.Invalid("the metadata block of the WOFF file has compressed data that is truncated or corrupted.");
    }

    private static void Unbrotli(byte[] compressed, Span<byte> destination)
    {
        try
        {
            using var input = new MemoryStream(compressed, writable: false);
            using var brotli = new BrotliStream(input, CompressionMode.Decompress);
            ReadExactly(brotli, destination);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or InvalidOperationException)
        {
            // BrotliStream reports malformed data as an InvalidOperationException.
            throw new InvalidFontException("The font is invalid: the metadata block of the WOFF2 file has malformed compressed data.", exception);
        }
    }

    /// <summary>Reads a stream that must expand to exactly the length of the destination.</summary>
    private static void ReadExactly(Stream stream, Span<byte> destination)
    {
        var written = 0;
        while (written < destination.Length)
        {
            var read = stream.Read(destination[written..]);
            if (read <= 0)
                throw FontErrors.Invalid("the metadata block of the WOFF file expands to less than its declared length.");

            written += read;
        }

        Span<byte> extra = stackalloc byte[1];
        if (stream.Read(extra) != 0)
            throw FontErrors.Invalid("the metadata block of the WOFF file expands to more than its declared length.");
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct BlockLocation(uint Offset, uint Length)
    {
        public bool IsInside(long fileLength) => Offset + (long)Length <= fileLength;
    }
}
