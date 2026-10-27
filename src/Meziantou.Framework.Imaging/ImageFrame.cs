using Meziantou.Framework.Imaging.Drawing;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging;

/// <summary>A borrowed view of one full-canvas displayed frame (or of the poster frame) of an <see cref="Image"/>.</summary>
/// <remarks>
/// <para>
/// Frames are owned by their image and are not disposable. A frame reference becomes invalid when the frame is removed,
/// when the poster frame is replaced, or when the image is disposed; using an invalid reference throws an
/// <see cref="ObjectDisposedException"/>. Moving frames and geometry operations keep frame identity: a reference keeps
/// designating the same logical frame after a successful resize or reorder.
/// </para>
/// <para>
/// Pixel access goes through scoped leases (<see cref="ProcessPixelBytes(PixelBytesAction)"/>,
/// <see cref="ImageFrame{TPixel}.ProcessPixelRows(PixelRowsAction{TPixel})"/>): the spans handed to a callback are valid
/// only during that callback. While a lease is active, disposing the image, structural edits, storage replacement and
/// conflicting pixel access throw an <see cref="InvalidOperationException"/>. Rows are contiguous, but the frame as a
/// whole is not guaranteed to be stored contiguously.
/// </para>
/// </remarks>
public abstract class ImageFrame
{
    private PixelStorage _storage;
    private FrameMetadata _metadata;

    private protected ImageFrame(Image image, PixelStorage storage, FrameMetadata metadata)
    {
        OwnerImage = image;
        _storage = storage;
        _metadata = metadata;
        metadata.Owner = this;
    }

    /// <summary>Gets the frame width, in pixels (always the canvas width).</summary>
    public int Width => Size.Width;

    /// <summary>Gets the frame height, in pixels (always the canvas height).</summary>
    public int Height => Size.Height;

    /// <summary>Gets the frame size (always the canvas size).</summary>
    /// <exception cref="ObjectDisposedException">The frame is no longer attached to a live image.</exception>
    public Size Size
    {
        get
        {
            var storage = GetStorage();
            return new Size(storage.Width, storage.Height);
        }
    }

    /// <summary>Gets the pixel format (always the image pixel format).</summary>
    public abstract PixelFormat PixelFormat { get; }

    /// <summary>Gets the mutable per-frame metadata, such as the display duration and the cursor hotspot.</summary>
    /// <exception cref="ObjectDisposedException">The frame is no longer attached to a live image.</exception>
    public FrameMetadata Metadata
    {
        get
        {
            _ = GetStorage();
            return MetadataCore;
        }
    }

    /// <summary>Gets the pixel storage without validation. Geometry transactions replace it (<see cref="ReplaceStorage"/>); removal disposes it.</summary>
    internal PixelStorage Storage => _storage;

    /// <summary>Gets the image this frame belongs to (pixel operations on a frame reconcile the image metadata).</summary>
    internal Image OwnerImage { get; }

    /// <summary>
    /// Gets or sets the metadata without validation. Only set while the owning image is being built (clones). The metadata
    /// is attached to this frame, which bounds its hotspot; a replaced instance is detached.
    /// </summary>
    internal FrameMetadata MetadataCore
    {
        get => _metadata;
        set
        {
            _metadata.Owner = null;
            value.Owner = this;
            _metadata = value;
        }
    }

    /// <summary>Gets a value indicating whether the frame is attached to a live image.</summary>
    internal bool IsValid => !_storage.IsDisposed;

    /// <summary>Gives scoped access to the visible pixel bytes, row by row, in the layout of <see cref="PixelFormat"/> (16-bit components in native endianness).</summary>
    /// <param name="action">The callback. It must not be asynchronous and must not retain the spans.</param>
    /// <exception cref="InvalidOperationException">A conflicting lease is active.</exception>
    /// <exception cref="ObjectDisposedException">The frame is no longer attached to a live image.</exception>
    public void ProcessPixelBytes(PixelBytesAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        using var lease = GetStorage().AcquireLease();
        action(new PixelBytesAccessor(lease.Storage, lease.Token, PixelFormat));
    }

    /// <summary>Gives scoped access to the visible pixel bytes, passing caller state to avoid closure allocations.</summary>
    /// <typeparam name="TState">The type of the state.</typeparam>
    /// <param name="state">The state passed to <paramref name="action"/>.</param>
    /// <param name="action">The callback. Use a static lambda to avoid allocations. It must not retain the spans.</param>
    /// <exception cref="InvalidOperationException">A conflicting lease is active.</exception>
    /// <exception cref="ObjectDisposedException">The frame is no longer attached to a live image.</exception>
    public void ProcessPixelBytes<TState>(TState state, PixelBytesAction<TState> action)
        where TState : allows ref struct
    {
        ArgumentNullException.ThrowIfNull(action);
        using var lease = GetStorage().AcquireLease();
        action(new PixelBytesAccessor(lease.Storage, lease.Token, PixelFormat), state);
    }

