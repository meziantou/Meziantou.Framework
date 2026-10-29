using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging;

// Internal entry points of the processing operations, called by
// ImageProcessingExtensions after argument validation. Dispatch to the pixel kernels is typed through the frames.
public abstract partial class Image
{
    /// <summary>
    /// Gets the pixel format the filtering kernels (resize, transform, convolution, blurs, median) work in: the pixel format
    /// of the image, or the lossless widening of a format they do not handle themselves (<see cref="ProcessingBridge"/>).
    /// </summary>
    internal PixelFormat KernelPixelFormat => ProcessingBridge.GetKernelFormat(PixelFormat);

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

        // A transposition swaps the axes of the pixels too: the ratio is computed before the transaction and published after it
        var pixelAspectRatio = transform.Transpose ? _metadata.PixelAspectRatio?.Transpose() : _metadata.PixelAspectRatio;
        ReplaceGeometry(transform.GetOutputSize(region.Size), normalizeOrientation, operation, new TransformFiller(region, transform), cancellationToken);
        _metadata.PixelAspectRatio = pixelAspectRatio;
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
    /// <exception cref="UnsupportedImageFeatureException">
    /// Linear-light filtering is requested for pixels that are not known to be sRGB encoded, or the result is padded and
    /// the pixel format has no alpha to store a padding color that is not opaque.
    /// </exception>
    internal void Resize(ResizeGeometry geometry, ResizeOptions options, CancellationToken cancellationToken)
    {
        const string Operation = "resize the image";
        Owner.EnsureCanModify(Operation);
        var transfer = options.WorkingSpace == ResizeWorkingSpace.LinearSrgb ? GetLinearLightTransfer("resizing", "resize") : (byte)0;
        var workingSpace = transfer is not 0 ? ResizeWorkingSpace.LinearSrgb : ResizeWorkingSpace.Encoded;
        cancellationToken.ThrowIfCancellationRequested();
        var padding = geometry.Padding;
        var background = padding is null ? default : GetPaddingColor(options.BackgroundColor, Operation, "ResizeOptions.BackgroundColor");
        if (geometry.IsIdentity)
        {
            // Nothing to resample: a padding resize only places the pixels, unchanged, on the larger canvas
            if (padding is { } canvas)
            {
                ReplaceGeometry(canvas.CanvasSize, normalizeOrientation: false, Operation, new ExtendFiller(new Point(-canvas.Offset.X, -canvas.Offset.Y), canvas.CanvasSize, analysis: null, background), cancellationToken);
            }

            return;
        }

        EnsureLinearLightDomain(transfer, GetAllFrames(), "resizing", "resize");

        // Large frames are resized by up to MaxDegreeOfParallelism workers (row bands, bit-identical results)
        using var plan = new ResizePlan(geometry, options.Filter, workingSpace, KernelPixelFormat, _configuration.MaxDegreeOfParallelism, transfer);
        if (padding is null)
        {
            ReplaceGeometry(geometry.OutputSize, normalizeOrientation: false, Operation, new ResizeFiller(plan, geometry), cancellationToken);
            return;
        }

        // The resampler writes whole storages: each frame is resized into a temporary storage, then placed on the canvas
        using var scratch = new PadResizeScratch(Owner, geometry.OutputSize, PixelFormats.GetBytesPerPixel(PixelFormat), LayoutOptions);
        ReplaceGeometry(geometry.CanvasSize, normalizeOrientation: false, Operation, new PadResizeFiller(plan, geometry, background, scratch), cancellationToken);
    }

    /// <summary>
    /// Enlarges the canvas of every displayed frame and the poster to <paramref name="width"/> x <paramref name="height"/>,
    /// transactionally (see <see cref="ReplaceGeometry{TFiller}"/>): the pixels are copied unchanged with their top-left
    /// corner at (<paramref name="left"/>, <paramref name="top"/>), and the rest of the canvas is filled with the padding
    /// color. A canvas of the current size leaves the image unchanged.
    /// </summary>
    /// <param name="left">The columns added on the left; validated by the caller to be within the new canvas.</param>
    /// <param name="top">The rows added on the top; validated by the caller to be within the new canvas.</param>
    /// <param name="width">The width of the new canvas, at least the current width.</param>
    /// <param name="height">The height of the new canvas, at least the current height.</param>
    /// <param name="color">The padding color, or <see langword="null"/> for transparent black.</param>
    /// <param name="operation">The operation, for exception messages.</param>
    /// <param name="colorParameterName">The parameter of the padding color, for exception messages.</param>
    /// <param name="cancellationToken">The token checked between frames and row bands, and once more right before the commit.</param>
    /// <exception cref="UnsupportedImageFeatureException">The pixel format has no alpha to store a padding color that is not opaque.</exception>
    /// <exception cref="ImageResourceLimitException">The new canvas exceeds the limits of the configuration.</exception>
    internal void ExtendCanvasCore(long left, long top, long width, long height, Rgba64? color, string operation, string colorParameterName, CancellationToken cancellationToken)
    {
        Owner.EnsureCanModify(operation);
        cancellationToken.ThrowIfCancellationRequested();
        if (width == _size.Width && height == _size.Height)
            return;

        var fill = GetPaddingColor(color, operation, colorParameterName);

        // The limits bound both dimensions to 32 bits, and the offsets are smaller than the dimensions
        _configuration.Limits.EnsureCanvasWithinLimits(width, height);
        var size = new Size((int)width, (int)height);
        ReplaceGeometry(size, normalizeOrientation: false, operation, new ExtendFiller(new Point((int)-left, (int)-top), size, analysis: null, fill), cancellationToken);
    }

    /// <summary>Gets the color of a padding: transparent black by default, which only a pixel format with alpha can store.</summary>
    /// <exception cref="UnsupportedImageFeatureException">The pixel format has no alpha and the color is not opaque.</exception>
    private Rgba64 GetPaddingColor(Rgba64? color, string operation, string colorParameterName)
    {
        if (PixelFormats.HasAlpha(PixelFormat))
            return color ?? default;

        if (color is { A: ushort.MaxValue } opaque)
            return opaque;

        throw new UnsupportedImageFeatureException(
            string.Create(CultureInfo.InvariantCulture, $"Cannot {operation}: the padding is {(color is null ? "transparent by default" : "translucent")}, and the {PixelFormat} pixel format has no alpha to store it. Set {colorParameterName} to an opaque color."),
            ImageFormat.Unknown,
            "Translucent background without alpha");
    }

