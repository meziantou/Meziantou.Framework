namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Reads the <c>CBLC</c> and <c>CBDT</c> tables, glyphs as PNG images, and the <c>EBLC</c> and <c>EBDT</c> tables,
/// glyphs as bitmaps without colors: both in strikes made for a number of pixels per em.
/// </summary>
/// <remarks>
/// <para>
/// OpenType specification, "CBLC - Color Bitmap Location Table" and "CBDT - Color Bitmap Data Table". The location
/// table has a record per strike, with index subtables that give, for ranges of glyphs, the place of their data in the
/// data table; the data of a glyph is its metrics (in the data, or once for a range of glyphs) and a PNG image.
/// </para>
/// <para>
/// "EBLC - Embedded Bitmap Location Table" and "EBDT - Embedded Bitmap Data Table" have the same structure; the data
/// of a glyph is its metrics and the bits of its pixels, row by row from the top, the most significant bit first,
/// each row starting at a byte or right after the row before it. Only the strikes of one bit per pixel are read, and
/// not the glyphs made of other glyphs (image formats 8 and 9).
/// </para>
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

    /// <summary>Finds the data of a glyph from an index subtable.</summary>
    /// <param name="locations">The location table.</param>
    /// <param name="data">The data table.</param>
    /// <param name="subtable">The position of the index subtable.</param>
    /// <param name="index">The index of the glyph in the range of the subtable.</param>
    /// <param name="glyphId">The glyph.</param>
    /// <param name="imageFormat">The format of the data.</param>
    /// <param name="metrics">The position, in the location table, of the metrics the glyphs of the subtable share, or -1.</param>
    /// <param name="glyphData">The data of the glyph.</param>
    /// <param name="dataStart">The position of the data of the glyph in the data table.</param>
    private static bool TryLocate(ReadOnlySpan<byte> locations, ReadOnlySpan<byte> data, int subtable, int index, int glyphId, out int imageFormat, out int metrics, out ReadOnlySpan<byte> glyphData, out long dataStart)
    {
        glyphData = default;
        dataStart = 0;
        var indexFormat = FontBinary.UInt16(locations, subtable);
        imageFormat = FontBinary.UInt16(locations, subtable + 2);
        var imageData = FontBinary.UInt32(locations, subtable + 4);
        long start;
        long end;
        metrics = -1;
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

        glyphData = FontBinary.Slice(data, imageData + start, end - start);
        dataStart = imageData + start;
        return true;
    }

    private static bool TryGetImage(ReadOnlySpan<byte> locations, ReadOnlySpan<byte> data, int dataOffset, int subtable, int index, int glyphId, int pixelsPerEmX, int pixelsPerEmY, out BitmapGlyph glyph)
    {
        glyph = default;
        if (!TryLocate(locations, data, subtable, index, glyphId, out var imageFormat, out var metrics, out var glyphData, out var start))
            return false;

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

        glyph = new BitmapGlyph(dataOffset + (int)start + header + 4, (int)length, pixelsPerEmX, pixelsPerEmY, left, Bottom: null, top, ImageFormat.Png);
        return true;
    }

    /// <summary>Finds the bitmap of a glyph in the strike of one bit per pixel that is made for a size.</summary>
    /// <param name="locations">The location table.</param>
    /// <param name="data">The data table.</param>
    /// <param name="glyphId">The glyph.</param>
    /// <param name="pixelsPerEm">The size, in pixels per em.</param>
    /// <param name="bitmap">The bitmap.</param>
    /// <returns><see langword="false"/> when the font has no such strike, or when the strike has no bitmap for the glyph.</returns>
    public bool TryGetMonochromeGlyph(ReadOnlySpan<byte> locations, ReadOnlySpan<byte> data, int glyphId, int pixelsPerEm, out MonochromeBitmap bitmap)
    {
        bitmap = default;
        for (var i = 0; i < _sizeCount; i++)
        {
            // A strike for this size, horizontally and vertically, of one bit per pixel, that has the glyph in its range.
            var strike = 8 + (i * SizeRecordLength);
            if (FontBinary.UInt8(locations, strike + 44) != pixelsPerEm || FontBinary.UInt8(locations, strike + 45) != pixelsPerEm || FontBinary.UInt8(locations, strike + 46) != 1)
                continue;

            if (glyphId < FontBinary.UInt16(locations, strike + 40) || glyphId > FontBinary.UInt16(locations, strike + 42))
                continue;

            var array = FontBinary.Offset32(locations, strike);
            var count = FontBinary.UInt32(locations, strike + 8);
            for (var j = 0; j < count; j++)
            {
                var entry = FontBinary.Slice(locations, (long)array + (j * 8L), 8);
                var first = FontBinary.UInt16(entry, 0);
                var last = FontBinary.UInt16(entry, 2);
                if (glyphId < first || glyphId > last)
                    continue;

                var subtable = FontBinary.AddOffset(array, FontBinary.Offset32(entry, 4));
                if (TryGetBits(locations, data, subtable, glyphId - first, glyphId, pixelsPerEm, out bitmap))
                    return true;
            }
        }

        return false;
    }

    private static bool TryGetBits(ReadOnlySpan<byte> locations, ReadOnlySpan<byte> data, int subtable, int index, int glyphId, int pixelsPerEm, out MonochromeBitmap bitmap)
    {
        bitmap = default;
        if (!TryLocate(locations, data, subtable, index, glyphId, out var imageFormat, out var sharedMetrics, out var glyphData, out var start))
            return false;

        // The metrics start with the height, the width and the place of the top left corner from the origin.
        ReadOnlySpan<byte> metrics;
        int header;
        bool isByteAligned;
        switch (imageFormat)
        {
            // Small metrics, then rows that each start at a byte (1) or bits that follow each other (2).
            case 1 or 2:
                metrics = glyphData;
                header = 5;
                isByteAligned = imageFormat == 1;
                break;

            // The metrics of the index subtable, then bits that follow each other.
            case 5 when sharedMetrics >= 0:
                metrics = locations[sharedMetrics..];
                header = 0;
                isByteAligned = false;
                break;

            // Big metrics, then rows that each start at a byte (6) or bits that follow each other (7).
            case 6 or 7:
                metrics = glyphData;
                header = 8;
                isByteAligned = imageFormat == 6;
                break;

            default:
                return false;
        }

        var height = FontBinary.UInt8(metrics, 0);
        var width = FontBinary.UInt8(metrics, 1);
        var left = FontBinary.Int8(metrics, 2);
        var top = FontBinary.Int8(metrics, 3);
        var length = isByteAligned ? ((width + 7) / 8) * height : ((width * height) + 7) / 8;
        if (length > glyphData.Length - header)
            throw FontErrors.InvalidTable(FontTags.Ebdt, "has a bitmap that is longer than the data of its glyph.");

        bitmap = new MonochromeBitmap((int)start + header, length, width, height, left, top, pixelsPerEm, isByteAligned);
        return true;
    }
}
