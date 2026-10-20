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
/// Still images are written straight to the sink, into the rows of the frame when they need no conversion (source layout
/// <see cref="PixelFormat.Rgba32"/>, or <see cref="PixelFormat.Rgb24"/> for an image without alpha requested as
/// <see cref="PixelFormat.Rgb24"/>); the frame ends at the end of the RIFF data after the trailing metadata (<c>EXIF</c>,
/// <c>XMP </c>) was collected, so readers and eager loads report the same metadata.
/// </description></item>
/// <item><description>
/// An eager load of a still lossy image without alpha converts the pixel rows as soon as they are final
/// (<see cref="Vp8Decoder.DecodeRows"/>): the planes are a window of a few macroblock rows. Elsewhere (sequential readers,
/// whose caller may own the frame, and images with alpha) the whole frame is decoded first, so that malformed data leaves
/// the frame untouched.
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
/// Memory: the payloads of one image (the walker), the decoded image (ARGB pixels, or Y'CbCr planes, or a window of them,
/// and an alpha plane),
/// the compositor canvas of an animation and two rows, all charged to the operation scope and released after each image.
/// Frames and pixels are charged to the per-input limits before each image is decoded.
/// </description></item>
/// </list>
/// </remarks>
internal sealed class WebPDecoder : WebPDecodeObserver, IPartialFrameRenderer
{
    private const int MaxBytesPerPixel = 4;

    private readonly ImageDecodeRequest _request;
    private readonly ImageCodecContext _context;
    private WebPStructureParser? _structure;
    private DecodedFrameSink? _sink;
    private AnimationCompositor? _compositor;
    private PooledBuffer? _row;
    private PooledBuffer? _scratch;
    private int _framesStarted;

    // The bytes per pixel of the rows given to the sink: 4 (RGBA), or 3 (RGB) for a still image without alpha decoded to RGB
    private int _bytesPerPixel = MaxBytesPerPixel;

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

        // A still image without alpha (the default pixel format is RGB only then) has opaque pixels: RGB rows lose nothing
        var defaultPixelFormat = structure.DefaultPixelFormat;
        var rgb = defaultPixelFormat == PixelFormat.Rgb24 && (_request.PixelFormat ?? defaultPixelFormat) == PixelFormat.Rgb24;
        _bytesPerPixel = rgb ? 3 : MaxBytesPerPixel;
        _sink = DecodedFrameSink.Create(_context, _request, ImageFormat.WebP, structure.Size, defaultPixelFormat, rgb ? PixelFormat.Rgb24 : PixelFormat.Rgba32, structure.Metadata.IccProfile);
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
        if (!isAnimated && !frame.IsLossless && alpha is null && _context.Sequential is null)
        {
            // An eager load discards the frame on failure: the rows are converted as soon as they are final
            using var decoder = Vp8Decoder.Open(_context.Scope, bitstream.Data, 0, bitstream.Length, _context.CancellationToken);
            var lossyRow = _row!.RawBuffer.AsSpan(0, frame.Width * _bytesPerPixel);
            using var lossyTarget = sink.LeaseCurrentFrame();
            while (decoder.DecodeRows(out var firstRow, out var endRow))
            {
                for (var y = firstRow; y < endRow; y++)
                {
                    var direct = sink.GetDirectRow(lossyTarget, y);
                    var destination = direct.IsEmpty ? lossyRow : direct;
                    WebPYuvConverter.ConvertRow(decoder.Planes, decoder.LumaOrigin, y, default, destination, _scratch!.RawBuffer, _bytesPerPixel);
                    if (direct.IsEmpty)
                    {
                        sink.WriteRow(lossyTarget, y, lossyRow);
                    }
                }
            }

            return true;
        }

        using var image = DecodeImage(frame, alpha, bitstream);
        if (!isAnimated)
        {
            // The still image is the canvas; the frame ends in OnEnd, after the trailing metadata
            var row = _row!.RawBuffer.AsSpan(0, frame.Width * _bytesPerPixel);
            using var target = sink.LeaseCurrentFrame();
            for (var y = 0; y < frame.Height; y++)
            {
                WriteRow(sink, target, y, image, row);
            }

            return true;
        }

        var rgbaRow = _row!.RawBuffer.AsSpan(0, frame.Width * MaxBytesPerPixel);

