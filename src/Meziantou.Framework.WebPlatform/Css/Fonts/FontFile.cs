namespace Meziantou.Framework.WebPlatform.Css.Fonts;

/// <summary>The format of a font file.</summary>
internal enum FontFileFormat
{
    /// <summary>An OpenType or TrueType font (https://learn.microsoft.com/typography/opentype/spec/otff).</summary>
    OpenType,

    /// <summary>A font collection, <c>ttcf</c> (https://learn.microsoft.com/typography/opentype/spec/otff#font-collections).</summary>
    Collection,

    /// <summary>A WOFF 1.0 file (https://www.w3.org/TR/WOFF/).</summary>
    Woff,

    /// <summary>A WOFF 2.0 file (https://www.w3.org/TR/WOFF2/).</summary>
    Woff2,
}

/// <summary>
/// A font file decoded by the managed font reader: the sfnt data (an OpenType font or a font collection, after the WOFF or
/// WOFF2 decompression) and its font faces. A file whose structure is invalid is not loaded (<see cref="Load(ReadOnlySpan{byte})"/>
/// returns null), like the browsers that sanitize the web fonts before using them.
/// </summary>
/// <remarks>
/// The rasterizer of a rendering backend creates its typefaces from <see cref="Data"/> and the index of the face in the
/// collection (<see cref="OpenTypeFont.FaceIndex"/>), and draws the glyphs whose ids and positions are computed by the
/// managed shaper: the outlines are not parsed by the reader. The instances are immutable and can be shared by the
/// documents and the threads.
/// </remarks>
internal sealed class FontFile
{
    /// <summary>The maximum size of the decoded data of a WOFF or WOFF2 file (the largest fonts are about 50 MB).</summary>
    internal const int MaxDecodedLength = 128 * 1024 * 1024;

    /// <summary>The maximum number of faces of a collection.</summary>
    private const int MaxFaceCount = 1024;

    private readonly OpenTypeFont[] _faces;

    private FontFile(FontFileFormat format, byte[] data, int faceCount)
    {
        Format = format;
        Data = data;
        _faces = new OpenTypeFont[faceCount];
    }

    /// <summary>The format of the file that was loaded.</summary>
    public FontFileFormat Format { get; }

    /// <summary>The sfnt data: an OpenType font, or a font collection when <see cref="IsCollection"/> is true.</summary>
    public byte[] Data { get; }

    /// <summary>Whether <see cref="Data"/> is a font collection (a <c>ttcf</c> file, or a WOFF2 file of a collection).</summary>
    public bool IsCollection => FontBinary.U32(Data, 0) == OpenTypeTag.Collection;

    /// <summary>The font faces of the file (one, except for a collection).</summary>
    public IReadOnlyList<OpenTypeFont> Faces => _faces;

    /// <summary>Returns whether the data starts with the signature of a font file (it may still be invalid).</summary>
    public static bool HasFontSignature(ReadOnlySpan<byte> data) => FontBinary.U32(data, 0) is OpenTypeTag.TrueTypeVersion or OpenTypeTag.OpenTypeCff or OpenTypeTag.AppleTrueType or OpenTypeTag.Collection or OpenTypeTag.Woff or OpenTypeTag.Woff2;

    /// <summary>Loads a font file (OpenType, TrueType, a collection, WOFF or WOFF2); returns null when the data is not a valid font file.</summary>
    public static FontFile? Load(ReadOnlySpan<byte> data)
    {
        return FontBinary.U32(data, 0) switch
        {
            OpenTypeTag.Woff => WoffDecoder.Decode(data) is { } sfnt ? FromSfnt(sfnt, FontFileFormat.Woff) : null,
            OpenTypeTag.Woff2 => Woff2Decoder.Decode(data) is { } sfnt ? FromSfnt(sfnt, FontFileFormat.Woff2) : null,
            OpenTypeTag.TrueTypeVersion or OpenTypeTag.OpenTypeCff or OpenTypeTag.AppleTrueType or OpenTypeTag.Collection => FromSfnt(data.ToArray(), FontFileFormat.OpenType),
            _ => null,
        };
    }

    /// <summary>Loads a font file without copying it when it is an OpenType file: the array must not be modified afterwards.</summary>
    public static FontFile? Load(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return FontBinary.U32(data, 0) is OpenTypeTag.TrueTypeVersion or OpenTypeTag.OpenTypeCff or OpenTypeTag.AppleTrueType or OpenTypeTag.Collection
            ? FromSfnt(data, FontFileFormat.OpenType)
            : Load((ReadOnlySpan<byte>)data);
    }

    private static FontFile? FromSfnt(byte[] data, FontFileFormat format)
    {
        if (FontBinary.U32(data, 0) != OpenTypeTag.Collection)
        {
            var file = new FontFile(format, data, 1);
            return OpenTypeFont.TryCreate(file, 0, 0) is { } font ? file.SetFace(0, font) : null;
        }

        // https://learn.microsoft.com/typography/opentype/spec/otff#ttc-header
        var majorVersion = FontBinary.U16(data, 4);
        var faceCount = FontBinary.U32(data, 8);
        if (majorVersion is not (1 or 2) || faceCount is 0 or > MaxFaceCount || !FontBinary.IsInRange(data, 12, faceCount * 4))
            return null;

        var collection = new FontFile(format == FontFileFormat.OpenType ? FontFileFormat.Collection : format, data, (int)faceCount);
        for (var i = 0; i < (int)faceCount; i++)
        {
            if (OpenTypeFont.TryCreate(collection, FontBinary.U32(data, 12 + (i * 4)), i) is not { } face)
                return null;

            collection.SetFace(i, face);
        }

        return collection;
    }

