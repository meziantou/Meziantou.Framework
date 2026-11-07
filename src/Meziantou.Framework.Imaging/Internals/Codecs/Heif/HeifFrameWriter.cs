namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Converts the decoded planes of an image of the HEIF family to pixels, and writes them to the frame sink: the whole image at
/// once (<see cref="WriteFrame"/>), or bands of rows as their planes are decoded (<see cref="WriteRows"/>).
/// </summary>
internal sealed class HeifFrameWriter : IDisposable
{
    private readonly ImageCodecContext _context;
    private readonly HeifYuvConverter _converter;
    private readonly Av1PooledArray<ushort> _rows;
    private readonly PooledBuffer _pixels;
    private readonly int _width;
    private readonly int _height;
    private readonly Rectangle _crop;

    /// <summary>Creates the converter of an image, and rents its rows.</summary>
    /// <param name="context">The operation.</param>
    /// <param name="structure">The walker of the file: its color description and its pixel layout.</param>
    /// <param name="color">The sample format of the color image.</param>
    /// <param name="alpha">The sample format of the alpha plane, or <see langword="null"/> for an opaque image.</param>
    /// <param name="codedSize">The size of the image before its clean aperture.</param>
    /// <param name="crop">The part of the image that the clean aperture keeps.</param>
    /// <exception cref="UnsupportedImageFeatureException">The matrix coefficients are not supported.</exception>
    /// <exception cref="InvalidImageContentException">The identity matrix is used with subsampled chroma.</exception>
    /// <exception cref="ImageResourceLimitException">The rows exceed the live-allocation limit.</exception>
    public HeifFrameWriter(ImageCodecContext context, HeifStructureParser structure, in HeifBitstreamFormat color, HeifBitstreamFormat? alpha, Size codedSize, Rectangle crop)
        : this(context, structure, color, alpha, codedSize, crop, structure.SourcePixelFormat)
    {
    }

    /// <summary>Creates the converter of an image to rows of <paramref name="layout"/>, and rents its rows.</summary>
    /// <param name="context">The operation.</param>
    /// <param name="structure">The walker of the file: its color description and its pixel layout.</param>
    /// <param name="color">The sample format of the color image.</param>
    /// <param name="alpha">The sample format of the alpha plane, or <see langword="null"/> for an opaque image.</param>
    /// <param name="codedSize">The size of the image before its clean aperture.</param>
    /// <param name="crop">The part of the image that the clean aperture keeps.</param>
    /// <param name="layout">The layout of the rows: the source pixel format of the sink.</param>
    /// <exception cref="UnsupportedImageFeatureException">The matrix coefficients are not supported.</exception>
    /// <exception cref="InvalidImageContentException">The identity matrix is used with subsampled chroma.</exception>
    /// <exception cref="ImageResourceLimitException">The rows exceed the live-allocation limit.</exception>
    public HeifFrameWriter(ImageCodecContext context, HeifStructureParser structure, in HeifBitstreamFormat color, HeifBitstreamFormat? alpha, Size codedSize, Rectangle crop, PixelFormat layout)
    {
        // A 'colr' box of type 'nclx' describes the colors of the image; otherwise the bitstream does
        var nclx = structure.ContainerColorDescription;
        var matrix = nclx?.MatrixCoefficients ?? color.MatrixCoefficients;
        var fullRange = nclx?.FullRange ?? color.FullRange;
        var primaries = nclx?.ColorPrimaries ?? color.ColorPrimaries;
        var transfer = nclx?.TransferCharacteristics ?? color.TransferCharacteristics;
        _converter = new HeifYuvConverter(structure.Binding, color, matrix, fullRange, alpha, layout, alpha is not null && structure.IsPremultiplied, primaries, transfer);
        _context = context;
        _width = codedSize.Width;
        _height = codedSize.Height;
        _crop = crop;
        _rows = Av1PooledArray<ushort>.Rent(context.Scope, 2L * _width, clear: false);
        try
        {
            _pixels = context.Scope.Rent(checked(_width * _converter.BytesPerPixel), AllocationKind.DecoderState, clear: false);
        }
        catch
        {
            _rows.Dispose();
            throw;
        }
    }

    /// <summary>Converts the planes of an image to pixels, and writes the rows of the clean aperture to the current frame of the sink.</summary>
    /// <param name="context">The operation.</param>
    /// <param name="sink">The frame sink, whose current frame receives the rows.</param>
    /// <param name="structure">The walker of the file: its color description and its pixel layout.</param>
    /// <param name="color">The planes of the color image.</param>
    /// <param name="alpha">The planes of the alpha plane, or <see langword="null"/> for an opaque image.</param>
    /// <param name="codedSize">The size of the image before its clean aperture.</param>
    /// <param name="crop">The part of the image that the clean aperture keeps.</param>
    public static void WriteFrame(ImageCodecContext context, DecodedFrameSink sink, HeifStructureParser structure, HeifPlanarImage color, HeifPlanarImage? alpha, Size codedSize, Rectangle crop)
    {
        using var writer = new HeifFrameWriter(context, structure, color.Format, alpha?.Format, codedSize, crop, sink.SourcePixelFormat);
        writer.WriteRows(sink, color, alpha, 0, crop.Height, context.Configuration.MaxDegreeOfParallelism);
    }

