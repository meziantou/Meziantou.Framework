namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Reads the <c>sbix</c> table: glyphs as images, in strikes made for a number of pixels per em.</summary>
/// <remarks>
/// OpenType specification, "sbix - Standard Bitmap Graphics Table". A strike has, for each glyph, an image in a
/// standard format (PNG, JPEG or TIFF) and the position of its lower left corner from the origin of the glyph. A glyph
/// can refer to the image of another glyph.
/// </remarks>
internal sealed class SbixTable
{
    private const uint Png = ((uint)'p' << 24) | ((uint)'n' << 16) | ((uint)'g' << 8) | ' ';
    private const uint Jpeg = ((uint)'j' << 24) | ((uint)'p' << 16) | ((uint)'g' << 8) | ' ';
    private const uint Tiff = ((uint)'t' << 24) | ((uint)'i' << 16) | ((uint)'f' << 8) | 'f';
    private const uint Duplicate = ((uint)'d' << 24) | ((uint)'u' << 16) | ((uint)'p' << 8) | 'e';

    private readonly int _strikeCount;
    private readonly int _glyphCount;

    private SbixTable(int strikeCount, int glyphCount)
    {
        _strikeCount = strikeCount;
        _glyphCount = glyphCount;
    }

    /// <summary>Reads the header of the table, or returns <see langword="null"/> when it has no strike or a version that is not known.</summary>
    public static SbixTable? Read(ReadOnlySpan<byte> table, int glyphCount)
    {
        if (table.Length < 8 || FontBinary.UInt16(table, 0) != 1)
            return null;

        var strikeCount = FontBinary.UInt32(table, 4);
        if (strikeCount == 0 || strikeCount > (table.Length - 8) / 4)
            return null;

        return new SbixTable((int)strikeCount, glyphCount);
    }

    /// <summary>Finds the image of a glyph in the strike that suits a size.</summary>
    /// <param name="table">The table.</param>
    /// <param name="tableOffset">The position of the table in the bytes of the font.</param>
    /// <param name="glyphId">The glyph.</param>
    /// <param name="size">The size the glyph is drawn at, in pixels per em.</param>
    /// <param name="glyph">The image.</param>
    /// <returns><see langword="false"/> when the strike has no image for the glyph, or an image of a type the specification does not have.</returns>
    public bool TryGetGlyph(ReadOnlySpan<byte> table, int tableOffset, int glyphId, double size, out BitmapGlyph glyph)
    {
        glyph = default;
        if ((uint)glyphId >= (uint)_glyphCount)
            return false;

        // The smallest strike that is at least as large as the size: it is scaled down. Else the largest one.
        var strike = 0;
        var strikeSize = 0;
        for (var i = 0; i < _strikeCount; i++)
        {
            var offset = FontBinary.Offset32(table, 8 + (i * 4));
            var pixelsPerEm = FontBinary.UInt16(table, offset);
            if (strike == 0 || (strikeSize < size ? pixelsPerEm > strikeSize : pixelsPerEm >= size && pixelsPerEm < strikeSize))
            {
                strike = offset;
                strikeSize = pixelsPerEm;
            }
        }

        if (strikeSize == 0)
            return false;

        // A glyph can stand for the image of another glyph, which can do the same: a few steps are followed.
        for (var step = 0; step < 8; step++)
        {
            var start = FontBinary.UInt32(table, strike + 4 + (glyphId * 4));
            var end = FontBinary.UInt32(table, strike + 8 + (glyphId * 4));
            if (end < start || end - start < 8)
                return false;

            var data = FontBinary.Slice(table, (long)strike + start, end - start);
            var type = FontBinary.UInt32(data, 4);
            if (type == Duplicate)
            {
                if (data.Length < 10)
                    return false;

                glyphId = FontBinary.UInt16(data, 8);
                if ((uint)glyphId >= (uint)_glyphCount)
                    return false;

                continue;
            }

            // The three types of the specification. Another type (the PDF and mask data of older fonts) is no image.
            var format = type switch
            {
                Png => ImageFormat.Png,
                Jpeg => ImageFormat.Jpeg,
                Tiff => ImageFormat.Tiff,
                _ => ImageFormat.Unknown,
            };
            if (format == ImageFormat.Unknown)
                return false;

            var left = FontBinary.Int16(data, 0);
            var bottom = FontBinary.Int16(data, 2);
            glyph = new BitmapGlyph(tableOffset + strike + (int)start + 8, data.Length - 8, strikeSize, strikeSize, left, bottom, 0, format);
            return true;
        }

        return false;
    }
}
