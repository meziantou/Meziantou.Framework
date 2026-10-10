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
/// The image ends at <c>IEND</c> (after <see cref="DecodedFrameSink.UpdateMetadata"/>), so that sequential reader frames carry
/// the metadata that follows the image data exactly like eager loads.
/// </description></item>
/// </list>
/// </remarks>
internal sealed class PngDecoder : PngDecodeObserver
{
    private readonly ImageDecodeRequest _request;
    private readonly ImageCodecContext _context;
    private PngStructureParser? _structure;
    private ApngDecoder? _animation;
    private PngSampleFormat? _format;
    private DecodedFrameSink? _sink;
    private PngImageDataDecoder? _data;
    private PooledBuffer? _passRowBuffer;
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
        sink.BeginFrame(FrameDuration.Zero);
        var interlaced = structure.InterlaceMethod == 1;
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
    }

    public override bool OnImageEnd()
    {
        if (_animation is not null)
            return _animation.OnImageEnd();

        var data = _data ?? throw new InvalidOperationException("No PNG image was started.");
        WriteAvailableRows(data);
        data.Complete();
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
    }

    private void WriteAvailableRows(PngImageDataDecoder data)
    {
        if (!data.TryReadRow(out var row))
            return;

        var sink = _sink!;
        using var lease = sink.LeaseCurrentFrame();
        do
        {
            if (row.Step == 1)
            {
                sink.WriteRow(lease, row.Y, row.Pixels);
            }
            else
            {
                WritePassRow(sink, lease, row);
            }
        }
        while (data.TryReadRow(out row));
    }

    /// <summary>Converts an Adam7 pass row to the destination format and scatters its pixels into the frame row.</summary>
    private void WritePassRow(DecodedFrameSink sink, scoped in PixelLease lease, in PngDecodedRow row)
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

        var destination = lease.GetRowBytes(row.Y);
        var stride = row.Step * bytesPerPixel;
        var offset = row.X * bytesPerPixel;
        for (var i = 0; i < row.Width; i++)
        {
            pixels.Slice(i * bytesPerPixel, bytesPerPixel).CopyTo(destination[offset..]);
            offset += stride;
        }
    }
}
