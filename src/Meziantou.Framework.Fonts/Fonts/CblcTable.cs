namespace Meziantou.Framework.Fonts;

/// <summary>
/// Reads the <c>CBLC</c> and <c>CBDT</c> tables, glyphs as images in color, and the <c>EBLC</c> and <c>EBDT</c> tables
/// (<c>bloc</c> and <c>bdat</c> in the fonts of Apple), glyphs as bitmaps without colors: both in strikes made for a
/// number of pixels per em.
/// </summary>
/// <remarks>
/// <para>
/// OpenType specification, "CBLC - Color Bitmap Location Table" and "CBDT - Color Bitmap Data Table". The location
/// table has a record per strike, with index subtables that give, for ranges of glyphs, the place of their data in the
/// data table; the data of a glyph is its metrics (in the data, or once for a range of glyphs) and a PNG image (image
/// formats 17, 18 and 19), or the pixels of the formats of <c>EBDT</c>.
/// </para>
/// <para>
/// "EBLC - Embedded Bitmap Location Table" and "EBDT - Embedded Bitmap Data Table" have the same structure; the data
/// of a glyph is its metrics and its pixels, row by row from the top, the most significant bit first, each row starting
/// at a byte (image formats 1 and 6) or right after the row before it (2, 5 and 7). A pixel has the number of bits of
/// its strike: 1, 2, 4 or 8 for its coverage, from 0 (nothing) to the largest value (covered), 32 in <c>CBDT</c> for
/// blue, green, red and opacity, the colors multiplied by the opacity. A glyph can be made of other glyphs of its
/// strike (image formats 8 and 9), placed by the offset of their top left corner from the top left corner of its box.
/// The Apple TrueType Reference Manual ("The 'bloc' table", "The 'bdat' table") gives the same structure to the tables
/// of Apple.
/// </para>
/// </remarks>
internal sealed class CblcTable
{
    private const int SizeRecordLength = 48;

    // A component of a component of ...: a font cannot nest its glyphs deeper than that
    private const int MaxCompositeDepth = 8;

    private readonly int _sizeCount;
    private readonly uint _dataTag;

    private CblcTable(int sizeCount, uint dataTag)
    {
        _sizeCount = sizeCount;
        _dataTag = dataTag;
    }

    /// <summary>Reads the header of the location table, or returns <see langword="null"/> when it has no strike or a version that is not known.</summary>
    /// <param name="table">The location table.</param>
    /// <param name="dataTag">The tag of the data table, for the messages of the errors.</param>
    public static CblcTable? Read(ReadOnlySpan<byte> table, uint dataTag)
    {
        if (table.Length < 8 || FontBinary.UInt16(table, 0) is not (2 or 3))
            return null;

        var sizeCount = FontBinary.UInt32(table, 4);
        if (sizeCount == 0 || sizeCount > (table.Length - 8) / SizeRecordLength)
            return null;

        return new CblcTable((int)sizeCount, dataTag);
    }

    /// <summary>Finds the image of a glyph in a strike that suits a size.</summary>
    /// <param name="locations">The location table.</param>
    /// <param name="data">The data table.</param>
    /// <param name="dataOffset">The position of the data table in the bytes of the font.</param>
    /// <param name="glyphId">The glyph.</param>
    /// <param name="size">The size the glyph is drawn at, in pixels per em.</param>
    /// <param name="isExactSize">
    /// Whether only a strike made for exactly this size is used; otherwise, the smallest strike that is at least as
    /// large as the size (it is scaled down), else the largest one.
    /// </param>
    /// <param name="isMonochromeTable">Whether the tables are <c>EBLC</c> and <c>EBDT</c> (or <c>bloc</c> and <c>bdat</c>).</param>
    /// <param name="glyph">The image.</param>
    /// <returns><see langword="false"/> when no such strike has an image for the glyph.</returns>
    public bool TryGetGlyph(ReadOnlySpan<byte> locations, ReadOnlySpan<byte> data, int dataOffset, int glyphId, double size, bool isExactSize, bool isMonochromeTable, out BitmapGlyph glyph)
    {
        glyph = default;
        if (isExactSize)
        {
            if (size is < 1 or > 255 || size != Math.Floor(size))
                return false;

            // The first strike for this size, horizontally and vertically, that has the glyph.
            for (var i = 0; i < _sizeCount; i++)
            {
                var record = 8 + (i * SizeRecordLength);
                if (FontBinary.UInt8(locations, record + 44) == size && FontBinary.UInt8(locations, record + 45) == size
                    && IsInRange(locations, record, glyphId) && TryGetGlyphInStrike(locations, data, dataOffset, record, glyphId, isMonochromeTable, out glyph))
                {
                    return true;
                }
            }

            return false;
        }

        // The smallest strike that is at least as large as the size: it is scaled down. Else the largest one. Only the
        // strikes whose range has the glyph are candidates.
        var strike = -1;
        var strikeSize = 0;
        for (var i = 0; i < _sizeCount; i++)
        {
            var record = 8 + (i * SizeRecordLength);
            if (!IsInRange(locations, record, glyphId))
                continue;

            var pixelsPerEm = FontBinary.UInt8(locations, record + 45);
            if (strike < 0 || (strikeSize < size ? pixelsPerEm > strikeSize : pixelsPerEm >= size && pixelsPerEm < strikeSize))
            {
                strike = record;
                strikeSize = pixelsPerEm;
            }
        }

        return strike >= 0 && TryGetGlyphInStrike(locations, data, dataOffset, strike, glyphId, isMonochromeTable, out glyph);
    }

