using System.Diagnostics;
using System.Runtime.InteropServices;
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

    internal override DrawingColor ConvertDrawingColor(Rgba64 color)
        => DrawingKernels.ConvertColor<TPixel>(color);

    internal override void ConvertDrawingColors(ReadOnlySpan<Rgba64> colors, Span<ushort> destination)
        => DrawingKernels.ConvertColors<TPixel>(colors, destination);

    internal override void ReadRgba64(PixelStorage storage, int y, int x, Span<Rgba64> destination)
        => DrawingKernels.ReadColors<TPixel>(storage, y, x, destination);

    internal override void ReadDrawingColors(PixelStorage storage, int y, int x, Span<ushort> destination)
        => DrawingKernels.ReadDrawingColors<TPixel>(storage, y, x, destination);

    internal override void CompositePixels(PixelStorage storage, int y, int x, ReadOnlySpan<uint> coverage, scoped in DrawingSource source)
        => DrawingKernels.Composite<TPixel>(storage, y, x, coverage, source);

    internal override void ApplyColorMatrix(PixelStorage storage, int y, int x, ColorMatrix matrix, Span<ushort> destination)
        => ColorMatrixKernel.Apply<TPixel>(storage, y, x, matrix, destination);

    internal override void WriteColorMatrixResult(PixelStorage storage, int y, int x, ReadOnlySpan<ushort> colors)
        => ColorMatrixKernel.Write<TPixel>(storage, y, x, colors);

    internal override void FlipPixels(scoped in PixelLease lease, FlipMode mode, CancellationToken cancellationToken)
        => ProcessingKernels.Flip<TPixel>(lease, mode, cancellationToken);

    internal override void GrayscalePixels(scoped in PixelLease lease, CancellationToken cancellationToken)
    {
        // The formats drawn in another color format are made gray in it (the gray formats are left as they are)
        if (!DrawingKernels.IsBridgedToColorFormat<TPixel>())
        {
            ProcessingKernels.Grayscale<TPixel>(lease, cancellationToken);
        }
        else if (BridgeFormat == PixelFormat.Rgba64)
        {
            ProcessingBridge.RunInPlace<TPixel>(OwnerImage.Owner, lease, (scoped in PixelLease working) => ProcessingKernels.Grayscale<Rgba64>(working, cancellationToken));
        }
        else if (BridgeFormat == PixelFormat.Rgb24)
        {
            ProcessingBridge.RunInPlace<TPixel>(OwnerImage.Owner, lease, (scoped in PixelLease working) => ProcessingKernels.Grayscale<Rgb24>(working, cancellationToken));
        }
        else if (BridgeFormat == PixelFormat.Rgba32)
        {
            ProcessingBridge.RunInPlace<TPixel>(OwnerImage.Owner, lease, (scoped in PixelLease working) => ProcessingKernels.Grayscale<Rgba32>(working, cancellationToken));
        }
        else
        {
            throw new UnreachableException();
        }
    }

    internal override void ReplaceColorPixels(scoped in PixelLease lease, Rgba64 color, Rgba64 replacement, int tolerance, CancellationToken cancellationToken)
        => ProcessingKernels.ReplaceColor<TPixel>(lease, color, replacement, tolerance, cancellationToken);

    internal override void TransformPixels(scoped in PixelLease source, scoped in PixelLease destination, Rectangle region, OrientationTransform transform, CancellationToken cancellationToken)
        => ProcessingKernels.Transform<TPixel>(source, destination, region, transform, cancellationToken);

    /// <summary>Gets the pixel format the filtering kernels work in for <typeparamref name="TPixel"/> (<see cref="ProcessingBridge.GetKernelFormat"/>).</summary>
    private static PixelFormat BridgeFormat => ProcessingBridge.GetKernelFormat(PixelFormats.GetPixelFormat<TPixel>());

    internal override void ResizePixels(scoped in PixelLease source, scoped in PixelLease destination, ResizePlan plan, CancellationToken cancellationToken)
    {
        if (!ProcessingBridge.IsBridged<TPixel>())
        {
            Resampler.Resize<TPixel>(source, destination, plan, cancellationToken);
        }
        else if (BridgeFormat == PixelFormat.Rgba64)
        {
            ProcessingBridge.RunCopy<TPixel>(OwnerImage.Owner, source, destination, (scoped in PixelLease from, scoped in PixelLease to) => Resampler.Resize<Rgba64>(from, to, plan, cancellationToken));
        }
        else if (BridgeFormat == PixelFormat.Rgb24)
        {
            ProcessingBridge.RunCopy<TPixel>(OwnerImage.Owner, source, destination, (scoped in PixelLease from, scoped in PixelLease to) => Resampler.Resize<Rgb24>(from, to, plan, cancellationToken));
        }
        else if (BridgeFormat == PixelFormat.Rgba32)
        {
            ProcessingBridge.RunCopy<TPixel>(OwnerImage.Owner, source, destination, (scoped in PixelLease from, scoped in PixelLease to) => Resampler.Resize<Rgba32>(from, to, plan, cancellationToken));
        }
        else
        {
            throw new UnreachableException();
        }
    }

    internal override void WarpPixels(scoped in PixelLease source, scoped in PixelLease destination, WarpPlan plan, CancellationToken cancellationToken)
    {
        if (!ProcessingBridge.IsBridged<TPixel>())
        {
            Warper.Warp<TPixel>(source, destination, plan, cancellationToken);
        }
        else if (BridgeFormat == PixelFormat.Rgba64)
        {
            ProcessingBridge.RunCopy<TPixel>(OwnerImage.Owner, source, destination, (scoped in PixelLease from, scoped in PixelLease to) => Warper.Warp<Rgba64>(from, to, plan, cancellationToken));
        }
        else if (BridgeFormat == PixelFormat.Rgb24)
        {
            ProcessingBridge.RunCopy<TPixel>(OwnerImage.Owner, source, destination, (scoped in PixelLease from, scoped in PixelLease to) => Warper.Warp<Rgb24>(from, to, plan, cancellationToken));
        }
        else if (BridgeFormat == PixelFormat.Rgba32)
        {
            ProcessingBridge.RunCopy<TPixel>(OwnerImage.Owner, source, destination, (scoped in PixelLease from, scoped in PixelLease to) => Warper.Warp<Rgba32>(from, to, plan, cancellationToken));
        }
        else
        {
            throw new UnreachableException();
        }
    }

    internal override void ConvolvePixels(scoped in PixelLease lease, ConvolutionPlan plan, CancellationToken cancellationToken)
    {
        if (!ProcessingBridge.IsBridged<TPixel>())
        {
            Convolver.Convolve<TPixel>(lease, plan, cancellationToken);
        }
        else if (BridgeFormat == PixelFormat.Rgba64)
        {
            ProcessingBridge.RunInPlace<TPixel>(OwnerImage.Owner, lease, (scoped in PixelLease working) => Convolver.Convolve<Rgba64>(working, plan, cancellationToken));
        }
        else if (BridgeFormat == PixelFormat.Rgb24)
        {
            ProcessingBridge.RunInPlace<TPixel>(OwnerImage.Owner, lease, (scoped in PixelLease working) => Convolver.Convolve<Rgb24>(working, plan, cancellationToken));
        }
        else if (BridgeFormat == PixelFormat.Rgba32)
        {
            ProcessingBridge.RunInPlace<TPixel>(OwnerImage.Owner, lease, (scoped in PixelLease working) => Convolver.Convolve<Rgba32>(working, plan, cancellationToken));
        }
        else
        {
            throw new UnreachableException();
        }
    }

    internal override void BoxBlurPixels(scoped in PixelLease lease, BoxBlurPlan plan, CancellationToken cancellationToken)
    {
        if (!ProcessingBridge.IsBridged<TPixel>())
        {
            BoxBlurrer.Blur<TPixel>(lease, plan, cancellationToken);
        }
        else if (BridgeFormat == PixelFormat.Rgba64)
        {
            ProcessingBridge.RunInPlace<TPixel>(OwnerImage.Owner, lease, (scoped in PixelLease working) => BoxBlurrer.Blur<Rgba64>(working, plan, cancellationToken));
        }
        else if (BridgeFormat == PixelFormat.Rgb24)
        {
            ProcessingBridge.RunInPlace<TPixel>(OwnerImage.Owner, lease, (scoped in PixelLease working) => BoxBlurrer.Blur<Rgb24>(working, plan, cancellationToken));
        }
        else if (BridgeFormat == PixelFormat.Rgba32)
        {
            ProcessingBridge.RunInPlace<TPixel>(OwnerImage.Owner, lease, (scoped in PixelLease working) => BoxBlurrer.Blur<Rgba32>(working, plan, cancellationToken));
        }
        else
        {
            throw new UnreachableException();
        }
    }

    internal override void MedianPixels(scoped in PixelLease lease, MedianPlan plan, CancellationToken cancellationToken)
    {
        if (!ProcessingBridge.IsBridged<TPixel>())
        {
            MedianFilter.Apply<TPixel>(lease, plan, cancellationToken);
        }
        else if (BridgeFormat == PixelFormat.Rgba64)
        {
            ProcessingBridge.RunInPlace<TPixel>(OwnerImage.Owner, lease, (scoped in PixelLease working) => MedianFilter.Apply<Rgba64>(working, plan, cancellationToken));
        }
        else if (BridgeFormat == PixelFormat.Rgb24)
        {
            ProcessingBridge.RunInPlace<TPixel>(OwnerImage.Owner, lease, (scoped in PixelLease working) => MedianFilter.Apply<Rgb24>(working, plan, cancellationToken));
        }
        else if (BridgeFormat == PixelFormat.Rgba32)
        {
            ProcessingBridge.RunInPlace<TPixel>(OwnerImage.Owner, lease, (scoped in PixelLease working) => MedianFilter.Apply<Rgba32>(working, plan, cancellationToken));
        }
        else
        {
            throw new UnreachableException();
        }
    }

    internal override void PixelatePixels(scoped in PixelLease lease, int size, EffectScratch scratch, CancellationToken cancellationToken)
    {
        if (!ProcessingBridge.IsBridged<TPixel>())
        {
            EffectKernels.Pixelate<TPixel>(lease, size, scratch, cancellationToken);
        }
        else if (BridgeFormat == PixelFormat.Rgba64)
        {
            ProcessingBridge.RunInPlace<TPixel>(OwnerImage.Owner, lease, (scoped in PixelLease working) => EffectKernels.Pixelate<Rgba64>(working, size, scratch, cancellationToken));
        }
        else if (BridgeFormat == PixelFormat.Rgb24)
        {
            ProcessingBridge.RunInPlace<TPixel>(OwnerImage.Owner, lease, (scoped in PixelLease working) => EffectKernels.Pixelate<Rgb24>(working, size, scratch, cancellationToken));
        }
        else if (BridgeFormat == PixelFormat.Rgba32)
        {
            ProcessingBridge.RunInPlace<TPixel>(OwnerImage.Owner, lease, (scoped in PixelLease working) => EffectKernels.Pixelate<Rgba32>(working, size, scratch, cancellationToken));
        }
        else
        {
            throw new UnreachableException();
        }
    }

    internal override void OilPaintPixels(scoped in PixelLease lease, int levels, int radius, EffectScratch scratch, CancellationToken cancellationToken)
    {
        // The working copy of a gray format has three equal colors: its intensity is that gray
        if (!ProcessingBridge.IsBridged<TPixel>())
        {
            EffectKernels.OilPaint<TPixel>(lease, levels, radius, grayColors: false, scratch, cancellationToken);
        }
        else if (BridgeFormat == PixelFormat.Rgba64)
        {
            ProcessingBridge.RunInPlace<TPixel>(OwnerImage.Owner, lease, (scoped in PixelLease working) => EffectKernels.OilPaint<Rgba64>(working, levels, radius, grayColors: ProcessingBridge.HasGrayColors<TPixel>(), scratch, cancellationToken));
        }
        else if (BridgeFormat == PixelFormat.Rgb24)
        {
            ProcessingBridge.RunInPlace<TPixel>(OwnerImage.Owner, lease, (scoped in PixelLease working) => EffectKernels.OilPaint<Rgb24>(working, levels, radius, grayColors: ProcessingBridge.HasGrayColors<TPixel>(), scratch, cancellationToken));
        }
        else if (BridgeFormat == PixelFormat.Rgba32)
        {
            ProcessingBridge.RunInPlace<TPixel>(OwnerImage.Owner, lease, (scoped in PixelLease working) => EffectKernels.OilPaint<Rgba32>(working, levels, radius, grayColors: ProcessingBridge.HasGrayColors<TPixel>(), scratch, cancellationToken));
        }
        else
        {
            throw new UnreachableException();
        }
    }

    internal override void FlattenPixels(scoped in PixelLease lease, Rgba64 background, int maxDegreeOfParallelism, CancellationToken cancellationToken)
    {
        // The flattened working copy of a gray format converts back to the luma of its colors: the conversion of the gray
        // format to a format without alpha
        if (!ProcessingBridge.IsBridged<TPixel>())
        {
            EffectKernels.Flatten<TPixel>(lease, background, maxDegreeOfParallelism, cancellationToken);
        }
        else if (typeof(TPixel) == typeof(A8))
        {
            throw new NotSupportedException($"The pixel type '{typeof(TPixel).Name}' is not supported.");
        }
        else if (BridgeFormat == PixelFormat.Rgba64)
        {
            ProcessingBridge.RunInPlace<TPixel>(OwnerImage.Owner, lease, (scoped in PixelLease working) => EffectKernels.Flatten<Rgba64>(working, background, maxDegreeOfParallelism, cancellationToken));
        }
        else if (BridgeFormat == PixelFormat.Rgb24)
        {
            ProcessingBridge.RunInPlace<TPixel>(OwnerImage.Owner, lease, (scoped in PixelLease working) => EffectKernels.Flatten<Rgb24>(working, background, maxDegreeOfParallelism, cancellationToken));
        }
        else if (BridgeFormat == PixelFormat.Rgba32)
        {
            ProcessingBridge.RunInPlace<TPixel>(OwnerImage.Owner, lease, (scoped in PixelLease working) => EffectKernels.Flatten<Rgba32>(working, background, maxDegreeOfParallelism, cancellationToken));
        }
        else
        {
            throw new UnreachableException();
        }
    }

    internal override void ConvertColorPixels(scoped in PixelLease source, scoped in PixelLease destination, IccPipeline pipeline, IccUInt16InputTables? uint16Tables, CancellationToken cancellationToken)
    {
        if (!DrawingKernels.IsBridgedToColorFormat<TPixel>())
        {
            ColorConversionKernels.ConvertRows<TPixel>(source, destination, pipeline, uint16Tables, cancellationToken);
        }
        else if (BridgeFormat == PixelFormat.Rgba64)
        {
            ProcessingBridge.RunCopy<TPixel>(OwnerImage.Owner, source, destination, (scoped in PixelLease from, scoped in PixelLease to) => ColorConversionKernels.ConvertRows<Rgba64>(from, to, pipeline, uint16Tables, cancellationToken));
        }
        else if (BridgeFormat == PixelFormat.Rgb24)
        {
            ProcessingBridge.RunCopy<TPixel>(OwnerImage.Owner, source, destination, (scoped in PixelLease from, scoped in PixelLease to) => ColorConversionKernels.ConvertRows<Rgb24>(from, to, pipeline, uint16Tables, cancellationToken));
        }
        else if (BridgeFormat == PixelFormat.Rgba32)
        {
            ProcessingBridge.RunCopy<TPixel>(OwnerImage.Owner, source, destination, (scoped in PixelLease from, scoped in PixelLease to) => ColorConversionKernels.ConvertRows<Rgba32>(from, to, pipeline, uint16Tables, cancellationToken));
        }
        else
        {
            throw new UnreachableException();
        }
    }

    internal override void AutoCropAnalyzePixels(scoped in PixelLease lease, AutoCropAnalyzer analyzer, CancellationToken cancellationToken)
        => analyzer.Accumulate<TPixel>(lease, cancellationToken);

    internal override void ExtendPixels(scoped in PixelLease source, scoped in PixelLease destination, Point origin, AutoCropAnalysis fill, CancellationToken cancellationToken)
    {
        // The typed background is the exact pixel (the Rgba64 of a floating-point analysis is clipped)
        TPixel pixel = default;
        if (fill is AutoCropAnalysis<TPixel> typed)
        {
            pixel = typed.BackgroundColor;
        }
        else
        {
            var color = fill.BackgroundColor;
            PixelConverter.ConvertRow(new ReadOnlySpan<Rgba64>(in color), new Span<TPixel>(ref pixel));
        }

        ProcessingKernels.Extend(source, destination, origin, pixel, cancellationToken);
    }

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
