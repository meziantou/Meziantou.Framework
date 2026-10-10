using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The WebP pixel decoder (still images and animations) plugged into the <see cref="WebPStructureParser"/> chunk walk,
/// shared by eager loads and sequential readers. The walker validates the container and
/// buffers each image's payloads; this observer decodes them with <see cref="Vp8LDecoder"/> or <see cref="Vp8Decoder"/>
/// (plus <see cref="WebPAlphaDecoder"/>), converts lossy frames to RGB with <see cref="WebPYuvConverter"/>, and resolves
/// animation frames through one <see cref="AnimationCompositor"/>, so that every produced frame is a full-canvas
/// displayed image.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>
/// Still images are written straight to the sink (source layout <see cref="PixelFormat.Rgba32"/>); the frame ends at the end of
/// the RIFF data after the trailing metadata (<c>EXIF</c>, <c>XMP </c>) was collected, so readers and eager loads report the
/// same metadata.
/// </description></item>
/// <item><description>
/// Animations: the canvas starts transparent black. Each frame rectangle is drawn with alpha blending (the exact OVER contract
/// of the library) or replaces the canvas ("do not blend"); the first frame is always drawn with SOURCE (on the
/// cleared canvas both display the same image, and SOURCE keeps the encoded colors of fully transparent pixels). The whole
/// canvas is the displayed frame; "dispose to the background color" then clears the rectangle to transparent black: the
/// <c>ANIM</c> background color is a hint the container specification lets viewers ignore, and it is never painted (as the
/// libwebp animation decoder and the browsers do). Durations are the exact milliseconds; zero is kept.
/// </description></item>
/// <item><description>
/// Memory: the payloads of one image (the walker), the decoded image (ARGB pixels, or Y'CbCr planes and an alpha plane),
/// the compositor canvas of an animation and two rows, all charged to the operation scope and released after each image.
/// Frames and pixels are charged to the per-input limits before each image is decoded.
/// </description></item>
/// </list>
/// </remarks>
internal sealed class WebPDecoder : WebPDecodeObserver
{
    private const int BytesPerPixel = 4;

    private readonly ImageDecodeRequest _request;
    private readonly ImageCodecContext _context;
    private WebPStructureParser? _structure;
    private DecodedFrameSink? _sink;
    private AnimationCompositor? _compositor;
    private PooledBuffer? _row;
    private PooledBuffer? _scratch;
    private int _framesStarted;

    public WebPDecoder(ImageDecodeRequest request, ImageCodecContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        _request = request;
        _context = context;
    }

    public override void OnHeaderComplete(WebPStructureParser structure)
    {
        ArgumentNullException.ThrowIfNull(structure);
        _structure = structure;
        _sink = DecodedFrameSink.Create(_context, _request, ImageFormat.WebP, structure.Size, structure.DefaultPixelFormat, PixelFormat.Rgba32, structure.Metadata.IccProfile);
    }

    public override bool OnFrame(WebPFrameInfo frame, WebPPayloadBuffer? alpha, WebPPayloadBuffer bitstream)
    {
        ArgumentNullException.ThrowIfNull(bitstream);
        var structure = _structure ?? throw new InvalidOperationException("The WebP header was not parsed.");
        var sink = _sink!;
        var isAnimated = structure.IsAnimated;
        sink.BeginFrame(isAnimated ? AnimationTiming.FromWebPDuration(frame.DurationMilliseconds) : FrameDuration.Zero);
        _framesStarted++;
        EnsureRows(structure.Width);
        using var image = DecodeImage(frame, alpha, bitstream);
        var row = _row!.RawBuffer.AsSpan(0, frame.Width * BytesPerPixel);
        if (!isAnimated)
        {
            // The still image is the canvas; the frame ends in OnEnd, after the trailing metadata
            using var target = sink.LeaseCurrentFrame();
            for (var y = 0; y < frame.Height; y++)
            {
                image.ReadRow(y, row, _scratch!.RawBuffer);
                sink.WriteRow(target, y, row);
            }

            return true;
        }

        var compositor = _compositor ??= new AnimationCompositor(_context.Scope, structure.Size, PixelFormat.Rgba32);
        var blend = frame.AlphaBlend && _framesStarted > 1 ? AnimationBlend.Over : AnimationBlend.Source;
        var disposal = frame.DisposeToBackground && !sink.IsFrameLimitReached ? AnimationDisposal.ClearToTransparent : AnimationDisposal.None;
        compositor.BeginFrame(frame.Bounds, blend, disposal);
        using (var canvas = compositor.LeaseCanvas())
        {
            for (var y = 0; y < frame.Height; y++)
            {
                image.ReadRow(y, row, _scratch!.RawBuffer);
                compositor.WritePixels(canvas, frame.X, frame.Y + y, step: 1, frame.Width, row);
            }
        }

        compositor.CopyTo(sink);
        compositor.EndFrame();
        return sink.EndImage();
    }

