using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging;

// Internal entry points of the processing operations, called by
// ImageProcessingExtensions after argument validation. Dispatch to the pixel kernels is typed through the frames.
public abstract partial class Image
{
    /// <summary>
    /// Replaces every displayed frame and the poster with <paramref name="region"/> of the canvas permuted by
    /// <paramref name="transform"/>, transactionally (see <see cref="ReplaceGeometry{TFiller}"/>).
    /// </summary>
    /// <param name="region">The source region; validated by the caller to be non-empty and inside the canvas.</param>
    /// <param name="transform">The permutation applied to the region.</param>
    /// <param name="normalizeOrientation">
    /// <see langword="true"/> for auto-orient: the typed orientation becomes <see cref="ExifOrientation.TopLeft"/>, as does an
    /// existing EXIF orientation tag. Otherwise the typed orientation is kept (stored-pixel coordinates).
    /// </param>
    /// <param name="operation">The operation, for exception messages.</param>
    /// <param name="cancellationToken">The token checked between frames and row bands, and once more right before the commit.</param>
    internal void TransformGeometry(Rectangle region, OrientationTransform transform, bool normalizeOrientation, string operation, CancellationToken cancellationToken)
    {
        Owner.EnsureCanModify(operation);
        cancellationToken.ThrowIfCancellationRequested();
        ReplaceGeometry(transform.GetOutputSize(region.Size), normalizeOrientation, operation, new TransformFiller(region, transform), cancellationToken);
    }

    /// <summary>
    /// Resizes every displayed frame and the poster, transactionally (see <see cref="ReplaceGeometry{TFiller}"/>). The
    /// scratch storage of the resampler is charged to the scope of the image together with the original and replacement
    /// storages, and released whatever the outcome. A resize that keeps the canvas size leaves the image unchanged.
    /// </summary>
    /// <param name="geometry">The geometry computed from the validated options.</param>
    /// <param name="options">The options.</param>
    /// <param name="cancellationToken">The token checked between frames and row bands, and once more right before the commit.</param>
    /// <exception cref="UnsupportedImageFeatureException">Linear-light filtering is requested for pixels that are not known to be sRGB encoded.</exception>
    internal void Resize(ResizeGeometry geometry, ResizeOptions options, CancellationToken cancellationToken)
    {
        const string Operation = "resize the image";
        Owner.EnsureCanModify(Operation);
        var workingSpace = options.WorkingSpace == ResizeWorkingSpace.LinearSrgb && UseLinearLight("resizing", "resize") ? ResizeWorkingSpace.LinearSrgb : ResizeWorkingSpace.Encoded;
        cancellationToken.ThrowIfCancellationRequested();
        if (geometry.IsIdentity)
            return;

        // Large frames are resized by up to MaxDegreeOfParallelism workers (row bands, bit-identical results)
        using var plan = new ResizePlan(geometry, options.Filter, workingSpace, PixelFormat, _configuration.MaxDegreeOfParallelism);
        ReplaceGeometry(geometry.OutputSize, normalizeOrientation: false, Operation, new ResizeFiller(plan), cancellationToken);
    }

    /// <summary>
    /// Detects the background and the content box of the image over every displayed frame and the poster, without changing
    /// anything: each pass leases one frame at a time, and no storage is rented.
    /// </summary>
    /// <param name="options">The options.</param>
    /// <param name="cancellationToken">The token checked before the first pass and between row bands.</param>
    /// <exception cref="InvalidOperationException">A lease is active on a frame.</exception>
    /// <exception cref="ObjectDisposedException">The image is disposed.</exception>
    internal AutoCropAnalysis AnalyzeAutoCropCore(AutoCropOptions options, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        var frames = GetAllFrames();
        var canvas = new Rectangle(0, 0, _size.Width, _size.Height);
        var analyzer = new AutoCropAnalyzer(_size, options.ColorThreshold);
        RunAutoCropPass(frames, analyzer, cancellationToken);
        var found = analyzer.CompleteBorderPass(options.BucketThreshold);
        var background = analyzer.BackgroundColor;
        if (!found)
        {
            analyzer.BeginRetryBorderPass();
            RunAutoCropPass(frames, analyzer, cancellationToken);
            if (!analyzer.CompleteBorderPass(bucketThreshold: null))
                return new AutoCropAnalysis(success: false, _size, canvas, background, weightX: 0, weightY: 0);

            background = analyzer.BackgroundColor;
        }

        analyzer.BeginBoundsPass();
        RunAutoCropPass(frames, analyzer, cancellationToken);
        if (!analyzer.TryGetBounds(out var bounds))
            return new AutoCropAnalysis(success: false, _size, canvas, background, weightX: 0, weightY: 0);

        var weights = (X: 0d, Y: 0d);
        if (options.AnalyzeWeights)
        {
            analyzer.BeginWeightsPass();
            RunAutoCropPass(frames, analyzer, cancellationToken);
            weights = analyzer.GetWeights();
        }

        return new AutoCropAnalysis(success: true, _size, bounds, background, weights.X, weights.Y);
    }

