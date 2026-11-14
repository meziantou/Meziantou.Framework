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
/// The blocks are copied from the file when it is read, still compressed, and counted with the font against
/// <see cref="FontResourceLimits.MaxFontBytes"/>. The metadata is expanded only when it is asked for, in a buffer rented
/// from the environment: they are not needed to use the font, so a block that lies outside a WOFF file does not prevent the font
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

    private WoffExtendedData(ReadOnlySpan<byte> file, BlockLocation metadata, uint metadataLength, BlockLocation privateData, bool isBrotli)
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

    /// <summary>The offset of the fields that locate the blocks in the header of a WOFF file.</summary>
    public const int WoffFieldsOffset = 24;

    /// <summary>The offset of the fields that locate the blocks in the header of a WOFF2 file.</summary>
    public const int Woff2FieldsOffset = 28;

    /// <summary>
    /// Gets the bytes the font keeps for the blocks: the blocks that lie inside the file, still compressed. They are
    /// counted with the font against <see cref="FontResourceLimits.MaxFontBytes"/> before anything is allocated.
    /// </summary>
    /// <param name="fields">The five fields of the header that locate the blocks.</param>
    /// <param name="fileLength">The length of the file.</param>
    public static long GetRetainedLength(ReadOnlySpan<byte> fields, long fileLength)
    {
        var (metadata, metadataLength, privateData) = ReadLocations(fields);
        long length = 0;
        if (metadata.Length != 0 && metadataLength != 0 && metadata.IsInside(fileLength))
        {
            length += metadata.Length;
        }

        if (privateData.Length != 0 && privateData.IsInside(fileLength))
        {
            length += privateData.Length;
        }

        return length;
    }

    /// <summary>Copies the blocks of a WOFF file, at the locations the header gives.</summary>
    /// <param name="file">The bytes of the file.</param>
    /// <param name="fields">The five fields of the header that locate the blocks: metaOffset, metaLength, metaOrigLength, privOffset and privLength.</param>
    /// <param name="isBrotli">Whether the metadata is compressed with Brotli (WOFF2) instead of zlib (WOFF).</param>
    public static WoffExtendedData? Create(ReadOnlySpan<byte> file, ReadOnlySpan<byte> fields, bool isBrotli)
    {
        var (metadata, metadataLength, privateData) = ReadLocations(fields);

        // A block is absent when its length is zero; the specifications also set its offset to zero.
        if (metadata.Length == 0 && privateData.Length == 0)
            return null;

        return new WoffExtendedData(file, metadata, metadataLength, privateData, isBrotli);
    }

    private static (BlockLocation Metadata, uint MetadataLength, BlockLocation PrivateData) ReadLocations(ReadOnlySpan<byte> fields)
    {
        var metadata = new BlockLocation(BinaryPrimitives.ReadUInt32BigEndian(fields), BinaryPrimitives.ReadUInt32BigEndian(fields[4..]));
        var metadataLength = BinaryPrimitives.ReadUInt32BigEndian(fields[8..]);
        var privateData = new BlockLocation(BinaryPrimitives.ReadUInt32BigEndian(fields[12..]), BinaryPrimitives.ReadUInt32BigEndian(fields[16..]));
        return (metadata, metadataLength, privateData);
    }

    /// <summary>Expands the metadata document, in a buffer rented from the environment.</summary>
    public string? GetMetadata(FontEnvironment environment)
    {
        if (_metadataError is not null)
            throw FontErrors.Invalid(_metadataError);

        if (_metadata is null)
            return null;

        var limits = environment.Limits;
        if (_metadataLength > limits.MaxFontBytes)
            throw new FontResourceLimitException(FontResourceLimitKind.FontBytes, limits.MaxFontBytes, _metadataLength);

        var scope = environment.CreateAllocator("WOFF metadata");
        if (_metadataLength > Array.MaxLength)
            throw new FontResourceLimitException(FontResourceLimitKind.LiveAllocationBytes, scope.Limit, _metadataLength);

        using var buffer = scope.Rent((int)_metadataLength, clear: false);
        var document = buffer.Span[..(int)_metadataLength];
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
