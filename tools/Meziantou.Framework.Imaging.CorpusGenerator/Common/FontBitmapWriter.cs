namespace Meziantou.Framework.Imaging.CorpusGenerator.Common;

/// <summary>
/// Writes the tables of a bitmap font from their description: OpenType specification, "sbix - Standard Bitmap Graphics
/// Table", "CBLC - Color Bitmap Location Table" and "CBDT - Color Bitmap Data Table", "EBLC - Embedded Bitmap Location
/// Table" and "EBDT - Embedded Bitmap Data Table".
/// </summary>
internal static class FontBitmapWriter
{
    /// <summary>Writes the <c>sbix</c> table.</summary>
    public static byte[] WriteSbix(IReadOnlyList<FontBitmapStrike> strikes, int glyphCount)
    {
        var data = new List<byte[]>();
        foreach (var strike in strikes)
        {
            var glyphs = new FontWriter();
            var offsets = new FontWriter();
            var headerLength = 4 + ((glyphCount + 1) * 4);
            for (var glyph = 0; glyph <= glyphCount; glyph++)
            {
                offsets.UInt32((uint)(headerLength + glyphs.Length));
                if (glyph < glyphCount && strike.Glyphs.FirstOrDefault(candidate => candidate.GlyphId == glyph) is { } image)
                {
                    glyphs.Int16(image.Left);
                    glyphs.Int16(image.Bottom);
                    if (image.DuplicateOf is { } other)
                    {
                        glyphs.Tag("dupe");
                        glyphs.UInt16(other);
                    }
                    else
                    {
                        glyphs.Tag(image.Type);
                        glyphs.Bytes(image.Data);
                    }
                }
            }

            var writer = new FontWriter();
            writer.UInt16(strike.PixelsPerEm);
            writer.UInt16(72);
            writer.Bytes(offsets.ToArray());
            writer.Bytes(glyphs.ToArray());
            data.Add(writer.ToArray());
        }

        var table = new FontWriter();
        table.UInt16(1);
        table.UInt16(1);
        table.UInt32((uint)strikes.Count);
        var offset = 8 + (strikes.Count * 4);
        foreach (var strike in data)
        {
            table.UInt32((uint)offset);
            offset += strike.Length;
        }

        foreach (var strike in data)
        {
            table.Bytes(strike);
        }

        return table.ToArray();
    }

    /// <summary>Writes the <c>CBLC</c> and <c>CBDT</c> tables: each strike has one index subtable, for the range of its glyphs.</summary>
    public static (byte[] Locations, byte[] Data) WriteColorBitmaps(IReadOnlyList<FontBitmapStrike> strikes) => WriteBitmaps(strikes, isMonochrome: false);

    /// <summary>
    /// Writes the <c>EBLC</c> and <c>EBDT</c> tables: strikes of bitmaps without colors, whose image formats are 1, 2, 5,
    /// 6 and 7. The data of a glyph is the bits of its pixels (<see cref="PackBits"/>), without length before them.
    /// </summary>
    public static (byte[] Locations, byte[] Data) WriteMonochromeBitmaps(IReadOnlyList<FontBitmapStrike> strikes) => WriteBitmaps(strikes, isMonochrome: true);

