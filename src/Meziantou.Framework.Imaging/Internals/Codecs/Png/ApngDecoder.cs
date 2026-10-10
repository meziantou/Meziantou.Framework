namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The APNG pixel decoder plugged into the <see cref="PngStructureParser"/> chunk walk, shared by eager loads and
/// sequential readers. The walker validates the container (<c>acTL</c>,
/// <c>fcTL</c> and <c>fdAT</c> order, sequence numbers, frame bounds, dispose/blend values, one data run per frame, frame count,
/// CRCs, limits); this observer inflates every image datastream with <see cref="PngImageDataDecoder"/> (frame regions of any
/// size, Adam7 included), converts the rows to the working RGBA layout and resolves blending and disposal through one
/// <see cref="AnimationCompositor"/>, so that every produced frame is a full-canvas displayed image.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>
/// The default image is frame zero when an <c>fcTL</c> precedes <c>IDAT</c>; otherwise it is a separate poster
/// (<see cref="DecodedFrameSink.BeginPoster"/>), decoded on the same canvas and cleared before the first frame: it is not part
/// of the animation. The canvas starts transparent black.
/// </description></item>
/// <item><description>
/// The first frame is drawn with SOURCE whatever its <c>blend_op</c> (<see cref="GetBlend"/>). A first frame disposed to
/// PREVIOUS is cleared like BACKGROUND (APNG specification); BACKGROUND clears to transparent black.
/// The disposal of the last frame (declared count or frame limit) is not applied: it has no visible effect, so no
/// restore-previous state is kept for it.
/// </description></item>
/// <item><description>
/// Durations are the exact <c>delay_num / delay_den</c> fractions (<c>delay_den = 0</c> means 1/100 s); zero is kept; total plays
/// come from <c>acTL num_plays</c> (0 is infinite). No player minimum-delay heuristic is applied.
/// </description></item>
/// <item><description>
/// Memory: the compositor canvas, at most one restore-previous region, one datastream decoder and one converted row, all charged
/// to the operation scope; frames and pixels are charged to the per-input limits before each image is produced.
/// </description></item>
/// </list>
/// </remarks>
internal sealed class ApngDecoder : PngDecodeObserver
{
    private readonly ImageDecodeRequest _request;
    private readonly ImageCodecContext _context;
    private PngStructureParser? _structure;
    private PngSampleFormat? _format;
    private DecodedFrameSink? _sink;
    private AnimationCompositor? _compositor;
    private PngImageDataDecoder? _data;
    private PooledBuffer? _rowBuffer;
    private PixelFormat _workingFormat;
    private int _workingBytesPerPixel;
    private Rectangle _region;
    private int _framesStarted;

    public ApngDecoder(ImageDecodeRequest request, ImageCodecContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        _request = request;
        _context = context;
    }

    public override void OnHeaderComplete(PngStructureParser structure)
    {
        ArgumentNullException.ThrowIfNull(structure);
        if (!structure.IsAnimated)
            throw new InvalidOperationException("The PNG file is not an APNG.");

        _structure = structure;
        _format = PngSampleFormat.Create(structure);
        _workingFormat = structure.BitDepth == 16 ? PixelFormat.Rgba64 : PixelFormat.Rgba32;
        _workingBytesPerPixel = PixelFormats.GetBytesPerPixel(_workingFormat);
        _sink = DecodedFrameSink.Create(_context, _request, ImageFormat.Png, structure.Size, structure.DefaultPixelFormat, _workingFormat, structure.Metadata.IccProfile);
    }

    public override void OnImageStart(PngFrameControl? control)
    {
        var structure = _structure ?? throw new InvalidOperationException("The PNG header was not parsed.");
        var sink = _sink!;
        AnimationBlend blend;
        AnimationDisposal disposal;
        if (control is { } frame)
        {
            sink.BeginFrame(frame.Duration);
            _framesStarted++;
            _region = new Rectangle(frame.X, frame.Y, frame.Width, frame.Height);
            var isFirstFrame = _framesStarted == 1;
            blend = GetBlend(frame.BlendOp, isFirstFrame);
            disposal = GetDisposal(frame.DisposeOp, isFirstFrame, isLastFrame: _framesStarted == structure.AnimationFrameCount || sink.IsFrameLimitReached);
        }
        else
        {
            // A separate poster (IDAT without fcTL): the whole canvas, cleared afterward (the animation starts transparent black)
            sink.BeginPoster();
            _region = new Rectangle(0, 0, structure.Width, structure.Height);
            blend = AnimationBlend.Source;
            disposal = AnimationDisposal.ClearToTransparent;
        }

        EnsureWorkingState(structure);
        _compositor!.BeginFrame(_region, blend, disposal);
        _data = new PngImageDataDecoder(_context, _format!, _region.Width, _region.Height, structure.InterlaceMethod == 1);
    }

