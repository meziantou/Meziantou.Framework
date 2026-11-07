namespace Meziantou.Framework.Imaging;

/// <summary>Immutable options for <see cref="ImageProcessingExtensions.RadialBlur(Image, RadialBlurOptions, CancellationToken)"/>.</summary>
/// <remarks>
/// <para>
/// Every pixel becomes the mean of the frame sampled along a path centered on the pixel: a segment of the line through
/// <see cref="Center"/> for <see cref="RadialBlurKind.Zoom"/>, an arc of the circle around it for
/// <see cref="RadialBlurKind.Spin"/>. The path grows with the distance to the center, so the center stays sharp. Its
/// samples are evenly spaced, at most one pixel apart up to <see cref="MaxSampleCount"/> samples, and read with bilinear
/// interpolation.
/// </para>
/// <para>
/// The coordinates are those of the stored pixels, where a frame covers <c>[0, width] x [0, height]</c> and the center of
/// pixel <c>(i, j)</c> is <c>(i + 1/2, j + 1/2)</c>: the center of a 100×80 frame is <c>(50, 40)</c>. The center may be
/// outside the frame.
/// </para>
/// </remarks>
public sealed class RadialBlurOptions
{
    /// <summary>The largest value of <see cref="MaxSampleCount"/>.</summary>
    public const int MaxSampleCountLimit = 1024;

    /// <summary>Initializes a new instance of the <see cref="RadialBlurOptions"/> class.</summary>
    /// <param name="kind">The direction of the blur.</param>
    /// <param name="center">The center of the blur, in stored-pixel coordinates (finite, at most 1,000,000 in absolute value).</param>
    /// <param name="amount">
    /// The length of the path (see <see cref="Amount"/>): for <see cref="RadialBlurKind.Zoom"/>, a fraction of the distance
    /// to the center, greater than 0 and at most 2 (0.2 blurs a pixel 500 pixels away from the center over 100 pixels); for
    /// <see cref="RadialBlurKind.Spin"/>, an angle in degrees, greater than 0 and at most 360 (the whole circle).
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> is not a defined value, <paramref name="center"/> is not finite or too far, or <paramref name="amount"/> is out of the range of the kind.</exception>
    public RadialBlurOptions(RadialBlurKind kind, PointD center, double amount)
    {
        const double MaxCoordinate = 1_000_000;
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "The kind of radial blur is not valid.");

        if (!(Math.Abs(center.X) <= MaxCoordinate && Math.Abs(center.Y) <= MaxCoordinate))
            throw new ArgumentOutOfRangeException(nameof(center), center, "The center must have finite coordinates of at most 1,000,000 in absolute value.");

        if (kind == RadialBlurKind.Zoom && !(amount is > 0 and <= 2))
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "The amount of a zoom blur must be greater than 0 and at most 2.");

        if (kind == RadialBlurKind.Spin && !(amount is > 0 and <= 360))
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "The angle of a spin blur must be greater than 0 and at most 360 degrees.");

        Kind = kind;
        Center = center;
        Amount = amount;
    }

    /// <summary>Gets the direction of the blur.</summary>
    public RadialBlurKind Kind { get; }

    /// <summary>Gets the center of the blur, in stored-pixel coordinates.</summary>
    public PointD Center { get; }

    /// <summary>
    /// Gets the length of the path: for <see cref="RadialBlurKind.Zoom"/>, the length of the segment as a fraction of the
    /// distance between the pixel and the center (the pixel is sampled at the scales <c>1 - Amount / 2</c> to
    /// <c>1 + Amount / 2</c> of its offset from the center); for <see cref="RadialBlurKind.Spin"/>, the angle of the arc, in
    /// degrees.
    /// </summary>
    public double Amount { get; }

    /// <summary>
    /// Gets the largest number of samples of a path, from 1 to <see cref="MaxSampleCountLimit"/>. Defaults to 64. A path
    /// longer than <c>MaxSampleCount - 1</c> pixels has samples more than one pixel apart, which shows as copies of the
    /// details instead of a smooth streak; the time per pixel grows with the number of samples.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 1 and <see cref="MaxSampleCountLimit"/>.</exception>
    public int MaxSampleCount
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, MaxSampleCountLimit);
            field = value;
        }
    } = 64;

    /// <summary>Gets the pixels read beyond the edges of the frame. Defaults to <see cref="ConvolutionEdgeMode.Clamp"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="ConvolutionEdgeMode"/>.</exception>
    public ConvolutionEdgeMode EdgeMode
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The edge mode is not valid.");

            field = value;
        }
    }

    /// <summary>
    /// Gets the numeric space in which the samples are averaged. Defaults to <see cref="ConvolutionWorkingSpace.Encoded"/>;
    /// <see cref="ConvolutionWorkingSpace.LinearSrgb"/> averages light, as a camera does, with the rules of the convolution.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="ConvolutionWorkingSpace"/>.</exception>
    public ConvolutionWorkingSpace WorkingSpace
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The working space is not valid.");

            field = value;
        }
    }
}
