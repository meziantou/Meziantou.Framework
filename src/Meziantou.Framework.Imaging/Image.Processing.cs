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
    internal void Resize(ResizeGeometry geometry, ResizeOptions options, CancellationToken cancellationToken)
    {
        const string Operation = "resize the image";
        Owner.EnsureCanModify(Operation);
        var transfer = options.WorkingSpace == ResizeWorkingSpace.LinearSrgb ? GetLinearLightTransfer("resizing", "resize") : (byte)0;
        var workingSpace = transfer is not 0 ? ResizeWorkingSpace.LinearSrgb : ResizeWorkingSpace.Encoded;
        cancellationToken.ThrowIfCancellationRequested();
        if (geometry.IsIdentity)
            return;

        // Large frames are resized by up to MaxDegreeOfParallelism workers (row bands, bit-identical results)
        using var plan = new ResizePlan(geometry, options.Filter, workingSpace, PixelFormat, _configuration.MaxDegreeOfParallelism, transfer);
        ReplaceGeometry(geometry.OutputSize, normalizeOrientation: false, Operation, new ResizeFiller(plan, geometry), cancellationToken);
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
                return CreateAutoCropAnalysis(success: false, canvas, background, weightX: 0, weightY: 0);

            background = analyzer.BackgroundColor;
        }

        analyzer.BeginBoundsPass();
        RunAutoCropPass(frames, analyzer, cancellationToken);
        if (!analyzer.TryGetBounds(out var bounds))
            return CreateAutoCropAnalysis(success: false, canvas, background, weightX: 0, weightY: 0);

        var weights = (X: 0d, Y: 0d);
        if (options.AnalyzeWeights)
        {
            analyzer.BeginWeightsPass();
            RunAutoCropPass(frames, analyzer, cancellationToken);
            weights = analyzer.GetWeights();
        }

        return CreateAutoCropAnalysis(success: true, bounds, background, weights.X, weights.Y);
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

        var fill = analysis.BackgroundColor;
        if (!PixelConverter.IsExactlyRepresentable(fill, PixelFormat))
            throw new UnsupportedImageFeatureException(
                $"Enlarging the canvas fills it with the background color of the analysis ({fill}), which {PixelFormat} pixels cannot represent exactly. Use {nameof(AutoCropPaddingMode)}.{nameof(AutoCropPaddingMode.Contain)}, or analyze an image of the same pixel format.",
                ImageFormat.Unknown,
                "Auto-crop background fill");

        // The limits bound both dimensions to 32 bits, and the origin is at most one padded canvas away
        _configuration.Limits.EnsureCanvasWithinLimits(geometry.Width, geometry.Height);
        var size = new Size((int)geometry.Width, (int)geometry.Height);
        ReplaceGeometry(size, normalizeOrientation: false, Operation, new ExtendFiller(new Point((int)geometry.X, (int)geometry.Y), size, fill), cancellationToken);
        return true;
    }

    /// <summary>Creates the analysis of this image, typed with its pixel type.</summary>
    /// <param name="success">Whether a border and a content box were found.</param>
    /// <param name="bounds">The content box, inside the canvas.</param>
    /// <param name="backgroundColor">The background, exactly representable in the pixel format of the image.</param>
    /// <param name="weightX">The horizontal weight.</param>
    /// <param name="weightY">The vertical weight.</param>
    internal abstract AutoCropAnalysis CreateAutoCropAnalysis(bool success, Rectangle bounds, Rgba64 backgroundColor, double weightX, double weightY);

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

    /// <summary>
    /// Blurs every displayed frame and the poster in place: a convolution by a row of weights, then by the same weights
    /// as a column. Both are prepared before a pixel changes.
    /// </summary>
    internal void BlurAllFrames(double[] weights, ConvolutionEdgeMode edgeMode, ConvolutionWorkingSpace workingSpace, CancellationToken cancellationToken)
    {
        Owner.EnsureCanModify("blur the image");
        using var horizontal = CreateConvolutionPlan(new ConvolutionOptions(new ConvolutionKernel(weights.Length, 1, weights)) { EdgeMode = edgeMode, WorkingSpace = workingSpace });
        using var vertical = CreateConvolutionPlan(new ConvolutionOptions(new ConvolutionKernel(1, weights.Length, weights)) { EdgeMode = edgeMode, WorkingSpace = workingSpace });
        cancellationToken.ThrowIfCancellationRequested();
        horizontal.Prepare(Owner.Scope);
        vertical.Prepare(Owner.Scope);
        _metadata.RemoveStaleThumbnail();
        foreach (var frame in GetAllFrames())
        {
            using var lease = frame.GetStorage().AcquireLease();
            frame.ConvolvePixels(lease, horizontal, cancellationToken);
            frame.ConvolvePixels(lease, vertical, cancellationToken);
        }
    }

    /// <summary>Blurs one attached frame of this image in place, as <see cref="BlurAllFrames"/> does for all of them.</summary>
    internal void BlurFrame(ImageFrame frame, PixelStorage storage, double[] weights, ConvolutionEdgeMode edgeMode, ConvolutionWorkingSpace workingSpace, CancellationToken cancellationToken)
    {
        using var horizontal = CreateConvolutionPlan(new ConvolutionOptions(new ConvolutionKernel(weights.Length, 1, weights)) { EdgeMode = edgeMode, WorkingSpace = workingSpace });
        using var vertical = CreateConvolutionPlan(new ConvolutionOptions(new ConvolutionKernel(1, weights.Length, weights)) { EdgeMode = edgeMode, WorkingSpace = workingSpace });
        cancellationToken.ThrowIfCancellationRequested();
        using var lease = storage.AcquireLease();
        horizontal.Prepare(Owner.Scope);
        vertical.Prepare(Owner.Scope);
        _metadata.RemoveStaleThumbnail();
        frame.ConvolvePixels(lease, horizontal, cancellationToken);
        frame.ConvolvePixels(lease, vertical, cancellationToken);
    }

    /// <summary>Large frames are convolved by up to MaxDegreeOfParallelism workers (row bands, bit-identical results).</summary>
    private ConvolutionPlan CreateConvolutionPlan(ConvolutionOptions options)
    {
        var transfer = options.WorkingSpace == ConvolutionWorkingSpace.LinearSrgb ? GetLinearLightTransfer("convolution", "convolve") : (byte)0;
        return new ConvolutionPlan(options.Kernel, options.EdgeMode, options.PreserveAlpha, transfer is not 0, PixelFormat, _size, _configuration.MaxDegreeOfParallelism, transfer);
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
            ReplaceGeometry(_size, normalizeOrientation: false, Operation, new ColorConversionFiller(pipeline), cancellationToken);
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
    /// their built-in profiles (<see cref="GetSourceColorProfile"/>).
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
        if (_metadata.IccProfile is not null || _metadata.Cicp is not { } cicp)
            return false;

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
    private byte GetLinearLightTransfer(string operation, string verb)
    {
        if (_metadata.TransferFunction == ColorTransferFunction.Linear)
            return 0;

        if (_metadata.IccProfile is { } profile)
        {
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

    private readonly struct ColorConversionFiller(IccPipeline pipeline) : IGeometryFiller
    {
        // Colors change, pixels do not move: a cursor hotspot stays where it is
        public bool TryMapPoint(Point source, out Point destination)
        {
            destination = source;
            return true;
        }

        public void Prepare(AllocationScope scope)
        {
        }

        public void Fill(ImageFrame frame, scoped in PixelLease source, scoped in PixelLease destination, CancellationToken cancellationToken)
            => frame.ConvertColorPixels(source, destination, pipeline, cancellationToken);
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly struct ExtendFiller(Point origin, Size size, Rgba64 fill) : IGeometryFiller
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
            => frame.ExtendPixels(source, destination, origin, fill, cancellationToken);
    }

    private readonly struct ResizeFiller(ResizePlan plan, ResizeGeometry geometry) : IGeometryFiller
    {
        public bool TryMapPoint(Point source, out Point destination) => geometry.TryMapPoint(source, out destination);

        public void Prepare(AllocationScope scope) => plan.Prepare(scope);

        public void Fill(ImageFrame frame, scoped in PixelLease source, scoped in PixelLease destination, CancellationToken cancellationToken)
            => frame.ResizePixels(source, destination, plan, cancellationToken);
    }
}
