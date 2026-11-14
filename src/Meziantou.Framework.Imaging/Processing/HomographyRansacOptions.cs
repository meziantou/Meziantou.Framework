namespace Meziantou.Framework.Imaging;

/// <summary>Immutable options of <see cref="ProjectiveTransform.EstimateHomographyRobust(ReadOnlySpan{PointD}, ReadOnlySpan{PointD}, HomographyRansacOptions?)"/>.</summary>
public sealed class HomographyRansacOptions
{
    /// <summary>Gets the default options: a threshold of 3 pixels, 1,000 samples and the seed 0.</summary>
    public static HomographyRansacOptions Default { get; } = new();

    /// <summary>
    /// Gets the largest distance, in the units of the destination points (pixels), between a destination point and the
    /// image of its source point for the correspondence to be an inlier, greater than 0. Defaults to 3.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a finite number greater than 0.</exception>
    public double Threshold
    {
        get;
        init
        {
            if (!(value > 0) || !double.IsFinite(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The threshold must be a finite number greater than 0.");

            field = value;
        }
    } = 3;

    /// <summary>
    /// Gets the number of random samples of four correspondences, from 1 to 1,000,000. Defaults to 1,000, enough to find a
    /// sample without outliers with a probability of 99.9% when up to about 60% of the correspondences are outliers.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not from 1 to 1,000,000.</exception>
    public int Iterations
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 1_000_000);
            field = value;
        }
    } = 1000;

    /// <summary>Gets the seed of the random samples: the same seed gives the same result on every platform. Defaults to 0.</summary>
    public int Seed { get; init; }
}
