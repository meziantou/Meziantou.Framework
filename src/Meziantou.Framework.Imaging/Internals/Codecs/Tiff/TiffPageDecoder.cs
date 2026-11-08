using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Decodes the pixels of one TIFF page into an image: strips or tiles are read one block row at a time, expanded to the
/// lossless working representation of the page (<see cref="TiffRowExpander"/>) and written row by row, so the memory of
/// a decode depends on the block geometry and never on the size of the page or of the document. LZW, Deflate and CCITT
/// blocks are decompressed by batches of one block per worker (<see cref="TiffBlockWorkers"/>) and written in order.
/// The blocks of the planes of a planar page are read one place at a time, each plane interleaved into rows of whole
/// pixels as soon as it is decoded, so the expander only ever sees chunky rows.
/// </summary>
/// <remarks>
/// <para>
/// A region (<see cref="ImageDecodeRequest.Region"/>) reads and decompresses only the strips or the tiles that it meets
/// (<see cref="TiffBlockWindow"/>), each of them whole, and expands only the columns of the region (from a byte boundary
/// of the stored rows): the image, the band of tiles and the expanded rows have the width of the region, or of its tiles,
/// not of the page. The pixels are those of a full decode, cropped.
/// </para>
/// <para>
/// A CMYK page is only decoded when the request carries a <see cref="CmykConversionOptions"/>: its rows are converted to
/// RGB by an ICC conversion from the CMYK profile of the page (or the assumed one) to the destination profile, which
/// labels the image. The pixels and their profile change together, and the CMYK profile is not kept. Without the options,
/// or without any source profile, the page is an <see cref="UnsupportedImageFeatureException"/> raised before any
/// allocation. A CMYK page without alpha is also loaded as CMYK pixels (<see cref="PixelFormat.Cmyk32"/>,
/// <see cref="PixelFormat.Cmyk64"/>) when the request asks for them, or for another pixel format with
/// <see cref="PixelConversionOptions.CmykConversion"/>: the inks are the rows, labeled with the CMYK profile.
/// </para>
/// </remarks>
internal static class TiffPageDecoder
{
    /// <summary>Decodes a page into a new image, as a single still frame.</summary>
    /// <param name="source">The input.</param>
    /// <param name="page">The resolved page.</param>
    /// <param name="request">The requested representation and conversions.</param>
    /// <param name="context">The operation context.</param>
    /// <returns>A new image owned by the caller.</returns>
    /// <exception cref="InvalidImageContentException">A strip or tile is missing, malformed or outside the input.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The page stores CMYK samples and the request has no conversion for them.</exception>
    public static Image Decode(RandomAccessSource source, TiffPage page, ImageDecodeRequest request, ImageCodecContext context)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var profile = page.Metadata.IccProfile;
        IccPipeline? inkConversion = null;
        var nativeInks = false;
        var requestsCmyk = request.PixelFormat is { } requested && PixelFormats.IsCmyk(requested);
        if (page.IsCmyk && (request.CmykConversion is null || requestsCmyk))
        {
            // The inks are the rows, labeled with the CMYK profile: the sink converts them when the request asks for
            // another pixel format with a CMYK conversion
            if (!requestsCmyk && (request.PixelFormat is null || request.Conversion.CmykConversion is null))
                throw TiffFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {page.Index} stores CMYK samples, which are only loaded as CMYK pixels (Cmyk32, Cmyk64) or with a color conversion: load the page as Cmyk32 or Cmyk64, or set the CmykConversion property of the decode options."), "PhotometricInterpretation: 5 without color conversion");

            if (page.HasAlpha)
                throw TiffFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {page.Index} stores CMYK samples with alpha, which the CMYK pixel formats cannot hold: set the CmykConversion property of the decode options to load it as RGBA."), "PhotometricInterpretation: 5 with alpha as CMYK pixels");

            nativeInks = true;
        }
        else if (page.IsCmyk)
        {
            // Only a CMYK profile is adopted for a CMYK page. The rows are converted to the destination profile, which is
            // the one that labels the image
            var conversion = request.CmykConversion!;
            var transform = conversion.CreateTransform(profile) ?? throw TiffFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The CMYK TIFF page {page.Index} has no CMYK color profile and CmykConversionOptions.AssumedSourceProfile is null."), "PhotometricInterpretation: 5 without color profile");
            inkConversion = transform.Pipeline;
            profile = conversion.DestinationProfile;
        }
        else if (page.IsCieLab)
        {
            // L*a*b* colors are colorimetric: they are converted to the destination profile, which labels the image
            var conversion = request.CieLabConversion ?? throw TiffFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {page.Index} stores {TiffFormat.GetPhotometricName(page.Photometric)} samples, which are only loaded with a color conversion: set the CieLabConversion property of the decode options."), string.Create(CultureInfo.InvariantCulture, $"PhotometricInterpretation: {page.Photometric} without color conversion"));
            inkConversion = conversion.CreatePipeline(CreateCieLabDecoding(page), CreateCieLabAdaptation(page));
            profile = conversion.DestinationProfile;
        }