    private static bool IsInRange(ReadOnlySpan<byte> locations, int strike, int glyphId)
        => glyphId >= FontBinary.UInt16(locations, strike + 40) && glyphId <= FontBinary.UInt16(locations, strike + 42);

    /// <summary>Finds the image of a glyph in a strike.</summary>
    private bool TryGetGlyphInStrike(ReadOnlySpan<byte> locations, ReadOnlySpan<byte> data, int dataOffset, int strike, int glyphId, bool isMonochromeTable, out BitmapGlyph glyph)
    {
        glyph = default;
        var pixelsPerEmX = FontBinary.UInt8(locations, strike + 44);
        var pixelsPerEmY = FontBinary.UInt8(locations, strike + 45);
        if (pixelsPerEmX == 0 || pixelsPerEmY == 0 || !TryLocateImage(locations, data, strike, glyphId, out var image))
            return false;

        // The encoded images of CBDT have their length before them.
        var offset = dataOffset + (int)image.Start + image.HeaderLength;
        var length = image.Data.Length - image.HeaderLength;
        var format = BitmapGlyphFormat.Png;
        if (image.Kind == ImageKind.Png)
        {
            var pngLength = FontBinary.UInt32(image.Data, image.HeaderLength);
            if (pngLength > image.Data.Length - image.HeaderLength - 4)
                throw FontErrors.InvalidTable(_dataTag, "has an image that is longer than the data of its glyph.");

            offset += 4;
            length = (int)pngLength;
        }
        else
        {
            format = image.BitDepth == 32 ? BitmapGlyphFormat.Bgra : BitmapGlyphFormat.Mask;
        }

        glyph = new BitmapGlyph(offset, length, pixelsPerEmX, pixelsPerEmY, image.Left, Bottom: null, image.Top, format)
        {
            Width = image.Width,
            Height = image.Height,
            BitDepth = image.BitDepth,
            GlyphId = glyphId,
            Strike = strike,
            IsMonochromeTable = isMonochromeTable,
        };
        return true;
    }

    /// <summary>
    /// Decodes the pixels of a glyph that are not encoded (<see cref="BitmapGlyphFormat.Mask"/> and
    /// <see cref="BitmapGlyphFormat.Bgra"/>), with the glyphs it is made of: four bytes per pixel, red, green, blue and
    /// opacity, the colors multiplied by the opacity; a mask is black. The pixels must be transparent.
    /// </summary>
    /// <param name="locations">The location table.</param>
    /// <param name="data">The data table.</param>
    /// <param name="glyph">The glyph, from <see cref="TryGetGlyph"/>.</param>
    /// <param name="pixels">The pixels of the box of the glyph, row by row from the top.</param>
    /// <param name="budget">The budget of the glyph: a row of pixels and a component cost one operation each.</param>
    public void Decode(ReadOnlySpan<byte> locations, ReadOnlySpan<byte> data, in BitmapGlyph glyph, Span<byte> pixels, ref GlyphBudget budget)
        => Draw(locations, data, glyph.Strike, glyph.GlyphId, pixels, coverage: default, glyph.Width, glyph.Height, 0, 0, 0, ref budget);

