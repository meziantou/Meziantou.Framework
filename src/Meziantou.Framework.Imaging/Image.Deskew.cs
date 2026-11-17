using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging;

// Internal entry point of the skew estimation of ImageProcessingExtensions, called after argument validation
public abstract partial class Image
{
    /// <summary>Estimates the skew of a frame of this image (<see cref="SkewEstimator"/>), without changing it.</summary>
    internal double EstimateSkewCore(ImageFrame frame, SkewEstimationOptions options, CancellationToken cancellationToken)
    {
        Owner.ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        using var lease = frame.GetStorage().AcquireLease();
        return SkewEstimator.Estimate(Owner.Scope, PixelFormat, lease, options, cancellationToken);
    }
}