    public override void OnImageData(ReadOnlySpan<byte> compressed)
    {
        var data = _data ?? throw new InvalidOperationException("No APNG image was started.");
        while (!compressed.IsEmpty)
        {
            var count = data.Feed(compressed);
            compressed = compressed[count..];
            WriteAvailableRows(data);
        }
    }

    public override bool OnImageEnd()
    {
        var data = _data ?? throw new InvalidOperationException("No APNG image was started.");
        WriteAvailableRows(data);
        data.Complete();
        ReleaseImageState();

        // The displayed frame (or poster) is the whole canvas; the disposal then prepares the canvas of the next frame
        var compositor = _compositor!;
        var sink = _sink!;
        compositor.CopyTo(sink);
        compositor.EndFrame();
        return sink.EndImage();
    }

    public override Image GetResult()
    {
        var structure = _structure ?? throw new InvalidOperationException("The PNG file was not decoded.");
        return _sink!.Build(structure.Metadata, structure.Animation);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ReleaseImageState();
            _rowBuffer?.Dispose();
            _rowBuffer = null;
            _compositor?.Dispose();
            _compositor = null;
            _sink?.Dispose();
            _sink = null;
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// Translates an APNG <c>blend_op</c> into a compositor blend. The first frame is always drawn with SOURCE: on the cleared
    /// canvas both operations display the same image ("functionally equivalent", APNG specification), and SOURCE keeps the
    /// encoded colors of fully transparent pixels like a still decode (and like FFmpeg and Apple ImageIO).
    /// </summary>
    internal static AnimationBlend GetBlend(byte blendOp, bool isFirstFrame)
        => blendOp == PngFrameControl.BlendOver && !isFirstFrame ? AnimationBlend.Over : AnimationBlend.Source;

    /// <summary>Translates an APNG <c>dispose_op</c> into a compositor disposal.</summary>
    internal static AnimationDisposal GetDisposal(byte disposeOp, bool isFirstFrame, bool isLastFrame)
    {
        if (isLastFrame)
            return AnimationDisposal.None; // nothing is displayed after the last frame

        return disposeOp switch
        {
            PngFrameControl.DisposeBackground => AnimationDisposal.ClearToTransparent,

            // "If the first fcTL chunk uses a dispose_op of APNG_DISPOSE_OP_PREVIOUS it should be treated as APNG_DISPOSE_OP_BACKGROUND"
            PngFrameControl.DisposePrevious => isFirstFrame ? AnimationDisposal.ClearToTransparent : AnimationDisposal.RestorePrevious,
            _ => AnimationDisposal.None,
        };
    }

    private void EnsureWorkingState(PngStructureParser structure)
    {
        // The canvas and one converted row (the widest region is the canvas) are allocated once, after the first image was charged
        _compositor ??= new AnimationCompositor(_context.Scope, structure.Size, _workingFormat);
        if (_rowBuffer is null && _format!.SourcePixelFormat != _workingFormat)
        {
            var length = (long)structure.Width * _workingBytesPerPixel;
            if (length > CheckedSizes.MaxBufferLength)
                throw CheckedSizes.CreateOverflowException(_context.Limits);

            _rowBuffer = _context.Scope.Rent((int)length, AllocationKind.DecoderState, clear: false);
        }
    }

    private void ReleaseImageState()
    {
        _data?.Dispose();
        _data = null;
    }

    private void WriteAvailableRows(PngImageDataDecoder data)
    {
        if (!data.TryReadRow(out var row))
            return;

        var compositor = _compositor!;
        var sourceFormat = _format!.SourcePixelFormat;
        using var canvas = compositor.LeaseCanvas();
        do
        {
            ReadOnlySpan<byte> pixels = row.Pixels;
            if (sourceFormat != _workingFormat)
            {
                // Lossless: gray replicated, missing alpha opaque, same precision (sub-byte gray was scaled to 8 bits)
                var converted = _rowBuffer!.RawBuffer.AsSpan(0, row.Width * _workingBytesPerPixel);
                PixelConverter.ConvertRow(sourceFormat, pixels, _workingFormat, converted, background: null, ImageFormat.Png);
                pixels = converted;
            }

            compositor.WritePixels(canvas, _region.X + row.X, _region.Y + row.Y, row.Step, row.Width, pixels);
        }
        while (data.TryReadRow(out row));
    }
}