    public override void OnEnd(WebPStructureParser structure)
    {
        ArgumentNullException.ThrowIfNull(structure);
        if (!structure.IsAnimated && _framesStarted > 0)
        {
            var sink = _sink!;
            sink.UpdateMetadata(structure.Metadata);
            sink.EndImage();
        }
    }

    public override Image GetResult()
    {
        var structure = _structure ?? throw new InvalidOperationException("The WebP file was not decoded.");
        return _sink!.Build(structure.Metadata, structure.Animation);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _row?.Dispose();
            _row = null;
            _scratch?.Dispose();
            _scratch = null;
            _compositor?.Dispose();
            _compositor = null;
            _sink?.Dispose();
            _sink = null;
        }

        base.Dispose(disposing);
    }

    private void EnsureRows(int canvasWidth)
    {
        // One RGBA row and the chroma upsampling scratch of the widest frame (the canvas width), allocated once
        _row ??= _context.Scope.Rent(canvasWidth * BytesPerPixel, AllocationKind.DecoderState, clear: false);
        _scratch ??= _context.Scope.Rent(canvasWidth * 2, AllocationKind.DecoderState, clear: false);
    }

    private DecodedWebPImage DecodeImage(WebPFrameInfo frame, WebPPayloadBuffer? alpha, WebPPayloadBuffer bitstream)
    {
        var scope = _context.Scope;
        var cancellationToken = _context.CancellationToken;
        if (frame.IsLossless)
        {
            // A still image without alpha (no VP8X alpha flag, VP8L alpha hint clear) is opaque whatever the decoded alpha
            // values: the hint is authoritative, so the pixels agree with the reported pixel format and transparency
            var pixels = Vp8LDecoder.Decode(scope, bitstream.Data, 0, bitstream.Length, cancellationToken, out _);
            return new DecodedWebPImage(frame.Width, pixels, opaque: !_structure!.IsAnimated && !frame.HasAlpha);
        }

        var planes = Vp8Decoder.Decode(scope, bitstream.Data, 0, bitstream.Length, cancellationToken);
        PooledBuffer? alphaPlane = null;
        try
        {
            if (alpha is not null)
            {
                alphaPlane = WebPAlphaDecoder.Decode(scope, alpha.Data, 0, alpha.Length, frame.Width, frame.Height, cancellationToken);
            }

            return new DecodedWebPImage(planes, alphaPlane);
        }
        catch
        {
            planes.Dispose();
            throw;
        }
    }

    /// <summary>A decoded image that produces RGBA rows: VP8L ARGB pixels, or VP8 planes with an optional alpha plane.</summary>
    private sealed class DecodedWebPImage : IDisposable
    {
        private readonly int _width;
        private readonly bool _opaque;
        private PooledBuffer? _argb;
        private Vp8Planes? _planes;
        private PooledBuffer? _alpha;

        public DecodedWebPImage(int width, PooledBuffer argb, bool opaque)
        {
            _width = width;
            _argb = argb;
            _opaque = opaque;
        }

        public DecodedWebPImage(Vp8Planes planes, PooledBuffer? alpha)
        {
            _width = planes.Width;
            _planes = planes;
            _alpha = alpha;
        }

        public void ReadRow(int y, Span<byte> rgba, byte[] scratch)
        {
            if (_argb is not null)
            {
                var pixels = unsafe(MemoryMarshal.Cast<byte, uint>(_argb.RawBuffer.AsSpan())).Slice(y * _width, _width);
                for (var x = 0; x < pixels.Length; x++)
                {
                    var argb = pixels[x];
                    var pixel = rgba.Slice(x * BytesPerPixel, BytesPerPixel);
                    pixel[0] = (byte)(argb >> 16);
                    pixel[1] = (byte)(argb >> 8);
                    pixel[2] = (byte)argb;
                    pixel[3] = _opaque ? byte.MaxValue : (byte)(argb >> 24);
                }

                return;
            }

            var alpha = _alpha is null ? default : _alpha.RawBuffer.AsSpan(y * _width, _width);
            WebPYuvConverter.ConvertRow(_planes!, y, alpha, rgba, scratch);
        }

        public void Dispose()
        {
            _argb?.Dispose();
            _argb = null;
            _planes?.Dispose();
            _planes = null;
            _alpha?.Dispose();
            _alpha = null;
        }
    }
}
