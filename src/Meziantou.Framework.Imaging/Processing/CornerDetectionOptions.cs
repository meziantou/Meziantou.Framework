namespace Meziantou.Framework.Imaging;

/// <summary>Immutable options of <see cref="ImageProcessingExtensions.DetectCorners(ImageFrame, CornerDetectionOptions?, CancellationToken)"/>.</summary>
public sealed class CornerDetectionOptions
{
    /// <summary>Gets the default options: the Harris detector, at most 500 corners at least 3 pixels apart.</summary>
    public static CornerDetectionOptions Default { get; } = new();

    /// <summary>Gets the detector. Defaults to <see cref="CornerDetectionMethod.Harris"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="CornerDetectionMethod"/>.</exception>
    public CornerDetectionMethod Method
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The corner detection method is not valid.");

            field = value;
        }
    }

    /// <summary>
    /// Gets the standard deviation, in pixels, of the Gaussian window of the structure tensor (Harris and Shi-Tomasi),
    /// from 0.5 to 20. Defaults to 1.5. A larger window finds fewer, larger corners.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not from 0.5 to 20.</exception>
    public double Sigma
    {
        get;
        init
        {
            if (!(value is >= 0.5 and <= 20))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The standard deviation must be from 0.5 to 20.");

            field = value;
        }
    } = 1.5;

    /// <summary>Gets the constant <c>k</c> of the Harris measure, greater than 0 and less than 0.25. Defaults to 0.04.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not greater than 0 and less than 0.25.</exception>
    public double HarrisK
    {
        get;
        init
        {
            if (!(value is > 0 and < 0.25))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The Harris constant must be greater than 0 and less than 0.25.");

            field = value;
        }
    } = 0.04;

    /// <summary>
    /// Gets the smallest response of a corner of the Harris and Shi-Tomasi detectors, as a fraction of the largest response
    /// of the frame, from 0 (excluded) to 1. Defaults to 0.01.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not greater than 0 and at most 1.</exception>
    public double ResponseThreshold
    {
        get;
        init
        {
            if (!(value is > 0 and <= 1))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The response threshold must be greater than 0 and at most 1.");

            field = value;
        }
    } = 0.01;

    /// <summary>
    /// Gets the difference of brightness, as a fraction of the largest sample, by which the pixels of the circle of the
    /// FAST detector must be brighter or darker than the center, from 0 (excluded) to 1. Defaults to 0.08 (about 20 levels of
    /// 255).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not greater than 0 and at most 1.</exception>
    public double FastThreshold
    {
        get;
        init
        {
            if (!(value is > 0 and <= 1))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The FAST threshold must be greater than 0 and at most 1.");

            field = value;
        }
    } = 0.08;

    /// <summary>
    /// Gets the smallest distance between two corners, in pixels, from 1 to 1,000. Defaults to 3. Among corners closer than
    /// that, the strongest is kept.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not from 1 to 1,000.</exception>
    public int MinDistance
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 1000);
            field = value;
        }
    } = 3;

    /// <summary>Gets the largest number of corners returned, the strongest ones, from 1 to 1,000,000. Defaults to 500.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not from 1 to 1,000,000.</exception>
    public int MaxCorners
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 1_000_000);
            field = value;
        }
    } = 500;
}