    /// <summary>Packs the pixels of a bitmap of one bit per pixel, row by row from the top, the most significant bit first.</summary>
    /// <param name="width">The width, in pixels.</param>
    /// <param name="height">The height, in pixels.</param>
    /// <param name="isByteAligned">Whether each row starts at a byte (image formats 1 and 6); otherwise the bits of the rows follow each other (2, 5 and 7).</param>
    /// <param name="isSet">Tells whether the pixel of a column and a row is set.</param>
    public static byte[] PackBits(int width, int height, bool isByteAligned, Func<int, int, bool> isSet)
    {
        var rowBits = isByteAligned ? ((width + 7) / 8) * 8 : width;
        var bits = new byte[((rowBits * height) + 7) / 8];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (isSet(x, y))
                {
                    var index = (y * rowBits) + x;
                    bits[index >> 3] |= (byte)(0x80 >> (index & 7));
                }
            }
        }

        return bits;
    }

    private static int GetHeaderLength(int imageFormat) => imageFormat switch
    {
        1 or 2 or 17 => 5,
        6 or 7 or 18 => 8,
        _ => 0,
    };

    private static (byte[] Locations, byte[] Data) WriteBitmaps(IReadOnlyList<FontBitmapStrike> strikes, bool isMonochrome)
    {
        // The data table: its version, then the data of each glyph of each strike.
        var data = new FontWriter();
        data.UInt16(isMonochrome ? 2 : 3);
        data.UInt16(0);
        var subtables = new List<byte[]>();
        foreach (var strike in strikes)
        {
            var glyphs = strike.Glyphs.OrderBy(glyph => glyph.GlyphId).ToList();
            var imageDataOffset = data.Length;
            var offsets = new List<int>();
            foreach (var glyph in glyphs)
            {
                offsets.Add(data.Length - imageDataOffset);
                var top = glyph.Bottom + glyph.Height;
                switch (GetHeaderLength(strike.ImageFormat))
                {
                    case 5:
                        data.Bytes([(byte)glyph.Height, (byte)glyph.Width, (byte)(sbyte)glyph.Left, (byte)(sbyte)top, (byte)glyph.Width]);
                        break;

                    case 8:
                        data.Bytes([(byte)glyph.Height, (byte)glyph.Width, (byte)(sbyte)glyph.Left, (byte)(sbyte)top, (byte)glyph.Width, 0, 0, (byte)glyph.Height]);
                        break;
                }

                // A PNG image has its length before it; the bits of a bitmap have the length their metrics give.
                if (!isMonochrome)
                {
                    data.UInt32((uint)glyph.Data.Length);
                }

                data.Bytes(glyph.Data);

                // The index formats 2 and 5 give every image the same size: the data of a glyph is padded to it.
                if (strike.IndexFormat is 2 or 5)
                {
                    var size = glyphs.Max(candidate => candidate.Data.Length) + (isMonochrome ? 0 : 4) + GetHeaderLength(strike.ImageFormat);
                    data.Zeros(size - (data.Length - imageDataOffset - offsets[^1]));
                }
            }

            offsets.Add(data.Length - imageDataOffset);

            var subtable = new FontWriter();
            subtable.UInt16(strike.IndexFormat);
            subtable.UInt16(strike.ImageFormat);
            subtable.UInt32((uint)imageDataOffset);
            var first = glyphs[0];
            switch (strike.IndexFormat)
            {
                case 1:
                    // An offset for each glyph of the range, with or without image, and one more.
                    for (var glyph = first.GlyphId; glyph <= glyphs[^1].GlyphId + 1; glyph++)
                    {
                        var index = glyphs.FindIndex(candidate => candidate.GlyphId >= glyph);
                        subtable.UInt32((uint)offsets[index < 0 ? ^1 : index]);
                    }

                    break;

                case 3:
                    for (var glyph = first.GlyphId; glyph <= glyphs[^1].GlyphId + 1; glyph++)
                    {
                        var index = glyphs.FindIndex(candidate => candidate.GlyphId >= glyph);
                        subtable.UInt16(offsets[index < 0 ? ^1 : index]);
                    }

                    break;

                case 2 or 5:
                    // The size of every image, and their metrics: height, width, bearings and advance, twice.
                    subtable.UInt32((uint)(offsets[1] - offsets[0]));
                    subtable.Bytes([(byte)first.Height, (byte)first.Width, (byte)(sbyte)first.Left, (byte)(sbyte)(first.Bottom + first.Height), (byte)first.Width, 0, 0, (byte)first.Height]);
                    if (strike.IndexFormat == 5)
                    {
                        subtable.UInt32((uint)glyphs.Count);
                        foreach (var glyph in glyphs)
                        {
                            subtable.UInt16(glyph.GlyphId);
                        }
                    }

                    break;

                case 4:
                    subtable.UInt32((uint)glyphs.Count);
                    for (var i = 0; i < glyphs.Count; i++)
                    {
                        subtable.UInt16(glyphs[i].GlyphId);
                        subtable.UInt16(offsets[i]);
                    }

                    subtable.UInt16(0);
                    subtable.UInt16(offsets[^1]);
                    break;

                default:
                    throw new InvalidOperationException("Unknown index format");
            }

            subtables.Add(subtable.ToArray());
        }

        // The location table: a record per strike, then, for each strike, an array of one index subtable and the subtable.
        var locations = new FontWriter();
        locations.UInt16(isMonochrome ? 2 : 3);
        locations.UInt16(0);
        locations.UInt32((uint)strikes.Count);
        var arrayOffset = 8 + (strikes.Count * 48);
        for (var i = 0; i < strikes.Count; i++)
        {
            var glyphs = strikes[i].Glyphs;
            locations.UInt32((uint)arrayOffset);
            locations.UInt32((uint)(8 + subtables[i].Length));
            locations.UInt32(1);
            locations.UInt32(0);
            locations.Zeros(24);
            locations.UInt16(glyphs.Min(glyph => glyph.GlyphId));
            locations.UInt16(glyphs.Max(glyph => glyph.GlyphId));
            locations.Byte((byte)strikes[i].PixelsPerEm);
            locations.Byte((byte)strikes[i].PixelsPerEm);
            locations.Byte((byte)(isMonochrome ? strikes[i].BitDepth : 32));
            locations.Byte(1);
            arrayOffset += 8 + subtables[i].Length;
        }

        for (var i = 0; i < strikes.Count; i++)
        {
            var glyphs = strikes[i].Glyphs;
            locations.UInt16(glyphs.Min(glyph => glyph.GlyphId));
            locations.UInt16(glyphs.Max(glyph => glyph.GlyphId));
            locations.UInt32(8);
            locations.Bytes(subtables[i]);
        }

        return (locations.ToArray(), data.ToArray());
    }
}