        var area = request.GetRegion(page.Size, ImageFormat.Tiff);
        var window = TiffBlockWindow.Create(page, area);
        var sourcePixelFormat = nativeInks ? TiffRowExpander.GetCmykPixelFormat(page) : page.SourcePixelFormat;
        using var sink = DecodedFrameSink.Create(context, request, ImageFormat.Tiff, area.Size, page.DefaultPixelFormat, sourcePixelFormat, profile);
        var scope = source.Scope;

        // The rows of tiles are assembled in a band that starts at the first column of tiles of the window
        var columns = ColumnSpan.Create(page, sourcePixelFormat, page.IsTiled ? window.FirstColumn * page.BlockWidth : 0, area);
        using var expander = new TiffRowExpander(page, inkConversion, scope, columns.Width, nativeInks);
        using var reader = new TiffBlockReader(source, page);
        using var expanded = scope.Rent(expander.ExpandedRowLength, AllocationKind.DecoderState, clear: false);
        sink.BeginFrame(FrameDuration.Zero);
        if (page.IsTiled)
        {
            DecodeTiles(source, page, sink, reader, expander, expanded.Span, area, window, columns);
        }
        else if (page.IsJpeg && !page.IsPlanar)
        {
            DecodeJpegStrips(source, page, sink, reader, expander, expanded, area, window, columns);
        }
        else
        {
            DecodeStrips(source, page, sink, reader, expander, expanded.Span, area, window, columns);
        }