    /// <summary>
    /// Decodes the coverage of the pixels of a mask (<see cref="BitmapGlyphFormat.Mask"/>), with the glyphs it is made
    /// of: a number from 0 to 1 per pixel. The coverage must be zero.
    /// </summary>
    /// <param name="locations">The location table.</param>
    /// <param name="data">The data table.</param>
    /// <param name="glyph">The glyph, from <see cref="TryGetGlyph"/>.</param>
    /// <param name="coverage">The coverage of the pixels of the box of the glyph, row by row from the top.</param>
    /// <param name="budget">The budget of the glyph: a row of pixels and a component cost one operation each.</param>
    public void DecodeCoverage(ReadOnlySpan<byte> locations, ReadOnlySpan<byte> data, in BitmapGlyph glyph, Span<double> coverage, ref GlyphBudget budget)
        => Draw(locations, data, glyph.Strike, glyph.GlyphId, pixels: default, coverage, glyph.Width, glyph.Height, 0, 0, 0, ref budget);

    /// <summary>Draws a glyph in the pixels of a box, or in its coverage when <paramref name="pixels"/> is empty.</summary>
    private void Draw(ReadOnlySpan<byte> locations, ReadOnlySpan<byte> data, int strike, int glyphId, Span<byte> pixels, Span<double> coverage, int width, int height, int x, int y, int depth, ref GlyphBudget budget)
    {
        budget.Charge(1);

        // A component the strike does not have, or that is an encoded image, draws nothing.
        if (!IsInRange(locations, strike, glyphId) || !TryLocateImage(locations, data, strike, glyphId, out var image))
            return;

        switch (image.Kind)
        {
            case ImageKind.Composite:
                if (depth >= MaxCompositeDepth)
                    throw FontErrors.InvalidTable(_dataTag, $"has glyphs made of glyphs nested deeper than {MaxCompositeDepth} levels.");

                for (var i = 0; i < image.ComponentCount; i++)
                {
                    var component = image.Data.Slice(image.HeaderLength + 2 + (i * 4), 4);
                    Draw(locations, data, strike, FontBinary.UInt16(component, 0), pixels, coverage, width, height, x + FontBinary.Int8(component, 2), y + FontBinary.Int8(component, 3), depth + 1, ref budget);
                }

                break;

            case ImageKind.Pixels:
                DrawPixels(image, pixels, coverage, width, height, x, y, ref budget);
                break;
        }
    }

    /// <summary>Draws the pixels of a glyph over the pixels of a box, with its top left corner at a place of the box: what is outside the box is left out.</summary>
    private static void DrawPixels(in ImageData image, Span<byte> pixels, Span<double> coverage, int width, int height, int x, int y, ref GlyphBudget budget)
    {
        var bits = image.Data[image.HeaderLength..];
        var depth = image.BitDepth;
        var rowBits = GetRowBits(image.Width, depth, image.IsByteAligned);
        var maximum = depth == 32 ? 0 : (1 << depth) - 1;
        for (var row = 0; row < image.Height; row++)
        {
            budget.Charge(1);
            var targetY = y + row;
            if ((uint)targetY >= (uint)height)
                continue;

            for (var column = 0; column < image.Width; column++)
            {
                var targetX = x + column;
                if ((uint)targetX >= (uint)width)
                    continue;

                var index = (row * rowBits) + (column * depth);
                if (pixels.IsEmpty)
                {
                    // The components of a glyph cover its pixels together; colors are opaque.
                    var value = depth == 32 ? bits[(int)(index >> 3) + 3] / 255.0 : (double)ReadBits(bits, index, depth) / maximum;
                    coverage[(targetY * width) + targetX] = Math.Max(coverage[(targetY * width) + targetX], value);
                    continue;
                }

                var target = pixels.Slice(((targetY * width) + targetX) * 4, 4);
                if (depth == 32)
                {
                    // Blue, green, red and opacity, multiplied by it: drawn over what is there.
                    var source = bits.Slice((int)(index >> 3), 4);
                    var inverse = 255 - source[3];
                    target[0] = (byte)Math.Min(255, source[2] + ((target[0] * inverse) + 127) / 255);
                    target[1] = (byte)Math.Min(255, source[1] + ((target[1] * inverse) + 127) / 255);
                    target[2] = (byte)Math.Min(255, source[0] + ((target[2] * inverse) + 127) / 255);
                    target[3] = (byte)(source[3] + ((target[3] * inverse) + 127) / 255);
                }
                else
                {
                    // A coverage: the components of a glyph cover its pixels together.
                    var value = ReadBits(bits, index, depth);
                    target[3] = Math.Max(target[3], (byte)(((value * 255) + (maximum / 2)) / maximum));
                }
            }
        }
    }

