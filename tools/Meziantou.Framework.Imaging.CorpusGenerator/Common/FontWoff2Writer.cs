using System.Buffers.Binary;
using System.IO.Compression;

namespace Meziantou.Framework.Imaging.CorpusGenerator.Common;

/// <summary>
/// Writes WOFF 2.0 files (W3C Recommendation "WOFF File Format 2.0") from the fonts of a <see cref="FontBuilder"/>,
/// with the transformations the reference encoder does not apply to them.
/// </summary>
/// <remarks>
/// The corpus generator reads every file this class writes with the reference decoder and with FreeType: the files it
/// commits are known to be what those two readers understand.
/// </remarks>
internal static class FontWoff2Writer
{
    private const string KnownTags =
        "cmapheadhheahmtxmaxpnameOS/2postcvt fpgmglyflocaprepCFF VORGEBDTEBLCgasphdmxkernLTSHPCLTVDMXvheavmtxBASEGDEFGPOSGSUBEBSCJSTFMATHCBDTCBLCCOLRCPALSVG sbixacntavarbdatblocbslncvarfdscfeatfmtxfvargvarhstyjustlcarmortmorxopbdproptrakZapfSilfGlatGlocFeatSill";

    /// <summary>Writes a font.</summary>
    public static byte[] Write(FontBuilder font, FontWoff2Options? options = null) => WriteContainer(0x00010000, CreateTables(font, options ?? new FontWoff2Options()));

    /// <summary>Creates the tables of a font, in the order of the file: by tag, the glyph locations after the glyphs.</summary>
    public static List<FontWoff2Table> CreateTables(FontBuilder font, FontWoff2Options options)
    {
        var tables = font.BuildTables();
        var result = new List<FontWoff2Table>();
        foreach (var (tag, data) in tables)
        {
            switch (tag)
            {
                case "glyf":
                    result.Add(options.TransformGlyphs ? new FontWoff2Table(tag, TransformGlyphs(font, tables, options), Version: 0, GetPaddedGlyphsLength(tables)) : new FontWoff2Table(tag, data, Version: 3));
                    result.Add(options.TransformGlyphs ? new FontWoff2Table("loca", [], Version: 0, tables["loca"].Length) : new FontWoff2Table("loca", tables["loca"], Version: 3));
                    break;

                case "loca":
                    break;

                case "hmtx" when options.HorizontalMetricsFlags != 0:
                    result.Add(new FontWoff2Table(tag, TransformHorizontalMetrics(tables, options.HorizontalMetricsFlags), Version: 1, data.Length));
                    break;

                default:
                    result.Add(new FontWoff2Table(tag, data));
                    break;
            }
        }

        return result;
    }

    /// <summary>
    /// Gets the length of the glyph table when each glyph ends at a multiple of four bytes, as the reference encoder
    /// declares it. FreeType rebuilds the glyphs in a buffer of the declared length and drops what does not fit.
    /// </summary>
    private static int GetPaddedGlyphsLength(SortedDictionary<string, byte[]> tables)
    {
        var locations = tables["loca"];
        var hasLongOffsets = BinaryPrimitives.ReadInt16BigEndian(tables["head"].AsSpan(50)) != 0;
        var count = (locations.Length / (hasLongOffsets ? 4 : 2)) - 1;
        var length = 0;
        for (var i = 0; i < count; i++)
        {
            var start = hasLongOffsets ? (int)BinaryPrimitives.ReadUInt32BigEndian(locations.AsSpan(i * 4)) : BinaryPrimitives.ReadUInt16BigEndian(locations.AsSpan(i * 2)) * 2;
            var end = hasLongOffsets ? (int)BinaryPrimitives.ReadUInt32BigEndian(locations.AsSpan((i + 1) * 4)) : BinaryPrimitives.ReadUInt16BigEndian(locations.AsSpan((i + 1) * 2)) * 2;
            length += (end - start + 3) & ~3;
        }

        return length;
    }