    /// <summary>Copies the visible pixel bytes to a caller buffer. Bytes between rows (stride padding) are left untouched.</summary>
    /// <param name="destination">The destination buffer.</param>
    /// <param name="strideInBytes">
    /// The distance between the starts of two consecutive destination rows, in bytes, or 0 for tightly packed rows. Must be
    /// 0 or at least <c>Width * bytesPerPixel</c>.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="strideInBytes"/> is invalid.</exception>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is too short (the last row only needs <c>Width * bytesPerPixel</c> bytes).</exception>
    /// <exception cref="InvalidOperationException">A conflicting lease is active.</exception>
    /// <exception cref="ObjectDisposedException">The frame is no longer attached to a live image.</exception>
    public void CopyPixelBytesTo(Span<byte> destination, int strideInBytes = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(strideInBytes);
        var storage = GetStorage();
        var rowLength = storage.RowLength;
        var stride = RowStride.Resolve(strideInBytes, rowLength, nameof(strideInBytes), "bytes");
        RowStride.EnsureLength(destination.Length, stride, rowLength, storage.Height, nameof(destination), "bytes");
        using var lease = storage.AcquireLease();
        for (var y = 0; y < storage.Height; y++)
        {
            lease.GetRowBytes(y).CopyTo(destination.Slice(RowStride.GetOffset(y, stride), rowLength));
        }
    }

    /// <summary>Draws on this frame only.</summary>
    /// <remarks>
    /// The callback runs once, while the pixels of the frame are leased. See <see cref="DrawingCanvas"/> for the
    /// coordinates and the rendering rules, and <see cref="Image.Draw(DrawingAction, CancellationToken)"/> to draw on every
    /// frame of an image.
    /// </remarks>
    /// <param name="action">The callback that draws. It must not be asynchronous and must not retain the canvas.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests. It is checked by every drawing call.</param>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">A conflicting lease is active.</exception>
    /// <exception cref="ImageResourceLimitException">The working storage would exceed the allocation limit.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled. The shapes already drawn stay, and some rows of the one being drawn may be painted.</exception>
    /// <exception cref="ObjectDisposedException">The frame is no longer attached to a live image.</exception>
    public void Draw(DrawingAction action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        using var session = BeginDrawing(cancellationToken);
        action(new DrawingCanvas(session));
    }

    /// <summary>Draws on this frame only, passing caller state to avoid closure allocations.</summary>
    /// <typeparam name="TState">The type of the state.</typeparam>
    /// <param name="state">The state passed to <paramref name="action"/>.</param>
    /// <param name="action">The callback that draws. Use a static lambda to avoid allocations. It must not retain the canvas.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests. It is checked by every drawing call.</param>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">A conflicting lease is active.</exception>
    /// <exception cref="ImageResourceLimitException">The working storage would exceed the allocation limit.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled. The shapes already drawn stay, and some rows of the one being drawn may be painted.</exception>
    /// <exception cref="ObjectDisposedException">The frame is no longer attached to a live image.</exception>
    public void Draw<TState>(TState state, DrawingAction<TState> action, CancellationToken cancellationToken = default)
        where TState : allows ref struct
    {
        ArgumentNullException.ThrowIfNull(action);
        using var session = BeginDrawing(cancellationToken);
        action(new DrawingCanvas(session), state);
    }

    /// <summary>Converts a paint color to the pixel type of the frame (see <see cref="DrawingKernels.ConvertColor{TPixel}"/>).</summary>
    internal abstract DrawingColor ConvertDrawingColor(Rgba64 color);

    /// <summary>Converts paint colors to the pixel type of the frame: four samples per color, laid out as <see cref="DrawingColor"/>.</summary>
    internal abstract void ConvertDrawingColors(ReadOnlySpan<Rgba64> colors, Span<ushort> destination);

    /// <summary>Reads pixels of a row as 16-bit colors, with the rules of pixel conversion. The caller holds the lease of the storage.</summary>
    internal abstract void ReadRgba64(PixelStorage storage, int y, int x, Span<Rgba64> destination);

    /// <summary>
    /// Reads pixels of a row as paint colors of a frame of the same pixel type, laid out as <see cref="DrawingColor"/>
    /// (see <see cref="DrawingKernels.ReadDrawingColors{TPixel}"/>). The caller holds the lease of the storage.
    /// </summary>
    internal abstract void ReadDrawingColors(PixelStorage storage, int y, int x, Span<ushort> destination);

    /// <summary>
    /// Composites a paint over a run of pixels, weighted by their coverage, with the kernel of the frame pixel type. The
    /// drawing session holds the lease of the storage for its whole duration.
    /// </summary>
    internal abstract void CompositePixels(PixelStorage storage, int y, int x, ReadOnlySpan<uint> coverage, scoped in DrawingSource source);

    /// <summary>Computes the colors a color matrix gives to pixels of a row, as paint colors (see <see cref="ColorMatrixKernel"/>). The caller holds the lease of the storage.</summary>
    internal abstract void ApplyColorMatrix(PixelStorage storage, int y, int x, ColorMatrix matrix, Span<ushort> destination);