    /// <summary>
    /// Transforms every displayed frame and the poster, transactionally (see <see cref="ReplaceGeometry{TFiller}"/>). A
    /// transformation that moves every pixel onto a pixel of a canvas it fills (a quarter turn, a mirror) is the exact
    /// permutation of <see cref="TransformGeometry"/>, and one that moves nothing leaves the image unchanged. The scratch
    /// storage of the workers is charged to the scope of the image together with the original and replacement storages.
    /// </summary>
    /// <param name="transform">The transformation, from the image to the plane of the result.</param>
    /// <param name="options">The validated options.</param>
    /// <param name="centered"><see langword="true"/> when <paramref name="transform"/> moves the center of the image to the origin, which then goes to the center of the canvas.</param>
    /// <param name="paramName">The parameter that describes the transformation, for the exceptions.</param>
    /// <param name="cancellationToken">The token checked between frames and rows, and once more right before the commit.</param>
    /// <exception cref="ArgumentException">The transformation cannot be applied (see <see cref="WarpGeometry.Compute"/>).</exception>
    /// <exception cref="UnsupportedImageFeatureException">The pixel format has no alpha and the background is not opaque, or linear-light filtering is requested for pixels that are not known to be sRGB encoded.</exception>
    internal void Warp(ProjectiveTransform transform, TransformOptions options, bool centered, string paramName, CancellationToken cancellationToken)
    {
        const string Operation = "transform the image";
        Owner.EnsureCanModify(Operation);
        var geometry = WarpGeometry.Compute(_size, transform, options, centered, paramName, _configuration.Limits);
        cancellationToken.ThrowIfCancellationRequested();
        if (geometry.IsIdentity && geometry.OutputSize == _size)
            return;

        if (geometry.Permutation is { } permutation)
        {
            TransformGeometry(new Rectangle(0, 0, _size.Width, _size.Height), permutation, normalizeOrientation: false, Operation, cancellationToken);
            return;
        }

        // A canvas the image does not cover shows the background: a format without alpha cannot store a translucent one
        if (!PixelFormats.HasAlpha(PixelFormat) && options.BackgroundColor.A != ushort.MaxValue && geometry.ReadsBeyondTheSource(options.Filter))
        {
            throw new UnsupportedImageFeatureException(
                string.Create(CultureInfo.InvariantCulture, $"Cannot {Operation}: the result shows the background, and the {PixelFormat} pixel format has no alpha to store the translucent background color. Set TransformOptions.BackgroundColor to an opaque color."),
                ImageFormat.Unknown,
                "Translucent background without alpha");
        }

        var transfer = options.WorkingSpace == ResizeWorkingSpace.LinearSrgb ? GetLinearLightTransfer("transforming", "transform") : (byte)0;
        var workingSpace = transfer is not 0 ? ResizeWorkingSpace.LinearSrgb : ResizeWorkingSpace.Encoded;
        EnsureLinearLightDomain(transfer, GetAllFrames(), "transforming", "transform");

        // Large frames are transformed by up to MaxDegreeOfParallelism workers (row bands, bit-identical results)
        using var plan = new WarpPlan(geometry, options.Filter, options.BackgroundColor, workingSpace, KernelPixelFormat, _configuration.MaxDegreeOfParallelism, transfer);
        ReplaceGeometry(geometry.OutputSize, normalizeOrientation: false, Operation, new WarpFiller(plan), cancellationToken);
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
        EnsureAutoCropDomain(frames);
        var canvas = new Rectangle(0, 0, _size.Width, _size.Height);
        var analyzer = new AutoCropAnalyzer(_size, options.ColorThreshold);
        RunAutoCropPass(frames, analyzer, cancellationToken);
        var found = analyzer.CompleteBorderPass(options.BucketThreshold);
        var background = analyzer.BackgroundColor;
        var floatingPointBackground = analyzer.FloatingPointBackgroundColor;
        if (!found)
        {
            analyzer.BeginRetryBorderPass();
            RunAutoCropPass(frames, analyzer, cancellationToken);
            if (!analyzer.CompleteBorderPass(bucketThreshold: null))
                return CreateAutoCropAnalysis(success: false, canvas, background, weightX: 0, weightY: 0, floatingPointBackground);

            background = analyzer.BackgroundColor;
            floatingPointBackground = analyzer.FloatingPointBackgroundColor;
        }

        analyzer.BeginBoundsPass();
        RunAutoCropPass(frames, analyzer, cancellationToken);
        if (!analyzer.TryGetBounds(out var bounds))
            return CreateAutoCropAnalysis(success: false, canvas, background, weightX: 0, weightY: 0, floatingPointBackground);

        var weights = (X: 0d, Y: 0d);
        if (options.AnalyzeWeights)
        {
            analyzer.BeginWeightsPass();
            RunAutoCropPass(frames, analyzer, cancellationToken);
            weights = analyzer.GetWeights();
        }

        return CreateAutoCropAnalysis(success: true, bounds, background, weights.X, weights.Y, floatingPointBackground);
    }

    /// <summary>
    /// Checks, before the analysis, that every floating-point sample is a finite number: the analysis works on the values
    /// as they are stored, beyond [0, 1] too, but a sample that is not a number has no distance to the background.
    /// </summary>
    /// <exception cref="UnsupportedImageFeatureException">A sample is not a number or is infinite.</exception>
    private void EnsureAutoCropDomain(ImageFrame[] frames)
    {
        if (!PixelFormats.IsFloatingPoint(PixelFormat))
            return;

        for (var i = 0; i < frames.Length; i++)
        {
            using var lease = frames[i].GetStorage().AcquireLease();
            if (FloatingPointSampleRange.TryFind(lease, PixelFormat, FloatingPointSampleRequirement.Finite, out var sample))
                throw new UnsupportedImageFeatureException(
                    string.Create(CultureInfo.InvariantCulture, $"The auto-crop analysis compares the {PixelFormat} samples with the background, but the pixel ({sample.X}, {sample.Y}) of {DescribeFrame(i)} is {sample.Describe()}: a sample that is not a number or is infinite has no distance to it. Replace such samples first."),
                    ImageFormat.Unknown,
                    "Auto-crop analysis of non-finite samples");
        }
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
    /// <exception cref="UnsupportedImageFeatureException">The canvas must be enlarged but the background of the analysis is not exactly representable in the pixel format, or the pixel a cursor hotspot designates is not part of the result.</exception>
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

        // The background of an analysis of floating-point pixels is exact in its own pixel type only: its Rgba64 is clipped
        var fill = analysis.BackgroundColor;
        var isFloatingPointAnalysis = analysis is AutoCropAnalysis<RgbaFloat> or AutoCropAnalysis<RgbaHalf>;
        if (isFloatingPointAnalysis ? !IsAnalysisOfThisPixelType(analysis) : !PixelConverter.IsExactlyRepresentable(fill, PixelFormat))
            throw new UnsupportedImageFeatureException(
                $"Enlarging the canvas fills it with the background color of the analysis ({(isFloatingPointAnalysis ? "an analysis of floating-point pixels" : fill)}), which {PixelFormat} pixels cannot represent exactly. Use {nameof(AutoCropPaddingMode)}.{nameof(AutoCropPaddingMode.Contain)}, or analyze an image of the same pixel format.",
                ImageFormat.Unknown,
                "Auto-crop background fill");

        // The limits bound both dimensions to 32 bits, and the origin is at most one padded canvas away
        _configuration.Limits.EnsureCanvasWithinLimits(geometry.Width, geometry.Height);
        var size = new Size((int)geometry.Width, (int)geometry.Height);
        ReplaceGeometry(size, normalizeOrientation: false, Operation, new ExtendFiller(new Point((int)geometry.X, (int)geometry.Y), size, analysis, color: default), cancellationToken);
        return true;
    }

    /// <summary>Gets a value indicating whether an analysis is typed with the pixel type of this image (its typed background is a pixel of the image).</summary>
    internal abstract bool IsAnalysisOfThisPixelType(AutoCropAnalysis analysis);

