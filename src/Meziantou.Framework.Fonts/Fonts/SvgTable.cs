using System.Buffers.Binary;
using System.IO.Compression;
using Meziantou.Framework.Hashing;

namespace Meziantou.Framework.Fonts;

/// <summary>Reads the <c>SVG </c> table: glyphs described by SVG documents.</summary>
/// <remarks>
/// OpenType specification, "SVG - Scalable vector graphics table". The table lists documents, each one for a range of
/// glyphs; a document has an element whose identifier is <c>glyph</c> followed by the glyph identifier for each glyph of
/// its range. A document is UTF-8 text, or that text compressed with gzip (RFC 1952). The records are validated when the
/// face is created; a document is expanded when it is asked for.
/// </remarks>
internal sealed class SvgTable
{
    private const int HeaderLength = 10;
    private const int RecordLength = 12;

    private readonly int _listOffset;
    private readonly int _count;

    private SvgTable(int listOffset, int count)
    {
        _listOffset = listOffset;
        _count = count;
    }

    /// <summary>Reads the header and validates the document records.</summary>
    /// <returns><see langword="null"/> when the table has a version that is not known or no record.</returns>
    /// <exception cref="InvalidFontException">The records are malformed: out of the table, not sorted or overlapping.</exception>
    public static SvgTable? Read(ReadOnlySpan<byte> table)
    {
        if (table.Length < HeaderLength || FontBinary.UInt16(table, 0) != 0)
            return null;

        var listOffset = FontBinary.Offset32(table, 2);
        if (listOffset == 0)
            throw FontErrors.InvalidTable(FontTags.Svg, "has no document list.");

        var count = FontBinary.UInt16(table, listOffset);
        if (count == 0)
            return null;

        // "Records must be sorted in increasing startGlyphID order", and the ranges do not overlap: a glyph has one document.
        var records = FontBinary.Slice(table, (long)listOffset + 2, (long)count * RecordLength);
        var previousEnd = -1;
        for (var i = 0; i < count; i++)
        {
            var record = records.Slice(i * RecordLength, RecordLength);
            var start = FontBinary.UInt16(record, 0);
            var end = FontBinary.UInt16(record, 2);
            if (end < start || start <= previousEnd)
                throw FontErrors.InvalidTable(FontTags.Svg, "has document records that are not sorted or that overlap.");

            var offset = FontBinary.UInt32(record, 4);
            var length = FontBinary.UInt32(record, 8);
            if (offset == 0 || length == 0)
                throw FontErrors.InvalidTable(FontTags.Svg, "has an empty document.");

            FontBinary.Slice(table, (long)listOffset + offset, length);
            previousEnd = end;
        }

        return new SvgTable(listOffset, count);
    }

    /// <summary>Finds the document of a glyph.</summary>
    /// <param name="table">The table.</param>
    /// <param name="tableOffset">The position of the table in the bytes of the font.</param>
    /// <param name="glyphId">The glyph.</param>
    /// <param name="glyph">Where the document of the glyph is.</param>
    /// <returns><see langword="false"/> when no document describes the glyph.</returns>
    public bool TryGetGlyph(ReadOnlySpan<byte> table, int tableOffset, int glyphId, out SvgGlyph glyph)
    {
        glyph = default;
        if ((uint)glyphId > ushort.MaxValue)
            return false;

        var low = 0;
        var high = _count - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var record = table.Slice(_listOffset + 2 + (middle * RecordLength), RecordLength);
            var start = BinaryPrimitives.ReadUInt16BigEndian(record);
            var end = BinaryPrimitives.ReadUInt16BigEndian(record[2..]);
            if (glyphId < start)
            {
                high = middle - 1;
            }
            else if (glyphId > end)
            {
                low = middle + 1;
            }
            else
            {
                // The records were validated when the table was read: the document is in the table.
                var offset = tableOffset + _listOffset + (int)BinaryPrimitives.ReadUInt32BigEndian(record[4..]);
                var length = (int)BinaryPrimitives.ReadUInt32BigEndian(record[8..]);
                var document = table.Slice(offset - tableOffset, length);
                glyph = new SvgGlyph(glyphId, start, end, offset, length, IsCompressed(document));
                return true;
            }
        }

        return false;
    }

    /// <summary>Tells whether a document is compressed: "the first three bytes of the gzip-encoded document header must be 0x1F, 0x8B, 0x08".</summary>
    private static bool IsCompressed(ReadOnlySpan<byte> document) => document is [0x1F, 0x8B, 0x08, ..];

    /// <summary>Gets the text of a document, expanded when it is compressed.</summary>
    /// <param name="document">The bytes of the document in the font.</param>
    /// <param name="documentMemory">The same bytes, returned as they are when the document is not compressed.</param>
    /// <param name="limits">The limits whose <see cref="FontResourceLimits.MaxFontBytes"/> bounds the expanded document.</param>
    /// <exception cref="InvalidFontException">The compressed document is malformed.</exception>
    /// <exception cref="FontResourceLimitException">The expanded document is larger than the limit.</exception>
    public static ReadOnlyMemory<byte> Decode(ReadOnlySpan<byte> document, ReadOnlyMemory<byte> documentMemory, FontResourceLimits limits)
    {
        if (!IsCompressed(document))
            return documentMemory;

        // RFC 1952: a member ends with the CRC-32 and the length (modulo 2^32) of the original data, little-endian. The
        // length sizes the destination, which the stream must then fill exactly.
        if (document.Length < 18)
            throw FontErrors.InvalidTable(FontTags.Svg, "has a compressed document that is truncated.");

        var expectedCrc = BinaryPrimitives.ReadUInt32LittleEndian(document[^8..]);
        var length = BinaryPrimitives.ReadUInt32LittleEndian(document[^4..]);
        if (length > limits.MaxFontBytes || length > Array.MaxLength)
            throw new FontResourceLimitException(FontResourceLimitKind.FontBytes, limits.MaxFontBytes, length);

        var destination = new byte[length];
        try
        {
            using var input = new MemoryStream(document.ToArray(), writable: false);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            var written = 0;
            while (written < destination.Length)
            {
                var read = gzip.Read(destination.AsSpan(written));
                if (read <= 0)
                    throw FontErrors.InvalidTable(FontTags.Svg, "has a compressed document that ends before its declared length.");

                written += read;
            }

            Span<byte> extra = stackalloc byte[1];
            if (gzip.Read(extra) != 0)
                throw FontErrors.InvalidTable(FontTags.Svg, "has a compressed document that is longer than its declared length.");
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException)
        {
            throw FontErrors.InvalidTable(FontTags.Svg, "has a malformed compressed document.", exception);
        }

        if (Crc32.Compute(destination) != expectedCrc)
            throw FontErrors.InvalidTable(FontTags.Svg, "has a compressed document that is corrupted.");

        return destination;
    }
}