    /// <summary>Writes a file from its tables, as they are given.</summary>
    public static byte[] WriteContainer(uint flavor, IReadOnlyList<FontWoff2Table> tables)
    {
        var directory = new FontWriter();
        var stream = new FontWriter();
        long fontLength = 12 + (16 * tables.Count);
        foreach (var table in tables)
        {
            var known = table.Tag.Length == 4 ? KnownTags.IndexOf(table.Tag, StringComparison.Ordinal) : -1;
            known = known >= 0 && known % 4 == 0 ? known / 4 : 63;
            directory.Byte((byte)((table.Version << 6) | known));
            if (known == 63)
            {
                directory.Tag(table.Tag);
            }

            var originalLength = table.OriginalLength ?? table.Data.Length;
            WriteBase128(directory, (uint)originalLength);
            if (table.IsTransformed)
            {
                WriteBase128(directory, (uint)table.Data.Length);
            }

            stream.Bytes(table.Data);
            fontLength += (originalLength + 3) & ~3;
        }

        var compressed = Compress(stream.ToArray());
        var writer = new FontWriter();
        writer.Tag("wOF2");
        writer.UInt32(flavor);
        writer.UInt32(0);
        writer.UInt16(tables.Count);
        writer.UInt16(0);
        writer.UInt32((uint)fontLength);
        writer.UInt32((uint)compressed.Length);

        // The version of the font, then the metadata and the private data: none.
        writer.UInt16(1);
        writer.UInt16(0);
        writer.Zeros(20);
        writer.Bytes(directory.ToArray());
        writer.Bytes(compressed);
        writer.Pad(4);
        writer.PatchUInt32(8, (uint)writer.Length);
        return writer.ToArray();
    }

    /// <summary>Compresses the tables of a file.</summary>
    public static byte[] Compress(ReadOnlySpan<byte> data)
    {
        var buffer = new byte[BrotliEncoder.GetMaxCompressedLength(data.Length)];
        if (!BrotliEncoder.TryCompress(data, buffer, out var written, quality: 11, window: 22))
            throw new InvalidOperationException("The tables of the font could not be compressed");

        return buffer[..written];
    }

    /// <summary>Writes the transformed form of the glyphs of a font: section 5.1 of the specification.</summary>
    public static byte[] TransformGlyphs(FontBuilder font, SortedDictionary<string, byte[]> tables, FontWoff2Options options)
    {
        var contourCounts = new FontWriter();
        var pointCounts = new FontWriter();
        var flags = new FontWriter();
        var glyphs = new FontWriter();
        var composites = new FontWriter();
        var boxes = new FontWriter();
        var instructions = new FontWriter();
        var glyphCount = font.Glyphs.Count;
        var boxBits = new byte[((glyphCount + 31) >> 5) * 4];
        var overlapBits = new byte[(glyphCount + 7) >> 3];
        var hasOverlaps = false;
        for (var index = 0; index < glyphCount; index++)
        {
            var glyph = font.Glyphs[index];
            if (glyph.RawData is not null)
                throw new InvalidOperationException("A glyph given as bytes cannot be transformed");

            var hasBox = false;
            if (glyph.Components.Count > 0)
            {
                contourCounts.Int16(-1);
                for (var i = 0; i < glyph.Components.Count; i++)
                {
                    FontBuilder.WriteComponent(composites, glyph.Components[i], hasMore: i + 1 < glyph.Components.Count, hasInstructions: glyph.Instructions.Length > 0 && i + 1 == glyph.Components.Count);
                }

                if (glyph.Instructions.Length > 0)
                {
                    Write255UInt16(glyphs, glyph.Instructions.Length);
                    instructions.Bytes(glyph.Instructions);
                }

                hasBox = true;
            }
            else if (glyph.Contours.Count > 0)
            {
                contourCounts.Int16(glyph.Contours.Count);
                var previousX = 0;
                var previousY = 0;
                foreach (var contour in glyph.Contours)
                {
                    Write255UInt16(pointCounts, contour.Count);
                    foreach (var point in contour)
                    {
                        WriteCoordinates(flags, glyphs, point.X - previousX, point.Y - previousY, point.OnCurve);
                        previousX = point.X;
                        previousY = point.Y;
                    }
                }

                Write255UInt16(glyphs, glyph.Instructions.Length);
                instructions.Bytes(glyph.Instructions);
                hasBox = options.ExplicitBoundingBoxes || glyph.BoundsOverride is not null;
                if (glyph.OverlapSimple)
                {
                    hasOverlaps = true;
                    overlapBits[index >> 3] |= (byte)(0x80 >> (index & 7));
                }
            }
            else
            {
                contourCounts.Int16(0);
            }

            if (hasBox)
            {
                var bounds = font.GetBounds(glyph);
                boxBits[index >> 3] |= (byte)(0x80 >> (index & 7));
                boxes.Int16(bounds.XMin);
                boxes.Int16(bounds.YMin);
                boxes.Int16(bounds.XMax);
                boxes.Int16(bounds.YMax);
            }
        }

        var writer = new FontWriter();
        writer.UInt16(0);
        writer.UInt16(hasOverlaps ? 1 : 0);
        writer.UInt16(glyphCount);
        writer.UInt16(BinaryPrimitives.ReadInt16BigEndian(tables["head"].AsSpan(50)));
        writer.UInt32((uint)contourCounts.Length);
        writer.UInt32((uint)pointCounts.Length);
        writer.UInt32((uint)flags.Length);
        writer.UInt32((uint)glyphs.Length);
        writer.UInt32((uint)composites.Length);
        writer.UInt32((uint)(boxBits.Length + boxes.Length));
        writer.UInt32((uint)instructions.Length);
        writer.Bytes(contourCounts.ToArray());
        writer.Bytes(pointCounts.ToArray());
        writer.Bytes(flags.ToArray());
        writer.Bytes(glyphs.ToArray());
        writer.Bytes(composites.ToArray());
        writer.Bytes(boxBits);
        writer.Bytes(boxes.ToArray());
        writer.Bytes(instructions.ToArray());
        if (hasOverlaps)
        {
            writer.Bytes(overlapBits);
        }

        return writer.ToArray();
    }