    /// <summary>
    /// Writes the rows <paramref name="start"/> to <paramref name="end"/> (excluded) of the clean aperture to the current frame
    /// of the sink: the planes hold the rows of the image that their conversion reads.
    /// </summary>
    /// <param name="sink">The frame sink, whose current frame receives the rows.</param>
    /// <param name="color">The planes of the color image.</param>
    /// <param name="alpha">The planes of the alpha plane, or <see langword="null"/> for an opaque image.</param>
    /// <param name="start">The first row of the clean aperture to write.</param>
    /// <param name="end">The row of the clean aperture after the last one to write.</param>
    /// <param name="maxDegreeOfParallelism">
    /// The largest number of workers that convert bands of the rows at the same time, when there are enough of them and the
    /// sink stores them as they are converted; 1 converts them on the calling thread.
    /// </param>
    /// <exception cref="ImageResourceLimitException">The rows of the workers exceed the live-allocation limit.</exception>
    public void WriteRows(DecodedFrameSink sink, HeifPlanarImage color, HeifPlanarImage? alpha, int start, int end, int maxDegreeOfParallelism = 1)
    {
        var width = _width;
        var pixelRow = _pixels.RawBuffer.AsSpan(0, width * _converter.BytesPerPixel);
        var whole = _crop.X is 0 && _crop.Width == width;
        using var target = sink.LeaseCurrentFrame();

        // The rows that the sink stores without conversion are converted in place when the clean aperture keeps them whole:
        // a large image is converted by bands of rows, each with chroma rows of its own
        var direct = whole && end > start && !sink.GetDirectRow(target, start).IsEmpty;
        var workers = direct ? RowBands.GetWorkerCount(new Size(width, end - start), maxDegreeOfParallelism) : 1;
        if (workers > 1)
        {
            using var chroma = Av1PooledArray<ushort>.Rent(_context.Scope, 2L * width * workers, clear: false);
            var storage = target.Storage;
            RowBands.RunParallelInBandOrder(workers, band =>
            {
                var rows = chroma.Span.Slice(2 * width * band, 2 * width);
                var bandEnd = start + RowBands.GetBandStart(end - start, band + 1, workers);
                for (var y = start + RowBands.GetBandStart(end - start, band, workers); y < bandEnd; y++)
                {
                    ConvertRow(color, alpha, y, rows, storage.GetRowSpanCore(y));
                }
            });

            return;
        }

        for (var y = start; y < end; y++)
        {
            var row = direct ? sink.GetDirectRow(target, y) : pixelRow;
            ConvertRow(color, alpha, y, _rows.Span, row);
            if (!direct)
            {
                sink.WriteRow(target, y, pixelRow.Slice(_crop.X * _converter.BytesPerPixel, _crop.Width * _converter.BytesPerPixel));
            }
        }
    }

    // Converts the row y of the clean aperture to pixels of the coded width; rows receives the two rows of chroma samples
    private void ConvertRow(HeifPlanarImage color, HeifPlanarImage? alpha, int y, Span<ushort> rows, Span<byte> destination)
    {
        if ((y & 31) is 0)
            _context.CancellationToken.ThrowIfCancellationRequested();

        var width = _width;
        var u = rows[..width];
        var v = rows.Slice(width, width);
        var sourceY = _crop.Y + y;
        ReadOnlySpan<ushort> uRow = u;
        ReadOnlySpan<ushort> vRow = v;
        var hasChroma = color.PlaneCount > 1;
        if (hasChroma && _converter.HasFullResolutionChroma)
        {
            uRow = color.GetRow(1, sourceY)[..width];
            vRow = color.GetRow(2, sourceY)[..width];
        }
        else if (hasChroma)
        {
            _converter.UpsampleChromaRow(color, 1, sourceY, width, _height, u);
            _converter.UpsampleChromaRow(color, 2, sourceY, width, _height, v);
        }

        var alphaRow = alpha is null ? default : alpha.GetRow(0, sourceY)[..width];
        _converter.ConvertRow(color.GetRow(0, sourceY)[..width], uRow, vRow, alphaRow, destination);
    }

    public void Dispose()
    {
        _pixels.Dispose();
        _rows.Dispose();
    }
}
