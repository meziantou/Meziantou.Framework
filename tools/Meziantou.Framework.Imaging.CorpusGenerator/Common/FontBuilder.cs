using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Meziantou.Framework.Imaging.CorpusGenerator.Common;

/// <summary>
/// Builds OpenType fonts with TrueType outlines from a description of their glyphs, for the fixtures and the tests of
/// the text rendering of Meziantou.Framework.Imaging.
/// </summary>
/// <remarks>
/// <para>
/// The file is compiled into the corpus generator and into the test projects, and has no dependency on the library
/// under test: a font written here is an independent statement of what the tables contain. It is written from the
/// OpenType specification (ISO/IEC 14496-22) and the WOFF 1.0 recommendation.
/// </para>
/// <para>
/// The output is deterministic: tables are written in the order of their tags, the dates are zero, and nothing depends
/// on the platform.
/// </para>
/// </remarks>
internal sealed class FontBuilder
{
    /// <summary>Gets or sets the number of font units per em (<c>head.unitsPerEm</c>).</summary>
    /// <summary>Gets or sets the signature of the file: 0x00010000 for TrueType outlines, 'OTTO' for CFF outlines.</summary>
    public uint SfntVersion { get; set; } = 0x00010000;

    public int UnitsPerEm { get; set; } = 1024;

    public string FamilyName { get; set; } = "Meziantou Test";

    public string SubfamilyName { get; set; } = "Regular";

    /// <summary>Gets or sets the typographic family name (name 16), when it differs from <see cref="FamilyName"/>.</summary>
    public string? TypographicFamilyName { get; set; }

    /// <summary>Gets or sets the typographic subfamily name (name 17).</summary>
    public string? TypographicSubfamilyName { get; set; }

    public string? PostScriptName { get; set; } = "MeziantouTest-Regular";

    /// <summary>Gets or sets a value indicating whether the names are also written for the Macintosh platform, in MacRoman.</summary>
    public bool WriteMacintoshNames { get; set; }

    /// <summary>Gets or sets a value indicating whether the Windows names are omitted, leaving only the Macintosh ones.</summary>
    public bool OmitWindowsNames { get; set; }

    public int WeightClass { get; set; } = 400;

    public bool IsItalic { get; set; }

    public bool IsBold { get; set; }

    public bool IsMonospaced { get; set; }

    // hhea
    public int Ascender { get; set; } = 800;

    public int Descender { get; set; } = -200;

    public int LineGap { get; set; }

    // OS/2
    public int TypoAscender { get; set; } = 800;

    public int TypoDescender { get; set; } = -200;

    public int TypoLineGap { get; set; }

    public int WinAscent { get; set; } = 800;

    public int WinDescent { get; set; } = 200;

    /// <summary>Gets or sets the USE_TYPO_METRICS bit of <c>OS/2.fsSelection</c>.</summary>
    public bool UseTypoMetrics { get; set; }

    public int CapHeight { get; set; } = 700;

    public int XHeight { get; set; } = 500;

    public int StrikeoutPosition { get; set; } = 250;

    public int StrikeoutSize { get; set; } = 50;

    public int UnderlinePosition { get; set; } = -100;

    public int UnderlineThickness { get; set; } = 50;

    /// <summary>Gets or sets <c>OS/2.usWidthClass</c>.</summary>
    public int WidthClass { get; set; } = 5;

    /// <summary>Gets or sets <c>OS/2.ySubscriptYOffset</c>: how far below the baseline subscripts are, positive downward.</summary>
    public int SubscriptYOffset { get; set; }

    /// <summary>Gets or sets <c>OS/2.ySuperscriptYOffset</c>: how far above the baseline superscripts are.</summary>
    public int SuperscriptYOffset { get; set; }

    /// <summary>Gets or sets <c>post.italicAngle</c>, in degrees counterclockwise from the vertical.</summary>
    public double ItalicAngle { get; set; }

    /// <summary>Gets or sets a value indicating whether the OS/2 table is omitted.</summary>
    public bool OmitOs2 { get; set; }

    /// <summary>Gets the glyphs; the index is the glyph identifier, and the glyph 0 is <c>.notdef</c>.</summary>
    public List<FontGlyph> Glyphs { get; } = [];

    /// <summary>Gets the character map: code point to glyph identifier.</summary>
    public SortedDictionary<int, int> CharacterMap { get; } = [];

    /// <summary>Gets the variation sequences of the format 14 subtable: (base, selector) to a glyph, or to -1 for the default glyph of the base.</summary>
    public SortedDictionary<(int Selector, int Base), int> VariationSequences { get; } = [];

    /// <summary>Gets or sets the subtables of the character map.</summary>
    public FontCmapFormats CmapFormats { get; set; } = FontCmapFormats.Format4;

    /// <summary>Gets or sets a value indicating whether the format 4 subtable maps through its glyph identifier array instead of deltas.</summary>
    public bool Cmap4UsesGlyphIdArray { get; set; }

    /// <summary>Gets the kerning pairs of the <c>kern</c> table: (left glyph, right glyph) to an adjustment of the advance.</summary>
    public SortedDictionary<(int Left, int Right), int> KerningPairs { get; } = [];

    /// <summary>Gets or sets how the coordinates of the glyphs are encoded.</summary>
    public FontGlyfEncoding GlyfEncoding { get; set; } = FontGlyfEncoding.Compact;

    /// <summary>Gets or sets a value indicating whether <c>loca</c> uses 32-bit offsets even when 16-bit ones are enough.</summary>
    public bool UseLongLoca { get; set; }

    /// <summary>Gets or sets a value indicating whether the trailing glyphs that share an advance only store their side bearing in <c>hmtx</c>.</summary>
    public bool CompactHorizontalMetrics { get; set; }

    /// <summary>Gets the axes of a variable font. With at least one axis, the font has the tables of a variable font.</summary>
    public List<FontAxisDefinition> Axes { get; } = [];