    /// <summary>Creates the analysis of this image, typed with its pixel type.</summary>
    /// <param name="success">Whether a border and a content box were found.</param>
    /// <param name="bounds">The content box, inside the canvas.</param>
    /// <param name="backgroundColor">The background, exactly representable in the pixel format of the image (clipped to [0, 1] for the floating-point formats).</param>
    /// <param name="weightX">The horizontal weight.</param>
    /// <param name="weightY">The vertical weight.</param>
    /// <param name="floatingPointBackground">The exact background of an image of a floating-point format, or <see langword="null"/> to use <paramref name="backgroundColor"/>.</param>
    internal abstract AutoCropAnalysis CreateAutoCropAnalysis(bool success, Rectangle bounds, Rgba64 backgroundColor, double weightX, double weightY, RgbaFloat? floatingPointBackground = null);

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
    /// A cursor hotspot follows the pixel it designates: the new hotspots are computed before anything is allocated (a
    /// hotspot whose pixel is not kept fails the operation) and published with the storages.
    /// </summary>
    /// <exception cref="UnsupportedImageFeatureException">The pixel a frame hotspot designates is not part of the result.</exception>
    private void ReplaceGeometry<TFiller>(Size newSize, bool normalizeOrientation, string operation, TFiller filler, CancellationToken cancellationToken)
        where TFiller : IGeometryFiller
    {
        _configuration.Limits.EnsureCanvasWithinLimits(newSize.Width, newSize.Height);

        var sources = GetAllFrames();
        var hotspots = MapHotspots(sources, filler, operation);
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
            if (hotspots is not null)
            {
                sources[i].MetadataCore.SetHotspotUnchecked(hotspots[i]);
            }

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
            using (var lease = frame.GetStorage().AcquireLease())
            {
                frame.FlipPixels(lease, mode, cancellationToken);
            }

            FlipHotspot(frame, mode);
        }
    }

    /// <summary>
    /// Moves the cursor hotspot of a frame that was just mirrored to the pixel it designated. It is called once the whole
    /// frame is mirrored, so a canceled flip leaves the hotspot of the interrupted frame where it was.
    /// </summary>
    internal static void FlipHotspot(ImageFrame frame, FlipMode mode)
    {
        if (frame.MetadataCore.Hotspot is { } hotspot)
        {
            var storage = frame.Storage;
            frame.MetadataCore.SetHotspotUnchecked(OrientationTransform.ForFlip(mode).MapPoint(new Size(storage.Width, storage.Height), hotspot));
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
    /// Gives a color to the pixels of frames of this image whose color is within a tolerance of another one, in place
    /// (every displayed frame and the poster when <paramref name="frames"/> is <see langword="null"/>). On failure, some
    /// frames or rows may already be changed.
    /// </summary>
    internal void ReplaceColorFrames(ImageFrame[]? frames, Rgba64 color, Rgba64 replacement, double tolerance, CancellationToken cancellationToken)
    {
        Owner.EnsureCanModify("replace a color");
        cancellationToken.ThrowIfCancellationRequested();

        // The tolerance in 16-bit steps, rounded to nearest with ties upward
        var steps = (int)Math.Floor((tolerance * ushort.MaxValue) + 0.5 + (1.0 / 1048576));
        _metadata.RemoveStaleThumbnail();
        foreach (var frame in frames ?? GetAllFrames())
        {
            using var lease = frame.GetStorage().AcquireLease();
            frame.ReplaceColorPixels(lease, color, replacement, steps, cancellationToken);
        }
    }

    /// <summary>
    /// Reduces the colors of frames of this image in place (every displayed frame and the poster when <paramref name="frames"/>
    /// is <see langword="null"/>). The working memory is charged to the scope of the image and rented before any pixel changes;
    /// afterward, on failure, some frames or rows may already be reduced.
    /// </summary>
    /// <exception cref="ArgumentException">The palette has no entry the pixel format can store.</exception>
    internal void QuantizeFrames(ImageFrame[]? frames, QuantizeOptions options, CancellationToken cancellationToken)
    {
        Owner.EnsureCanModify("quantize the image");
        var usableKeys = options.Palette is { } palette ? FrameQuantization.GetUsableKeys(palette, PixelFormat) : null;
        cancellationToken.ThrowIfCancellationRequested();
        frames ??= GetAllFrames();
        var scope = Owner.Scope;
        using var rows = new FrameQuantization(scope, Size.Width);
        using var quantizer = new PaletteQuantizer(scope, translucent: PixelFormats.HasAlpha(PixelFormat));
        using var mapper = new PaletteRowMapper(scope, Size.Width, options.Dithering, options.DitherStrength);
        _metadata.RemoveStaleThumbnail();
        foreach (var frame in frames)
        {
            using var lease = frame.GetStorage().AcquireLease();
            quantizer.Begin(ImagePalette.MaxCount);
            rows.AddRows(quantizer, lease, PixelFormat, cancellationToken);
            if (usableKeys is not null)
            {
                quantizer.SetPalette(usableKeys);
            }
            else
            {
                quantizer.BuildPalette(options.MaxColors, includeTransparent: true);
            }

            mapper.Reset();
            rows.MapRows(quantizer, mapper, lease, PixelFormat, cancellationToken);
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
        var frames = GetAllFrames();
        EnsureLinearLightDomain(plan.Transfer, frames, "convolution", "convolve");
        plan.Prepare(Owner.Scope);
        _metadata.RemoveStaleThumbnail();
        foreach (var frame in frames)
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
        EnsureLinearLightDomain(plan.Transfer, lease, "the frame", "convolution", "convolve");
        plan.Prepare(Owner.Scope);
        _metadata.RemoveStaleThumbnail();
        frame.ConvolvePixels(lease, plan, cancellationToken);
    }

    /// <summary>
    /// Replaces every displayed frame and the poster by its edges, in place: converted to grayscale first when
    /// <paramref name="grayscale"/> is set, then convolved by the kernels of the operator, whose sums are combined. The
    /// scratch is charged to the scope of the image and rented before any pixel changes; afterward, on failure, some
    /// frames or rows may already be changed.
    /// </summary>
    /// <exception cref="UnsupportedImageFeatureException"><paramref name="grayscale"/> is set and the ICC profile is incompatible with the pixel format.</exception>
    internal void DetectEdgesAllFrames(EdgeDetector detector, bool grayscale, ConvolutionEdgeMode edgeMode, CancellationToken cancellationToken)
    {
        Owner.EnsureCanModify("detect the edges of the image");
        if (grayscale)
        {
            EnsureColorProfileCompatible();
        }

        using var plan = ConvolutionPlan.CreateEdgeDetection(detector, edgeMode, KernelPixelFormat, _size, _configuration.MaxDegreeOfParallelism);
        cancellationToken.ThrowIfCancellationRequested();
        plan.Prepare(Owner.Scope);
        _metadata.RemoveStaleThumbnail();
        foreach (var frame in GetAllFrames())
        {
            using var lease = frame.GetStorage().AcquireLease();
            if (grayscale)
            {
                frame.GrayscalePixels(lease, cancellationToken);
            }

            frame.ConvolvePixels(lease, plan, cancellationToken);
        }
    }

    /// <summary>Replaces one attached frame of this image by its edges, in place, as <see cref="DetectEdgesAllFrames"/> does for all of them.</summary>
    /// <exception cref="UnsupportedImageFeatureException"><paramref name="grayscale"/> is set and the ICC profile is incompatible with the pixel format.</exception>
    internal void DetectEdgesFrame(ImageFrame frame, PixelStorage storage, EdgeDetector detector, bool grayscale, ConvolutionEdgeMode edgeMode, CancellationToken cancellationToken)
    {
        if (grayscale)
        {
            EnsureColorProfileCompatible();
        }

        using var plan = ConvolutionPlan.CreateEdgeDetection(detector, edgeMode, KernelPixelFormat, _size, _configuration.MaxDegreeOfParallelism);
        cancellationToken.ThrowIfCancellationRequested();
        using var lease = storage.AcquireLease();
        plan.Prepare(Owner.Scope);
        _metadata.RemoveStaleThumbnail();
        if (grayscale)
        {
            frame.GrayscalePixels(lease, cancellationToken);
        }

        frame.ConvolvePixels(lease, plan, cancellationToken);
    }

    /// <summary>
    /// Blurs every displayed frame and the poster in place: one separable convolution, each row filtered by the weights
    /// then the filtered rows combined by the same weights as a column, without rounding in between. The scratch is
    /// prepared before a pixel changes. A positive <paramref name="sharpenAmount"/> makes it an unsharp mask.
    /// </summary>
    internal void BlurAllFrames(double[] weights, ConvolutionEdgeMode edgeMode, ConvolutionWorkingSpace workingSpace, double sharpenAmount, double sharpenThreshold, CancellationToken cancellationToken)
    {
        Owner.EnsureCanModify(sharpenAmount > 0 ? "sharpen the image" : "blur the image");
        using var plan = CreateBlurPlan(weights, edgeMode, workingSpace, sharpenAmount, sharpenThreshold);
        cancellationToken.ThrowIfCancellationRequested();
        var frames = GetAllFrames();
        EnsureLinearLightDomain(plan.Transfer, frames, "convolution", "convolve");
        plan.Prepare(Owner.Scope);
        _metadata.RemoveStaleThumbnail();
        foreach (var frame in frames)
        {
            using var lease = frame.GetStorage().AcquireLease();
            frame.ConvolvePixels(lease, plan, cancellationToken);
        }
    }

    /// <summary>Blurs one attached frame of this image in place, as <see cref="BlurAllFrames"/> does for all of them.</summary>
    internal void BlurFrame(ImageFrame frame, PixelStorage storage, double[] weights, ConvolutionEdgeMode edgeMode, ConvolutionWorkingSpace workingSpace, double sharpenAmount, double sharpenThreshold, CancellationToken cancellationToken)
    {
        using var plan = CreateBlurPlan(weights, edgeMode, workingSpace, sharpenAmount, sharpenThreshold);
        cancellationToken.ThrowIfCancellationRequested();
        using var lease = storage.AcquireLease();
        EnsureLinearLightDomain(plan.Transfer, lease, "the frame", "convolution", "convolve");
        plan.Prepare(Owner.Scope);
        _metadata.RemoveStaleThumbnail();
        frame.ConvolvePixels(lease, plan, cancellationToken);
    }

    /// <summary>
    /// Blurs every displayed frame and the poster in place with a box blur. The scratch is charged to the scope of the image
    /// and rented before any pixel changes; afterward, on failure, some frames or rows may already be blurred.
    /// </summary>
    internal void BoxBlurAllFrames(int radius, ConvolutionEdgeMode edgeMode, ConvolutionWorkingSpace workingSpace, CancellationToken cancellationToken)
    {
        Owner.EnsureCanModify("blur the image");
        using var plan = CreateBoxBlurPlan(radius, edgeMode, workingSpace);
        cancellationToken.ThrowIfCancellationRequested();
        var frames = GetAllFrames();
        EnsureLinearLightDomain(plan.Transfer, frames, "convolution", "convolve");
        plan.Prepare(Owner.Scope);
        _metadata.RemoveStaleThumbnail();
        foreach (var frame in frames)
        {
            using var lease = frame.GetStorage().AcquireLease();
            frame.BoxBlurPixels(lease, plan, cancellationToken);
        }
    }

    /// <summary>Blurs one attached frame of this image in place with a box blur, as <see cref="BoxBlurAllFrames"/> does for all of them.</summary>
    internal void BoxBlurFrame(ImageFrame frame, PixelStorage storage, int radius, ConvolutionEdgeMode edgeMode, ConvolutionWorkingSpace workingSpace, CancellationToken cancellationToken)
    {
        using var plan = CreateBoxBlurPlan(radius, edgeMode, workingSpace);
        cancellationToken.ThrowIfCancellationRequested();
        using var lease = storage.AcquireLease();
        EnsureLinearLightDomain(plan.Transfer, lease, "the frame", "convolution", "convolve");
        plan.Prepare(Owner.Scope);
        _metadata.RemoveStaleThumbnail();
        frame.BoxBlurPixels(lease, plan, cancellationToken);
    }

    /// <summary>
    /// Replaces every sample of every displayed frame and the poster by the median of its neighborhood, in place. The scratch
    /// is charged to the scope of the image and rented before any pixel changes; afterward, on failure, some frames or rows
    /// may already be filtered.
    /// </summary>
    internal void MedianAllFrames(int radius, ConvolutionEdgeMode edgeMode, CancellationToken cancellationToken)
    {
        Owner.EnsureCanModify("filter the image");
        using var plan = new MedianPlan(radius, edgeMode, KernelPixelFormat, _size, _configuration.MaxDegreeOfParallelism);
        cancellationToken.ThrowIfCancellationRequested();
        plan.Prepare(Owner.Scope);
        _metadata.RemoveStaleThumbnail();
        foreach (var frame in GetAllFrames())
        {
            using var lease = frame.GetStorage().AcquireLease();
            frame.MedianPixels(lease, plan, cancellationToken);
        }
    }

    /// <summary>Filters one attached frame of this image in place with a median filter, as <see cref="MedianAllFrames"/> does for all of them.</summary>
    internal void MedianFrame(ImageFrame frame, PixelStorage storage, int radius, ConvolutionEdgeMode edgeMode, CancellationToken cancellationToken)
    {
        using var plan = new MedianPlan(radius, edgeMode, KernelPixelFormat, _size, _configuration.MaxDegreeOfParallelism);
        cancellationToken.ThrowIfCancellationRequested();
        using var lease = storage.AcquireLease();
        plan.Prepare(Owner.Scope);
        _metadata.RemoveStaleThumbnail();
        frame.MedianPixels(lease, plan, cancellationToken);
    }

    /// <summary>Large frames are convolved by up to MaxDegreeOfParallelism workers (row bands, bit-identical results).</summary>
    private ConvolutionPlan CreateConvolutionPlan(ConvolutionOptions options)
    {
        var transfer = options.WorkingSpace == ConvolutionWorkingSpace.LinearSrgb ? GetLinearLightTransfer("convolution", "convolve") : (byte)0;
        return new ConvolutionPlan(options.Kernel, options.EdgeMode, options.PreserveAlpha, transfer is not 0, KernelPixelFormat, _size, _configuration.MaxDegreeOfParallelism, transfer, bias: options.Bias);
    }

    /// <summary>The separable plan of a blur: the weights as a column, every row filtered by the same weights first (and an unsharp mask when <paramref name="sharpenAmount"/> is positive).</summary>
    private ConvolutionPlan CreateBlurPlan(double[] weights, ConvolutionEdgeMode edgeMode, ConvolutionWorkingSpace workingSpace, double sharpenAmount, double sharpenThreshold)
    {
        var transfer = workingSpace == ConvolutionWorkingSpace.LinearSrgb ? GetLinearLightTransfer("convolution", "convolve") : (byte)0;
        return new ConvolutionPlan(new ConvolutionKernel(1, weights.Length, weights), edgeMode, preserveAlpha: false, transfer is not 0, KernelPixelFormat, _size, _configuration.MaxDegreeOfParallelism, transfer, rowWeights: weights)
        {
            SharpenAmount = sharpenAmount,
            SharpenThreshold = sharpenThreshold,
        };
    }

    private BoxBlurPlan CreateBoxBlurPlan(int radius, ConvolutionEdgeMode edgeMode, ConvolutionWorkingSpace workingSpace)
    {
        var transfer = workingSpace == ConvolutionWorkingSpace.LinearSrgb ? GetLinearLightTransfer("convolution", "convolve") : (byte)0;
        return new BoxBlurPlan(radius, edgeMode, KernelPixelFormat, _size, _configuration.MaxDegreeOfParallelism, transfer);
    }

    /// <summary>
    /// Converts the colors of every displayed frame and the poster from the color space of the image to
    /// <paramref name="destination"/>, transactionally (see <see cref="ReplaceGeometry{TFiller}"/>): the converted pixels
    /// and the new profile are published together, so a failure never leaves converted pixels labeled with the previous
    /// profile, nor the reverse.
    /// </summary>
    /// <param name="destination">The profile of the converted pixels.</param>
    /// <param name="options">The conversion options.</param>
    /// <param name="cancellationToken">The token checked between frames and row bands, and once more right before the commit.</param>
    /// <exception cref="UnsupportedImageFeatureException">A profile cannot label the pixel format, the color space of the image is ambiguous, or a profile is not supported.</exception>
    /// <exception cref="InvalidImageContentException">A profile is malformed.</exception>
    internal void ConvertAllFramesToColorProfile(IccProfile destination, IccColorTransformOptions options, CancellationToken cancellationToken)
    {
        const string Operation = "convert the color profile of the image";
        Owner.EnsureCanModify(Operation);
        EnsureColorProfileCompatible();
        if (!ColorProfileCompatibility.IsCompatible(destination, PixelFormat))
        {
            var kind = PixelFormats.IsGrayscale(PixelFormat) ? "grayscale" : "color";
            throw new UnsupportedImageFeatureException(
                $"The destination ICC profile declares the {destination.ColorSpace} color space, which cannot label {kind} {PixelFormat} pixels. Convert the image to a compatible pixel format first, or convert sample buffers with IccColorTransform.",
                ImageFormat.Unknown,
                "Incompatible color profile");
        }

        // The colors are those of the CICP color description when it is what describes them; otherwise those of a profile
        var hasCicpSource = TryGetCicpColorSpace(options.ToneMapping, out var colorSpace, out var toneMapping);
        var pipeline = hasCicpSource
            ? IccPipeline.Create(colorSpace, destination, options.Intent, options.BlackPointCompensation, toneMapping)
            : IccColorTransform.Create(GetSourceColorProfile(), destination, options).Pipeline;
        cancellationToken.ThrowIfCancellationRequested();
        if (!pipeline.IsIdentity)
        {
            EnsureColorConversionDomain(GetAllFrames());

            // 16-bit samples read the first stage from tables when the image is large enough (the tables are rented once,
            // after the replacements are reserved, and released with the conversion)
            var colors = PixelFormats.GetBitsPerComponent(KernelPixelFormat) == 16 && !PixelFormats.IsFloatingPoint(KernelPixelFormat) ? (long)_size.Width * _size.Height * GetAllFrames().Length : 0;
            using var scratch = new ColorConversionScratch();
            ReplaceGeometry(_size, normalizeOrientation: false, Operation, new ColorConversionFiller(pipeline, colors, scratch), cancellationToken);
        }

        // Committed (or nothing to convert): the pixels are now described by the destination profile. A CICP description
        // of another color space than sRGB, and the light levels that go with it, no longer describe them, whether they
        // were converted from it or from a profile that stood in its place. A description that is equivalent to sRGB is
        // kept: it says nothing that the profile contradicts, and its matrix coefficients are those of the file
        _metadata.IccProfile = destination;
        _metadata.TransferFunction = ColorTransferFunction.Srgb;
        if (hasCicpSource || (_metadata.Cicp is { } description && !MetadataWritePlan.IsSrgbEquivalent(description)))
        {
            _metadata.Cicp = null;
            _metadata.ContentLightLevel = null;
            _metadata.MasteringDisplay = null;
        }
    }

    /// <summary>
    /// Gets the color space that the CICP color description gives to the stored pixels, when it is what describes them:
    /// an image without ICC profile whose description is not the one of sRGB or of linear sRGB samples, which keep
    /// their built-in profiles (<see cref="GetSourceColorProfile"/>), or an image whose ICC profile has a <c>cicp</c> tag
    /// of high dynamic range samples (PQ, HLG), whose curves the profile only approximates.
    /// </summary>
    /// <remarks>
    /// Unspecified code points (2) say nothing: the primaries are then those of BT.709 and the function is the sRGB
    /// one, as for an image without description. The linear transfer function label
    /// (<see cref="ImageMetadata.TransferFunction"/>) wins over the transfer characteristics, as it does for a profile.
    /// The matrix coefficients and the range describe how a file stored the samples, not the pixels of the image.
    /// </remarks>
    /// <param name="options">How high dynamic range samples are brought to a standard dynamic range, or <see langword="null"/> to refuse them.</param>
    /// <param name="colorSpace">Receives the color space.</param>
    /// <param name="toneMapping">Receives the stage that compresses the light of PQ and HLG samples, or <see langword="null"/> for the other ones.</param>
    /// <exception cref="UnsupportedImageFeatureException">
    /// The primaries or the transfer characteristics are reserved, or the samples are high dynamic range ones (PQ, HLG),
    /// which no ICC profile describes, and <paramref name="options"/> is <see langword="null"/>: their highlights are
    /// not compressed implicitly.
    /// </exception>
    private bool TryGetCicpColorSpace(HdrToneMappingOptions? options, out CicpColorSpace colorSpace, out HdrToneMapStage? toneMapping)
    {
        colorSpace = default;
        toneMapping = null;
        CicpColorDescription cicp;
        if (_metadata.IccProfile is { } profile)
        {
            // A profile on samples labeled as linear light is ambiguous (GetSourceColorProfile)
            if (profile.Cicp is not { } tag || !CicpTransfer.IsHdr(tag.TransferCharacteristics) || _metadata.TransferFunction == ColorTransferFunction.Linear)
                return false;

            cicp = tag;
        }
        else if (_metadata.Cicp is { } description)
        {
            cicp = description;
        }
        else
        {
            return false;
        }

        var isGray = PixelFormats.IsGrayscale(PixelFormat);
        var isLabeledLinear = _metadata.TransferFunction == ColorTransferFunction.Linear;
        var primaries = cicp.ColorPrimaries is 2 ? (byte)1 : cicp.ColorPrimaries;
        var transfer = isLabeledLinear ? CicpTransfer.Linear
            : cicp.TransferCharacteristics is 2 ? CicpTransfer.Srgb
            : cicp.TransferCharacteristics;

        // sRGB samples, and samples labeled as linear sRGB, are those of the built-in profiles
        if ((isGray || primaries is 1) && (transfer is CicpTransfer.Srgb || isLabeledLinear))
            return false;

        if (!CicpTransfer.IsSupported(transfer))
            throw new UnsupportedImageFeatureException(
                $"The transfer characteristics of the CICP color description ({cicp}) are reserved: the color space of the image is unknown. Set Metadata.Cicp or Metadata.IccProfile to what the samples are before converting them.",
                ImageFormat.Unknown,
                "Color conversion from reserved transfer characteristics");

        if (!isGray && !CicpPrimaries.IsSupported(primaries))
            throw new UnsupportedImageFeatureException(
                $"The color primaries of the CICP color description ({cicp}) are reserved: the color space of the image is unknown. Set Metadata.Cicp or Metadata.IccProfile to what the samples are before converting them.",
                ImageFormat.Unknown,
                "Color conversion from reserved color primaries");

        colorSpace = new CicpColorSpace(primaries, transfer, isGray);
        if (!CicpTransfer.IsHdr(transfer))
            return true;

        if (options is null)
            throw new UnsupportedImageFeatureException(
                $"The samples of the image are high dynamic range ones ({cicp}): an ICC profile describes a standard dynamic range, so converting them means choosing how their highlights are compressed, which is not done implicitly. Set IccColorTransformOptions.ToneMapping.",
                ImageFormat.Unknown,
                "Color conversion of high dynamic range samples without tone mapping");

        // The peak of the source: what the caller says, else what the image declares, else the nominal peak of the
        // function. A content light level describes the light of a display, which PQ samples are; the light of HLG
        // samples depends on the display, whose peak is the one of the mastering display
        var peak = options.SourcePeakLuminance
            ?? (transfer is CicpTransfer.Pq && _metadata.ContentLightLevel is { MaxContentLightLevel: > 0 } lightLevel ? lightLevel.MaxContentLightLevel
            : _metadata.MasteringDisplay is { MaxLuminance: > 0 } display ? Math.Min(display.MaxLuminanceCandelasPerSquareMeter, 10_000)
            : transfer is CicpTransfer.Pq ? 10_000
            : 1_000);
        toneMapping = new HdrToneMapStage(colorSpace, options.Operator, peak, options.TargetWhiteLuminance);
        return true;
    }

    /// <summary>
    /// Gets the profile describing the stored pixels: the ICC profile of the image, else sRGB (sGray for grayscale
    /// formats), or their linear-light counterparts when the samples are labeled linear.
    /// </summary>
    /// <exception cref="UnsupportedImageFeatureException">The image has both an ICC profile and the linear transfer function label.</exception>
    private IccProfile GetSourceColorProfile()
    {
        var linear = _metadata.TransferFunction == ColorTransferFunction.Linear;
        if (_metadata.IccProfile is { } profile)
        {
            if (linear)
                throw new UnsupportedImageFeatureException(
                    "The image has an ICC profile and is also labeled as linear light (ImageMetadata.TransferFunction), so its color space is ambiguous. Remove the profile if the samples are linear sRGB, or reset the transfer function if the profile describes them.",
                    ImageFormat.Unknown,
                    "ICC profile on linear-light samples");

            return profile;
        }

        if (PixelFormats.IsGrayscale(PixelFormat))
            return linear ? BuiltInIccProfiles.LinearGray : BuiltInIccProfiles.SrgbGray;

        return linear ? BuiltInIccProfiles.LinearSrgb : BuiltInIccProfiles.Srgb;
    }

    /// <summary>
    /// Gets the transfer characteristics (<see cref="CicpTransfer"/>) whose function decodes the samples of the image to
    /// linear light, for a filter that is requested in linear light: the function that the image declares, which is
    /// sRGB for an image that declares none. Returns 0 when the samples are filtered as stored: linear-light samples
    /// (QOI colorspace 1, or linear transfer characteristics) are already what the filter works on, and decoding them
    /// would apply a curve to light.
    /// </summary>
    /// <remarks>
    /// An ICC profile describes the colors in the place of a CICP description; only a profile that is recognized as sRGB
    /// has a function here. Without profile, the transfer characteristics of the CICP description are used, whatever its
    /// color primaries: a filter combines the light of each channel, which does not depend on the primaries. PQ and HLG
    /// samples are filtered in the light their function decodes to, relative to its nominal peak.
    /// </remarks>
    /// <param name="operation">The operation, as a noun ("resizing").</param>
    /// <param name="verb">The operation, as a verb ("resize").</param>
    /// <exception cref="UnsupportedImageFeatureException">
    /// The ICC profile is not recognized as sRGB for the pixel format (no ICC transform is applied implicitly), or the
    /// transfer characteristics of the CICP description have no function.
    /// </exception>
    internal byte GetLinearLightTransfer(string operation, string verb)
    {
        if (_metadata.TransferFunction == ColorTransferFunction.Linear)
            return 0;

        if (_metadata.IccProfile is { } profile)
        {
            // A profile of high dynamic range samples names their function in its cicp tag
            if (profile.Cicp is { } tag && CicpTransfer.IsHdr(tag.TransferCharacteristics))
                return tag.TransferCharacteristics;

            if (!SrgbProfileRecognition.IsSrgb(profile, PixelFormat))
                throw new UnsupportedImageFeatureException(
                    $"Linear-light {operation} assumes sRGB pixels, but the ICC profile ({profile.ColorSpace} color space) is not recognized as sRGB for {PixelFormat} pixels. No ICC transform is applied implicitly: convert the image to sRGB with ConvertColorProfile first, {verb} in the encoded working space, or remove the profile if the pixels are sRGB.",
                    ImageFormat.Unknown,
                    $"Linear sRGB {operation} of a non-sRGB color profile");

            return CicpTransfer.Srgb;
        }

        // Unspecified transfer characteristics (2) say nothing: the samples are sRGB, like those of an image without description
        if (_metadata.Cicp is not { } cicp || cicp.TransferCharacteristics is CicpTransfer.Srgb or 2)
            return CicpTransfer.Srgb;

        if (!CicpTransfer.IsSupported(cicp.TransferCharacteristics))
            throw new UnsupportedImageFeatureException(
                $"Linear-light {operation} decodes the samples with their transfer function, but the transfer characteristics of the CICP color description ({cicp}) are reserved. Use the encoded working space to {verb} the stored samples.",
                ImageFormat.Unknown,
                $"Linear-light {operation} of reserved transfer characteristics");

        return cicp.TransferCharacteristics is CicpTransfer.Linear ? (byte)0 : cicp.TransferCharacteristics;
    }

    /// <summary>
    /// Checks, before anything changes, that the transfer function of linear-light filtering can decode every color of
    /// floating-point frames: an encoded value outside [0, 1], the domain of the function, has no linear-light value, and
    /// is not clipped silently. Samples labeled as linear light (<paramref name="transfer"/> 0) are filtered as they are,
    /// whatever their values, and integer samples are always in the domain.
    /// </summary>
    /// <param name="transfer">The transfer characteristics of the filter (<see cref="GetLinearLightTransfer"/>), or 0.</param>
    /// <param name="frames">The frames of the operation (<see cref="GetAllFrames"/>).</param>
    /// <param name="operation">The operation, as a noun ("resizing").</param>
    /// <param name="verb">The operation, as a verb ("resize").</param>
    /// <exception cref="UnsupportedImageFeatureException">A color sample is outside [0, 1] or is not a number.</exception>
    private void EnsureLinearLightDomain(byte transfer, ImageFrame[] frames, string operation, string verb)
    {
        if (transfer is 0 || !PixelFormats.IsFloatingPoint(PixelFormat))
            return;

        for (var i = 0; i < frames.Length; i++)
        {
            using var lease = frames[i].GetStorage().AcquireLease();
            EnsureLinearLightDomain(transfer, lease, DescribeFrame(i), operation, verb);
        }
    }

    /// <summary>Checks one leased frame (see <see cref="EnsureLinearLightDomain(byte, ImageFrame[], string, string)"/>).</summary>
    private void EnsureLinearLightDomain(byte transfer, scoped in PixelLease lease, string frame, string operation, string verb)
    {
        if (transfer is 0 || !FloatingPointSampleRange.TryFind(lease, PixelFormat, FloatingPointSampleRequirement.UnitColors, out var sample))
            return;

        throw new UnsupportedImageFeatureException(
            string.Create(CultureInfo.InvariantCulture, $"Linear-light {operation} decodes the {PixelFormat} samples with the transfer function of the image (transfer characteristics {transfer}), which is only defined on [0, 1], but the pixel ({sample.X}, {sample.Y}) of {frame} is {sample.Describe()}: an encoded value outside [0, 1] or that is not a number has no linear-light value, and it is not clipped implicitly. Set Metadata.TransferFunction to Linear if the samples are linear light, or {verb} in the encoded working space."),
            ImageFormat.Unknown,
            $"Linear-light {operation} of floating-point samples outside [0, 1]");
    }

    /// <summary>
    /// Checks, before anything changes, that a color conversion can read every color of floating-point frames: an ICC
    /// profile and a CICP color description encode values in [0, 1], and a sample outside that range is not clipped
    /// silently. Alpha is copied unchanged, whatever its value.
    /// </summary>
    /// <exception cref="UnsupportedImageFeatureException">A color sample is outside [0, 1] or is not a number.</exception>
    private void EnsureColorConversionDomain(ImageFrame[] frames)
    {
        if (!PixelFormats.IsFloatingPoint(PixelFormat))
            return;

        for (var i = 0; i < frames.Length; i++)
        {
            using var lease = frames[i].GetStorage().AcquireLease();
            if (FloatingPointSampleRange.TryFind(lease, PixelFormat, FloatingPointSampleRequirement.UnitColors, out var sample))
                throw new UnsupportedImageFeatureException(
                    string.Create(CultureInfo.InvariantCulture, $"A color conversion reads the {PixelFormat} samples as values in [0, 1], the range that an ICC profile or a CICP color description encodes, but the pixel ({sample.X}, {sample.Y}) of {DescribeFrame(i)} is {sample.Describe()}: a color outside that range, or a sample that is not a number, is not clipped implicitly. Bring the samples into [0, 1] first, for example by converting the image to Rgba64 with Image.CloneAs and PixelConversionOptions.ToneMapping or PixelConversionOptions.OutOfRangeSamples."),
                    ImageFormat.Unknown,
                    "Color conversion of floating-point samples outside [0, 1]");
        }
    }

    /// <summary>Names a frame of <see cref="GetAllFrames"/> for an exception message.</summary>
    private string DescribeFrame(int index)
        => index < _frames.Count ? string.Create(CultureInfo.InvariantCulture, $"frame {index}") : "the poster frame";

    /// <summary>
    /// Gets the tone curves of the ICC profile of the image, for what combines its samples in linear light, when the
    /// profile is what says how they are encoded and is not recognized as sRGB: the samples of any other image are
    /// decoded by the function of <see cref="GetLinearLightTransfer"/>.
    /// </summary>
    /// <returns>The curves, or <see langword="null"/> when a transfer function decodes the samples.</returns>
    /// <exception cref="InvalidImageContentException">The profile is malformed.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The profile cannot label the pixel format, or has no tone curve for each channel.</exception>
    /// <exception cref="ImageResourceLimitException">The tables of the curves exceed the allocation limit.</exception>
    internal LinearLightCurves? CreateLinearLightCurves()
    {
        if (_metadata.TransferFunction == ColorTransferFunction.Linear || _metadata.IccProfile is not { } profile || SrgbProfileRecognition.IsSrgb(profile, PixelFormat))
            return null;

        EnsureColorProfileCompatible();
        return LinearLightCurves.Create(profile, Owner.Scope);
    }

    /// <summary>
    /// Creates the conversion that brings the colors of a profile to the color space of the stored pixels: the one of
    /// the ICC profile of the image, else the one of its CICP color description, else sRGB (see
    /// <see cref="GetSourceColorProfile"/> and <see cref="TryGetCicpColorSpace"/>).
    /// </summary>
    /// <param name="paints">The profile of the colors: an RGB profile.</param>
    /// <param name="options">The options of the conversion.</param>
    /// <returns>The conversion, or <see langword="null"/> when the profile is the one of the image: the colors are not changed.</returns>
    /// <exception cref="InvalidImageContentException">A profile is malformed.</exception>
    /// <exception cref="UnsupportedImageFeatureException">
    /// The profile of the paints is not an RGB profile, a profile is not supported in its role, the color space of the
    /// image is ambiguous or unknown, or its samples are high dynamic range ones.
    /// </exception>
    internal IccPipeline? CreatePaintPipeline(IccProfile paints, IccColorTransformOptions options)
    {
        if (paints.ColorSpace != IccProfileColorSpace.Rgb)
            throw new UnsupportedImageFeatureException(
                $"The colors of the paints are red, green and blue values, but the profile that describes them declares the {paints.ColorSpace} color space. Use an RGB profile.",
                ImageFormat.Unknown,
                "Paint colors of a profile that is not RGB");

        EnsureColorProfileCompatible();
        if (_metadata.IccProfile is null && _metadata.TransferFunction != ColorTransferFunction.Linear && _metadata.Cicp is { } cicp && CicpTransfer.IsHdr(cicp.TransferCharacteristics))
            throw new UnsupportedImageFeatureException(
                $"The samples of the image are high dynamic range ones ({cicp}): the light that the white of a paint has among them is a choice that is not made implicitly. Give the paints as sample values of the image, or convert the image to a standard dynamic range with ConvertColorProfile first.",
                ImageFormat.Unknown,
                "Paint colors of a profile on high dynamic range samples");

        var pipeline = TryGetCicpColorSpace(options: null, out var colorSpace, out _)
            ? IccPipeline.Create(paints, colorSpace, options.Intent, options.BlackPointCompensation)
            : IccPipeline.Create(paints, GetSourceColorProfile(), options.Intent, options.BlackPointCompensation);
        return pipeline.IsIdentity ? null : pipeline;
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

    /// <summary>
    /// Computes where the cursor hotspot of every frame lands after a geometry operation, before anything is allocated.
    /// </summary>
    /// <returns>The new hotspot of each frame, or <see langword="null"/> when no frame has one.</returns>
    /// <exception cref="UnsupportedImageFeatureException">The pixel a hotspot designates is not part of the result: the hotspot is never dropped or moved to another pixel silently.</exception>
    private Point?[]? MapHotspots<TFiller>(ImageFrame[] sources, TFiller filler, string operation)
        where TFiller : IGeometryFiller
    {
        Point?[]? result = null;
        for (var i = 0; i < sources.Length; i++)
        {
            if (sources[i].MetadataCore.Hotspot is not { } hotspot)
                continue;

            if (!filler.TryMapPoint(hotspot, out var mapped))
            {
                var frame = i < _frames.Count ? string.Create(CultureInfo.InvariantCulture, $"frame {i}") : "the poster frame";
                throw new UnsupportedImageFeatureException(
                    string.Create(CultureInfo.InvariantCulture, $"Cannot {operation}: the cursor hotspot ({hotspot.X}, {hotspot.Y}) of {frame} designates a pixel that is not part of the result. Set FrameMetadata.Hotspot to null or to a pixel that is kept first."),
                    ImageFormat.Unknown,
                    "Cursor hotspot outside the kept region");
            }

            result ??= new Point?[sources.Length];
            result[i] = mapped;
        }

        return result;
    }

    /// <summary>Fills the replacement storages of a <see cref="ReplaceGeometry{TFiller}"/> transaction (typed dispatch through the frames).</summary>
    private interface IGeometryFiller
    {
        /// <summary>Gets where a pixel of a source frame lands in its replacement (the new position of a cursor hotspot).</summary>
        /// <returns><see langword="false"/> when the pixel is not part of the result.</returns>
        bool TryMapPoint(Point source, out Point destination);

        /// <summary>Rents the scratch storage of the operation, after the replacements are reserved.</summary>
        void Prepare(AllocationScope scope);

        /// <summary>Fills the replacement of one frame.</summary>
        void Fill(ImageFrame frame, scoped in PixelLease source, scoped in PixelLease destination, CancellationToken cancellationToken);
    }

    private readonly struct TransformFiller(Rectangle region, OrientationTransform transform) : IGeometryFiller
    {
        public bool TryMapPoint(Point source, out Point destination)
        {
            var x = source.X - region.X;
            var y = source.Y - region.Y;
            if (x < 0 || y < 0 || x >= region.Width || y >= region.Height)
            {
                destination = default;
                return false;
            }

            destination = transform.MapPoint(region.Size, new Point(x, y));
            return true;
        }

        public void Prepare(AllocationScope scope)
        {
        }

        public void Fill(ImageFrame frame, scoped in PixelLease source, scoped in PixelLease destination, CancellationToken cancellationToken)
            => frame.TransformPixels(source, destination, region, transform, cancellationToken);
    }

    private readonly struct ColorConversionFiller(IccPipeline pipeline, long colors, ColorConversionScratch scratch) : IGeometryFiller
    {
        // Colors change, pixels do not move: a cursor hotspot stays where it is
        public bool TryMapPoint(Point source, out Point destination)
        {
            destination = source;
            return true;
        }

        public void Prepare(AllocationScope scope) => scratch.Uint16Tables = IccUInt16InputTables.Create(pipeline, colors, scope);

        public void Fill(ImageFrame frame, scoped in PixelLease source, scoped in PixelLease destination, CancellationToken cancellationToken)
            => frame.ConvertColorPixels(source, destination, pipeline, scratch.Uint16Tables, cancellationToken);
    }

    /// <summary>The storage rented by <see cref="ColorConversionFiller.Prepare"/>, released with the conversion.</summary>
    private sealed class ColorConversionScratch : IDisposable
    {
        public IccUInt16InputTables? Uint16Tables { get; set; }

        public void Dispose() => Uint16Tables?.Dispose();
    }

    /// <summary>
    /// Writes the rectangle of the canvas at <paramref name="origin"/> to the replacement, filling what lies outside the
    /// canvas with the background of <paramref name="analysis"/> when one is given, or with <paramref name="color"/>.
    /// </summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly struct ExtendFiller(Point origin, Size size, AutoCropAnalysis? analysis, Rgba64 color) : IGeometryFiller
    {
        public bool TryMapPoint(Point source, out Point destination)
        {
            // The new canvas is the rectangle of that size at the origin, which may lie outside the old canvas on any side
            var x = (long)source.X - origin.X;
            var y = (long)source.Y - origin.Y;
            if (x < 0 || y < 0 || x >= size.Width || y >= size.Height)
            {
                destination = default;
                return false;
            }

            destination = new Point((int)x, (int)y);
            return true;
        }

        public void Prepare(AllocationScope scope)
        {
        }

        public void Fill(ImageFrame frame, scoped in PixelLease source, scoped in PixelLease destination, CancellationToken cancellationToken)
        {
            if (analysis is not null)
            {
                frame.ExtendPixels(source, destination, origin, analysis, cancellationToken);
            }
            else
            {
                frame.ExtendPixels(source, destination, origin, color, cancellationToken);
            }
        }
    }

    private readonly struct WarpFiller(WarpPlan plan) : IGeometryFiller
    {
        public bool TryMapPoint(Point source, out Point destination) => plan.Geometry.TryMapPoint(source, out destination);

        public void Prepare(AllocationScope scope) => plan.Prepare(scope);

        public void Fill(ImageFrame frame, scoped in PixelLease source, scoped in PixelLease destination, CancellationToken cancellationToken)
            => frame.WarpPixels(source, destination, plan, cancellationToken);
    }

    private readonly struct ResizeFiller(ResizePlan plan, ResizeGeometry geometry) : IGeometryFiller
    {
        public bool TryMapPoint(Point source, out Point destination) => geometry.TryMapPoint(source, out destination);

        public void Prepare(AllocationScope scope) => plan.Prepare(scope);

        public void Fill(ImageFrame frame, scoped in PixelLease source, scoped in PixelLease destination, CancellationToken cancellationToken)
            => frame.ResizePixels(source, destination, plan, cancellationToken);
    }

    /// <summary>Resizes each frame into the temporary storage of <paramref name="scratch"/>, then places it on the padded canvas.</summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly struct PadResizeFiller(ResizePlan plan, ResizeGeometry geometry, Rgba64 background, PadResizeScratch scratch) : IGeometryFiller
    {
        public bool TryMapPoint(Point source, out Point destination) => geometry.TryMapPoint(source, out destination);

        public void Prepare(AllocationScope scope)
        {
            plan.Prepare(scope);
            scratch.Allocate();
        }

        public void Fill(ImageFrame frame, scoped in PixelLease source, scoped in PixelLease destination, CancellationToken cancellationToken)
        {
            var offset = geometry.Padding!.Value.Offset;
            using var resized = scratch.Storage!.AcquireLease();
            frame.ResizePixels(source, resized, plan, cancellationToken);
            frame.ExtendPixels(resized, destination, new Point(-offset.X, -offset.Y), background, cancellationToken);
        }
    }

    /// <summary>The temporary storage of a padding resize, rented by <see cref="PadResizeFiller.Prepare"/> and released with the resize.</summary>
    private sealed class PadResizeScratch(StorageOwner owner, Size size, int bytesPerPixel, PixelStorageLayoutOptions? layoutOptions) : IDisposable
    {
        public PixelStorage? Storage { get; private set; }

        public void Allocate() => Storage = owner.Allocate(PixelStorageLayout.Create(size.Width, size.Height, bytesPerPixel, layoutOptions), AllocationKind.Temporary, reservation: null);

        public void Dispose() => Storage?.Dispose();
    }
}
