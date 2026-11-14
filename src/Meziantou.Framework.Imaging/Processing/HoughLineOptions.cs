namespace Meziantou.Framework.Imaging;

/// <summary>Immutable options of <see cref="ImageProcessingExtensions.DetectLines(ImageFrame, HoughLineOptions?, CancellationToken)"/>.</summary>
public sealed class HoughLineOptions
{
    /// <summary>Gets the default options.</summary>
    public static HoughLineOptions Default { get; } = new();

    /// <summary>Gets the options of the Canny edge detector that finds the pixels that vote. Defaults to <see cref="CannyEdgeOptions.Default"/>.</summary>
    /// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
    public CannyEdgeOptions EdgeOptions
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = CannyEdgeOptions.Default;

    /// <summary>
    /// Gets the resolution of the angles, in degrees, from 0.05 to 45. Defaults to 1. The angles are the multiples of
    /// <c>180 / round(180 / AngleStep)</c>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not from 0.05 to 45.</exception>
    public double AngleStep
    {
        get;
        init
        {
            if (!(value is >= 0.05 and <= 45))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The angle step must be from 0.05 to 45 degrees.");

            field = value;
        }
    } = 1;

    /// <summary>Gets the resolution of the distances, in pixels, from 0.25 to 100. Defaults to 1.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not from 0.25 to 100.</exception>
    public double DistanceStep
    {
        get;
        init
        {
            if (!(value is >= 0.25 and <= 100))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The distance step must be from 0.25 to 100 pixels.");

            field = value;
        }
    } = 1;

    /// <summary>
    /// Gets the smallest number of edge pixels on a line, or <see langword="null"/> for a quarter of the smaller side of the
    /// frame (at least 2). Defaults to <see langword="null"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public int? MinVotes
    {
        get;
        init
        {
            if (value is not null)
            {
                ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value.Value);
            }

            field = value;
        }
    }

    /// <summary>
    /// Gets the smallest difference of angle, in degrees, between two lines that are not also closer than
    /// <see cref="MinDistanceSeparation"/>, from 0 to 90. Defaults to 5. Of two lines closer than both separations, the
    /// strongest is kept: a straight edge also gives weaker peaks a few degrees away from its angle, which this removes.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not from 0 to 90.</exception>
    public double MinAngleSeparation
    {
        get;
        init
        {
            if (!(value is >= 0 and <= 90))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The angle separation must be from 0 to 90 degrees.");

            field = value;
        }
    } = 5;

    /// <summary>Gets the smallest difference of distance, in pixels, between two lines that are not also closer than <see cref="MinAngleSeparation"/>, from 0 to 10,000. Defaults to 5.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not from 0 to 10,000.</exception>
    public double MinDistanceSeparation
    {
        get;
        init
        {
            if (!(value is >= 0 and <= 10_000))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The distance separation must be from 0 to 10,000 pixels.");

            field = value;
        }
    } = 5;

    /// <summary>Gets the largest number of lines returned, those with the most votes, from 1 to 100,000. Defaults to 32.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not from 1 to 100,000.</exception>
    public int MaxLines
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 100_000);
            field = value;
        }
    } = 32;
}
