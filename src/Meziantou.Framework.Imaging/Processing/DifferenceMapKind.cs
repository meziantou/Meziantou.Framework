namespace Meziantou.Framework.Imaging;

/// <summary>What the image of <see cref="ImageSimilarity.CreateDifferenceMap(ImageFrame, ImageFrame, DifferenceMapOptions?, CancellationToken)"/> shows.</summary>
public enum DifferenceMapKind
{
    /// <summary>
    /// The pixels that differ in <see cref="DifferenceMapOptions.HighlightColor"/>, over the expected frame faded toward
    /// white, as ImageMagick's <c>compare</c> draws them. This is the default.
    /// </summary>
    Highlight = 0,

    /// <summary>
    /// The size of the difference of every pixel as a gray level: the largest absolute difference of its samples (colors
    /// premultiplied by alpha), multiplied by <see cref="DifferenceMapOptions.Gain"/>. Identical pixels are black.
    /// </summary>
    Absolute = 1,

    /// <summary>
    /// The structural dissimilarity around every pixel as a gray level: <c>1 - SSIM</c> of the 7×7 window centered on the
    /// pixel, multiplied by <see cref="DifferenceMapOptions.Gain"/>. Identical areas are black.
    /// </summary>
    Ssim = 2,
}
