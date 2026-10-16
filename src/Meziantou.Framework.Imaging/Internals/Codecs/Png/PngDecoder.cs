namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The static PNG pixel decoder plugged into the <see cref="PngStructureParser"/> chunk walk, shared by eager loads
/// and sequential readers. The walker validates the container (chunk order, lengths,
/// CRCs, IHDR/PLTE/tRNS, metadata, limits); this observer inflates the <c>IDAT</c> payloads as they arrive and writes every
/// reconstructed row straight into the requested representation through the frame sink.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>
/// APNG files (an <c>acTL</c> before the first <c>IDAT</c>) are routed to <see cref="ApngDecoder"/> from the header
/// callback: every later callback is forwarded to it, so an APNG is never decoded as a static image.
/// </description></item>
/// <item><description>
/// The rows are produced in the lossless source layout of <see cref="PngSampleFormat"/> (16-bit samples kept) and converted
/// by the sink's plan (typed loads, alpha/background and profile policies). Adam7 pass rows are converted to the destination
/// format and scattered into the frame: no intermediate full image is allocated, interlaced or not.
/// </description></item>
/// <item><description>
/// A sequential reader that asks for renderings gets one when Adam7 passes were completed by the data of an <c>IDAT</c>
/// piece: every pixel that was not decoded takes the value of the pixel of its block (the top left pixel) in the passes that
/// were completed. The passes are then decoded in an image of the decoder, from which every rendering and the frame are
/// written: the image of the caller may change between two reads. When the input ends before the image, the rendering
/// shows what was decoded the same way (rows that were not decoded are zero when the image is not interlaced).
/// </description></item>
/// <item><description>
/// The image ends at <c>IEND</c> (after <see cref="DecodedFrameSink.UpdateMetadata"/>), so that sequential reader frames carry
/// the metadata that follows the image data exactly like eager loads.
/// </description></item>
/// </list>
/// </remarks>
internal sealed class PngDecoder : PngDecodeObserver, IPartialFrameRenderer
{
    private readonly ImageDecodeRequest _request;
    private readonly ImageCodecContext _context;
    private PngStructureParser? _structure;
    private ApngDecoder? _animation;
    private PngSampleFormat? _format;
    private DecodedFrameSink? _sink;
    private PngImageDataDecoder? _data;
    private PooledBuffer? _passRowBuffer;
    private PooledBuffer? _image;
    private int _renderedPasses;
    private int _destinationBytesPerPixel;
    private bool _imageDecoded;

    public PngDecoder(ImageDecodeRequest request, ImageCodecContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        _request = request;
        _context = context;
    }

    public override void OnHeaderComplete(PngStructureParser structure)
    {
        ArgumentNullException.ThrowIfNull(structure);
        if (structure.IsAnimated)
        {
            _animation = new ApngDecoder(_request, _context);
            _animation.OnHeaderComplete(structure);
            return;
        }

        _structure = structure;
        _format = PngSampleFormat.Create(structure);
        _sink = DecodedFrameSink.Create(_context, _request, ImageFormat.Png, structure.Size, structure.DefaultPixelFormat, _format.SourcePixelFormat, structure.Metadata.IccProfile);
        _destinationBytesPerPixel = PixelFormats.GetBytesPerPixel(_sink.DestinationPixelFormat);
        _sink.SetPartialFrameRenderer(this);
    }

    public override void OnImageStart(PngFrameControl? control)
    {
        if (_animation is not null)
        {
            _animation.OnImageStart(control);
            return;
        }

        var structure = _structure ?? throw new InvalidOperationException("The PNG header was not parsed.");
        var sink = _sink!;
        var interlaced = structure.InterlaceMethod == 1;
        var imageLength = (long)structure.Width * structure.Height * _destinationBytesPerPixel;
        if (interlaced && sink.WantsRefinement && imageLength <= CheckedSizes.MaxBufferLength)
        {
            // The passes are decoded in an image of the decoder: the frame begins once they are all decoded
            _image = _context.Scope.Rent((int)imageLength, AllocationKind.DecoderState, clear: false);
            _renderedPasses = 0;
        }
        else
        {
            sink.BeginFrame(FrameDuration.Zero);
        }

        _data = new PngImageDataDecoder(_context, _format!, structure.Width, structure.Height, interlaced);
        if (interlaced && !sink.Plan.IsIdentity)
        {
            // One pass row converted to the destination format before it is scattered (pass 7 is as wide as the image)
            var length = (long)structure.Width * _destinationBytesPerPixel;
            if (length > CheckedSizes.MaxBufferLength)
                throw CheckedSizes.CreateOverflowException(_context.Limits);

            _passRowBuffer = _context.Scope.Rent((int)length, AllocationKind.DecoderState, clear: false);
        }
    }