        var compositor = _compositor ??= new AnimationCompositor(_context.Scope, structure.Size, PixelFormat.Rgba32);
        var blend = frame.AlphaBlend && _framesStarted > 1 ? AnimationBlend.Over : AnimationBlend.Source;
        var disposal = frame.DisposeToBackground && !sink.IsFrameLimitReached ? AnimationDisposal.ClearToTransparent : AnimationDisposal.None;
        compositor.BeginFrame(frame.Bounds, blend, disposal);
        using (var canvas = compositor.LeaseCanvas())
        {
            for (var y = 0; y < frame.Height; y++)
            {
                image.ReadRow(y, rgbaRow, _scratch!.RawBuffer, MaxBytesPerPixel);
                compositor.WritePixels(canvas, frame.X, frame.Y + y, step: 1, frame.Width, rgbaRow);
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
        var row = _row!.RawBuffer.AsSpan(0, frame.Width * _bytesPerPixel);
        sink.BeginRefinement(FrameDuration.Zero);
        using (var target = sink.LeaseCurrentFrame())
        {
            for (var y = 0; y < frame.Height; y++)
            {
                if (y < rows)
                {
                    WriteRow(sink, target, y, image, row);
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

    /// <summary>Writes row <paramref name="y"/> of a still image: straight into the frame when its rows need no conversion.</summary>
    private void WriteRow(DecodedFrameSink sink, scoped in PixelLease target, int y, DecodedWebPImage image, Span<byte> row)
    {
        var direct = sink.GetDirectRow(target, y);
        if (!direct.IsEmpty)
        {
            image.ReadRow(y, direct, _scratch!.RawBuffer, _bytesPerPixel);
            return;
        }

        image.ReadRow(y, row, _scratch!.RawBuffer, _bytesPerPixel);
        sink.WriteRow(target, y, row);
    }

    private void EnsureRows(int canvasWidth)
    {
        // One RGBA row and the chroma upsampling scratch of the widest frame (the canvas width), allocated once
        _row ??= _context.Scope.Rent(canvasWidth * MaxBytesPerPixel, AllocationKind.DecoderState, clear: false);
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

    /// <summary>Converts ARGB pixels (<c>0xAARRGGBB</c>) to RGB bytes, 4 pixels at a time.</summary>
    internal static void ArgbToRgb(ReadOnlySpan<uint> pixels, Span<byte> rgb)
    {
        var x = 0;
        if (Vector128.IsHardwareAccelerated && BitConverter.IsLittleEndian)
        {
            // Each pixel is B, G, R, A in memory; 16 bytes are written for 12, the next store overwrites the last 4
            var order = Vector128.Create((byte)2, 1, 0, 6, 5, 4, 10, 9, 8, 14, 13, 12, 0xFF, 0xFF, 0xFF, 0xFF);
            for (; x <= pixels.Length - Vector128<uint>.Count && (3 * x) + Vector128<byte>.Count <= rgb.Length; x += Vector128<uint>.Count)
            {
                var bytes = Vector128.Create(pixels.Slice(x, Vector128<uint>.Count)).AsByte();
                Vector128.Shuffle(bytes, order).CopyTo(rgb.Slice(3 * x, Vector128<byte>.Count));
            }
        }

        ArgbToRgbScalar(pixels[x..], rgb[(3 * x)..]);
    }

    /// <summary>The scalar reference of <see cref="ArgbToRgb"/>.</summary>
    internal static void ArgbToRgbScalar(ReadOnlySpan<uint> pixels, Span<byte> rgb)
    {
        for (var x = 0; x < pixels.Length; x++)
        {
            var argb = pixels[x];
            var pixel = rgb.Slice(x * 3, 3);
            pixel[0] = (byte)(argb >> 16);
            pixel[1] = (byte)(argb >> 8);
            pixel[2] = (byte)argb;
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
                (Vector128.Shuffle(bytes, order) | alpha).CopyTo(rgba.Slice(x * MaxBytesPerPixel, Vector128<byte>.Count));
            }
        }

        for (; x < pixels.Length; x++)
        {
            var argb = pixels[x];
            var pixel = rgba.Slice(x * MaxBytesPerPixel, MaxBytesPerPixel);
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
            var pixel = rgba.Slice(x * MaxBytesPerPixel, MaxBytesPerPixel);
            pixel[0] = (byte)(argb >> 16);
            pixel[1] = (byte)(argb >> 8);
            pixel[2] = (byte)argb;
            pixel[3] = opaque ? byte.MaxValue : (byte)(argb >> 24);
        }
    }

    /// <summary>A decoded image that produces RGBA or RGB rows: VP8L ARGB pixels, or VP8 planes with an optional alpha plane.</summary>
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

        /// <summary>Writes row <paramref name="y"/> as RGBA (<paramref name="bytesPerPixel"/> 4) or RGB (3: the image is opaque) pixels.</summary>
        public void ReadRow(int y, Span<byte> destination, byte[] scratch, int bytesPerPixel)
        {
            if (_argb is not null)
            {
                var pixels = unsafe(MemoryMarshal.Cast<byte, uint>(_argb.RawBuffer.AsSpan())).Slice(y * _width, _width);
                if (bytesPerPixel == 3)
                {
                    ArgbToRgb(pixels, destination);
                }
                else
                {
                    ArgbToRgba(pixels, destination, _opaque);
                }

                return;
            }

            var alpha = _alpha is null ? default : _alpha.RawBuffer.AsSpan(y * _width, _width);
            WebPYuvConverter.ConvertRow(_planes!, lumaOrigin: 0, y, alpha, destination, scratch, bytesPerPixel);
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
