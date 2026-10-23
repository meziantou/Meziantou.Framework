using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;

namespace Meziantou.Framework.Fonts;

/// <summary>
/// Subsets the outlines of a font to the glyphs a document uses, keeping the glyph identifiers: the glyphs that are not
/// kept are empty, so that what refers to a glyph by its identifier (a PDF content stream, a shaped text) is unchanged.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>
/// TrueType outlines (<see cref="SubsetTrueType"/>): an sfnt with the <c>glyf</c> and <c>loca</c> tables rebuilt from the
/// kept glyphs and the components of the composite glyphs among them, the metrics tables as they are, the
/// instructions on request, and a <c>post</c> table without glyph names.
/// </description></item>
/// <item><description>
/// CFF outlines (<see cref="SubsetCff"/>): a CID-keyed <c>CFF</c> table whose CIDs are the glyph identifiers (OpenType
/// specification, "CFF"; Adobe Technical Note #5176): the charstrings of the glyphs that are not kept are
/// <c>endchar</c>, the subroutines are kept whole.
/// </description></item>
/// </list>
/// </remarks>
[SuppressMessage("Design", "MA0182:Unused internal type", Justification = "Consumed by Meziantou.Framework.Pdf, which embeds the subsets of the fonts, and covered by its tests.")]
internal static class FontSubsetter
{
    private const uint Cvt = ((uint)'c' << 24) | ((uint)'v' << 16) | ((uint)'t' << 8) | ' ';
    private const uint Fpgm = ((uint)'f' << 24) | ((uint)'p' << 16) | ((uint)'g' << 8) | 'm';
    private const uint Prep = ((uint)'p' << 24) | ((uint)'r' << 16) | ((uint)'e' << 8) | 'p';

    // The flags of the components of a composite glyph: OpenType specification, "glyf", "Composite glyph description"
    private const int ArgumentsAreWords = 0x0001;
    private const int HasScale = 0x0008;
    private const int MoreComponents = 0x0020;
    private const int HasXAndYScale = 0x0040;
    private const int HasTwoByTwo = 0x0080;

    // The components a glyph may reference through every level of composites
    private const int MaxComponents = 65536;

    /// <summary>Gets the glyphs a subset keeps: the requested glyphs, the glyph 0 (<c>.notdef</c>) and the components of the composite glyphs.</summary>
    /// <param name="face">The face.</param>
    /// <param name="glyphs">The glyphs a document uses; the identifiers that are not glyphs of the face are ignored.</param>
    /// <returns>The glyphs, sorted.</returns>
    public static SortedSet<int> GetClosure(FontFace face, IEnumerable<int> glyphs)
    {
        ArgumentNullException.ThrowIfNull(face);
        ArgumentNullException.ThrowIfNull(glyphs);
        var result = new SortedSet<int> { 0 };
        foreach (var glyph in glyphs)
        {
            if (glyph >= 0 && glyph < face.GlyphCount)
            {
                result.Add(glyph);
            }
        }

        if (face.Outlines != FontOutlineKind.TrueType || !face.TryGetTable(FontTags.Glyf, out var glyf) || !TryReadLoca(face, out var loca))
            return result;

        var pending = new Stack<int>(result);
        var components = 0;
        while (pending.Count > 0)
        {
            var glyph = pending.Pop();
            var data = GetGlyphData(glyf.Span, loca, glyph);
            if (data.Length < 10 || BinaryPrimitives.ReadInt16BigEndian(data) >= 0)
                continue;

            foreach (var component in EnumerateComponents(data))
            {
                if (++components > MaxComponents)
                    throw FontErrors.InvalidTable(FontTags.Glyf, "has composite glyphs that reference more components than the limit.");

                if (component < face.GlyphCount && result.Add(component))
                {
                    pending.Push(component);
                }
            }
        }

        return result;
    }

