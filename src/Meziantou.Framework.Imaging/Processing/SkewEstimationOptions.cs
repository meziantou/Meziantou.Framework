namespace Meziantou.Framework.Imaging;

/// <summary>
/// Immutable options of <see cref="ImageProcessingExtensions.EstimateSkew(ImageFrame, SkewEstimationOptions?, CancellationToken)"/>
/// and <see cref="ImageProcessingExtensions.Deskew(Image, SkewEstimationOptions?, TransformOptions?, CancellationToken)"/>.
/// </summary>
public sealed class SkewEstimationOptions
{
    /// <summary>Gets the default options: angles up to 15 degrees, to 0.05 degree.</summary>
    public static SkewEstimationOptions Default { get; } = new();

    /// <summary>Gets the largest angle searched, in degrees, greater than 0 and at most 45. Defaults to 15.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not greater than 0 and at most 45.</exception>
    public double MaxAngle
    {
        get;
        init
        {
            if (!(value is > 0 and <= 45))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The largest angle must be greater than 0 and at most 45 degrees.");

            field = value;
        }
    } = 15;

    /// <summary>Gets the precision of the angle, in degrees, from 0.005 to 0.5. Defaults to 0.05. The time grows with its inverse.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not from 0.005 to 0.5.</exception>
    public double Precision
    {
        get;
        init
        {
            if (!(value is >= 0.005 and <= 0.5))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The precision must be from 0.005 to 0.5 degree.");

            field = value;
        }
    } = 0.05;
}
