namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Reads the <c>CBLC</c> and <c>CBDT</c> tables: glyphs as PNG images, in strikes made for a number of pixels per em.
/// </summary>
/// <remarks>
/// OpenType specification, "CBLC - Color Bitmap Location Table" and "CBDT - Color Bitmap Data Table". The location
/// table has a record per strike, with index subtables that give, for ranges of glyphs, the place of their data in the
/// data table; the data of a glyph is its metrics (in the data, or once for a range of glyphs) and a PNG image.
/// </remarks>
internal sealed class CblcTable
{
    private const int SizeRecordLength = 48;

    private readonly int _sizeCount;

    private CblcTable(int sizeCount)
    {
        _sizeCount = sizeCount;
    }

    /// <summary>Reads the header of the location table, or returns <see langword="null"/> when it has no strike or a version that is not known.</summary>
    public static CblcTable? Read(ReadOnlySpan<byte> table)
    {
        if (table.Length < 8 || FontBinary.UInt16(table, 0) is not (2 or 3))
            return null;

        var sizeCount = FontBinary.UInt32(table, 4);
        if (sizeCount == 0 || sizeCount > (table.Length - 8) / SizeRecordLength)
            return null;

        return new CblcTable((int)sizeCount);
    }

    /// <summary>Finds the image of a glyph in the strike that suits a size.</summary>
    /// <param name="locations">The location table.</param>
    /// <param name="data">The data table.</param>
    /// <param name="dataOffset">The position of the data table in the bytes of the font.</param>
    /// <param name="glyphId">The glyph.</param>
    /// <param name="size">The size the glyph is drawn at, in pixels per em.</param>
    /// <param name="glyph">The image.</param>
    /// <returns><see langword="false"/> when the strike has no PNG image for the glyph.</returns>
    public bool TryGetGlyph(ReadOnlySpan<byte> locations, ReadOnlySpan<byte> data, int dataOffset, int glyphId, double size, out BitmapGlyph glyph)
    {
        glyph = default;

        // The smallest strike that is at least as large as the size: it is scaled down. Else the largest one.
        var strike = -1;
        var strikeSize = 0;
        for (var i = 0; i < _sizeCount; i++)
        {
            var record = 8 + (i * SizeRecordLength);
            var pixelsPerEm = FontBinary.UInt8(locations, record + 45);
            if (strike < 0 || (strikeSize < size ? pixelsPerEm > strikeSize : pixelsPerEm >= size && pixelsPerEm < strikeSize))
            {
                strike = record;
                strikeSize = pixelsPerEm;
            }
        }

        var pixelsPerEmX = FontBinary.UInt8(locations, strike + 44);
        if (strikeSize == 0 || pixelsPerEmX == 0 || glyphId < FontBinary.UInt16(locations, strike + 40) || glyphId > FontBinary.UInt16(locations, strike + 42))
            return false;

        // The index subtables of the strike: each one is for a range of glyphs.
        var array = FontBinary.Offset32(locations, strike);
        var count = FontBinary.UInt32(locations, strike + 8);
        for (var i = 0; i < count; i++)
        {
            var entry = FontBinary.Slice(locations, (long)array + (i * 8L), 8);
            var first = FontBinary.UInt16(entry, 0);
            var last = FontBinary.UInt16(entry, 2);
            if (glyphId < first || glyphId > last)
                continue;

            var subtable = FontBinary.AddOffset(array, FontBinary.Offset32(entry, 4));
            return TryGetImage(locations, data, dataOffset, subtable, glyphId - first, glyphId, pixelsPerEmX, strikeSize, out glyph);
        }

        return false;
    }

