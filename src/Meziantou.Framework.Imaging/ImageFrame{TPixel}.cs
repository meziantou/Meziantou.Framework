using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging;

/// <summary>A borrowed, typed view of one full-canvas displayed frame (or of the poster frame) of an <see cref="Image{TPixel}"/>.</summary>
/// <typeparam name="TPixel">The pixel type.</typeparam>
/// <remarks>See <see cref="ImageFrame"/> for the lifetime and lease contract.</remarks>
public sealed class ImageFrame<TPixel> : ImageFrame
    where TPixel : unmanaged
{
    internal ImageFrame(Image image, PixelStorage storage, FrameMetadata metadata)
        : base(image, storage, metadata)
    {
    }

    /// <inheritdoc />
    public override PixelFormat PixelFormat => PixelFormats.GetPixelFormat<TPixel>();

    /// <summary>Gets or sets a single pixel. Prefer <see cref="ProcessPixelRows(PixelRowsAction{TPixel})"/> for bulk access.</summary>
    /// <param name="x">The column, from 0 to <c>Width - 1</c>.</param>
    /// <param name="y">The row, from 0 to <c>Height - 1</c>.</param>
    /// <returns>The pixel value.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A coordinate is out of range.</exception>
    /// <exception cref="InvalidOperationException">A conflicting lease is active.</exception>
    /// <exception cref="ObjectDisposedException">The frame is no longer attached to a live image.</exception>
    public TPixel this[int x, int y]
    {
        get
        {
            var storage = GetStorage();
            ValidateCoordinates(storage, x, y);
            using var lease = storage.AcquireLease();
            return lease.GetRow<TPixel>(y)[x];
        }
        set
        {
            var storage = GetStorage();
            ValidateCoordinates(storage, x, y);
            using var lease = storage.AcquireLease();
            lease.GetRow<TPixel>(y)[x] = value;
        }
    }

    /// <summary>Gives scoped read/write access to the visible pixels, row by row.</summary>
    /// <param name="action">The callback. It must not be asynchronous and must not retain the spans.</param>
    /// <exception cref="InvalidOperationException">A conflicting lease is active.</exception>
    /// <exception cref="ObjectDisposedException">The frame is no longer attached to a live image.</exception>
    public void ProcessPixelRows(PixelRowsAction<TPixel> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        using var lease = GetStorage().AcquireLease();
        action(new PixelAccessor<TPixel>(lease.Storage, lease.Token));
    }

    /// <summary>Gives scoped read/write access to the visible pixels, passing caller state to avoid closure allocations.</summary>
    /// <typeparam name="TState">The type of the state.</typeparam>
    /// <param name="state">The state passed to <paramref name="action"/>.</param>
    /// <param name="action">The callback. Use a static lambda to avoid allocations. It must not retain the spans.</param>
    /// <exception cref="InvalidOperationException">A conflicting lease is active.</exception>
    /// <exception cref="ObjectDisposedException">The frame is no longer attached to a live image.</exception>
    public void ProcessPixelRows<TState>(TState state, PixelRowsAction<TPixel, TState> action)
        where TState : allows ref struct
    {
        ArgumentNullException.ThrowIfNull(action);
        using var lease = GetStorage().AcquireLease();
        action(new PixelAccessor<TPixel>(lease.Storage, lease.Token), state);
    }

    /// <summary>Copies the visible pixels to a caller buffer. Elements between rows (stride padding) are left untouched.</summary>
    /// <param name="destination">The destination buffer.</param>
    /// <param name="strideInPixels">
    /// The distance between the starts of two consecutive destination rows, in pixels, or 0 for tightly packed rows. Must be
    /// 0 or at least <c>Width</c>.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="strideInPixels"/> is invalid.</exception>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is too short (the last row only needs <c>Width</c> pixels).</exception>
    /// <exception cref="InvalidOperationException">A conflicting lease is active.</exception>
    /// <exception cref="ObjectDisposedException">The frame is no longer attached to a live image.</exception>
    public void CopyPixelDataTo(Span<TPixel> destination, int strideInPixels = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(strideInPixels);
        var storage = GetStorage();
        var width = storage.Width;
        var stride = RowStride.Resolve(strideInPixels, width, nameof(strideInPixels), "pixels");
        RowStride.EnsureLength(destination.Length, stride, width, storage.Height, nameof(destination), "pixels");
        using var lease = storage.AcquireLease();
        for (var y = 0; y < storage.Height; y++)
        {
            lease.GetRow<TPixel>(y).CopyTo(destination.Slice(RowStride.GetOffset(y, stride), width));
        }
    }

    internal override void FlipPixels(scoped in PixelLease lease, FlipMode mode, CancellationToken cancellationToken)
        => ProcessingKernels.Flip<TPixel>(lease, mode, cancellationToken);

    internal override void GrayscalePixels(scoped in PixelLease lease, CancellationToken cancellationToken)
        => ProcessingKernels.Grayscale<TPixel>(lease, cancellationToken);

    internal override void TransformPixels(scoped in PixelLease source, scoped in PixelLease destination, Rectangle region, OrientationTransform transform, CancellationToken cancellationToken)
        => ProcessingKernels.Transform<TPixel>(source, destination, region, transform, cancellationToken);

    internal override void ResizePixels(scoped in PixelLease source, scoped in PixelLease destination, ResizePlan plan, CancellationToken cancellationToken)
        => Resampler.Resize<TPixel>(source, destination, plan, cancellationToken);

    internal override void ConvolvePixels(scoped in PixelLease lease, ConvolutionPlan plan, CancellationToken cancellationToken)
        => Convolver.Convolve<TPixel>(lease, plan, cancellationToken);

    internal override void ConvertColorPixels(scoped in PixelLease source, scoped in PixelLease destination, IccPipeline pipeline, CancellationToken cancellationToken)
        => ColorConversionKernels.ConvertRows<TPixel>(source, destination, pipeline, cancellationToken);

    internal override void AutoCropAnalyzePixels(scoped in PixelLease lease, AutoCropAnalyzer analyzer, CancellationToken cancellationToken)
        => analyzer.Accumulate<TPixel>(lease, cancellationToken);

    internal override void ExtendPixels(scoped in PixelLease source, scoped in PixelLease destination, Point origin, Rgba64 fill, CancellationToken cancellationToken)
    {
        TPixel pixel = default;
        PixelConverter.ConvertRow(new ReadOnlySpan<Rgba64>(in fill), new Span<TPixel>(ref pixel));
        ProcessingKernels.Extend(source, destination, origin, pixel, cancellationToken);
    }

    private static void ValidateCoordinates(PixelStorage storage, int x, int y)
    {
        if ((uint)x >= (uint)storage.Width)
            throw new ArgumentOutOfRangeException(nameof(x), x, "The column is out of range.");

        if ((uint)y >= (uint)storage.Height)
            throw new ArgumentOutOfRangeException(nameof(y), y, "The row is out of range.");
    }
}