        sink.EndImage();
        return sink.Build(page.Metadata, animation: null);
    }

    /// <summary>
    /// Creates the stage that turns the normalized samples of an L*a*b* page into L* (0 to 100), a* and b*. The expander
    /// gives the signed a* and b* of <c>CIELab</c> as unsigned samples larger by half of their range, which is how
    /// <c>ICCLab</c> stores them (TIFF 6.0 section 23; Adobe Photoshop TIFF Technical Notes): 8-bit a* and b* are integers
    /// from -128 to 127, 16-bit ones multiples of 1/256 from -128 to 128 - 1/256.
    /// </summary>
    private static IccMatrixStage CreateCieLabDecoding(TiffPage page)
    {
        var max = (double)((1 << page.BitsPerSample) - 1);
        var unit = page.BitsPerSample == 8 ? 1 : 256.0;
        var offset = (1 << (page.BitsPerSample - 1)) / unit;
        return IccMatrixStage.CreateScale(100, max / unit, max / unit, 0, -offset, -offset);
    }

    /// <summary>
    /// Creates the stage that turns CIEXYZ relative to the white of the connection space into CIEXYZ adapted from the white
    /// point of the page with the linear Bradford transform, or returns <see langword="null"/> when the page declares none.
    /// </summary>
    private static IccMatrixStage? CreateCieLabAdaptation(TiffPage page)
    {
        if (page.WhitePoint is not var (x, y))
            return null;

        // The colors relative to the white point: the same L*a*b* values give the colorimetry of the connection space scaled by the ratio of the whites
        var (whiteX, whiteY, whiteZ) = (x / y, 1.0, (1 - x - y) / y);
        var relative = new ColorValueMatrix(whiteX / IccColorimetry.D50X, 0, 0, 0, whiteY / IccColorimetry.D50Y, 0, 0, 0, whiteZ / IccColorimetry.D50Z);
        var adaptation = ColorValueMath.GetBradfordAdaptation(whiteX, whiteY, whiteZ, IccColorimetry.D50X, IccColorimetry.D50Y, IccColorimetry.D50Z).Multiply(relative);
        Span<double> matrix = stackalloc double[9];
        adaptation.CopyTo(matrix);
        return new IccMatrixStage(matrix);
    }

    private static void DecodeStrips(RandomAccessSource source, TiffPage page, DecodedFrameSink sink, TiffBlockReader reader, TiffRowExpander expander, Span<byte> expanded, Rectangle area, TiffBlockWindow window, ColumnSpan columns)
    {
        // The rows of whole pixels the planes of a strip are interleaved into
        using var pixels = page.IsPlanar ? RentInterleavedRows(source, page, page.RowLength) : null;

        // The workers are given the live bytes that are left once everything else of the decoding is rented
        using var workers = TiffBlockWorkers.Create(source, page, reader, window);
        using var lease = sink.LeaseCurrentFrame();
        for (var block = window.FirstRow; block < window.RowEnd; block++)
        {
            source.Context.CancellationToken.ThrowIfCancellationRequested();
            var firstRow = block * page.BlockHeight;
            var rowCount = Math.Min(page.BlockHeight, page.Size.Height - firstRow);
            ReadOnlySpan<byte> stored;
            int storedRowLength;
            if (pixels is null)
            {
                stored = workers is null ? reader.ReadBlock(block, rowCount) : workers.Next(block);
                storedRowLength = page.BlockRowLength;
            }
            else
            {
                storedRowLength = (int)page.RowLength;
                for (var plane = 0; plane < page.PlaneCount; plane++)
                {
                    var position = (block * page.PlaneCount) + plane;
                    var samples = workers is null ? reader.ReadBlock(page.GetBlockIndex(position), rowCount) : workers.Next(position);
                    Interleave(page, samples, plane, page.Size.Width, rowCount, pixels.Span, storedRowLength);
                }

                stored = pixels.Span;
            }

            var (start, end) = GetRows(area, firstRow, rowCount);
            for (var row = start; row < end; row++)
            {
                WriteRow(sink, lease, firstRow + row - area.Y, expander, stored.Slice(row * storedRowLength, storedRowLength), expanded, columns);
            }
        }
    }

    /// <summary>Gets the rows of a block, from its first row, that are in the area.</summary>
    private static (int Start, int End) GetRows(Rectangle area, int firstRow, int rowCount)
        => (Math.Max(0, area.Y - firstRow), Math.Min(rowCount, area.Bottom - firstRow));

    /// <summary>Rents the rows of whole pixels the planes of a block row are interleaved into.</summary>
    private static PooledBuffer RentInterleavedRows(RandomAccessSource source, TiffPage page, long rowLength)
    {
        if (!CheckedSizes.TryMultiply(rowLength, page.BlockHeight, out var length) || length > CheckedSizes.MaxBufferLength)
            throw CheckedSizes.CreateOverflowException(source.Limits);

        return source.Scope.Rent((int)length, AllocationKind.DecoderState, clear: false);
    }

    /// <summary>
    /// Copies the samples of one plane of a block (one sample per pixel, rows of <see cref="TiffPage.BlockRowLength"/>
    /// bytes) to their place in rows of whole pixels: the sample <paramref name="plane"/> of each pixel. The samples of a
    /// planar page have at least 8 bits, so each is a whole number of bytes.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <param name="samples">The decoded rows of the plane.</param>
    /// <param name="plane">The plane, which is the index of the sample in a pixel.</param>
    /// <param name="columns">The number of pixels of a row to copy.</param>
    /// <param name="rows">The number of rows to copy.</param>
    /// <param name="destination">The first pixel of the first row of the destination.</param>
    /// <param name="destinationRowLength">The distance between two rows of the destination, in bytes.</param>
    internal static void Interleave(TiffPage page, ReadOnlySpan<byte> samples, int plane, int columns, int rows, Span<byte> destination, int destinationRowLength)
    {
        var bytesPerSample = page.DecodedBitsPerSample / 8;
        var pixelLength = page.SamplesPerPixel * bytesPerSample;
        var sampleOffset = plane * bytesPerSample;
        for (var row = 0; row < rows; row++)
        {
            var source = samples.Slice(row * page.BlockRowLength, columns * bytesPerSample);
            var target = destination[((row * destinationRowLength) + sampleOffset)..];
            switch (bytesPerSample)
            {
                case 1:
                    for (var x = 0; x < columns; x++)
                    {
                        target[x * pixelLength] = source[x];
                    }

                    break;

                case 2:
                    for (var x = 0; x < columns; x++)
                    {
                        target[x * pixelLength] = source[x * 2];
                        target[(x * pixelLength) + 1] = source[(x * 2) + 1];
                    }

                    break;

                default:
                    for (var x = 0; x < columns; x++)
                    {
                        source.Slice(x * bytesPerSample, bytesPerSample).CopyTo(target[(x * pixelLength)..]);
                    }

                    break;
            }
        }
    }

    /// <summary>
    /// Decodes strips that are JPEG datastreams: the rows are expanded and written as the JPEG decoder produces them, so a
    /// page stored as a single strip (what many writers do) costs the working memory of a JPEG file, not a second page.
    /// </summary>
    private static void DecodeJpegStrips(RandomAccessSource source, TiffPage page, DecodedFrameSink sink, TiffBlockReader reader, TiffRowExpander expander, PooledBuffer expanded, Rectangle area, TiffBlockWindow window, ColumnSpan columns)
    {
        var handler = new StripRowHandler(sink, expander, expanded, area, columns);
        for (var block = window.FirstRow; block < window.RowEnd; block++)
        {
            source.Context.CancellationToken.ThrowIfCancellationRequested();
            handler.FirstRow = block * page.BlockHeight;
            reader.ReadJpegStrip(block, Math.Min(page.BlockHeight, page.Size.Height - handler.FirstRow), handler);
        }
    }

    private static void DecodeTiles(RandomAccessSource source, TiffPage page, DecodedFrameSink sink, TiffBlockReader reader, TiffRowExpander expander, Span<byte> expanded, Rectangle area, TiffBlockWindow window, ColumnSpan columns)
    {
        // Tile widths are multiples of 16 pixels, so a tile starts on a byte of the band whatever the sample width. The band
        // holds the columns of tiles of the window
        var bitsPerPixel = page.BitsPerPixel;
        var bandX = window.FirstColumn * page.BlockWidth;
        var bandWidth = Math.Min((long)window.ColumnEnd * page.BlockWidth, page.Size.Width) - bandX;
        var bandRowLength = (int)(((bandWidth * bitsPerPixel) + 7) / 8);
        if (!CheckedSizes.TryMultiply(bandRowLength, page.BlockHeight, out var bandLength) || bandLength > CheckedSizes.MaxBufferLength)
            throw CheckedSizes.CreateOverflowException(source.Limits);

        using var band = source.Scope.Rent((int)bandLength, AllocationKind.DecoderState, clear: false);
        using var workers = TiffBlockWorkers.Create(source, page, reader, window);
        using var lease = sink.LeaseCurrentFrame();
        for (var blockRow = window.FirstRow; blockRow < window.RowEnd; blockRow++)
        {
            source.Context.CancellationToken.ThrowIfCancellationRequested();
            var firstRow = blockRow * page.BlockHeight;
            var rowCount = Math.Min(page.BlockHeight, page.Size.Height - firstRow);
            for (var blockColumn = window.FirstColumn; blockColumn < window.ColumnEnd; blockColumn++)
            {
                var index = (blockRow * page.BlocksAcross) + blockColumn;
                var firstColumn = blockColumn * page.BlockWidth;
                var columnCount = Math.Min(page.BlockWidth, page.Size.Width - firstColumn);
                var bandOffset = (int)((long)(firstColumn - bandX) * bitsPerPixel / 8);
                if (page.IsPlanar)
                {
                    // The tiles of the planes at this place are decoded one after the other and interleaved into the band
                    for (var plane = 0; plane < page.PlaneCount; plane++)
                    {
                        var position = (index * page.PlaneCount) + plane;
                        var samples = workers is null ? reader.ReadBlock(page.GetBlockIndex(position), rowCount) : workers.Next(position);
                        Interleave(page, samples, plane, columnCount, rowCount, band.Span[bandOffset..], bandRowLength);
                    }

                    continue;
                }

                var tile = workers is null ? reader.ReadBlock(index, rowCount) : workers.Next(index);
                var copyLength = (int)((((long)columnCount * bitsPerPixel) + 7) / 8);
                for (var row = 0; row < rowCount; row++)
                {
                    tile.Slice(row * page.BlockRowLength, copyLength)
                        .CopyTo(band.Span.Slice((row * bandRowLength) + bandOffset, copyLength));
                }
            }

            var (start, end) = GetRows(area, firstRow, rowCount);
            for (var row = start; row < end; row++)
            {
                WriteRow(sink, lease, firstRow + row - area.Y, expander, band.Span.Slice(row * bandRowLength, bandRowLength), expanded, columns);
            }
        }
    }

    /// <summary>
    /// Expands the columns of a decoded row that the image holds into the frame, through the scratch row only when the frame
    /// stores another pixel format or when the expansion starts before the first column of the image.
    /// </summary>
    private static void WriteRow(DecodedFrameSink sink, scoped in PixelLease lease, int y, TiffRowExpander expander, ReadOnlySpan<byte> stored, Span<byte> expanded, ColumnSpan columns)
    {
        stored = stored[columns.StoredOffset..];
        if (columns.SkippedBytes == 0)
        {
            var destination = sink.GetDirectRow(lease, y);
            if (!destination.IsEmpty)
            {
                expander.Expand(stored, destination);
                return;
            }
        }

        expander.Expand(stored, expanded);
        sink.WriteRow(lease, y, expanded.Slice(columns.SkippedBytes, columns.ImageBytes));
    }

    /// <summary>
    /// The columns of the rows that are expanded: from a byte boundary of the stored row at or before the first column of the
    /// area, to its last column.
    /// </summary>
    /// <param name="StoredOffset">The byte of the stored row (of the band of tiles for a tiled page) the expansion starts at.</param>
    /// <param name="Width">The number of pixels that are expanded.</param>
    /// <param name="SkippedBytes">The bytes of the expanded row before the first column of the area.</param>
    /// <param name="ImageBytes">The bytes of the expanded row that the image holds.</param>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct ColumnSpan(int StoredOffset, int Width, int SkippedBytes, int ImageBytes)
    {
        /// <summary>Gets the columns of the area, in stored rows that start at the column <paramref name="rowX"/> of the page, expanded to <paramref name="expandedFormat"/>.</summary>
        public static ColumnSpan Create(TiffPage page, PixelFormat expandedFormat, int rowX, Rectangle area)
        {
            // Eight pixels of packed samples are a whole number of bytes, whatever the width of a pixel
            var bitsPerPixel = page.BitsPerPixel;
            var start = bitsPerPixel % 8 == 0 ? area.X : Math.Max(rowX, area.X & ~7);
            var bytesPerPixel = PixelFormats.GetBytesPerPixel(expandedFormat);
            return new ColumnSpan(
                (int)((long)(start - rowX) * bitsPerPixel / 8),
                area.Right - start,
                (area.X - start) * bytesPerPixel,
                area.Width * bytesPerPixel);
        }
    }

    /// <summary>Expands and writes the rows of a strip as its decoder produces them.</summary>
    private sealed class StripRowHandler : TiffBlockRowHandler
    {
        private readonly DecodedFrameSink _sink;
        private readonly TiffRowExpander _expander;
        private readonly PooledBuffer _expanded;
        private readonly Rectangle _area;
        private readonly ColumnSpan _columns;

        public StripRowHandler(DecodedFrameSink sink, TiffRowExpander expander, PooledBuffer expanded, Rectangle area, ColumnSpan columns)
        {
            _sink = sink;
            _expander = expander;
            _expanded = expanded;
            _area = area;
            _columns = columns;
        }

        /// <summary>Gets or sets the row of the page the first row of the current strip is.</summary>
        public int FirstRow { get; set; }

        public override void OnRow(int row, ReadOnlySpan<byte> samples)
        {
            // The rows of the strip outside the area are decoded, not written
            var y = FirstRow + row - _area.Y;
            if (y < 0 || y >= _area.Height)
                return;

            using var lease = _sink.LeaseCurrentFrame();
            WriteRow(_sink, lease, y, _expander, samples, _expanded.Span, _columns);
        }
    }
}