    /// <summary>Reads a value of 1, 2, 4 or 8 bits, the most significant bit first: the values of these sizes never span two bytes.</summary>
    private static int ReadBits(ReadOnlySpan<byte> bits, long index, int depth)
    {
        var value = bits[(int)(index >> 3)];
        var shift = 8 - depth - (int)(index & 7);
        return (value >> shift) & ((1 << depth) - 1);
    }

    private static long GetRowBits(int width, int depth, bool isByteAligned)
    {
        var bits = (long)width * depth;
        return isByteAligned ? (bits + 7) & ~7L : bits;
    }

    /// <summary>
    /// Emits the pixels of a glyph of one bit per pixel that is not made of other glyphs as an outline: a rectangle for
    /// each run of pixels of each row, in font units. The rectangles of two rows share an edge and do not overlap:
    /// filled, they cover exactly the pixels of the bitmap.
    /// </summary>
    /// <returns><see langword="false"/> when the glyph is made of other glyphs: decode it with <see cref="Decode"/>.</returns>
    public bool TryEmitBits(ReadOnlySpan<byte> locations, ReadOnlySpan<byte> data, in BitmapGlyph glyph, double unitsPerPixelX, double unitsPerPixelY, OutlineSink sink, ref GlyphBudget budget)
    {
        if (!TryLocateImage(locations, data, glyph.Strike, glyph.GlyphId, out var image) || image.Kind != ImageKind.Pixels || image.BitDepth != 1)
            return false;

        var bits = image.Data[image.HeaderLength..];
        var rowBits = GetRowBits(image.Width, 1, image.IsByteAligned);
        for (var row = 0; row < image.Height; row++)
        {
            budget.Charge(1);
            var first = row * rowBits;
            for (var column = 0; column < image.Width;)
            {
                if (ReadBits(bits, first + column, 1) == 0)
                {
                    column++;
                    continue;
                }

                var end = column + 1;
                while (end < image.Width && ReadBits(bits, first + end, 1) != 0)
                {
                    end++;
                }

                budget.Charge(1);
                EmitRectangle(sink, glyph.Left + column, glyph.Left + end, glyph.Top - row, unitsPerPixelX, unitsPerPixelY);
                column = end;
            }
        }

        return true;
    }

    /// <summary>Emits the rectangle of a run of pixels, in the direction of the contours of a TrueType outline.</summary>
    /// <param name="sink">The sink.</param>
    /// <param name="left">The left of the run, in pixels from the origin.</param>
    /// <param name="right">The right of the run, in pixels from the origin.</param>
    /// <param name="top">The top of the row, in pixels from the baseline.</param>
    /// <param name="unitsPerPixelX">The width of a pixel, in font units.</param>
    /// <param name="unitsPerPixelY">The height of a pixel, in font units.</param>
    public static void EmitRectangle(OutlineSink sink, int left, int right, int top, double unitsPerPixelX, double unitsPerPixelY)
    {
        var x0 = left * unitsPerPixelX;
        var x1 = right * unitsPerPixelX;
        var y1 = top * unitsPerPixelY;
        var y0 = (top - 1) * unitsPerPixelY;
        sink.MoveTo(x0, y0);
        sink.LineTo(x0, y1);
        sink.LineTo(x1, y1);
        sink.LineTo(x1, y0);
        sink.LineTo(x0, y0);
        sink.Close();
    }

    /// <summary>Finds the data of a glyph in a strike, and reads its metrics.</summary>
    private bool TryLocateImage(ReadOnlySpan<byte> locations, ReadOnlySpan<byte> data, int strike, int glyphId, out ImageData image)
    {
        image = default;

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
            if (!TryLocate(locations, data, subtable, glyphId - first, glyphId, out var imageFormat, out var sharedMetrics, out var glyphData, out var start))
                return false;

            return TryReadImage(locations, imageFormat, sharedMetrics, glyphData, start, FontBinary.UInt8(locations, strike + 46), out image);
        }