    /// <summary>Stores the colors <see cref="ApplyColorMatrix"/> gave to pixels of a row. The caller holds the lease of the storage.</summary>
    internal abstract void WriteColorMatrixResult(PixelStorage storage, int y, int x, ReadOnlySpan<ushort> colors);

    /// <summary>Mirrors the leased pixels in place with the kernel of the frame pixel type.</summary>
    internal abstract void FlipPixels(scoped in PixelLease lease, FlipMode mode, CancellationToken cancellationToken);

    /// <summary>Converts the leased pixels to grayscale in place with the kernel of the frame pixel type.</summary>
    internal abstract void GrayscalePixels(scoped in PixelLease lease, CancellationToken cancellationToken);

    /// <summary>Writes a region of the leased source, permuted by <paramref name="transform"/>, to the leased destination (geometry transactions).</summary>
    internal abstract void TransformPixels(scoped in PixelLease source, scoped in PixelLease destination, Rectangle region, OrientationTransform transform, CancellationToken cancellationToken);

    /// <summary>Resamples the leased source to the leased destination with the scalar reference resampler (resize transactions).</summary>
    internal abstract void ResizePixels(scoped in PixelLease source, scoped in PixelLease destination, ResizePlan plan, CancellationToken cancellationToken);

    /// <summary>Writes the leased source, transformed by the geometry of <paramref name="plan"/>, to the leased destination (transformation transactions).</summary>
    internal abstract void WarpPixels(scoped in PixelLease source, scoped in PixelLease destination, WarpPlan plan, CancellationToken cancellationToken);

    /// <summary>Convolves the leased pixels in place with the kernel of the frame pixel type.</summary>
    internal abstract void ConvolvePixels(scoped in PixelLease lease, ConvolutionPlan plan, CancellationToken cancellationToken);

    /// <summary>Blurs the leased pixels in place with the box blur of the frame pixel type.</summary>
    internal abstract void BoxBlurPixels(scoped in PixelLease lease, BoxBlurPlan plan, CancellationToken cancellationToken);

    /// <summary>Replaces the leased pixels in place with the medians of their neighborhoods.</summary>
    internal abstract void MedianPixels(scoped in PixelLease lease, MedianPlan plan, CancellationToken cancellationToken);

    /// <summary>Converts the colors of the leased source to the leased destination with an ICC conversion (color profile transactions), with the input tables of 16-bit samples when given.</summary>
    internal abstract void ConvertColorPixels(scoped in PixelLease source, scoped in PixelLease destination, IccPipeline pipeline, IccUInt16InputTables? uint16Tables, CancellationToken cancellationToken);

    /// <summary>Runs the current pass of an auto-crop analysis over the leased pixels, which are only read.</summary>
    internal abstract void AutoCropAnalyzePixels(scoped in PixelLease lease, AutoCropAnalyzer analyzer, CancellationToken cancellationToken);

    /// <summary>
    /// Writes the rectangle of the leased source located at <paramref name="origin"/> to the leased destination, filling
    /// what lies outside the source with the background of <paramref name="fill"/> (geometry transactions): its typed
    /// background when it is an analysis of the frame pixel type, otherwise its <see cref="AutoCropAnalysis.BackgroundColor"/>,
    /// which must then be exactly representable in the frame pixel format.
    /// </summary>
    internal abstract void ExtendPixels(scoped in PixelLease source, scoped in PixelLease destination, Point origin, AutoCropAnalysis fill, CancellationToken cancellationToken);

    /// <summary>
    /// Writes the rectangle of the leased source located at <paramref name="origin"/> to the leased destination, filling
    /// what lies outside the source with <paramref name="fill"/> converted to the frame pixel format (geometry transactions).
    /// </summary>
    internal abstract void ExtendPixels(scoped in PixelLease source, scoped in PixelLease destination, Point origin, Rgba64 fill, CancellationToken cancellationToken);

    /// <summary>Replaces the storage after a committed geometry transaction; the frame keeps its identity.</summary>
    /// <param name="storage">The replacement storage, owned by the same image.</param>
    internal void ReplaceStorage(PixelStorage storage) => _storage = storage;

    /// <summary>Gets the storage of an attached frame.</summary>
    /// <exception cref="ObjectDisposedException">The frame was removed, replaced, or its image was disposed.</exception>
    internal PixelStorage GetStorage()
    {
        var storage = _storage;
        if (storage.IsDisposed)
            ThrowDetached();

        return storage;
    }

    private DrawingSession BeginDrawing(CancellationToken cancellationToken)
    {
        var storage = GetStorage();
        cancellationToken.ThrowIfCancellationRequested();
        return DrawingSession.Begin(OwnerImage, [this], [storage], cancellationToken);
    }

    [DoesNotReturn]
    private static void ThrowDetached()
        => throw new ObjectDisposedException(nameof(ImageFrame), "The frame is no longer attached to a live image: it was removed or replaced, or its image was disposed.");
}
