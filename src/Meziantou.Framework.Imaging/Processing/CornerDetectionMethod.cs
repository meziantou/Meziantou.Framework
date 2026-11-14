namespace Meziantou.Framework.Imaging;

/// <summary>The corner detectors of <see cref="ImageProcessingExtensions.DetectCorners(ImageFrame, CornerDetectionOptions?, CancellationToken)"/>.</summary>
public enum CornerDetectionMethod
{
    /// <summary>
    /// The Harris and Stephens detector (1988): the structure tensor of the gradients, smoothed by a Gaussian window, gives
    /// <c>det(M) - k trace(M)²</c>, large where the gradients vary in every direction.
    /// </summary>
    Harris,

    /// <summary>The Shi and Tomasi detector (1994), "good features to track": the smaller eigenvalue of the same structure tensor.</summary>
    ShiTomasi,

    /// <summary>
    /// The FAST-9 segment test of Rosten and Drummond (2006): a pixel is a corner when 9 contiguous pixels of the circle of
    /// 16 around it, of radius 3, are all brighter or all darker by more than a threshold. Much faster, less stable under
    /// noise and blur.
    /// </summary>
    Fast,
}