    /// <summary>Writes the transformed form of the horizontal metrics: section 5.4 of the specification.</summary>
    public static byte[] TransformHorizontalMetrics(SortedDictionary<string, byte[]> tables, int flags)
    {
        var metrics = tables["hmtx"];
        var metricCount = BinaryPrimitives.ReadUInt16BigEndian(tables["hhea"].AsSpan(34));
        var glyphCount = BinaryPrimitives.ReadUInt16BigEndian(tables["maxp"].AsSpan(4));
        var writer = new FontWriter();
        writer.Byte((byte)flags);
        for (var i = 0; i < metricCount; i++)
        {
            writer.Bytes(metrics.AsSpan(i * 4, 2));
        }

        if ((flags & 1) == 0)
        {
            for (var i = 0; i < metricCount; i++)
            {
                writer.Bytes(metrics.AsSpan((i * 4) + 2, 2));
            }
        }

        if ((flags & 2) == 0)
        {
            writer.Bytes(metrics.AsSpan(metricCount * 4, (glyphCount - metricCount) * 2));
        }

        return writer.ToArray();
    }

    /// <summary>Writes a UIntBase128 value: section 3.1.</summary>
    public static void WriteBase128(FontWriter writer, uint value)
    {
        var started = false;
        for (var shift = 28; shift >= 0; shift -= 7)
        {
            var bits = (value >> shift) & 0x7F;
            if (bits != 0 || started || shift == 0)
            {
                writer.Byte((byte)(bits | (shift == 0 ? 0u : 0x80u)));
                started = true;
            }
        }
    }

    /// <summary>Writes a 255UInt16 value: section 3.2.</summary>
    public static void Write255UInt16(FontWriter writer, int value)
    {
        switch (value)
        {
            case < 253:
                writer.Byte((byte)value);
                break;

            case < 506:
                writer.Byte(255);
                writer.Byte((byte)(value - 253));
                break;

            case < 762:
                writer.Byte(254);
                writer.Byte((byte)(value - 506));
                break;

            default:
                writer.Byte(253);
                writer.UInt16(value);
                break;
        }
    }

    /// <summary>Writes the coordinates of a point with the shortest of the encodings of section 5.2.</summary>
    private static void WriteCoordinates(FontWriter flags, FontWriter glyphs, int dx, int dy, bool onCurve)
    {
        var x = Math.Abs(dx);
        var y = Math.Abs(dy);
        var signs = (dx >= 0 ? 1 : 0) + (dy >= 0 ? 2 : 0);
        int index;
        if (dx == 0 && y < 1280)
        {
            index = ((y >> 8) << 1) + (dy >= 0 ? 1 : 0);
            glyphs.Byte((byte)y);
        }
        else if (dy == 0 && x < 1280)
        {
            index = 10 + ((x >> 8) << 1) + (dx >= 0 ? 1 : 0);
            glyphs.Byte((byte)x);
        }
        else if (x is >= 1 and <= 64 && y is >= 1 and <= 64)
        {
            index = 20 + (((x - 1) >> 4) << 4) + (((y - 1) >> 4) << 2) + signs;
            glyphs.Byte((byte)((((x - 1) & 15) << 4) | ((y - 1) & 15)));
        }
        else if (x is >= 1 and <= 768 && y is >= 1 and <= 768)
        {
            index = 84 + (12 * ((x - 1) >> 8)) + (((y - 1) >> 8) << 2) + signs;
            glyphs.Byte((byte)(x - 1));
            glyphs.Byte((byte)(y - 1));
        }
        else if (x < 4096 && y < 4096)
        {
            index = 120 + signs;
            glyphs.Byte((byte)(x >> 4));
            glyphs.Byte((byte)(((x & 15) << 4) | (y >> 8)));
            glyphs.Byte((byte)y);
        }
        else
        {
            index = 124 + signs;
            glyphs.UInt16(x);
            glyphs.UInt16(y);
        }

        flags.Byte((byte)(index | (onCurve ? 0 : 0x80)));
    }
}
