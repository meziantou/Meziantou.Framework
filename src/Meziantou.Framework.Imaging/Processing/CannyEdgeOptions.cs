using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging;

/// <summary>
/// Immutable options of the Canny edge detector
/// (<see cref="ImageProcessingExtensions.DetectCannyEdges(Image, CannyEdgeOptions?, CancellationToken)"/>): the blur that
/// removes the noise, and the two thresholds of the hysteresis.
/// </summary>
/// <remarks>
/// The thresholds are fractions of the sample range, compared with the magnitude of the gradient of the Sobel operator
/// (<see cref="ConvolutionKernel.SobelX"/>, <see cref="ConvolutionKernel.SobelY"/>), which is not normalized: a step from
/// black to white across a straight vertical edge has a magnitude of 4. OpenCV's <c>cv2.Canny</c> takes the same
/// thresholds multiplied by 255 for 8-bit images, and scikit-image's <c>canny</c> the same values for floating-point images.
/// </remarks>
public sealed class CannyEdgeOptions
{
    /// <summary>Gets the default options: a blur of standard deviation 1, and the thresholds 0.1 and 0.2 (the defaults of scikit-image).</summary>
    public static CannyEdgeOptions Default { get; } = new();

    /// <summary>
    /// Gets the standard deviation of the Gaussian blur applied to the luminance before its gradient is computed, in
    /// pixels: the weights of <see cref="ImageProcessingExtensions.GaussianBlur(Image, double, ConvolutionEdgeMode, ConvolutionWorkingSpace, CancellationToken)"/>,
    /// with the pixels outside the image read as the nearest edge pixel. Defaults to 1. A larger value ignores more noise and
    /// finer details; 0 does not blur, as OpenCV's <c>cv2.Canny</c>.
    /// </summary>
    /// <exception cref="ArgumentException">The value is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or exceeds 10000.</exception>
    public double Sigma
    {
        get;
        init
        {
            GaussianKernel.Validate(value, nameof(value));
            field = value;
        }
    } = 1;

    /// <summary>
    /// Gets the lower threshold, a fraction of the sample range: a pixel whose gradient is a local maximum and greater than
    /// it is an edge when it is connected to a pixel greater than <see cref="HighThreshold"/> by such pixels. Defaults to 0.1.
    /// </summary>
    /// <exception cref="ArgumentException">The value is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public double LowThreshold
    {
        get;
        init
        {
            ValidateThreshold(value);
            field = value;
        }
    } = 0.1;

    /// <summary>
    /// Gets the upper threshold, a fraction of the sample range: a pixel whose gradient is a local maximum and greater than
    /// it is an edge. Defaults to 0.2. It must not be lower than <see cref="LowThreshold"/>.
    /// </summary>
    /// <exception cref="ArgumentException">The value is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public double HighThreshold
    {
        get;
        init
        {
            ValidateThreshold(value);
            field = value;
        }
    } = 0.2;

    private static void ValidateThreshold(double value)
    {
        if (!double.IsFinite(value))
            throw new ArgumentException("The threshold must be a finite number.", nameof(value));

        if (value < 0)
            throw new ArgumentOutOfRangeException(nameof(value), value, "The threshold must not be negative.");
    }
}