    private FontFile SetFace(int index, OpenTypeFont font)
    {
        _faces[index] = font;
        return this;
    }
}

/// <summary>Writes the sfnt data of the tables decoded from a WOFF or WOFF2 file (https://learn.microsoft.com/typography/opentype/spec/otff#organization-of-an-opentype-font).</summary>
internal static class SfntWriter
{
    /// <summary>Writes a font whose table directory contains the tables, in the order of their tags.</summary>
    public static byte[] WriteFont(uint flavor, List<(uint Tag, ReadOnlyMemory<byte> Data)> tables)
    {
        tables.Sort(static (a, b) => a.Tag.CompareTo(b.Tag));
        var length = 12L + (16L * tables.Count);
        foreach (var (_, data) in tables)
        {
            length += Pad4(data.Length);
        }

        var output = new byte[length];
        var offset = 12 + (16 * tables.Count);
        WriteOffsetTable(output, 0, flavor, tables.Count);
        var headOffset = -1;
        for (var i = 0; i < tables.Count; i++)
        {
            var (tag, data) = tables[i];
            data.Span.CopyTo(output.AsSpan(offset));
            if (tag == OpenTypeTag.Head && data.Length >= 12)
            {
                // The checkSumAdjustment is computed when the whole font is written
                headOffset = offset;
                output.AsSpan(offset + 8, 4).Clear();
            }

            WriteTableRecord(output, 12 + (16 * i), tag, FontBinary.CalculateChecksum(output.AsSpan(offset, data.Length)), offset, data.Length);
            offset += Pad4(data.Length);
        }

        if (headOffset >= 0)
        {
            WriteU32(output, headOffset + 8, 0xB1B0AFBA - FontBinary.CalculateChecksum(output));
        }

        return output;
    }

    /// <summary>Writes a font collection: the table directory of each font lists the indexes of its tables, whose data is shared.</summary>
    public static byte[] WriteCollection(List<(uint Flavor, int[] Tables)> fonts, List<(uint Tag, ReadOnlyMemory<byte> Data)> tables)
    {
        // https://learn.microsoft.com/typography/opentype/spec/otff#ttc-header: version 1.0 (no DSIG table)
        var headerLength = 12 + (4 * fonts.Count);
        var length = (long)headerLength;
        foreach (var (_, fontTables) in fonts)
        {
            length += 12 + (16 * fontTables.Length);
        }

        var tableOffsets = new int[tables.Count];
        foreach (var (_, data) in tables)
        {
            length += Pad4(data.Length);
        }

        var output = new byte[length];
        WriteU32(output, 0, OpenTypeTag.Collection);
        WriteU32(output, 4, 0x00010000);
        WriteU32(output, 8, (uint)fonts.Count);
        var offset = headerLength;
        for (var i = 0; i < fonts.Count; i++)
        {
            WriteU32(output, 12 + (4 * i), (uint)offset);
            offset += 12 + (16 * fonts[i].Tables.Length);
        }

        for (var i = 0; i < tables.Count; i++)
        {
            tableOffsets[i] = offset;
            tables[i].Data.Span.CopyTo(output.AsSpan(offset));
            offset += Pad4(tables[i].Data.Length);
        }

        for (var i = 0; i < fonts.Count; i++)
        {
            var (flavor, fontTables) = fonts[i];
            var directory = (int)FontBinary.U32(output, 12 + (4 * i));
            var sorted = (int[])fontTables.Clone();
            Array.Sort(sorted, (a, b) => tables[a].Tag.CompareTo(tables[b].Tag));
            WriteOffsetTable(output, directory, flavor, sorted.Length);
            for (var j = 0; j < sorted.Length; j++)
            {
                var table = tables[sorted[j]];
                var tableOffset = tableOffsets[sorted[j]];
                WriteTableRecord(output, directory + 12 + (16 * j), table.Tag, FontBinary.CalculateChecksum(output.AsSpan(tableOffset, table.Data.Length)), tableOffset, table.Data.Length);
            }
        }

        return output;
    }

    private static int Pad4(int length) => (length + 3) & ~3;

    private static void WriteOffsetTable(byte[] output, int offset, uint flavor, int tableCount)
    {
        // https://learn.microsoft.com/typography/opentype/spec/otff#table-directory
        var entrySelector = tableCount == 0 ? 0 : 31 - System.Numerics.BitOperations.LeadingZeroCount((uint)tableCount);
        var searchRange = (1 << entrySelector) * 16;
        WriteU32(output, offset, flavor);
        WriteU16(output, offset + 4, (ushort)tableCount);
        WriteU16(output, offset + 6, (ushort)searchRange);
        WriteU16(output, offset + 8, (ushort)entrySelector);
        WriteU16(output, offset + 10, (ushort)((tableCount * 16) - searchRange));
    }

    private static void WriteTableRecord(byte[] output, int offset, uint tag, uint checksum, int tableOffset, int length)
    {
        WriteU32(output, offset, tag);
        WriteU32(output, offset + 4, checksum);
        WriteU32(output, offset + 8, (uint)tableOffset);
        WriteU32(output, offset + 12, (uint)length);
    }

    private static void WriteU16(byte[] output, int offset, ushort value) => System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(offset), value);

    private static void WriteU32(byte[] output, int offset, uint value) => System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(offset), value);
}
