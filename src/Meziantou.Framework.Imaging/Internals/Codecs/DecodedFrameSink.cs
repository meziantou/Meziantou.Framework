using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The destination of the displayed frames (and the separate poster) produced by a decoder, shared by eager loads and
/// sequential readers. Decoders produce rows in a lossless <em>source</em> layout of
/// their choice; the sink converts them to the requested representation row by row.
/// </summary>
/// <remarks>
/// <para>
/// Decoders obtain their sink from <see cref="Create"/> (in the header callback, once the canvas and the encoded sample
/// layout are known) and never depend on the kind of sink:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <see cref="DecodedImageBuilder"/> (eager loads) appends every frame to one image, returned by <see cref="Build"/>.
/// </description></item>
/// <item><description>
/// <see cref="SequentialFrameSink"/> (readers) writes each image into the target requested by the current reader call: a
/// new owned image charged to the reader scope, the caller's <c>ReadFrameInto</c> destination, or a scratch image for a
/// poster the caller skipped. <see cref="EndImage"/> hands the image to the reader and requests a yield.
/// </description></item>
/// </list>
/// <para>Per image, decoders call:</para>
/// <list type="number">
/// <item><description><see cref="BeginFrame"/> or <see cref="BeginPoster"/> (the frame is charged to the per-input limits);</description></item>
/// <item><description>
/// <see cref="WriteRow(int, ReadOnlySpan{byte})"/> or <see cref="LeaseCurrentFrame"/> for every row of the full-canvas
/// displayed image (the compositor output, never an encoded delta rectangle). Rows that are not written stay zero
/// (transparent black, or black); a compositor keeps its own canvas, so caller edits of returned images never affect it;
/// </description></item>
/// <item><description>
/// <see cref="EndImage"/>, whose result says whether the walk continues (<see langword="false"/> once the frame limit is
/// reached: the rest of the data is not examined).
/// </description></item>
/// </list>
/// <para>
/// In sequential mode the pixel target is bound lazily, on the first row access (or at <see cref="EndImage"/>): a decoder may
/// begin a frame in its header callback (for example a JPEG frame begun at the first scan), but must not write pixels before
/// the container walker yielded to the reader after the header.
/// </para>
/// </remarks>
internal abstract class DecodedFrameSink : IDisposable
{
    private protected DecodedFrameSink(ImageCodecContext context, ImageDecodeRequest request, ImageFormat format, Size canvas, PixelFormat defaultPixelFormat, PixelFormat sourcePixelFormat, IccProfile? iccProfile)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        context.Limits.EnsureCanvasWithinLimits(canvas.Width, canvas.Height);
        Context = context;
        Request = request;
        Format = format;
        Canvas = canvas;
        SourcePixelFormat = sourcePixelFormat;
        DestinationPixelFormat = request.PixelFormat ?? defaultPixelFormat;
        if (request.PixelFormat is null && !ColorProfileCompatibility.IsCompatible(iccProfile, DestinationPixelFormat))
        {
            // The decoder chose the representation: a profile that cannot label it is not adopted (never applied)
            iccProfile = null;
        }