    /// <summary>
    /// Crops every displayed frame and the poster to the padded content box of an analysis, transactionally (see
    /// <see cref="ReplaceGeometry{TFiller}"/>). A rectangle inside the canvas is a plain crop; otherwise the canvas is
    /// enlarged and what lies outside the original canvas is filled with the background of the analysis.
    /// </summary>
    /// <param name="analysis">An analysis of a canvas of the same size (validated by the caller), or <see langword="null"/> to analyze this image.</param>
    /// <param name="options">The options.</param>
    /// <param name="cancellationToken">The token checked during the analysis, between frames and row bands, and once more right before the commit.</param>
    /// <returns><see langword="true"/> if the image changed; <see langword="false"/> if the analysis failed or the rectangle is the whole canvas.</returns>
    /// <exception cref="UnsupportedImageFeatureException">The canvas must be enlarged but the background of the analysis is not exactly representable in the pixel format.</exception>
    internal bool AutoCropCore(AutoCropAnalysis? analysis, AutoCropOptions options, CancellationToken cancellationToken)
    {
        const string Operation = "auto-crop the image";
        Owner.EnsureCanModify(Operation);
        cancellationToken.ThrowIfCancellationRequested();
        analysis ??= AnalyzeAutoCropCore(options, cancellationToken);
        if (!analysis.Success)
            return false;

        var geometry = AutoCropGeometry.Compute(_size, analysis, options);
        if (geometry.IsIdentity(_size))
            return false;

        if (geometry.IsInside(_size))
        {
            TransformGeometry(new Rectangle((int)geometry.X, (int)geometry.Y, (int)geometry.Width, (int)geometry.Height), OrientationTransform.Identity, normalizeOrientation: false, Operation, cancellationToken);
            return true;
        }

        var fill = analysis.BackgroundColor;
        if (!PixelConverter.IsExactlyRepresentable(fill, PixelFormat))
            throw new UnsupportedImageFeatureException(
                $"Enlarging the canvas fills it with the background color of the analysis ({fill}), which {PixelFormat} pixels cannot represent exactly. Use {nameof(AutoCropPaddingMode)}.{nameof(AutoCropPaddingMode.Contain)}, or analyze an image of the same pixel format.",
                ImageFormat.Unknown,
                "Auto-crop background fill");

        // The limits bound both dimensions to 32 bits, and the origin is at most one padded canvas away
        _configuration.Limits.EnsureCanvasWithinLimits(geometry.Width, geometry.Height);
        ReplaceGeometry(new Size((int)geometry.Width, (int)geometry.Height), normalizeOrientation: false, Operation, new ExtendFiller(new Point((int)geometry.X, (int)geometry.Y), fill), cancellationToken);
        return true;
    }

    private static void RunAutoCropPass(ImageFrame[] frames, AutoCropAnalyzer analyzer, CancellationToken cancellationToken)
    {
        foreach (var frame in frames)
        {
            using var lease = frame.GetStorage().AcquireLease();
            frame.AutoCropAnalyzePixels(lease, analyzer, cancellationToken);
        }
    }