        return false;
    }

    /// <summary>Reads the metrics of the data of a glyph, and checks that the data has what its metrics say.</summary>
    private bool TryReadImage(ReadOnlySpan<byte> locations, int imageFormat, int sharedMetrics, ReadOnlySpan<byte> glyphData, long start, int bitDepth, out ImageData image)
    {
        image = default;
        ReadOnlySpan<byte> metrics;
        int header;
        var kind = ImageKind.Pixels;
        var isByteAligned = false;
        switch (imageFormat)
        {
            // Small metrics (height, width, bearings, advance), then rows that each start at a byte (1) or bits that
            // follow each other (2). Big metrics, which also have the vertical ones (6 and 7).
            case 1 or 2 or 6 or 7:
                metrics = glyphData;
                header = imageFormat <= 2 ? 5 : 8;
                isByteAligned = imageFormat is 1 or 6;
                break;

            // The metrics of the index subtable, then bits that follow each other.
            case 5 when sharedMetrics >= 0:
                metrics = locations[sharedMetrics..];
                header = 0;
                break;

            // Small metrics and a byte of padding (8), or big metrics (9), then the glyphs the glyph is made of.
            case 8 or 9:
                metrics = glyphData;
                header = imageFormat == 8 ? 6 : 8;
                kind = ImageKind.Composite;
                break;

            // Small metrics (17), big metrics (18), the metrics of the index subtable (19), then the length of a PNG image and the image.
            case 17 or 18:
                metrics = glyphData;
                header = imageFormat == 17 ? 5 : 8;
                kind = ImageKind.Png;
                break;

            case 19 when sharedMetrics >= 0:
                metrics = locations[sharedMetrics..];
                header = 0;
                kind = ImageKind.Png;
                break;

            default:
                return false;
        }

        if (kind != ImageKind.Png && bitDepth is not (1 or 2 or 4 or 8 or 32))
            return false;

        var height = FontBinary.UInt8(metrics, 0);
        var width = FontBinary.UInt8(metrics, 1);
        var left = FontBinary.Int8(metrics, 2);
        var top = FontBinary.Int8(metrics, 3);
        var componentCount = 0;
        switch (kind)
        {
            case ImageKind.Pixels:
                if (((GetRowBits(width, bitDepth, isByteAligned) * height) + 7) / 8 > glyphData.Length - header)
                    throw FontErrors.InvalidTable(_dataTag, "has a bitmap that is longer than the data of its glyph.");

                break;

            case ImageKind.Composite:
                componentCount = FontBinary.UInt16(glyphData, header);
                if (header + 2 + (componentCount * 4L) > glyphData.Length)
                    throw FontErrors.InvalidTable(_dataTag, "has a glyph whose components are longer than its data.");

                break;

            case ImageKind.Png when header + 4 > glyphData.Length:
                throw FontErrors.InvalidTable(_dataTag, "has an image that is longer than the data of its glyph.");
        }

        image = new ImageData(kind, glyphData, start, header, width, height, left, top, kind == ImageKind.Png ? 32 : bitDepth, isByteAligned, componentCount);
        return true;
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

    private enum ImageKind
    {
        Pixels,
        Composite,
        Png,
    }

    /// <summary>The data of a glyph and its metrics.</summary>
    private readonly ref struct ImageData
    {
        public ImageData(ImageKind kind, ReadOnlySpan<byte> data, long start, int headerLength, int width, int height, int left, int top, int bitDepth, bool isByteAligned, int componentCount)
        {
            Kind = kind;
            Data = data;
            Start = start;
            HeaderLength = headerLength;
            Width = width;
            Height = height;
            Left = left;
            Top = top;
            BitDepth = bitDepth;
            IsByteAligned = isByteAligned;
            ComponentCount = componentCount;
        }

        /// <summary>Gets what the data is.</summary>
        public ImageKind Kind { get; }

        /// <summary>Gets the data of the glyph, with its metrics.</summary>
        public ReadOnlySpan<byte> Data { get; }

        /// <summary>Gets the position of the data in the data table.</summary>
        public long Start { get; }

        /// <summary>Gets the length of the metrics at the start of the data, and of the padding after them.</summary>
        public int HeaderLength { get; }

        /// <summary>Gets the width of the image, in pixels.</summary>
        public int Width { get; }

        /// <summary>Gets the height of the image, in pixels.</summary>
        public int Height { get; }

        /// <summary>Gets the distance from the origin to the left of the image, in pixels.</summary>
        public int Left { get; }

        /// <summary>Gets the distance from the baseline up to the top of the image, in pixels.</summary>
        public int Top { get; }

        /// <summary>Gets the number of bits per pixel.</summary>
        public int BitDepth { get; }

        /// <summary>Gets a value indicating whether each row of pixels starts at a byte.</summary>
        public bool IsByteAligned { get; }

        /// <summary>Gets the number of glyphs a composite glyph is made of.</summary>
        public int ComponentCount { get; }
    }
}