    /// <summary>Gets the named instances of a variable font.</summary>
    public List<FontNamedInstanceDefinition> NamedInstances { get; } = [];

    /// <summary>Gets or sets a value indicating whether the peaks of the tuple variations are stored once in the <c>gvar</c> table.</summary>
    public bool ShareVariationTuples { get; set; }

    /// <summary>Gets or sets a value indicating whether the offsets of the <c>gvar</c> table are 32 bits.</summary>
    public bool UseLongVariationOffsets { get; set; }

    /// <summary>Gets or sets the deltas of the normalized values of the axes, for version 2 of the <c>avar</c> table: by default, the item of an axis is its index in the first subtable.</summary>
    public FontVariationStore? AxisVariations { get; set; }

    /// <summary>Gets or sets the delta set index map of version 2 of the <c>avar</c> table, as written by <see cref="FontVariationWriter.WriteIndexMap"/>.</summary>
    public byte[]? AxisVariationMap { get; set; }

    /// <summary>Gets or sets the deltas of the advances, for the <c>HVAR</c> table: by default, the item of a glyph is its index in the first subtable.</summary>
    public FontVariationStore? AdvanceVariations { get; set; }

    /// <summary>Gets or sets the delta set index map of the <c>HVAR</c> table, as written by <see cref="FontVariationWriter.WriteIndexMap"/>.</summary>
    public byte[]? AdvanceVariationMap { get; set; }

    /// <summary>Gets or sets the deltas of the metrics, for the <c>MVAR</c> table.</summary>
    public FontVariationStore? MetricVariations { get; set; }

    /// <summary>Gets the item of each metric the <c>MVAR</c> table varies, by its tag.</summary>
    public SortedDictionary<string, (int Outer, int Inner)> MetricVariationItems { get; } = new(StringComparer.Ordinal);

    /// <summary>Gets the palettes of a color font, for the <c>CPAL</c> table: the red, green, blue and alpha of each color.</summary>
    public List<(byte R, byte G, byte B, byte A)[]> ColorPalettes { get; } = [];

    /// <summary>Gets or sets the backgrounds each palette is made for (1: light, 2: dark), which makes the <c>CPAL</c> table a version 1.</summary>
    public List<int>? ColorPaletteTypes { get; set; }

    /// <summary>Gets the layers of the color glyphs, for a <c>COLR</c> table of version 0: a glyph and an entry of the palette, from the bottom to the top.</summary>
    public SortedDictionary<int, (int Glyph, int PaletteIndex)[]> ColorLayers { get; } = [];

    /// <summary>Gets the paints of the color glyphs: with one of them, the <c>COLR</c> table is a version 1.</summary>
    public SortedDictionary<int, FontColorPaint> ColorPaints { get; } = [];

    /// <summary>Gets the clip boxes of the color glyphs of a <c>COLR</c> table of version 1: left, bottom, right, top.</summary>
    public SortedDictionary<int, (int XMin, int YMin, int XMax, int YMax)> ColorClipBoxes { get; } = [];

    /// <summary>Gets the strikes of a bitmap font: the images of its glyphs at sizes in pixels per em.</summary>
    public List<FontBitmapStrike> BitmapStrikes { get; } = [];

    /// <summary>Gets or sets a value indicating whether the strikes are written as <c>CBLC</c> and <c>CBDT</c> tables instead of an <c>sbix</c> table.</summary>
    public bool UseColorBitmapTables { get; set; }

    /// <summary>Gets the strikes of bitmaps without colors of the font, written to the <c>EBLC</c> and <c>EBDT</c> tables.</summary>
    public List<FontBitmapStrike> MonochromeBitmapStrikes { get; } = [];

    /// <summary>Gets names that are added to the <c>name</c> table, by identifier.</summary>
    public SortedDictionary<int, string> ExtraNames { get; } = [];

    /// <summary>Gets the tables written as given, which replace the generated table of the same tag.</summary>
    public SortedDictionary<string, byte[]> RawTables { get; } = new(StringComparer.Ordinal);

    /// <summary>Gets the tags of the generated tables that are left out, to build fonts with a missing table.</summary>
    public HashSet<string> OmittedTables { get; } = new(StringComparer.Ordinal);

    /// <summary>Adds a glyph and maps a code point to it.</summary>
    public int AddGlyph(FontGlyph glyph, int codePoint = -1)
    {
        Glyphs.Add(glyph);
        if (codePoint >= 0)
        {
            CharacterMap[codePoint] = Glyphs.Count - 1;
        }

        return Glyphs.Count - 1;
    }

    /// <summary>Builds the font as a TrueType-flavored OpenType file.</summary>
    public byte[] Build() => WriteSfnt(BuildTables(), SfntVersion);

    /// <summary>Builds the tables of the font, by tag.</summary>
    public SortedDictionary<string, byte[]> BuildTables()
    {
        if (Glyphs.Count == 0)
            throw new InvalidOperationException("A font needs at least the .notdef glyph");

        var bounds = Glyphs.Select(glyph => GetBounds(glyph, depth: 0)).ToArray();
        var glyphData = new byte[Glyphs.Count][];
        for (var i = 0; i < Glyphs.Count; i++)
        {
            glyphData[i] = WriteGlyph(Glyphs[i], bounds[i]);
        }

        var glyfLength = glyphData.Sum(data => (long)data.Length);
        var useLongLoca = UseLongLoca || glyfLength > 0x1FFFE;

        var glyf = new FontWriter();
        var loca = new FontWriter();
        foreach (var data in glyphData)
        {
            WriteLocaOffset(loca, glyf.Length, useLongLoca);
            glyf.Bytes(data);
        }

        WriteLocaOffset(loca, glyf.Length, useLongLoca);

        var tables = new SortedDictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["cmap"] = WriteCmap(),
            ["glyf"] = glyf.ToArray(),
            ["head"] = WriteHead(bounds, useLongLoca),
            ["hhea"] = WriteHhea(bounds, out var horizontalMetricCount),
            ["hmtx"] = WriteHmtx(bounds, horizontalMetricCount),
            ["loca"] = loca.ToArray(),
            ["maxp"] = WriteMaxp(),
            ["name"] = WriteName(ExtraNames),
            ["post"] = WritePost(),
        };