    /// <summary>
    /// Replaces every displayed frame and the poster with a storage of <paramref name="newSize"/> filled by
    /// <paramref name="filler"/>, transactionally: all replacement storages are budgeted together with the live ones (and
    /// with the scratch that <see cref="IGeometryFiller.Prepare"/> rents), allocated and filled before anything is
    /// published. Any failure before the commit (limit, allocation, cancellation) releases the replacements and leaves
    /// dimensions, pixels, frame identities and order, and metadata unchanged. After the commit, the frame objects keep
    /// designating the same logical frames over their new storage, and the EXIF profile is reconciled with the new canvas.
    /// </summary>
    private void ReplaceGeometry<TFiller>(Size newSize, bool normalizeOrientation, string operation, TFiller filler, CancellationToken cancellationToken)
        where TFiller : IGeometryFiller
    {
        _configuration.Limits.EnsureCanvasWithinLimits(newSize.Width, newSize.Height);

        var sources = GetAllFrames();
        var bytesPerPixel = PixelFormats.GetBytesPerPixel(PixelFormat);
        var replacements = new PixelStorage[sources.Length];
        ExifProfile? exifProfile;
        using (var transaction = new PixelStorageTransaction(Owner, operation))
        {
            // Old and new buffers are live at the same time: reserve every replacement up front so that an insufficient
            // budget fails before anything is allocated
            transaction.Reserve(sources.Length, newSize.Width, newSize.Height, bytesPerPixel, LayoutOptions);
            filler.Prepare(Owner.Scope);
            for (var i = 0; i < sources.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var source = sources[i];
                var replacement = transaction.Allocate(newSize.Width, newSize.Height, bytesPerPixel, LayoutOptions);
                replacements[i] = replacement;
                using var leases = PixelLeasePair.Acquire(source.GetStorage(), replacement);
                filler.Fill(source, leases.First, leases.Second, cancellationToken);
            }

            exifProfile = _metadata.GetReconciledExifProfile(newSize, normalizeOrientation);
            cancellationToken.ThrowIfCancellationRequested();
            transaction.Commit();
        }

        // Committed: publish without any further failure point, then release the original storages
        for (var i = 0; i < sources.Length; i++)
        {
            var original = sources[i].Storage;
            sources[i].ReplaceStorage(replacements[i]);
            original.Dispose();
        }

        _size = newSize;
        _metadata.ExifProfile = exifProfile;
        if (normalizeOrientation)
        {
            _metadata.Orientation = ExifOrientation.TopLeft;
        }
    }

    /// <summary>Mirrors every displayed frame and the poster in place. On failure, some frames or rows may already be mirrored.</summary>
    internal void FlipAllFrames(FlipMode mode, CancellationToken cancellationToken)
    {
        Owner.EnsureCanModify("flip the image");
        cancellationToken.ThrowIfCancellationRequested();
        _metadata.RemoveStaleThumbnail();
        foreach (var frame in GetAllFrames())
        {
            using var lease = frame.GetStorage().AcquireLease();
            frame.FlipPixels(lease, mode, cancellationToken);
        }
    }

    /// <summary>Converts every displayed frame and the poster to grayscale in place. On failure, some frames or rows may already be converted.</summary>
    internal void GrayscaleAllFrames(CancellationToken cancellationToken)
    {
        Owner.EnsureCanModify("convert the image to grayscale");
        EnsureColorProfileCompatible();
        cancellationToken.ThrowIfCancellationRequested();
        _metadata.RemoveStaleThumbnail();
        foreach (var frame in GetAllFrames())
        {
            using var lease = frame.GetStorage().AcquireLease();
            frame.GrayscalePixels(lease, cancellationToken);
        }
    }

    /// <summary>
    /// Convolves every displayed frame and the poster in place. The scratch storage is charged to the scope of the image and
    /// rented before any pixel changes; afterward, on failure, some frames or rows may already be convolved.
    /// </summary>
    /// <exception cref="UnsupportedImageFeatureException">Linear-light filtering is requested for pixels that are not known to be sRGB encoded.</exception>
    internal void ConvolveAllFrames(ConvolutionOptions options, CancellationToken cancellationToken)
    {
        Owner.EnsureCanModify("convolve the image");
        using var plan = CreateConvolutionPlan(options);
        cancellationToken.ThrowIfCancellationRequested();
        plan.Prepare(Owner.Scope);
        _metadata.RemoveStaleThumbnail();
        foreach (var frame in GetAllFrames())
        {
            using var lease = frame.GetStorage().AcquireLease();
            frame.ConvolvePixels(lease, plan, cancellationToken);
        }
    }

    /// <summary>Convolves one attached frame of this image in place, as <see cref="ConvolveAllFrames"/> does for all of them.</summary>
    /// <param name="frame">The frame.</param>
    /// <param name="storage">The storage of the frame, obtained by the caller (which rejects a detached frame).</param>
    /// <param name="options">The options.</param>
    /// <param name="cancellationToken">The token checked before the first row and between row bands.</param>
    /// <exception cref="UnsupportedImageFeatureException">Linear-light filtering is requested for pixels that are not known to be sRGB encoded.</exception>
    internal void ConvolveFrame(ImageFrame frame, PixelStorage storage, ConvolutionOptions options, CancellationToken cancellationToken)
    {
        using var plan = CreateConvolutionPlan(options);
        cancellationToken.ThrowIfCancellationRequested();
        using var lease = storage.AcquireLease();
        plan.Prepare(Owner.Scope);
        _metadata.RemoveStaleThumbnail();
        frame.ConvolvePixels(lease, plan, cancellationToken);
    }

