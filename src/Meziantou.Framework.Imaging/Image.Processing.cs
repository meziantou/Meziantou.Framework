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
        ReplaceGeometry(geometry.OutputSize, normalizeOrientation: false, Operation, new ResizeFiller(plan, geometry), cancellationToken);
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

    private readonly struct ResizeFiller(ResizePlan plan, ResizeGeometry geometry) : IGeometryFiller
    {
        public bool TryMapPoint(Point source, out Point destination) => geometry.TryMapPoint(source, out destination);

        public void Prepare(AllocationScope scope) => plan.Prepare(scope);

        public void Fill(ImageFrame frame, scoped in PixelLease source, scoped in PixelLease destination, CancellationToken cancellationToken)
            => frame.ResizePixels(source, destination, plan, cancellationToken);
    }
}