        if (!OmitOs2)
        {
            tables["OS/2"] = WriteOs2();
        }

        if (KerningPairs.Count > 0)
        {
            tables["kern"] = WriteKern();
        }

        if (MonochromeBitmapStrikes.Count > 0)
        {
            (tables["EBLC"], tables["EBDT"]) = FontBitmapWriter.WriteMonochromeBitmaps(MonochromeBitmapStrikes);
        }

        if (BitmapStrikes.Count > 0)
        {
            if (UseColorBitmapTables)
            {
                (tables["CBLC"], tables["CBDT"]) = FontBitmapWriter.WriteColorBitmaps(BitmapStrikes);
            }
            else
            {
                tables["sbix"] = FontBitmapWriter.WriteSbix(BitmapStrikes, Glyphs.Count);
            }
        }

        if (ColorPalettes.Count > 0)
        {
            tables["CPAL"] = FontColorWriter.WriteCpal(ColorPalettes, ColorPaletteTypes);
        }

        if (ColorLayers.Count > 0 || ColorPaints.Count > 0)
        {
            tables["COLR"] = FontColorWriter.WriteColr(ColorLayers, ColorPaints.Count > 0 ? ColorPaints : null, ColorClipBoxes);
        }

        if (Axes.Count > 0)
        {
            // The names of the axes and of the instances follow the names of the font.
            var names = new SortedDictionary<int, string>(ExtraNames);
            tables["fvar"] = FontVariationWriter.WriteFvar(Axes, NamedInstances, 256, names);
            tables["name"] = WriteName(names);
            if (Axes.Any(axis => axis.Map.Count > 0) || AxisVariations is not null)
            {
                tables["avar"] = FontVariationWriter.WriteAvar(Axes, AxisVariations, AxisVariationMap);
            }

            if (Glyphs.Any(glyph => glyph.Variations.Count > 0))
            {
                tables["gvar"] = FontVariationWriter.WriteGvar(Glyphs, Axes.Count, ShareVariationTuples, UseLongVariationOffsets);
            }

            if (AdvanceVariations is not null)
            {
                tables["HVAR"] = FontVariationWriter.WriteHvar(AdvanceVariations, Axes.Count, AdvanceVariationMap);
            }

            if (MetricVariations is not null)
            {
                tables["MVAR"] = FontVariationWriter.WriteMvar(MetricVariations, Axes.Count, MetricVariationItems);
            }
        }

        foreach (var (tag, data) in RawTables)
        {
            tables[tag] = data;
        }

        foreach (var tag in OmittedTables)
        {
            tables.Remove(tag);
        }

