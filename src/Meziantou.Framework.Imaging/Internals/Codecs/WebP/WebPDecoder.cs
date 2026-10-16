using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

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
/// When the input ends in the bitstream of a still lossy image, a sequential reader that asks for renderings gets the
/// macroblock rows whose data was received (<see cref="Vp8Decoder.DecodePartial"/>), the other rows being zero.
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
internal sealed class WebPDecoder : WebPDecodeObserver, IPartialFrameRenderer
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
        _sink.SetPartialFrameRenderer(this);
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

    /// <summary>The rows of a still lossy image whose data was received, when the input ends in its bitstream.</summary>
    public bool TryRenderPartialFrame()
    {
        if (_structure is not { } structure || _sink is not { } sink || !structure.TryGetPartialStillImage(out var frame, out var alpha, out var bitstream) || frame.IsLossless || alpha is { IsComplete: false })
            return false;

        var planes = Vp8Decoder.DecodePartial(_context.Scope, bitstream.Data, 0, bitstream.Length, (int)bitstream.DeclaredLength, _context.CancellationToken, out var rows);
        if (planes is null)
            return false;

        using var image = CreateLossyImage(frame, alpha, planes);
        EnsureRows(structure.Width);
        var row = _row!.RawBuffer.AsSpan(0, frame.Width * BytesPerPixel);
        sink.BeginRefinement(FrameDuration.Zero);
        using (var target = sink.LeaseCurrentFrame())
        {
            for (var y = 0; y < frame.Height; y++)
            {
                if (y < rows)
                {
                    image.ReadRow(y, row, _scratch!.RawBuffer);
                    sink.WriteRow(target, y, row);
                }
                else
                {
                    target.GetRowBytes(y).Clear();
                }
            }
        }

        sink.EndRefinement(downsampling: 1, passCount: 0);
        return true;
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

        return CreateLossyImage(frame, alpha, Vp8Decoder.Decode(scope, bitstream.Data, 0, bitstream.Length, cancellationToken));
    }

    /// <summary>Decodes the alpha data of a lossy image, if any: the image owns the planes, which are disposed on failure.</summary>
    private DecodedWebPImage CreateLossyImage(WebPFrameInfo frame, WebPPayloadBuffer? alpha, Vp8Planes planes)
    {
        PooledBuffer? alphaPlane = null;
        try
        {
            if (alpha is not null)
            {
                alphaPlane = WebPAlphaDecoder.Decode(_context.Scope, alpha.Data, 0, alpha.Length, frame.Width, frame.Height, _context.CancellationToken);
            }

            return new DecodedWebPImage(planes, alphaPlane);
        }
        catch
        {
            planes.Dispose();
            throw;
        }
    }

    /// <summary>Converts ARGB pixels (<c>0xAARRGGBB</c>) to RGBA bytes (alpha 255 when <paramref name="opaque"/>), 4 pixels at a time.</summary>
    internal static void ArgbToRgba(ReadOnlySpan<uint> pixels, Span<byte> rgba, bool opaque)
    {
        var x = 0;
        if (Vector128.IsHardwareAccelerated)
        {
            // Each little-endian pixel is B, G, R, A in memory: swapping the first and third byte gives R, G, B, A
            var order = Vector128.Create((byte)2, 1, 0, 3, 6, 5, 4, 7, 10, 9, 8, 11, 14, 13, 12, 15);
            var alpha = Vector128.Create(opaque ? 0xFF000000u : 0u).AsByte();
            if (!BitConverter.IsLittleEndian)
            {
                order = Vector128.Create((byte)1, 2, 3, 0, 5, 6, 7, 4, 9, 10, 11, 8, 13, 14, 15, 12);
                alpha = Vector128.Create(opaque ? 0x000000FFu : 0u).AsByte();
            }

            for (; x <= pixels.Length - Vector128<uint>.Count; x += Vector128<uint>.Count)
            {
                var bytes = Vector128.Create(pixels.Slice(x, Vector128<uint>.Count)).AsByte();
                (Vector128.Shuffle(bytes, order) | alpha).CopyTo(rgba.Slice(x * BytesPerPixel, Vector128<byte>.Count));
            }
        }

        for (; x < pixels.Length; x++)
        {
            var argb = pixels[x];
            var pixel = rgba.Slice(x * BytesPerPixel, BytesPerPixel);
            pixel[0] = (byte)(argb >> 16);
            pixel[1] = (byte)(argb >> 8);
            pixel[2] = (byte)argb;
            pixel[3] = opaque ? byte.MaxValue : (byte)(argb >> 24);
        }
    }

    /// <summary>The scalar reference of <see cref="ArgbToRgba"/>.</summary>
    internal static void ArgbToRgbaScalar(ReadOnlySpan<uint> pixels, Span<byte> rgba, bool opaque)
    {
        for (var x = 0; x < pixels.Length; x++)
        {
            var argb = pixels[x];
            var pixel = rgba.Slice(x * BytesPerPixel, BytesPerPixel);
            pixel[0] = (byte)(argb >> 16);
            pixel[1] = (byte)(argb >> 8);
            pixel[2] = (byte)argb;
            pixel[3] = opaque ? byte.MaxValue : (byte)(argb >> 24);
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
                ArgbToRgba(pixels, rgba, _opaque);
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