    public override void OnImageData(ReadOnlySpan<byte> compressed)
    {
        if (_animation is not null)
        {
            _animation.OnImageData(compressed);
            return;
        }

        var data = _data ?? throw new InvalidOperationException("No PNG image was started.");
        while (!compressed.IsEmpty)
        {
            var count = data.Feed(compressed);
            compressed = compressed[count..];
            WriteAvailableRows(data);
        }

        if (_image is not null)
        {
            RenderCompletedPasses(data);
        }
    }

    public override bool OnImageEnd()
    {
        if (_animation is not null)
            return _animation.OnImageEnd();

        var data = _data ?? throw new InvalidOperationException("No PNG image was started.");
        WriteAvailableRows(data);
        data.Complete();
        if (_image is { } image)
        {
            WriteDecodedImage(image);
        }

        ReleaseImageState();
        _imageDecoded = true;

        // A still image ends at IEND, so that it carries the metadata that follows its image data
        return true;
    }

    public override void OnEnd(PngStructureParser structure)
    {
        ArgumentNullException.ThrowIfNull(structure);
        if (_animation is not null)
        {
            _animation.OnEnd(structure);
            return;
        }

        if (!_imageDecoded)
            throw new InvalidOperationException("The PNG image data was not decoded.");

        _sink!.UpdateMetadata(structure.Metadata);
        _sink.EndImage();
    }

    public override Image GetResult()
    {
        if (_animation is not null)
            return _animation.GetResult();

        var structure = _structure ?? throw new InvalidOperationException("The PNG file was not decoded.");
        return _sink!.Build(structure.Metadata, animation: null);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _animation?.Dispose();
            _animation = null;
            ReleaseImageState();
            _sink?.Dispose();
            _sink = null;
        }