        return tables;
    }

    /// <summary>Writes tables as an OpenType file: the table directory, then the tables in the order of their tags.</summary>
    public static byte[] WriteSfnt(SortedDictionary<string, byte[]> tables, uint sfntVersion = 0x00010000)
    {
        var writer = new FontWriter();
        WriteDirectory(writer, tables, sfntVersion, out var recordOffsets);
        var headOffset = -1;
        foreach (var (tag, data) in tables)
        {
            writer.Pad(4);
            if (tag == "head")
            {
                headOffset = writer.Length;
            }

            writer.PatchUInt32(recordOffsets[tag] + 8, (uint)writer.Length);
            writer.Bytes(data);
        }

        writer.Pad(4);
        var result = writer.ToArray();
        if (headOffset >= 0 && tables["head"].Length >= 12)
        {
            // checkSumAdjustment: 0xB1B0AFBA minus the checksum of the whole font, computed with the field at 0.
            BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(headOffset + 8), 0);
            BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(headOffset + 8), 0xB1B0AFBA - ComputeChecksum(result));
        }

        return result;
    }

    /// <summary>Builds a font collection (TTC) from several fonts; the tables that are identical are stored once.</summary>
    public static byte[] WriteCollection(IReadOnlyList<SortedDictionary<string, byte[]>> fonts, int majorVersion = 1)
    {
        var writer = new FontWriter();
        writer.Tag("ttcf");
        writer.UInt16(majorVersion);
        writer.UInt16(0);
        writer.UInt32((uint)fonts.Count);
        var offsetTable = writer.Length;
        foreach (var _ in fonts)
        {
            writer.UInt32(0);
        }

        if (majorVersion == 2)
        {
            // No digital signature.
            writer.UInt32(0);
            writer.UInt32(0);
            writer.UInt32(0);
        }

        var directories = new List<Dictionary<string, int>>();
        for (var i = 0; i < fonts.Count; i++)
        {
            writer.PatchUInt32(offsetTable + (i * 4), (uint)writer.Length);
            WriteDirectory(writer, fonts[i], 0x00010000, out var recordOffsets);
            directories.Add(recordOffsets);
        }

        var written = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < fonts.Count; i++)
        {
            foreach (var (tag, data) in fonts[i])
            {
                var key = tag + ":" + Convert.ToHexString(data);
                if (!written.TryGetValue(key, out var offset))
                {
                    writer.Pad(4);
                    offset = writer.Length;
                    writer.Bytes(data);
                    written.Add(key, offset);
                }

                writer.PatchUInt32(directories[i][tag] + 8, (uint)offset);
            }
        }

        writer.Pad(4);
        return writer.ToArray();
    }

    /// <summary>Wraps an OpenType file into a WOFF 1.0 file: each table is compressed with zlib when that makes it smaller.</summary>
    public static byte[] WriteWoff(ReadOnlySpan<byte> sfnt, bool compress = true)
    {
        var tableCount = BinaryPrimitives.ReadUInt16BigEndian(sfnt[4..]);
        var tables = new List<(uint Tag, uint Checksum, byte[] Data)>();
        for (var i = 0; i < tableCount; i++)
        {
            var record = sfnt.Slice(12 + (i * 16), 16);
            var offset = (int)BinaryPrimitives.ReadUInt32BigEndian(record[8..]);
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(record[12..]);
            tables.Add((BinaryPrimitives.ReadUInt32BigEndian(record), BinaryPrimitives.ReadUInt32BigEndian(record[4..]), sfnt.Slice(offset, length).ToArray()));
        }

        var writer = new FontWriter();
        writer.Tag("wOFF");
        writer.UInt32(BinaryPrimitives.ReadUInt32BigEndian(sfnt));
        var lengthOffset = writer.Length;
        writer.UInt32(0);
        writer.UInt16(tableCount);
        writer.UInt16(0);

        // The size of the font once the tables are padded and laid out after their directory.
        writer.UInt32((uint)(12 + (16 * tableCount) + tables.Sum(table => (table.Data.Length + 3) & ~3)));
        writer.UInt16(1);
        writer.UInt16(0);
        writer.UInt32(0);
        writer.UInt32(0);
        writer.UInt32(0);
        writer.UInt32(0);
        writer.UInt32(0);

        var directory = writer.Length;
        foreach (var _ in tables)
        {
            writer.Zeros(20);
        }

        for (var i = 0; i < tables.Count; i++)
        {
            var (tag, checksum, data) = tables[i];
            var stored = data;
            if (compress)
            {
                using var output = new MemoryStream();
                using (var zlib = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
                {
                    zlib.Write(data);
                }

                // A table is stored compressed only when that is smaller.
                if (output.Length < data.Length)
                {
                    stored = output.ToArray();
                }
            }

            writer.Pad(4);
            var record = directory + (i * 20);
            writer.PatchUInt32(record, tag);
            writer.PatchUInt32(record + 4, (uint)writer.Length);
            writer.PatchUInt32(record + 8, (uint)stored.Length);
            writer.PatchUInt32(record + 12, (uint)data.Length);
            writer.PatchUInt32(record + 16, checksum);
            writer.Bytes(stored);
        }

        writer.Pad(4);
        writer.PatchUInt32(lengthOffset, (uint)writer.Length);
        return writer.ToArray();
    }

    /// <summary>Computes the checksum of a table: the sum of its big-endian 32-bit words, the last one padded with zeros.</summary>
    public static uint ComputeChecksum(ReadOnlySpan<byte> data)
    {
        var sum = 0u;
        var index = 0;
        for (; index + 4 <= data.Length; index += 4)
        {
            sum += BinaryPrimitives.ReadUInt32BigEndian(data[index..]);
        }

        if (index < data.Length)
        {
            Span<byte> last = stackalloc byte[4];
            data[index..].CopyTo(last);
            sum += BinaryPrimitives.ReadUInt32BigEndian(last);
        }

        return sum;
    }

    private static void WriteDirectory(FontWriter writer, SortedDictionary<string, byte[]> tables, uint sfntVersion, out Dictionary<string, int> recordOffsets)
    {
        var count = tables.Count;
        var entrySelector = 0;
        while ((2 << entrySelector) <= count)
        {
            entrySelector++;
        }

        var searchRange = (1 << entrySelector) * 16;
        writer.UInt32(sfntVersion);
        writer.UInt16(count);
        writer.UInt16(searchRange);
        writer.UInt16(entrySelector);
        writer.UInt16((count * 16) - searchRange);

        recordOffsets = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (tag, data) in tables)
        {
            recordOffsets.Add(tag, writer.Length);
            writer.Tag(tag);
            writer.UInt32(tag == "head" ? ComputeHeadChecksum(data) : ComputeChecksum(data));
            writer.UInt32(0);
            writer.UInt32((uint)data.Length);
        }
    }

    private static uint ComputeHeadChecksum(byte[] head)
    {
        // The checksum of head is computed with checkSumAdjustment at 0.
        if (head.Length < 12)
            return ComputeChecksum(head);

        var copy = (byte[])head.Clone();
        copy.AsSpan(8, 4).Clear();
        return ComputeChecksum(copy);
    }

    private static void WriteLocaOffset(FontWriter loca, int offset, bool isLong)
    {
        if (isLong)
        {
            loca.UInt32((uint)offset);
        }
        else
        {
            loca.UInt16(offset / 2);
        }
    }

    internal FontBounds GetBounds(FontGlyph glyph, int depth = 0)
    {
        // A glyph that gives its bounds is not walked: it can then refer to itself, or to a glyph that does not exist.
        if (glyph.BoundsOverride is { } declared)
            return declared;

        if (depth > 32)
            throw new InvalidOperationException("The composite glyphs of the builder are nested too deeply");

        var bounds = FontBounds.Empty;
        foreach (var contour in glyph.Contours)
        {
            foreach (var point in contour)
            {
                bounds = bounds.Include(point.X, point.Y);
            }
        }

        foreach (var component in glyph.Components)
        {
            if (component.MatchesPoints)
                throw new InvalidOperationException("The bounds of a component placed by point matching must be given by the glyph");

            var inner = GetBounds(Glyphs[component.GlyphId], depth + 1);
            if (inner.IsEmpty)
                continue;

            foreach (var (x, y) in ((int, int)[])[(inner.XMin, inner.YMin), (inner.XMin, inner.YMax), (inner.XMax, inner.YMin), (inner.XMax, inner.YMax)])
            {
                var transformedX = (x * component.XScale) + (y * component.Scale10) + component.Argument1;
                var transformedY = (x * component.Scale01) + (y * component.YScale) + component.Argument2;
                bounds = bounds.Include((int)Math.Round(transformedX, MidpointRounding.AwayFromZero), (int)Math.Round(transformedY, MidpointRounding.AwayFromZero));
            }
        }

        return bounds;
    }

    private byte[] WriteGlyph(FontGlyph glyph, FontBounds bounds)
    {
        if (glyph.RawData is not null)
            return Align(glyph.RawData);

        if (glyph.Contours.Count == 0 && glyph.Components.Count == 0)
            return [];

        var writer = new FontWriter();
        writer.Int16(glyph.Components.Count > 0 ? -1 : glyph.Contours.Count);
        writer.Int16(bounds.XMin);
        writer.Int16(bounds.YMin);
        writer.Int16(bounds.XMax);
        writer.Int16(bounds.YMax);

        if (glyph.Components.Count > 0)
        {
            for (var i = 0; i < glyph.Components.Count; i++)
            {
                WriteComponent(writer, glyph.Components[i], hasMore: i + 1 < glyph.Components.Count, hasInstructions: glyph.Instructions.Length > 0 && i + 1 == glyph.Components.Count);
            }

            if (glyph.Instructions.Length > 0)
            {
                writer.UInt16(glyph.Instructions.Length);
                writer.Bytes(glyph.Instructions);
            }

            return Align(writer.ToArray());
        }

        var pointCount = 0;
        foreach (var contour in glyph.Contours)
        {
            pointCount += contour.Count;
            writer.UInt16(pointCount - 1);
        }

        writer.UInt16(glyph.Instructions.Length);
        writer.Bytes(glyph.Instructions);

        var points = glyph.Contours.SelectMany(contour => contour).ToArray();
        var flags = new byte[points.Length];
        var xs = new FontWriter();
        var ys = new FontWriter();
        var previousX = 0;
        var previousY = 0;
        for (var i = 0; i < points.Length; i++)
        {
            var flag = points[i].OnCurve ? 0x01 : 0x00;
            if (i == 0 && glyph.OverlapSimple)
            {
                flag |= 0x40;
            }

            flag |= WriteCoordinate(xs, points[i].X - previousX, shortFlag: 0x02, sameOrPositiveFlag: 0x10);
            flag |= WriteCoordinate(ys, points[i].Y - previousY, shortFlag: 0x04, sameOrPositiveFlag: 0x20);
            flags[i] = (byte)flag;
            previousX = points[i].X;
            previousY = points[i].Y;
        }

        for (var i = 0; i < flags.Length; i++)
        {
            var repeat = 0;
            if (GlyfEncoding is FontGlyfEncoding.Compact)
            {
                while (i + repeat + 1 < flags.Length && flags[i + repeat + 1] == flags[i] && repeat < 255)
                {
                    repeat++;
                }
            }

            if (repeat > 0)
            {
                writer.Byte((byte)(flags[i] | 0x08));
                writer.Byte((byte)repeat);
                i += repeat;
            }
            else
            {
                writer.Byte(flags[i]);
            }
        }

        writer.Bytes(xs.ToArray());
        writer.Bytes(ys.ToArray());
        return Align(writer.ToArray());
    }

    private int WriteCoordinate(FontWriter writer, int delta, int shortFlag, int sameOrPositiveFlag)
    {
        if (GlyfEncoding is FontGlyfEncoding.Compact)
        {
            if (delta == 0)
                return sameOrPositiveFlag;

            if (delta is >= -255 and <= 255)
            {
                writer.Byte((byte)Math.Abs(delta));
                return shortFlag | (delta > 0 ? sameOrPositiveFlag : 0);
            }
        }

        writer.Int16(delta);
        return 0;
    }

    internal static void WriteComponent(FontWriter writer, FontComponent component, bool hasMore, bool hasInstructions)
    {
        var argumentsAreWords = component.MatchesPoints
            ? component.Argument1 > 255 || component.Argument2 > 255
            : component.Argument1 is < -128 or > 127 || component.Argument2 is < -128 or > 127 || component.ForceWordArguments;
        var hasTwoByTwo = component.Scale01 != 0 || component.Scale10 != 0;
        var hasXAndYScale = !hasTwoByTwo && component.XScale != component.YScale;
        var hasScale = !hasTwoByTwo && !hasXAndYScale && component.XScale != 1;

        var flags = (argumentsAreWords ? 0x0001 : 0)
            | (component.MatchesPoints ? 0 : 0x0002)
            | (component.RoundToGrid ? 0x0004 : 0)
            | (hasScale ? 0x0008 : 0)
            | (hasMore ? 0x0020 : 0)
            | (hasXAndYScale ? 0x0040 : 0)
            | (hasTwoByTwo ? 0x0080 : 0)
            | (hasInstructions ? 0x0100 : 0)
            | (component.UseMyMetrics ? 0x0200 : 0)
            | (component.OverlapCompound ? 0x0400 : 0)
            | (component.ScaledComponentOffset ? 0x0800 : 0)
            | (component.UnscaledComponentOffset ? 0x1000 : 0);

        writer.UInt16(flags);
        writer.UInt16(component.GlyphId);
        if (argumentsAreWords)
        {
            writer.Int16(component.Argument1);
            writer.Int16(component.Argument2);
        }
        else
        {
            writer.Byte((byte)component.Argument1);
            writer.Byte((byte)component.Argument2);
        }

        if (hasScale)
        {
            writer.F2Dot14(component.XScale);
        }
        else if (hasXAndYScale)
        {
            writer.F2Dot14(component.XScale);
            writer.F2Dot14(component.YScale);
        }
        else if (hasTwoByTwo)
        {
            writer.F2Dot14(component.XScale);
            writer.F2Dot14(component.Scale01);
            writer.F2Dot14(component.Scale10);
            writer.F2Dot14(component.YScale);
        }
    }

    private static byte[] Align(byte[] data)
    {
        // Glyph data is padded so that the short form of loca, which stores half offsets, can address it.
        if ((data.Length & 1) == 0)
            return data;

        var result = new byte[data.Length + 1];
        data.CopyTo(result, 0);
        return result;
    }

    private byte[] WriteHead(FontBounds[] bounds, bool useLongLoca)
    {
        var all = FontBounds.Empty;
        foreach (var glyphBounds in bounds)
        {
            if (!glyphBounds.IsEmpty)
            {
                all = all.Include(glyphBounds.XMin, glyphBounds.YMin).Include(glyphBounds.XMax, glyphBounds.YMax);
            }
        }

        if (all.IsEmpty)
        {
            all = new FontBounds(0, 0, 0, 0);
        }

        var writer = new FontWriter();
        writer.UInt16(1);
        writer.UInt16(0);
        writer.UInt32(0x00010000);
        writer.UInt32(0);
        writer.UInt32(0x5F0F3CF5);

        // Baseline at y = 0, left side bearing at x = 0.
        writer.UInt16(0x0003);
        writer.UInt16(UnitsPerEm);
        writer.Zeros(16);
        writer.Int16(all.XMin);
        writer.Int16(all.YMin);
        writer.Int16(all.XMax);
        writer.Int16(all.YMax);
        writer.UInt16((IsBold ? 1 : 0) | (IsItalic ? 2 : 0));
        writer.UInt16(8);
        writer.Int16(2);
        writer.Int16(useLongLoca ? 1 : 0);
        writer.Int16(0);
        return writer.ToArray();
    }

    private byte[] WriteHhea(FontBounds[] bounds, out int horizontalMetricCount)
    {
        horizontalMetricCount = Glyphs.Count;
        if (CompactHorizontalMetrics)
        {
            while (horizontalMetricCount > 1 && Glyphs[horizontalMetricCount - 1].AdvanceWidth == Glyphs[horizontalMetricCount - 2].AdvanceWidth)
            {
                horizontalMetricCount--;
            }
        }

        var minLeft = int.MaxValue;
        var minRight = int.MaxValue;
        var maxExtent = int.MinValue;
        for (var i = 0; i < Glyphs.Count; i++)
        {
            if (bounds[i].IsEmpty)
                continue;

            minLeft = Math.Min(minLeft, bounds[i].XMin);
            minRight = Math.Min(minRight, Glyphs[i].AdvanceWidth - bounds[i].XMax);
            maxExtent = Math.Max(maxExtent, bounds[i].XMax);
        }

        var writer = new FontWriter();
        writer.UInt32(0x00010000);
        writer.Int16(Ascender);
        writer.Int16(Descender);
        writer.Int16(LineGap);
        writer.UInt16(Glyphs.Max(glyph => glyph.AdvanceWidth));
        writer.Int16(minLeft == int.MaxValue ? 0 : minLeft);
        writer.Int16(minRight == int.MaxValue ? 0 : minRight);
        writer.Int16(maxExtent == int.MinValue ? 0 : maxExtent);
        writer.Int16(1);
        writer.Int16(0);
        writer.Int16(0);
        writer.Zeros(8);
        writer.Int16(0);
        writer.UInt16(horizontalMetricCount);
        return writer.ToArray();
    }

    private byte[] WriteHmtx(FontBounds[] bounds, int horizontalMetricCount)
    {
        var writer = new FontWriter();
        for (var i = 0; i < Glyphs.Count; i++)
        {
            if (i < horizontalMetricCount)
            {
                writer.UInt16(Glyphs[i].AdvanceWidth);
            }

            writer.Int16(Glyphs[i].LeftSideBearing ?? (bounds[i].IsEmpty ? 0 : bounds[i].XMin));
        }

        return writer.ToArray();
    }

    private byte[] WriteMaxp()
    {
        var maxPoints = 0;
        var maxContours = 0;
        foreach (var glyph in Glyphs)
        {
            maxPoints = Math.Max(maxPoints, glyph.Contours.Sum(contour => contour.Count));
            maxContours = Math.Max(maxContours, glyph.Contours.Count);
        }

        var writer = new FontWriter();
        writer.UInt32(0x00010000);
        writer.UInt16(Glyphs.Count);
        writer.UInt16(maxPoints);
        writer.UInt16(maxContours);

        // The limits of the composite glyphs are generous upper bounds: no rasterizer relies on them being tight.
        writer.UInt16(maxPoints * 8);
        writer.UInt16(maxContours * 8);
        writer.UInt16(2);
        writer.Zeros(12);
        writer.UInt16(Glyphs.Max(glyph => glyph.Components.Count));
        writer.UInt16(Glyphs.Any(glyph => glyph.Components.Count > 0) ? 8 : 0);
        return writer.ToArray();
    }

    private byte[] WriteOs2()
    {
        var writer = new FontWriter();
        writer.UInt16(4);
        writer.Int16((int)Math.Round(Glyphs.Average(glyph => (double)glyph.AdvanceWidth), MidpointRounding.AwayFromZero));
        writer.UInt16(WeightClass);
        writer.UInt16(WidthClass);
        writer.UInt16(0);

        // The sizes and the horizontal offsets of subscripts and superscripts are not used by the library.
        writer.Zeros(6);
        writer.Int16(SubscriptYOffset);
        writer.Zeros(6);
        writer.Int16(SuperscriptYOffset);
        writer.Int16(StrikeoutSize);
        writer.Int16(StrikeoutPosition);
        writer.Int16(0);
        writer.Zeros(10);
        writer.Zeros(16);
        writer.Tag("MZTU");
        writer.UInt16((IsItalic ? 0x0001 : 0) | (IsBold ? 0x0020 : 0) | (!IsItalic && !IsBold ? 0x0040 : 0) | (UseTypoMetrics ? 0x0080 : 0));
        writer.UInt16(CharacterMap.Count == 0 ? 0 : Math.Min(0xFFFF, CharacterMap.Keys.First()));
        writer.UInt16(CharacterMap.Count == 0 ? 0 : Math.Min(0xFFFF, CharacterMap.Keys.Last()));
        writer.Int16(TypoAscender);
        writer.Int16(TypoDescender);
        writer.Int16(TypoLineGap);
        writer.UInt16(WinAscent);
        writer.UInt16(WinDescent);
        writer.Zeros(8);
        writer.Int16(XHeight);
        writer.Int16(CapHeight);
        writer.UInt16(0);
        writer.UInt16(0x20);
        writer.UInt16(0);
        return writer.ToArray();
    }

    private byte[] WriteName(SortedDictionary<int, string> extraNames)
    {
        var names = new List<(int Id, string Value)> { (1, FamilyName), (2, SubfamilyName), (4, FamilyName + " " + SubfamilyName) };
        if (PostScriptName is not null)
        {
            names.Add((6, PostScriptName));
        }

        if (TypographicFamilyName is not null)
        {
            names.Add((16, TypographicFamilyName));
        }

        if (TypographicSubfamilyName is not null)
        {
            names.Add((17, TypographicSubfamilyName));
        }

        names.AddRange(extraNames.Select(name => (name.Key, name.Value)));

        // Records are sorted by platform, encoding, language and name identifier.
        var records = new List<(int Platform, int Encoding, int Language, int Id, byte[] Data)>();
        if (WriteMacintoshNames || OmitWindowsNames)
        {
            foreach (var (id, value) in names)
            {
                records.Add((1, 0, 0, id, Encoding.Latin1.GetBytes(value)));
            }
        }

        if (!OmitWindowsNames)
        {
            foreach (var (id, value) in names)
            {
                records.Add((3, 1, 0x0409, id, Encoding.BigEndianUnicode.GetBytes(value)));
            }
        }

        var writer = new FontWriter();
        writer.UInt16(0);
        writer.UInt16(records.Count);
        writer.UInt16(6 + (records.Count * 12));
        var storage = new FontWriter();
        foreach (var (platform, encoding, language, id, data) in records)
        {
            writer.UInt16(platform);
            writer.UInt16(encoding);
            writer.UInt16(language);
            writer.UInt16(id);
            writer.UInt16(data.Length);
            writer.UInt16(storage.Length);
            storage.Bytes(data);
        }

        writer.Bytes(storage.ToArray());
        return writer.ToArray();
    }

    private byte[] WritePost()
    {
        var writer = new FontWriter();
        writer.UInt32(0x00030000);
        writer.Int32((int)Math.Round(ItalicAngle * 65536, MidpointRounding.AwayFromZero));
        writer.Int16(UnderlinePosition);
        writer.Int16(UnderlineThickness);
        writer.UInt32(IsMonospaced ? 1u : 0u);
        writer.Zeros(16);
        return writer.ToArray();
    }

    private byte[] WriteKern()
    {
        var count = KerningPairs.Count;
        var entrySelector = 0;
        while ((2 << entrySelector) <= count)
        {
            entrySelector++;
        }

        var searchRange = (1 << entrySelector) * 6;
        var writer = new FontWriter();
        writer.UInt16(0);
        writer.UInt16(1);
        writer.UInt16(0);
        writer.UInt16(14 + (count * 6));

        // Horizontal kerning, format 0.
        writer.UInt16(0x0001);
        writer.UInt16(count);
        writer.UInt16(searchRange);
        writer.UInt16(entrySelector);
        writer.UInt16((count * 6) - searchRange);
        foreach (var ((left, right), value) in KerningPairs)
        {
            writer.UInt16(left);
            writer.UInt16(right);
            writer.Int16(value);
        }

        return writer.ToArray();
    }

    private byte[] WriteCmap()
    {
        var subtables = new List<(int Platform, int Encoding, byte[] Data)>();
        if (CmapFormats.HasFlag(FontCmapFormats.Format14))
        {
            subtables.Add((0, 5, WriteCmap14()));
        }

        if (CmapFormats.HasFlag(FontCmapFormats.MacintoshFormat6))
        {
            subtables.Add((1, 0, WriteCmap6()));
        }

        if (CmapFormats.HasFlag(FontCmapFormats.Format4))
        {
            subtables.Add((3, 1, WriteCmap4()));
        }

        if (CmapFormats.HasFlag(FontCmapFormats.Format12))
        {
            subtables.Add((3, 10, WriteCmap12(format: 12)));
        }

        if (CmapFormats.HasFlag(FontCmapFormats.Format13))
        {
            subtables.Add((3, 10, WriteCmap12(format: 13)));
        }

        var writer = new FontWriter();
        writer.UInt16(0);
        writer.UInt16(subtables.Count);
        var offset = 4 + (subtables.Count * 8);
        foreach (var (platform, encoding, data) in subtables)
        {
            writer.UInt16(platform);
            writer.UInt16(encoding);
            writer.UInt32((uint)offset);
            offset += data.Length;
        }

        foreach (var (_, _, data) in subtables)
        {
            writer.Bytes(data);
        }

        return writer.ToArray();
    }

    private byte[] WriteCmap4()
    {
        // Segments of consecutive code points; a segment maps by delta when its glyphs are consecutive too.
        var segments = new List<(int Start, int End, int[] Glyphs)>();
        foreach (var (codePoint, glyph) in CharacterMap)
        {
            if (codePoint >= 0xFFFF)
                continue;

            if (segments.Count > 0 && segments[^1].End + 1 == codePoint && (Cmap4UsesGlyphIdArray || segments[^1].Glyphs[^1] + 1 == glyph))
            {
                var last = segments[^1];
                segments[^1] = (last.Start, codePoint, [.. last.Glyphs, glyph]);
            }
            else
            {
                segments.Add((codePoint, codePoint, [glyph]));
            }
        }

        var segmentCount = segments.Count + 1;
        var entrySelector = 0;
        while ((2 << entrySelector) <= segmentCount)
        {
            entrySelector++;
        }

        var searchRange = (1 << entrySelector) * 2;
        var endCodes = new FontWriter();
        var startCodes = new FontWriter();
        var deltas = new FontWriter();
        var rangeOffsets = new FontWriter();
        var glyphIds = new FontWriter();
        for (var i = 0; i < segments.Count; i++)
        {
            var (start, end, glyphs) = segments[i];
            endCodes.UInt16(end);
            startCodes.UInt16(start);
            if (Cmap4UsesGlyphIdArray)
            {
                // The offset is counted from the position of this idRangeOffset entry.
                deltas.UInt16(0);
                rangeOffsets.UInt16(((segmentCount - i) * 2) + glyphIds.Length);
                foreach (var glyph in glyphs)
                {
                    glyphIds.UInt16(glyph);
                }
            }
            else
            {
                deltas.UInt16((glyphs[0] - start) & 0xFFFF);
                rangeOffsets.UInt16(0);
            }
        }

        // The last segment maps 0xFFFF to the missing glyph.
        endCodes.UInt16(0xFFFF);
        startCodes.UInt16(0xFFFF);
        deltas.UInt16(1);
        rangeOffsets.UInt16(0);

        var writer = new FontWriter();
        writer.UInt16(4);
        writer.UInt16(16 + (segmentCount * 8) + glyphIds.Length);
        writer.UInt16(0);
        writer.UInt16(segmentCount * 2);
        writer.UInt16(searchRange);
        writer.UInt16(entrySelector);
        writer.UInt16((segmentCount * 2) - searchRange);
        writer.Bytes(endCodes.ToArray());
        writer.UInt16(0);
        writer.Bytes(startCodes.ToArray());
        writer.Bytes(deltas.ToArray());
        writer.Bytes(rangeOffsets.ToArray());
        writer.Bytes(glyphIds.ToArray());
        return writer.ToArray();
    }

    private byte[] WriteCmap6()
    {
        // A trimmed table over the code points below 256.
        var entries = CharacterMap.Where(pair => pair.Key < 256).ToList();
        var first = entries.Count == 0 ? 0 : entries[0].Key;
        var count = entries.Count == 0 ? 0 : entries[^1].Key - first + 1;
        var writer = new FontWriter();
        writer.UInt16(6);
        writer.UInt16(10 + (count * 2));
        writer.UInt16(0);
        writer.UInt16(first);
        writer.UInt16(count);
        for (var codePoint = first; codePoint < first + count; codePoint++)
        {
            writer.UInt16(CharacterMap.GetValueOrDefault(codePoint));
        }

        return writer.ToArray();
    }

    private byte[] WriteCmap12(int format)
    {
        // Format 12 groups map consecutive code points to consecutive glyphs; format 13 groups map them to one glyph.
        var groups = new List<(int Start, int End, int Glyph)>();
        foreach (var (codePoint, glyph) in CharacterMap)
        {
            if (groups.Count > 0 && groups[^1].End + 1 == codePoint
                && (format == 12 ? groups[^1].Glyph + (codePoint - groups[^1].Start) == glyph : groups[^1].Glyph == glyph))
            {
                groups[^1] = (groups[^1].Start, codePoint, groups[^1].Glyph);
            }
            else
            {
                groups.Add((codePoint, codePoint, glyph));
            }
        }

        var writer = new FontWriter();
        writer.UInt16(format);
        writer.UInt16(0);
        writer.UInt32((uint)(16 + (groups.Count * 12)));
        writer.UInt32(0);
        writer.UInt32((uint)groups.Count);
        foreach (var (start, end, glyph) in groups)
        {
            writer.UInt32((uint)start);
            writer.UInt32((uint)end);
            writer.UInt32((uint)glyph);
        }

        return writer.ToArray();
    }

    private byte[] WriteCmap14()
    {
        var selectors = VariationSequences.Keys.Select(key => key.Selector).Distinct().Order().ToList();
        var header = new FontWriter();
        var data = new FontWriter();
        var dataStart = 10 + (selectors.Count * 11);
        header.UInt16(14);
        var lengthOffset = header.Length;
        header.UInt32(0);
        header.UInt32((uint)selectors.Count);
        foreach (var selector in selectors)
        {
            var sequences = VariationSequences.Where(pair => pair.Key.Selector == selector).ToList();
            var defaults = sequences.Where(pair => pair.Value < 0).Select(pair => pair.Key.Base).ToList();
            var others = sequences.Where(pair => pair.Value >= 0).ToList();
            header.UInt24(selector);
            if (defaults.Count == 0)
            {
                header.UInt32(0);
            }
            else
            {
                // Default sequences: ranges of base characters displayed with their default glyph.
                header.UInt32((uint)(dataStart + data.Length));
                var ranges = new List<(int Start, int Additional)>();
                foreach (var value in defaults)
                {
                    if (ranges.Count > 0 && ranges[^1].Start + ranges[^1].Additional + 1 == value && ranges[^1].Additional < 255)
                    {
                        ranges[^1] = (ranges[^1].Start, ranges[^1].Additional + 1);
                    }
                    else
                    {
                        ranges.Add((value, 0));
                    }
                }

                data.UInt32((uint)ranges.Count);
                foreach (var (start, additional) in ranges)
                {
                    data.UInt24(start);
                    data.Byte((byte)additional);
                }
            }

            if (others.Count == 0)
            {
                header.UInt32(0);
            }
            else
            {
                header.UInt32((uint)(dataStart + data.Length));
                data.UInt32((uint)others.Count);
                foreach (var (key, glyph) in others)
                {
                    data.UInt24(key.Base);
                    data.UInt16(glyph);
                }
            }
        }

        header.PatchUInt32(lengthOffset, (uint)(header.Length + data.Length));
        header.Bytes(data.ToArray());
        return header.ToArray();
    }
}