    /// <summary>Subsets a font with TrueType outlines.</summary>
    /// <param name="face">The face: its outlines are TrueType outlines.</param>
    /// <param name="glyphs">The glyphs to keep; the closure (<see cref="GetClosure"/>) is computed.</param>
    /// <param name="keepHinting">Whether to keep the instructions (<c>cvt</c>, <c>fpgm</c>, <c>prep</c> and the instructions of the glyphs).</param>
    /// <returns>The font file.</returns>
    /// <exception cref="ArgumentException">The face does not have TrueType outlines.</exception>
    /// <exception cref="InvalidFontException">A table the subset needs is missing or malformed.</exception>
    public static byte[] SubsetTrueType(FontFace face, IEnumerable<int> glyphs, bool keepHinting = true)
    {
        ArgumentNullException.ThrowIfNull(face);
        if (face.Outlines != FontOutlineKind.TrueType)
            throw new ArgumentException("The face does not have TrueType outlines", nameof(face));

        var closure = GetClosure(face, glyphs);
        if (!face.TryGetTable(FontTags.Glyf, out var glyfTable) || !TryReadLoca(face, out var loca))
            throw FontErrors.Invalid("the 'glyf' or the 'loca' table is missing.");

        var glyf = glyfTable.Span;
        var glyphCount = face.GlyphCount;
        var newGlyf = new List<byte>();
        var newLoca = new byte[(glyphCount + 1) * 4];
        for (var glyph = 0; glyph < glyphCount; glyph++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(newLoca.AsSpan(glyph * 4), (uint)newGlyf.Count);
            if (!closure.Contains(glyph))
                continue;

            var data = GetGlyphData(glyf, loca, glyph);
            if (data.Length == 0)
                continue;

            // The glyphs of a valid font do not overlap: the subset is not larger than the table, with the padding
            if (newGlyf.Count + (long)data.Length > glyf.Length + (3L * closure.Count))
                throw FontErrors.InvalidTable(FontTags.Glyf, "has glyphs that overlap.");

            newGlyf.AddRange(keepHinting ? data : RemoveInstructions(data));
            while (newGlyf.Count % 4 != 0)
            {
                newGlyf.Add(0);
            }
        }

        BinaryPrimitives.WriteUInt32BigEndian(newLoca.AsSpan(glyphCount * 4), (uint)newGlyf.Count);

        var tables = new SortedDictionary<uint, byte[]>
        {
            [FontTags.Glyf] = [.. newGlyf],
            [FontTags.Loca] = newLoca,
        };
        foreach (var tag in new[] { FontTags.Head, FontTags.Hhea, FontTags.Maxp, FontTags.Hmtx })
        {
            if (!face.TryGetTable(tag, out var table))
                throw FontErrors.Invalid($"the '{FontTags.Format(tag)}' table is missing.");

            tables[tag] = table.ToArray();
        }

        // The offsets of loca are 32 bits; the checksum adjustment is computed once the font is written
        var head = tables[FontTags.Head];
        if (head.Length < 54)
            throw FontErrors.InvalidTable(FontTags.Head, "is too short.");

        BinaryPrimitives.WriteInt16BigEndian(head.AsSpan(50), 1);
        BinaryPrimitives.WriteUInt32BigEndian(head.AsSpan(8), 0);

        tables[FontTags.Post] = CreatePost(face);
        foreach (var tag in keepHinting ? new[] { FontTags.Os2, FontTags.Name, Cvt, Fpgm, Prep } : [FontTags.Os2, FontTags.Name])
        {
            if (face.TryGetTable(tag, out var table))
            {
                tables[tag] = table.ToArray();
            }
        }

        if (!keepHinting)
        {
            // maxp version 1.0 describes the instructions: there are none left
            var maxp = tables[FontTags.Maxp];
            if (maxp.Length >= 32 && BinaryPrimitives.ReadUInt32BigEndian(maxp) == 0x00010000)
            {
                // maxTwilightPoints to maxSizeOfInstructions; the zones and the components are kept
                maxp.AsSpan(16, 12).Clear();
            }
        }

        return WriteSfnt(FontTags.TrueType, tables);
    }

    /// <summary>Subsets the <c>CFF</c> table of a font into a CID-keyed CFF table whose CIDs are the glyph identifiers.</summary>
    /// <param name="face">The face: its outlines are CFF outlines.</param>
    /// <param name="glyphs">The glyphs to keep; the glyph 0 is always kept.</param>
    /// <param name="fontName">The name of the font in the table.</param>
    /// <returns>The CFF table.</returns>
    /// <exception cref="ArgumentException">The face does not have CFF outlines.</exception>
    /// <exception cref="InvalidFontException">The CFF table is malformed.</exception>
    public static byte[] SubsetCff(FontFace face, IEnumerable<int> glyphs, string fontName)
    {
        ArgumentNullException.ThrowIfNull(face);
        ArgumentNullException.ThrowIfNull(fontName);
        if (face.Outlines != FontOutlineKind.Cff || !face.TryGetTable(FontTags.Cff, out var table))
            throw new ArgumentException("The face does not have CFF outlines", nameof(face));

        return CffSubsetWriter.Write(table.Span, GetClosure(face, glyphs), fontName);
    }

    /// <summary>Writes an sfnt from its tables, sorted by tag, and sets the checksum adjustment of its <c>head</c> table.</summary>
    internal static byte[] WriteSfnt(uint version, SortedDictionary<uint, byte[]> tables)
    {
        var directoryLength = SfntWriter.GetDirectoryLength(tables.Count);
        var length = directoryLength + tables.Values.Sum(t => SfntWriter.Pad(t.Length));
        var output = new byte[length];
        SfntWriter.WriteHeader(output, version, tables.Count);
        var offset = directoryLength;
        var record = SfntWriter.HeaderLength;
        long headOffset = -1;
        foreach (var (tag, data) in tables)
        {
            data.CopyTo(output.AsSpan((int)offset));
            SfntWriter.WriteRecord(output.AsSpan(record), tag, Checksum(output.AsSpan((int)offset, (int)SfntWriter.Pad(data.Length))), offset, data.Length);
            if (tag == FontTags.Head)
            {
                headOffset = offset;
            }

            offset += SfntWriter.Pad(data.Length);
            record += SfntWriter.RecordLength;
        }

        if (headOffset >= 0)
        {
            BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan((int)headOffset + 8), 0xB1B0AFBA - Checksum(output));
        }

