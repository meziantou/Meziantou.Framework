using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The GIF pixel decoder plugged into the <see cref="GifStructureParser"/> block walk, shared by eager loads and
/// sequential readers. The walker validates the container (blocks, sub-block
/// lengths, Graphic Control Extensions, LZW code sizes, limits, metadata); this observer decodes every image datastream with
/// <see cref="GifLzwDecoder"/>, maps the color indices through the local or global color table and resolves transparency,
/// partial rectangles and disposal through one <see cref="AnimationCompositor"/>, so that every produced frame is a
/// full-canvas <see cref="PixelFormat.Rgba32"/> displayed image.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>
/// The canvas starts transparent black, and "restore to background color" (disposal 2) clears the image rectangle to
/// transparent black: the logical-screen background color index is never painted (the browser and Apple ImageIO behavior).
/// Restore to previous (3, and the undefined value 4) restores
/// the rectangle as it was before the image; on the first image it clears it (the initial canvas). 0, 1 and 5-7 keep the canvas.
/// </description></item>
/// <item><description>
/// A pixel equal to the transparent index of the Graphic Control Extension leaves the canvas unchanged (its palette color is
/// never used); other pixels replace it with their opaque palette color. Image rectangles are clipped to the logical screen;
/// an image entirely outside it still produces a displayed frame (the unchanged canvas).
/// </description></item>
/// <item><description>
/// Strict datastreams (all <see cref="InvalidImageContentException"/>): no global or local color table, an index outside the
/// color table (unless it is the transparent index), invalid LZW codes, fewer indices than the image rectangle (an end code too
/// early, or data ending without one), and more indices than the rectangle. A missing end code after a complete image and any
/// data after the end code are accepted (ignored), like FFmpeg and Apple ImageIO.
/// </description></item>
/// <item><description>
/// Durations are the exact Graphic Control Extension hundredths (zero kept, no minimum-delay heuristic; zero without an
/// extension). Total plays come from the loop extension (<see cref="AnimationTiming.FromGifLoopCount"/>), animation settings
/// exist for several images or a loop extension (eager loads; reader images carry none).
/// </description></item>
/// <item><description>
/// Memory: the compositor canvas, at most one restore-previous rectangle, the LZW table and two image-width rows (indices and
/// RGBA), all charged to the operation scope; frames and pixels are charged to the per-input limits before each image.
/// </description></item>
/// </list>
/// </remarks>
internal sealed class GifDecoder : GifDecodeObserver
{
    private const int BytesPerPixel = 4;

    private readonly ImageDecodeRequest _request;
    private readonly ImageCodecContext _context;
    private readonly uint[] _palette = new uint[256];
    private GifStructureParser? _structure;
    private DecodedFrameSink? _sink;
    private AnimationCompositor? _compositor;
    private GifLzwDecoder? _lzw;
    private PooledBuffer? _indexRow;
    private PooledBuffer? _pixelRow;

    // Current image
    private bool _imageOpen;
    private int _left;
    private int _top;
    private int _width;
    private int _height;
    private int _visibleLeft;
    private int _visibleRight;
    private int _visibleTop;
    private int _visibleBottom;
    private bool _visible;
    private bool _interlaced;
    private int _paletteEntries;
    private int _transparentIndex;
    private int _rowFill;
    private int _rowsDone;
    private int _pass;
    private int _passRow;
    private int _framesStarted;

    public GifDecoder(ImageDecodeRequest request, ImageCodecContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        _request = request;
        _context = context;
    }

    public override void OnHeaderComplete(GifStructureParser structure)
    {
        ArgumentNullException.ThrowIfNull(structure);
        _structure = structure;
        _sink = DecodedFrameSink.Create(_context, _request, ImageFormat.Gif, structure.Size, DefaultPixelFormats.Gif, PixelFormat.Rgba32, structure.Metadata.IccProfile);
    }