        Plan = PixelConversionPlan.Create(sourcePixelFormat, DestinationPixelFormat, request.Conversion, iccProfile, format);
    }

    public ImageFormat Format { get; }

    public Size Canvas { get; }

    /// <summary>Gets the layout of the rows passed to <see cref="WriteRow(int, ReadOnlySpan{byte})"/>.</summary>
    public PixelFormat SourcePixelFormat { get; }

    /// <summary>Gets the pixel format of the produced frames.</summary>
    public PixelFormat DestinationPixelFormat { get; }

    /// <summary>Gets the validated conversion from <see cref="SourcePixelFormat"/> to <see cref="DestinationPixelFormat"/>.</summary>
    public PixelConversionPlan Plan { get; }

    /// <summary>Gets the number of displayed frames started so far.</summary>
    public int FrameCount { get; private set; }

    /// <summary>Gets a value indicating whether a poster frame was started.</summary>
    public bool HasPoster { get; private set; }

    /// <summary>Gets a value indicating whether <see cref="ImageDecodeRequest.FrameLimit"/> displayed frames have been started.</summary>
    public bool IsFrameLimitReached => Request.FrameLimit is { } limit && FrameCount >= limit;

    private protected ImageCodecContext Context { get; }

    private protected ImageDecodeRequest Request { get; }

    /// <summary>Creates the sink of the operation: a <see cref="SequentialFrameSink"/> for readers, otherwise a <see cref="DecodedImageBuilder"/>.</summary>
    /// <param name="context">The per-input context.</param>
    /// <param name="request">The requested representation and selection.</param>
    /// <param name="format">The decoded format.</param>
    /// <param name="canvas">The canvas size.</param>
    /// <param name="defaultPixelFormat">The default working representation of the file.</param>
    /// <param name="sourcePixelFormat">The layout of the rows the decoder produces.</param>
    /// <param name="iccProfile">The decoded ICC profile, or <see langword="null"/>.</param>
    /// <returns>The sink.</returns>
    /// <exception cref="ImageResourceLimitException">The canvas exceeds a limit.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The conversion policy rejects the profile.</exception>
    public static DecodedFrameSink Create(ImageCodecContext context, ImageDecodeRequest request, ImageFormat format, Size canvas, PixelFormat defaultPixelFormat, PixelFormat sourcePixelFormat, IccProfile? iccProfile)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Sequential is { } session
            ? new SequentialFrameSink(session, context, request, format, canvas, defaultPixelFormat, sourcePixelFormat, iccProfile)
            : new DecodedImageBuilder(context, request, format, canvas, defaultPixelFormat, sourcePixelFormat, iccProfile);
    }

    /// <summary>Starts the next displayed frame (zeroed: transparent black or black).</summary>
    /// <param name="duration">The exact frame duration.</param>
    /// <exception cref="ImageResourceLimitException">A frame, pixel or allocation limit is exceeded.</exception>
    /// <exception cref="InvalidOperationException">The frame limit is already reached (decoder bug).</exception>
    public void BeginFrame(FrameDuration duration)
    {
        if (IsFrameLimitReached)
            throw new InvalidOperationException("The requested frame limit is already reached.");

        Context.CancellationToken.ThrowIfCancellationRequested();
        Context.Tracker.ChargeFrame(Canvas);
        OnBeginFrame(duration);
        FrameCount++;
    }

    /// <summary>Starts the separate poster frame.</summary>
    /// <exception cref="ImageResourceLimitException">A frame or allocation limit is exceeded.</exception>
    /// <exception cref="InvalidOperationException">A poster was already started.</exception>
    public void BeginPoster()
    {
        if (HasPoster)
            throw new InvalidOperationException("The poster frame was already started.");

        Context.CancellationToken.ThrowIfCancellationRequested();
        Context.Tracker.ChargeFrame(Canvas, isPoster: true);
        OnBeginPoster();
        HasPoster = true;
    }

    /// <summary>Writes one row of the current frame, converting it from <see cref="SourcePixelFormat"/>.</summary>
    /// <param name="y">The row index.</param>
    /// <param name="sourceRow">Exactly <c>Canvas.Width</c> source pixels.</param>
    /// <exception cref="UnsupportedImageFeatureException">The row has non-opaque pixels and the destination has no alpha and no background is configured.</exception>
    public void WriteRow(int y, ReadOnlySpan<byte> sourceRow)
    {
        using var lease = LeaseCurrentFrame();
        WriteRow(lease, y, sourceRow);
    }

    /// <summary>Writes one row through a lease obtained from <see cref="LeaseCurrentFrame"/> (decoders writing many rows per call).</summary>
    public void WriteRow(scoped in PixelLease lease, int y, ReadOnlySpan<byte> sourceRow)
    {
        var expected = (long)Canvas.Width * PixelFormats.GetBytesPerPixel(SourcePixelFormat);
        if (sourceRow.Length != expected)
            throw new ArgumentException($"The source row has {sourceRow.Length} bytes; {expected} are expected.", nameof(sourceRow));

        var destination = lease.GetRowBytes(y);
        if (Plan.IsIdentity)
        {
            sourceRow.CopyTo(destination);
        }
        else
        {
            Plan.ConvertRow(sourceRow, destination);
        }
    }

    /// <summary>
    /// Leases the pixels of the current frame. When <see cref="SourcePixelFormat"/> equals <see cref="DestinationPixelFormat"/>,
    /// decoders may write destination rows directly (for example a compositor copying its canvas).
    /// </summary>
    /// <returns>The lease; dispose it before returning from the parser call.</returns>
    public PixelLease LeaseCurrentFrame() => GetCurrentFrame().GetStorage().AcquireLease();

    /// <summary>
    /// Ends the current displayed frame or poster: every row of the displayed image has been written. For sequential
    /// readers, the image is handed to the reader and a yield is requested (container walkers stop at their next yield point).
    /// </summary>
    /// <returns><see langword="true"/> to continue the walk; <see langword="false"/> once the frame limit is reached.</returns>
    /// <exception cref="InvalidOperationException">No image was started (decoder bug).</exception>
    public bool EndImage()
    {
        OnEndImage();
        return !IsFrameLimitReached;
    }

    /// <summary>
    /// Replaces the image-wide metadata given to the images produced afterward by a sequential reader (for example metadata
    /// found between frames). Eager loads receive their metadata in <see cref="Build"/> and ignore it. By default, readers use
    /// the metadata of the header snapshot.
    /// </summary>
    /// <param name="metadata">The metadata (copied).</param>
    public virtual void UpdateMetadata(ImageMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
    }

    /// <summary>Completes an eager load and transfers the image to the caller.</summary>
    /// <param name="metadata">The decoded metadata (copied; the source format and the resolved color profile are set).</param>
    /// <param name="animation">The animation settings, or <see langword="null"/> for a still image (created by default when there are several frames or a poster).</param>
    /// <returns>The image.</returns>
    /// <exception cref="InvalidOperationException">The sink belongs to a sequential reader, or no displayed frame was produced.</exception>
    public abstract Image Build(ImageMetadata metadata, AnimationMetadata? animation);

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Creates the image-wide metadata of a decoded image: a copy with the source format and the resolved color profile.</summary>
    private protected ImageMetadata CreateImageMetadata(ImageMetadata metadata)
    {
        var result = metadata.Clone();
        result.SourceFormat = Format;
        result.IccProfile = Plan.ColorProfile;
        return result;
    }

    private protected abstract void OnBeginFrame(FrameDuration duration);

    private protected abstract void OnBeginPoster();

    private protected abstract ImageFrame GetCurrentFrame();

    private protected abstract void OnEndImage();

    protected virtual void Dispose(bool disposing)
    {
    }
}