        return output;
    }

    private static uint Checksum(ReadOnlySpan<byte> data)
    {
        uint sum = 0;
        var i = 0;
        for (; i + 4 <= data.Length; i += 4)
        {
            sum += BinaryPrimitives.ReadUInt32BigEndian(data[i..]);
        }

        if (i < data.Length)
        {
            Span<byte> last = stackalloc byte[4];
            data[i..].CopyTo(last);
            sum += BinaryPrimitives.ReadUInt32BigEndian(last);
        }

        return sum;
    }

    /// <summary>Creates a <c>post</c> table of version 3: the fields of the original table, without glyph names.</summary>
    private static byte[] CreatePost(FontFace face)
    {
        var post = new byte[32];
        if (face.TryGetTable(FontTags.Post, out var original) && original.Length >= 32)
        {
            original.Span[..32].CopyTo(post);
        }

        BinaryPrimitives.WriteUInt32BigEndian(post, 0x00030000);
        return post;
    }

    private static bool TryReadLoca(FontFace face, out (ReadOnlyMemory<byte> Table, bool IsLong) loca)
    {
        loca = default;
        if (!face.TryGetTable(FontTags.Loca, out var table) || !face.TryGetTable(FontTags.Head, out var head) || head.Length < 54)
            return false;

        loca = (table, BinaryPrimitives.ReadInt16BigEndian(head.Span[50..]) != 0);
        return true;
    }

    /// <summary>Gets the data of a glyph; an entry of <c>loca</c> that is out of order or out of the table gives an empty glyph.</summary>
    private static ReadOnlySpan<byte> GetGlyphData(ReadOnlySpan<byte> glyf, (ReadOnlyMemory<byte> Table, bool IsLong) loca, int glyph)
    {
        var table = loca.Table.Span;
        long start;
        long end;
        if (loca.IsLong)
        {
            if ((glyph + 2L) * 4 > table.Length)
                return default;

            start = BinaryPrimitives.ReadUInt32BigEndian(table[(glyph * 4)..]);
            end = BinaryPrimitives.ReadUInt32BigEndian(table[((glyph + 1) * 4)..]);
        }
        else
        {
            if ((glyph + 2L) * 2 > table.Length)
                return default;

            start = BinaryPrimitives.ReadUInt16BigEndian(table[(glyph * 2)..]) * 2L;
            end = BinaryPrimitives.ReadUInt16BigEndian(table[((glyph + 1) * 2)..]) * 2L;
        }

        if (end <= start || end > glyf.Length)
            return default;

        return glyf[(int)start..(int)end];
    }

    /// <summary>Enumerates the glyphs the components of a composite glyph reference.</summary>
    private static List<int> EnumerateComponents(ReadOnlySpan<byte> data)
    {
        var result = new List<int>();
        var position = 10;
        while (position + 4 <= data.Length)
        {
            var flags = BinaryPrimitives.ReadUInt16BigEndian(data[position..]);
            result.Add(BinaryPrimitives.ReadUInt16BigEndian(data[(position + 2)..]));
            position += 4 + ((flags & ArgumentsAreWords) != 0 ? 4 : 2);
            if ((flags & HasScale) != 0)
            {
                position += 2;
            }
            else if ((flags & HasXAndYScale) != 0)
            {
                position += 4;
            }
            else if ((flags & HasTwoByTwo) != 0)
            {
                position += 8;
            }

            if ((flags & MoreComponents) == 0)
                break;
        }

        return result;
    }

    /// <summary>Removes the instructions of a simple glyph (the composite glyphs keep theirs: their flags say whether they have some).</summary>
    private static byte[] RemoveInstructions(ReadOnlySpan<byte> data)
    {
        if (data.Length < 12)
            return data.ToArray();

        var contours = BinaryPrimitives.ReadInt16BigEndian(data);
        if (contours < 0)
            return data.ToArray();

        // header, end points of the contours, then the length of the instructions and the instructions
        var instructionsLengthOffset = 10 + (contours * 2);
        if (instructionsLengthOffset + 2 > data.Length)
            return data.ToArray();

        var instructionsLength = BinaryPrimitives.ReadUInt16BigEndian(data[instructionsLengthOffset..]);
        var instructionsEnd = instructionsLengthOffset + 2 + instructionsLength;
        if (instructionsEnd > data.Length)
            return data.ToArray();

        var result = new byte[data.Length - instructionsLength];
        data[..instructionsLengthOffset].CopyTo(result);
        data[instructionsEnd..].CopyTo(result.AsSpan(instructionsLengthOffset + 2));
        return result;
    }
}