        base.Dispose(disposing);
    }

    private void ReleaseImageState()
    {
        _data?.Dispose();
        _data = null;
        _passRowBuffer?.Dispose();
        _passRowBuffer = null;
        _image?.Dispose();
        _image = null;
    }

    /// <summary>The image as far as it was decoded, when the input ends before it.</summary>
    public bool TryRenderPartialFrame()
    {
        if (_data is not { } data || _sink is not { } sink)
            return false;

        if (_image is not null)
        {
            sink.BeginRefinement(FrameDuration.Zero);
        }

        RenderPartialImage(sink, data);
        return true;
    }

    /// <summary>Shows the passes that were completed since the last rendering, when the reader asks for renderings.</summary>
    private void RenderCompletedPasses(PngImageDataDecoder data)
    {
        // The last pass completes the frame, which follows
        var (pass, _) = data.Progress;
        var sink = _sink!;
        if (pass <= _renderedPasses || pass == PngInterlace.PassCount || !sink.WantsRefinement)
            return;

        _renderedPasses = pass;

        // A rendering counts like the pixels of a frame that is not shown
        var structure = _structure!;
        _context.Tracker.ChargeHiddenPixels((long)structure.Width * structure.Height);
        sink.BeginRefinement(FrameDuration.Zero);
        RenderPartialImage(sink, data);
    }

    /// <summary>Writes the image as far as it was decoded to the frame of the sink, then ends the rendering.</summary>
    private void RenderPartialImage(DecodedFrameSink sink, PngImageDataDecoder data)
    {
        var structure = _structure!;
        var width = structure.Width;
        var height = structure.Height;
        var (pass, rows) = data.Progress;
        using (var lease = sink.LeaseCurrentFrame())
        {
            if (structure.InterlaceMethod != 1)
            {
                // The rows that were not decoded are zero
                for (var y = pass == 0 ? rows : height; y < height; y++)
                {
                    lease.GetRowBytes(y).Clear();
                }
            }
            else
            {
                WritePasses(lease, width, height, pass, rows);
            }
        }

        if (structure.InterlaceMethod != 1)
        {
            sink.EndRefinement(downsampling: 1, passCount: 0);
        }
        else
        {
            var (blockWidth, blockHeight) = GetBlockSize(pass);
            sink.EndRefinement(Math.Max(blockWidth, blockHeight), pass);
        }
    }

    /// <summary>
    /// Writes the pixels of the passes that were decoded, from the image of the decoder or in place: every other pixel
    /// takes the value of the top left pixel of its block in the completed passes (the first pass rows that were decoded
    /// when no pass was completed, or zero).
    /// </summary>
    /// <param name="lease">The frame of the sink.</param>
    /// <param name="width">The width of the image.</param>
    /// <param name="height">The height of the image.</param>
    /// <param name="pass">The number of completed passes: the pass being decoded.</param>
    /// <param name="rows">The number of rows of the pass being decoded that were decoded.</param>
    private void WritePasses(scoped in PixelLease lease, int width, int height, int pass, int rows)
    {
        var bytesPerPixel = _destinationBytesPerPixel;
        var rowLength = width * bytesPerPixel;
        var image = _image;
        var (blockWidth, blockHeight) = GetBlockSize(pass);
        var (_, currentY0, _, currentDy) = pass < PngInterlace.PassCount ? PngInterlace.GetPass(pass) : (0, 0, 1, 1);
        for (var y = 0; y < height; y++)
        {
            var destination = lease.GetRowBytes(y);
            var anchorY = y - (y % blockHeight);
            ReadOnlySpan<byte> source = image is null ? destination : image.RawBuffer.AsSpan(y * rowLength, rowLength);
            ReadOnlySpan<byte> anchor = image is null ? lease.GetRowBytes(anchorY) : image.RawBuffer.AsSpan(anchorY * rowLength, rowLength);

            // With no completed pass, only the blocks of the rows of the first pass that were decoded have a pixel
            var hasAnchor = pass > 0 || anchorY / 8 < rows;
            var isCurrentRowDecoded = y >= currentY0 && (y - currentY0) % currentDy == 0 && (y - currentY0) / currentDy < rows;
            for (var x = 0; x < width; x++)
            {
                var pixelPass = PngInterlace.GetPassOfPixel(x, y);
                var offset = x * bytesPerPixel;
                if (pixelPass < pass || (pixelPass == pass && isCurrentRowDecoded))
                {
                    if (image is not null)
                    {
                        source.Slice(offset, bytesPerPixel).CopyTo(destination[offset..]);
                    }
                }
                else if (hasAnchor)
                {
                    // The top left pixel of the block is decoded: it is never written here
                    anchor.Slice((x - (x % blockWidth)) * bytesPerPixel, bytesPerPixel).CopyTo(destination[offset..]);
                }
                else
                {
                    destination.Slice(offset, bytesPerPixel).Clear();
                }
            }
        }
    }

    /// <summary>Gets the size of the blocks that the completed passes hold one pixel of (the first pass when none is).</summary>
    private static (int Width, int Height) GetBlockSize(int completedPasses) => completedPasses switch
    {
        0 => (8, 8),
        PngInterlace.PassCount => (1, 1),
        _ => PngInterlace.GetBlockSize(completedPasses),
    };

    /// <summary>Writes the image of the decoder, every pass of which was decoded, to the frame.</summary>
    private void WriteDecodedImage(PooledBuffer image)
    {
        var structure = _structure!;
        var sink = _sink!;
        var rowLength = structure.Width * _destinationBytesPerPixel;
        _context.Sequential?.SetFramePassCount(PngInterlace.PassCount);
        sink.BeginFrame(FrameDuration.Zero);
        using var lease = sink.LeaseCurrentFrame();
        for (var y = 0; y < structure.Height; y++)
        {
            image.RawBuffer.AsSpan(y * rowLength, rowLength).CopyTo(lease.GetRowBytes(y));
        }
    }

    private void WriteAvailableRows(PngImageDataDecoder data)
    {
        if (!data.TryReadRow(out var row))
            return;

        var sink = _sink!;
        if (_image is { } image)
        {
            var rowLength = _structure!.Width * _destinationBytesPerPixel;
            do
            {
                WritePassRow(sink, image.RawBuffer.AsSpan(row.Y * rowLength, rowLength), row);
            }
            while (data.TryReadRow(out row));

            return;
        }

        using var lease = sink.LeaseCurrentFrame();
        do
        {
            if (row.Step == 1)
            {
                sink.WriteRow(lease, row.Y, row.Pixels);
            }
            else
            {
                WritePassRow(sink, lease.GetRowBytes(row.Y), row);
            }
        }
        while (data.TryReadRow(out row));
    }

    /// <summary>Converts an Adam7 pass row to the destination format and scatters its pixels into the frame row.</summary>
    private void WritePassRow(DecodedFrameSink sink, Span<byte> destination, in PngDecodedRow row)
    {
        var bytesPerPixel = _destinationBytesPerPixel;
        ReadOnlySpan<byte> pixels;
        if (sink.Plan.IsIdentity)
        {
            pixels = row.Pixels;
        }
        else
        {
            var converted = _passRowBuffer!.RawBuffer.AsSpan(0, row.Width * bytesPerPixel);
            sink.Plan.ConvertRow(row.Pixels, converted);
            pixels = converted;
        }

        var stride = row.Step * bytesPerPixel;
        var offset = row.X * bytesPerPixel;
        for (var i = 0; i < row.Width; i++)
        {
            pixels.Slice(i * bytesPerPixel, bytesPerPixel).CopyTo(destination[offset..]);
            offset += stride;
        }
    }
}