    public override void OnImageStart(GifImageDescriptor descriptor, ReadOnlySpan<byte> localColorTable, GifGraphicControl? control, int lzwMinimumCodeSize)
    {
        var structure = _structure ?? throw new InvalidOperationException("The GIF header was not parsed.");
        var sink = _sink!;
        var colorTable = localColorTable.IsEmpty ? structure.GlobalColorTable.Span : localColorTable;
        if (colorTable.IsEmpty)
            throw Invalid("The GIF image has neither a local nor a global color table.");

        sink.BeginFrame(control?.Duration ?? FrameDuration.Zero);
        _framesStarted++;
        var isFirstFrame = _framesStarted == 1;
        var isLastFrame = sink.IsFrameLimitReached; // the frame count is otherwise unknown until the trailer

        _left = descriptor.Left;
        _top = descriptor.Top;
        _width = descriptor.Width;
        _height = descriptor.Height;
        _interlaced = descriptor.IsInterlaced;
        _visibleLeft = Math.Min(_left, structure.Width);
        _visibleRight = Math.Min(_left + _width, structure.Width);
        _visibleTop = Math.Min(_top, structure.Height);
        _visibleBottom = Math.Min(_top + _height, structure.Height);
        _visible = _visibleRight > _visibleLeft && _visibleBottom > _visibleTop;
        _rowFill = 0;
        _rowsDone = 0;
        _pass = 0;
        _passRow = 0;
        LoadPalette(colorTable, control);

        EnsureWorkingState(structure.Size);
        _lzw!.Reset(lzwMinimumCodeSize);
        if (_visible)
        {
            var blend = control?.HasTransparency == true ? AnimationBlend.Over : AnimationBlend.Source;
            var disposal = GetDisposal(control?.DisposalMethod ?? GifGraphicControl.DisposeNotSpecified, isFirstFrame, isLastFrame);
            _compositor!.BeginFrame(new Rectangle(_visibleLeft, _visibleTop, _visibleRight - _visibleLeft, _visibleBottom - _visibleTop), blend, disposal);
        }

        _imageOpen = true;
    }

    public override void OnImageData(ReadOnlySpan<byte> subBlock)
    {
        if (!_imageOpen)
            throw new InvalidOperationException("No GIF image was started.");

        if (_visible)
        {
            using var canvas = _compositor!.LeaseCanvas();
            Decode(subBlock, canvas);
        }
        else
        {
            Decode(subBlock, default);
        }
    }

    public override bool OnImageEnd()
    {
        if (!_imageOpen)
            throw new InvalidOperationException("No GIF image was started.");

        _imageOpen = false;
        if (_rowsDone < _height)
        {
            throw Invalid(_lzw!.IsEnded
                ? "The GIF LZW end code occurs before every pixel of the image was decoded."
                : "The GIF image data ends before every pixel of the image was decoded.");
        }

        // The displayed frame is the whole canvas; the disposal then prepares the canvas of the next image
        var compositor = _compositor!;
        var sink = _sink!;
        compositor.CopyTo(sink);
        if (_visible)
        {
            compositor.EndFrame();
        }

        return sink.EndImage();
    }

    public override Image GetResult()
    {
        var structure = _structure ?? throw new InvalidOperationException("The GIF file was not decoded.");
        return _sink!.Build(structure.Metadata, structure.Animation);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _indexRow?.Dispose();
            _indexRow = null;
            _pixelRow?.Dispose();
            _pixelRow = null;
            _lzw?.Dispose();
            _lzw = null;
            _compositor?.Dispose();
            _compositor = null;
            _sink?.Dispose();
            _sink = null;
        }