    /// <summary>Large frames are convolved by up to MaxDegreeOfParallelism workers (row bands, bit-identical results).</summary>
    private ConvolutionPlan CreateConvolutionPlan(ConvolutionOptions options)
    {
        var linear = options.WorkingSpace == ConvolutionWorkingSpace.LinearSrgb && UseLinearLight("convolution", "convolve");
        return new ConvolutionPlan(options.Kernel, options.EdgeMode, options.PreserveAlpha, linear, PixelFormat, _size, _configuration.MaxDegreeOfParallelism);
    }

    /// <summary>
    /// Gets a value indicating whether a filter requested in linear light decodes the samples as sRGB: linear-light samples
    /// (QOI colorspace 1) are filtered as stored, since decoding them as sRGB would apply the curve twice.
    /// </summary>
    /// <param name="operation">The operation, as a noun ("resizing").</param>
    /// <param name="verb">The operation, as a verb ("resize").</param>
    /// <exception cref="UnsupportedImageFeatureException">The ICC profile is not recognized as sRGB for the pixel format. No ICC transform is ever applied.</exception>
    private bool UseLinearLight(string operation, string verb)
    {
        if (_metadata.TransferFunction == ColorTransferFunction.Linear)
            return false;

        if (_metadata.IccProfile is { } profile && !SrgbProfileRecognition.IsSrgb(profile, PixelFormat))
            throw new UnsupportedImageFeatureException(
                $"Linear-light {operation} assumes sRGB pixels, but the ICC profile ({profile.ColorSpace} color space) is not recognized as sRGB for {PixelFormat} pixels. No ICC transform is applied: {verb} in the encoded working space, or remove the profile if the pixels are sRGB.",
                ImageFormat.Unknown,
                $"Linear sRGB {operation} of a non-sRGB color profile");

        return true;
    }

    /// <summary>
    /// Validates that the retained ICC profile can label the pixels before an in-place grayscale conversion. The storage
    /// format is kept, so a compatible profile stays compatible; an incompatible one is never retained silently.
    /// </summary>
    /// <exception cref="UnsupportedImageFeatureException">The profile is incompatible with the pixel format.</exception>
    internal void EnsureColorProfileCompatible()
        => _ = ColorProfileCompatibility.Resolve(_metadata.IccProfile, PixelFormat, discardIncompatible: false);

    /// <summary>Gets the displayed frames followed by the poster, if any.</summary>
    private ImageFrame[] GetAllFrames()
    {
        var count = _frames.Count;
        var result = new ImageFrame[count + (_poster is null ? 0 : 1)];
        _frames.CopyTo(result);
        if (_poster is not null)
        {
            result[count] = _poster;
        }

        return result;
    }

    /// <summary>Fills the replacement storages of a <see cref="ReplaceGeometry{TFiller}"/> transaction (typed dispatch through the frames).</summary>
    private interface IGeometryFiller
    {
        /// <summary>Rents the scratch storage of the operation, after the replacements are reserved.</summary>
        void Prepare(AllocationScope scope);

        /// <summary>Fills the replacement of one frame.</summary>
        void Fill(ImageFrame frame, scoped in PixelLease source, scoped in PixelLease destination, CancellationToken cancellationToken);
    }

    private readonly struct TransformFiller(Rectangle region, OrientationTransform transform) : IGeometryFiller
    {
        public void Prepare(AllocationScope scope)
        {
        }

        public void Fill(ImageFrame frame, scoped in PixelLease source, scoped in PixelLease destination, CancellationToken cancellationToken)
            => frame.TransformPixels(source, destination, region, transform, cancellationToken);
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly struct ExtendFiller(Point origin, Rgba64 fill) : IGeometryFiller
    {
        public void Prepare(AllocationScope scope)
        {
        }

        public void Fill(ImageFrame frame, scoped in PixelLease source, scoped in PixelLease destination, CancellationToken cancellationToken)
            => frame.ExtendPixels(source, destination, origin, fill, cancellationToken);
    }

    private readonly struct ResizeFiller(ResizePlan plan) : IGeometryFiller
    {
        public void Prepare(AllocationScope scope) => plan.Prepare(scope);

        public void Fill(ImageFrame frame, scoped in PixelLease source, scoped in PixelLease destination, CancellationToken cancellationToken)
            => frame.ResizePixels(source, destination, plan, cancellationToken);
    }
}