    private static bool TryGetImage(ReadOnlySpan<byte> locations, ReadOnlySpan<byte> data, int dataOffset, int subtable, int index, int glyphId, int pixelsPerEmX, int pixelsPerEmY, out BitmapGlyph glyph)
    {
        glyph = default;
        var indexFormat = FontBinary.UInt16(locations, subtable);
        var imageFormat = FontBinary.UInt16(locations, subtable + 2);
        var imageData = FontBinary.UInt32(locations, subtable + 4);
        long start;
        long end;
        var metrics = -1;
        switch (indexFormat)
        {
            // An offset per glyph of the range, and one more: 32 bits each.
            case 1:
                start = FontBinary.UInt32(locations, subtable + 8 + (index * 4));
                end = FontBinary.UInt32(locations, subtable + 12 + (index * 4));
                break;

            // Images of one size, with the same metrics.
            case 2:
                {
                    var imageSize = FontBinary.UInt32(locations, subtable + 8);
                    start = (long)index * imageSize;
                    end = start + imageSize;
                    metrics = subtable + 12;
                    break;
                }

            // An offset per glyph of the range, and one more: 16 bits each.
            case 3:
                start = FontBinary.UInt16(locations, subtable + 8 + (index * 2));
                end = FontBinary.UInt16(locations, subtable + 10 + (index * 2));
                break;

            // Pairs of a glyph and an offset, sorted by glyph, and one more for the end of the last image.
            case 4:
                {
                    var count = FontBinary.UInt32(locations, subtable + 8);
                    var pairs = FontBinary.Slice(locations, subtable + 12, ((long)count + 1) * 4);
                    start = end = -1;
                    for (var i = 0; i < count; i++)
                    {
                        if (FontBinary.UInt16(pairs, i * 4) == glyphId)
                        {
                            start = FontBinary.UInt16(pairs, (i * 4) + 2);
                            end = FontBinary.UInt16(pairs, (i * 4) + 6);
                            break;
                        }
                    }

                    if (start < 0)
                        return false;

                    break;
                }

            // Images of one size, with the same metrics, for the glyphs of a list.
            case 5:
                {
                    var imageSize = FontBinary.UInt32(locations, subtable + 8);
                    var count = FontBinary.UInt32(locations, subtable + 20);
                    var glyphs = FontBinary.Slice(locations, subtable + 24, (long)count * 2);
                    start = -1;
                    for (var i = 0; i < count; i++)
                    {
                        if (FontBinary.UInt16(glyphs, i * 2) == glyphId)
                        {
                            start = (long)i * imageSize;
                            break;
                        }
                    }

                    if (start < 0)
                        return false;

                    end = start + imageSize;
                    metrics = subtable + 12;
                    break;
                }

            default:
                return false;
        }

        // A glyph without image has an empty range.
        if (end <= start)
            return false;

        var glyphData = FontBinary.Slice(data, imageData + start, end - start);
        int left;
        int top;
        int header;
        switch (imageFormat)
        {
            // Small metrics (height, width, bearings, advance), the length of the image, the image.
            case 17:
                left = FontBinary.Int8(glyphData, 2);
                top = FontBinary.Int8(glyphData, 3);
                header = 5;
                break;

            // Big metrics, which also have the vertical ones.
            case 18:
                left = FontBinary.Int8(glyphData, 2);
                top = FontBinary.Int8(glyphData, 3);
                header = 8;
                break;

            // The metrics are those of the index subtable.
            case 19 when metrics >= 0:
                left = FontBinary.Int8(locations, metrics + 2);
                top = FontBinary.Int8(locations, metrics + 3);
                header = 0;
                break;

            // The other formats are bitmaps without color, which are not drawn.
            default:
                return false;
        }

        var length = FontBinary.UInt32(glyphData, header);
        if (length > glyphData.Length - header - 4)
            throw FontErrors.InvalidTable(FontTags.Cbdt, "has an image that is longer than the data of its glyph.");

        glyph = new BitmapGlyph(dataOffset + (int)(imageData + start) + header + 4, (int)length, pixelsPerEmX, pixelsPerEmY, left, Bottom: null, top);
        return true;
    }
}
