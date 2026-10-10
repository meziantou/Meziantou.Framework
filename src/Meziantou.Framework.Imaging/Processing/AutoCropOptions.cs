namespace Meziantou.Framework.Imaging;

/// <summary>
/// Immutable options for <see cref="ImageProcessingExtensions.AutoCrop(Image, AutoCropOptions?, CancellationToken)"/> and
/// <see cref="ImageProcessingExtensions.AnalyzeAutoCrop(Image, AutoCropOptions?, CancellationToken)"/>.
/// </summary>
/// <remarks>
/// <para>
/// The background is the most frequent color of the one-pixel outer border of the image. A border exists when the border
/// has fewer than <see cref="ColorThreshold"/> distinct colors, or when <see cref="BucketThreshold"/> is set and at least
/// that share of the border pixels is as bright as the background. The content is then the bounding box of the pixels
/// that differ from the background by more than <see cref="ColorThreshold"/>.
/// </para>
/// <para>
/// Thresholds are expressed on the 8-bit scale whatever the pixel format; comparisons are made at the storage precision
/// (a threshold of 35 is 35 * 257 for 16-bit samples).
/// </para>
/// </remarks>
public sealed class AutoCropOptions
{
    /// <summary>Gets the default options: no padding, a color threshold of 35, no bucket threshold, <see cref="AutoCropPaddingMode.Expand"/>, no weight analysis.</summary>
    public static AutoCropOptions Default { get; } = new();

    /// <summary>Gets the padding kept on the left and on the right of the content, in pixels. Defaults to 0.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public int PaddingX
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            field = value;
        }
    }

    /// <summary>Gets the padding kept above and below the content, in pixels. Defaults to 0.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public int PaddingY
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            field = value;
        }
    }

    /// <summary>
    /// Gets the color threshold, from 1 to 255 on the 8-bit scale. Defaults to 35. It is both the tolerance of the
    /// background test (a pixel is background when its luma-weighted difference from the background color,
    /// <c>0.2126 |dR| + 0.7152 |dG| + 0.0722 |dB|</c>, is at most the threshold and its alpha difference is below the
    /// threshold) and the number of distinct border colors (after reduction to 8 bits) from which the border is no
    /// longer considered uniform. When no border is found, the detection is retried once with half the threshold
    /// (rounded up) on the image without its outer 5% on each side.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 1 and 255.</exception>
    public int ColorThreshold
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, byte.MaxValue);
            field = value;
        }
    } = 35;

    /// <summary>
    /// Gets the minimum share of the border pixels, from 0 to 1, whose luma falls in the same of 11 equal luma buckets as
    /// the background color, for the border to be accepted even when it has <see cref="ColorThreshold"/> distinct colors
    /// or more (noisy or compressed backgrounds). Defaults to <see langword="null"/>: only the number of distinct colors
    /// decides. It is not used by the retry.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 0 and 1.</exception>
    public double? BucketThreshold
    {
        get;
        init
        {
            if (value is { } threshold && !(threshold >= 0 && threshold <= 1))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The bucket threshold must be between 0 and 1.");

            field = value;
        }
    }

    /// <summary>Gets what happens when the padding reaches outside the canvas. Defaults to <see cref="AutoCropPaddingMode.Expand"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="AutoCropPaddingMode"/>.</exception>
    public AutoCropPaddingMode PaddingMode
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The padding mode is not valid.");

            field = value;
        }
    }

    /// <summary>
    /// Gets a value indicating whether the analysis also computes <see cref="AutoCropAnalysis.WeightX"/> and
    /// <see cref="AutoCropAnalysis.WeightY"/>, which shift the padded rectangle toward the visually heavier side of the
    /// image by <c>padding * weight</c> pixels (truncated). Defaults to <see langword="false"/>. It needs one more pass
    /// over the pixels.
    /// </summary>
    public bool AnalyzeWeights { get; init; }
}
