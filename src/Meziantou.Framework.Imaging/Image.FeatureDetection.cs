using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging;

// Internal entry points of the line and corner detection of ImageProcessingExtensions, called after argument validation
public abstract partial class Image
{
    /// <summary>Finds the straight lines of a frame of this image (<see cref="HoughLineDetector"/>), without changing it.</summary>
    internal IReadOnlyList<HoughLine> DetectLinesCore(ImageFrame frame, HoughLineOptions options, CancellationToken cancellationToken)
    {
        Owner.ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        using var lease = frame.GetStorage().AcquireLease();
        return HoughLineDetector.Detect(Owner.Scope, PixelFormat, lease, options, cancellationToken);
    }

    /// <summary>Finds the corners of a frame of this image (<see cref="CornerDetector"/>), without changing it.</summary>
    internal IReadOnlyList<ImageCorner> DetectCornersCore(ImageFrame frame, CornerDetectionOptions options, CancellationToken cancellationToken)
    {
        Owner.ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        using var lease = frame.GetStorage().AcquireLease();
        return CornerDetector.Detect(Owner.Scope, PixelFormat, lease, options, cancellationToken);
    }
}
