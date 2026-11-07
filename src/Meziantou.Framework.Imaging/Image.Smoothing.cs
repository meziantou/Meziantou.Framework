using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging;

// Internal entry points of the edge-preserving filters (bilateral, guided) and the radial blur of
// ImageProcessingExtensions, called after argument validation
public abstract partial class Image
{
    /// <summary>
    /// Blurs frames of this image along lines or circles around a center, in place (every displayed frame and the poster
    /// when <paramref name="frames"/> is <see langword="null"/>). The working copy is charged to the image and rented before
    /// any pixel changes; afterward, on failure, some frames or rows may already be blurred.
    /// </summary>
    /// <exception cref="UnsupportedImageFeatureException">Linear light is requested for an image with an incompatible ICC profile, or for floating-point samples outside [0, 1] that are not labeled as linear light.</exception>
    internal void RadialBlurFrames(ImageFrame[]? frames, RadialBlurOptions options, CancellationToken cancellationToken)
    {
        Owner.EnsureCanModify("blur the image");
        var transfer = options.WorkingSpace == ConvolutionWorkingSpace.LinearSrgb ? GetLinearLightTransfer("convolution", "convolve") : (byte)0;
        cancellationToken.ThrowIfCancellationRequested();
        frames ??= GetAllFrames();
        EnsureLinearLightDomain(transfer, frames, "convolution", "convolve");
        var format = KernelPixelFormat;
        using var copy = new WorkingFrameCopy(format, _size, options.EdgeMode, RowBands.GetWorkerCount(_size, _configuration.MaxDegreeOfParallelism), transfer, rowDoublesPerWorker: 0);
        copy.Prepare(Owner);
        _metadata.RemoveStaleThumbnail();
        foreach (var frame in frames)
        {
            using var lease = frame.GetStorage().AcquireLease();
            RunWorkingCopyKernel(lease, storage => RadialBlurrer.Apply(storage, copy, options, cancellationToken));
        }
    }

    /// <summary>
    /// Applies a bilateral filter to frames of this image, in place (every displayed frame and the poster when
    /// <paramref name="frames"/> is <see langword="null"/>). The working copy is charged to the image and rented before any
    /// pixel changes; afterward, on failure, some frames or rows may already be filtered.
    /// </summary>
    internal void BilateralFilterFrames(ImageFrame[]? frames, int radius, double spatialSigma, double rangeSigma, ConvolutionEdgeMode edgeMode, CancellationToken cancellationToken)
    {
        Owner.EnsureCanModify("filter the image");
        cancellationToken.ThrowIfCancellationRequested();
        frames ??= GetAllFrames();
        var format = KernelPixelFormat;
        var rowDoubles = BilateralFilterer.GetRowDoubles(_size.Width, PixelFormats.GetComponentCount(format));
        using var copy = new WorkingFrameCopy(format, _size, edgeMode, RowBands.GetWorkerCount(_size, _configuration.MaxDegreeOfParallelism), transfer: 0, rowDoubles);
        copy.Prepare(Owner);
        _metadata.RemoveStaleThumbnail();
        foreach (var frame in frames)
        {
            using var lease = frame.GetStorage().AcquireLease();
            RunWorkingCopyKernel(lease, storage => BilateralFilterer.Apply(storage, copy, radius, spatialSigma, rangeSigma, cancellationToken));
        }
    }

    /// <summary>
    /// Applies a guided filter to frames of this image, with each frame as its own guide, in place (every displayed frame
    /// and the poster when <paramref name="frames"/> is <see langword="null"/>). The working copy, the coefficients and the
    /// scratch are charged to the image and rented before any pixel changes; afterward, on failure, some frames or rows may
    /// already be filtered.
    /// </summary>
    internal void GuidedFilterFrames(ImageFrame[]? frames, int radius, double epsilon, ConvolutionEdgeMode edgeMode, CancellationToken cancellationToken)
    {
        Owner.EnsureCanModify("filter the image");
        cancellationToken.ThrowIfCancellationRequested();
        frames ??= GetAllFrames();
        var format = KernelPixelFormat;
        var channels = PixelFormats.GetComponentCount(format);
        var grayGuide = !PixelFormats.HasColor(PixelFormat) || PixelFormats.IsGrayscale(PixelFormat);
        var coefficientCount = GuidedFilterer.GetCoefficientCount(GuidedFilterer.GetGuideChannels(format), channels);
        using var copy = new WorkingFrameCopy(format, _size, edgeMode, workers: 1, transfer: 0, rowDoublesPerWorker: 0);
        copy.Prepare(Owner);
        var coefficients = Owner.Allocate(_size.Width, _size.Height, coefficientCount * sizeof(float), options: null, AllocationKind.Temporary);
        try
        {
            using var scratch = ResampleWeights.Rent<double>(Owner.Scope, GuidedFilterer.GetScratchDoubles(format, _size, radius));
            _metadata.RemoveStaleThumbnail();
            foreach (var frame in frames)
            {
                using var lease = frame.GetStorage().AcquireLease();
                RunWorkingCopyKernel(lease, storage => GuidedFilterer.Apply(storage, copy, grayGuide, coefficients, unsafe(System.Runtime.InteropServices.MemoryMarshal.Cast<byte, double>(scratch.Span)), radius, epsilon, cancellationToken));
            }
        }
        finally
        {
            coefficients.Dispose();
        }
    }

    /// <summary>Runs a kernel of the working copies on a leased frame, through a copy in the kernel format when the format of the image needs one (<see cref="ProcessingBridge"/>).</summary>
    private void RunWorkingCopyKernel(scoped in PixelLease lease, Action<PixelStorage> kernel)
    {
        if (KernelPixelFormat == PixelFormat)
        {
            kernel(lease.Storage);
        }
        else
        {
            ProcessingBridge.RunInPlace(Owner, PixelFormat, lease, (scoped in PixelLease working) => kernel(working.Storage));
        }
    }
}