        base.Dispose(disposing);
    }

    /// <summary>Translates a raw GIF disposal method into a compositor disposal.</summary>
    internal static AnimationDisposal GetDisposal(int disposalMethod, bool isFirstFrame, bool isLastFrame)
    {
        if (isLastFrame)
            return AnimationDisposal.None; // nothing is displayed after the last frame

        return GifGraphicControl.GetEffectiveDisposalMethod(disposalMethod) switch
        {
            // "Restore to background color": transparent black, never the logical-screen background color
            GifGraphicControl.DisposeRestoreBackground => AnimationDisposal.ClearToTransparent,

            // Before the first image the canvas is transparent black: restoring it is clearing the rectangle
            GifGraphicControl.DisposeRestorePrevious => isFirstFrame ? AnimationDisposal.ClearToTransparent : AnimationDisposal.RestorePrevious,
            _ => AnimationDisposal.None,
        };
    }

    /// <summary>Maps an image row index in stream order to its row in the image (interlaced images store four passes).</summary>
    internal static int GetInterlacedRow(int height, ref int pass, ref int passRow)
    {
        while (true)
        {
            var (start, step) = pass switch
            {
                0 => (0, 8),
                1 => (4, 8),
                2 => (2, 4),
                _ => (1, 2),
            };

            var row = start + (passRow * step);
            if (row < height)
            {
                passRow++;
                return row;
            }

            if (pass == 3)
                throw new InvalidOperationException("Every interlaced row was already produced.");

            pass++;
            passRow = 0;
        }
    }

    private void LoadPalette(ReadOnlySpan<byte> colorTable, GifGraphicControl? control)
    {
        _paletteEntries = colorTable.Length / 3;
        var palette = _palette;
        Span<byte> rgba = stackalloc byte[BytesPerPixel];
        rgba[3] = byte.MaxValue;
        for (var i = 0; i < _paletteEntries; i++)
        {
            // Native-endian packed Rgba32 (the row is written through a uint view)
            colorTable.Slice(i * 3, 3).CopyTo(rgba);
            palette[i] = unsafe(MemoryMarshal.Read<uint>(rgba));
        }

        _transparentIndex = -1;
        if (control is { HasTransparency: true } gce)
        {
            _transparentIndex = gce.TransparentIndex;
            palette[gce.TransparentIndex] = 0; // leaves the canvas unchanged (straight alpha 0)
        }
    }

    private void EnsureWorkingState(Size canvas)
    {
        // The canvas and the LZW table are allocated once, after the first image was charged; the rows grow with the widest image
        _compositor ??= new AnimationCompositor(_context.Scope, canvas, PixelFormat.Rgba32);
        _lzw ??= new GifLzwDecoder(_context.Scope);
        if (_indexRow is null || _indexRow.Length < _width)
        {
            _indexRow?.Dispose();
            _indexRow = null;
            _indexRow = _context.Scope.Rent(Math.Max(_width, canvas.Width), AllocationKind.DecoderState, clear: false);
        }

        _pixelRow ??= _context.Scope.Rent(canvas.Width * BytesPerPixel, AllocationKind.DecoderState, clear: false);
    }

    private void Decode(ReadOnlySpan<byte> input, scoped in PixelLease canvas)
    {
        var lzw = _lzw!;
        var row = _indexRow!.RawBuffer.AsSpan(0, _width);
        while (true)
        {
            if (_rowsDone == _height)
            {
                // Every pixel was decoded: only clear codes, the end code (or padding bits) and ignored data may follow
                lzw.ReadTrailer(ref input);
                return;
            }

            var count = lzw.Decode(ref input, row[_rowFill..]);
            _rowFill += count;
            if (_rowFill < _width)
                return; // the input is exhausted (or the end code was read: OnImageEnd reports the missing pixels)

            WriteRow(row, canvas);
            _rowFill = 0;
            _rowsDone++;
        }
    }

    private void WriteRow(ReadOnlySpan<byte> indices, scoped in PixelLease canvas)
    {
        // Every index must have a color, including the clipped ones (the transparent index needs none)
        var entries = _paletteEntries;
        if (entries < 256)
        {
            var transparent = _transparentIndex;
            foreach (var index in indices)
            {
                if (index >= entries && index != transparent)
                    throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The GIF color index {index} is outside the {entries}-entry color table."));
            }
        }

        var imageRow = _interlaced ? GetInterlacedRow(_height, ref _pass, ref _passRow) : _rowsDone;
        var y = _top + imageRow;
        if (!_visible || y < _visibleTop || y >= _visibleBottom)
            return;

        var count = _visibleRight - _visibleLeft;
        var visible = indices.Slice(_visibleLeft - _left, count);
        var pixels = unsafe(MemoryMarshal.Cast<byte, uint>(_pixelRow!.RawBuffer.AsSpan(0, count * BytesPerPixel)));
        var palette = _palette;
        for (var i = 0; i < visible.Length; i++)
        {
            pixels[i] = palette[visible[i]];
        }

        _compositor!.WritePixels(canvas, _visibleLeft, y, step: 1, count, unsafe(MemoryMarshal.AsBytes(pixels)));
    }

    private static InvalidImageContentException Invalid(string message) => new(message, ImageFormat.Gif);
}
