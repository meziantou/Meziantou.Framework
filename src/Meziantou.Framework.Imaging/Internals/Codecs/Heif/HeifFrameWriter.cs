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
    {
        // A 'colr' box of type 'nclx' describes the colors of the image; otherwise the bitstream does
        var nclx = structure.ContainerColorDescription;
        var matrix = nclx?.MatrixCoefficients ?? color.MatrixCoefficients;
        var fullRange = nclx?.FullRange ?? color.FullRange;
        var primaries = nclx?.ColorPrimaries ?? color.ColorPrimaries;
        var transfer = nclx?.TransferCharacteristics ?? color.TransferCharacteristics;
        _converter = new HeifYuvConverter(structure.Binding, color, matrix, fullRange, alpha, structure.SourcePixelFormat, alpha is not null && structure.IsPremultiplied, primaries, transfer);
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
        using var writer = new HeifFrameWriter(context, structure, color.Format, alpha?.Format, codedSize, crop);
        writer.WriteRows(sink, color, alpha, 0, crop.Height);
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
    public void WriteRows(DecodedFrameSink sink, HeifPlanarImage color, HeifPlanarImage? alpha, int start, int end)
    {
        var width = _width;
        var u = _rows.Span[..width];
        var v = _rows.Span.Slice(width, width);
        var pixelRow = _pixels.RawBuffer.AsSpan(0, width * _converter.BytesPerPixel);
        var hasChroma = color.PlaneCount > 1;
        using var target = sink.LeaseCurrentFrame();
        for (var y = start; y < end; y++)
        {
            if ((y & 31) is 0)
                _context.CancellationToken.ThrowIfCancellationRequested();

            var sourceY = _crop.Y + y;
            if (hasChroma)
            {
                _converter.UpsampleChromaRow(color, 1, sourceY, width, _height, u);
                _converter.UpsampleChromaRow(color, 2, sourceY, width, _height, v);
            }

            var alphaRow = alpha is null ? default : alpha.GetRow(0, sourceY)[..width];
            _converter.ConvertRow(color.GetRow(0, sourceY)[..width], u, v, alphaRow, pixelRow);
            sink.WriteRow(target, y, pixelRow.Slice(_crop.X * _converter.BytesPerPixel, _crop.Width * _converter.BytesPerPixel));
        }
    }

    public void Dispose()
    {
        _pixels.Dispose();
        _rows.Dispose();
    }
}
