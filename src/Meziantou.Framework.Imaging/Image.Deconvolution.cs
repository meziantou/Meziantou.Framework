using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging;

// Internal entry point of the Wiener deconvolution of ImageProcessingExtensions, called after argument validation
public abstract partial class Image
{
    /// <summary>
    /// Deconvolves frames of this image, in place (every displayed frame and the poster when <paramref name="frames"/> is
    /// <see langword="null"/>). The working copy, the plane, the filter and the scratch are charged to the image and rented
    /// before any pixel changes; afterward, on failure, some frames may already be deconvolved.
    /// </summary>
    /// <exception cref="UnsupportedImageFeatureException">Linear light is requested for an image with an incompatible ICC profile, or for floating-point samples outside [0, 1] that are not labeled as linear light.</exception>
    internal void WienerDeconvolveFrames(ImageFrame[]? frames, WienerDeconvolutionOptions options, CancellationToken cancellationToken)
    {
        Owner.EnsureCanModify("deconvolve the image");
        var transfer = options.WorkingSpace == ConvolutionWorkingSpace.LinearSrgb ? GetLinearLightTransfer("deconvolution", "deconvolve") : (byte)0;
        cancellationToken.ThrowIfCancellationRequested();
        frames ??= GetAllFrames();
        EnsureLinearLightDomain(transfer, frames, "deconvolution", "deconvolve");
        var format = KernelPixelFormat;
        using var copy = new WorkingFrameCopy(format, _size, options.EdgeMode, workers: 1, transfer, rowDoublesPerWorker: 0);
        copy.Prepare(Owner);
        using var deconvolver = WienerDeconvolver.Create(Owner, _size, options.Kernel, options.NoiseToSignalRatio, options.EdgeMode, cancellationToken);
        _metadata.RemoveStaleThumbnail();
        foreach (var frame in frames)
        {
            using var lease = frame.GetStorage().AcquireLease();
            RunWorkingCopyKernel(lease, storage => deconvolver.Apply(storage, copy, cancellationToken));
        }
    }
}
