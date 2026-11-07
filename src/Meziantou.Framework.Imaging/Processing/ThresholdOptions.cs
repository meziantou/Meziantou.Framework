namespace Meziantou.Framework.Imaging;

/// <summary>
/// Immutable options of a binarization (<see cref="ImageProcessingExtensions.Threshold(Image, ThresholdOptions?, CancellationToken)"/>):
/// how the threshold of each pixel is chosen, and the two colors of the result.
/// </summary>
/// <remarks>
/// <para>
/// Every pixel is compared by its luminance: the Rec. 709 luma of its stored (encoded) samples at the storage precision,
/// the value <see cref="ImageProcessingExtensions.Grayscale(Image, CancellationToken)"/> stores (a gray pixel is its own
/// luminance). A pixel whose luminance is greater than its threshold gets <see cref="UpperColor"/>, any other pixel gets
/// <see cref="LowerColor"/>. The thresholds are on the scale of the samples, from 0 to <c>max</c>, the largest sample value
/// (255 for 8-bit pixel formats, 65,535 for 16-bit ones); the levels of these options are fractions of it.
/// </para>
/// <para>
/// The windows of the adaptive methods (<see cref="ThresholdMethod.Mean"/> and <see cref="ThresholdMethod.Sauvola"/>) are
/// squares of <see cref="WindowSize"/> pixels centered on each pixel, which read the pixels outside the frame as
/// <see cref="EdgeMode"/> says. Their sums are exact integers; the means, the deviations and the thresholds are IEEE
/// doubles, so the result is the same on every platform.
/// </para>
/// </remarks>
public sealed class ThresholdOptions
{
    private const int MaxWindowSize = 20001;

    /// <summary>
    /// Gets the default options: Otsu's method on each frame, which becomes black (below the threshold) and white (above
    /// it).
    /// </summary>
    public static ThresholdOptions Default { get; } = new();

    /// <summary>Gets how the thresholds are chosen. Defaults to <see cref="ThresholdMethod.Otsu"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="ThresholdMethod"/>.</exception>
    public ThresholdMethod Method
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The threshold method is not valid.");

            field = value;
        }
    }

    /// <summary>
    /// Gets the threshold of <see cref="ThresholdMethod.Fixed"/>, a fraction of the sample range: a pixel whose luminance is
    /// greater than <c>Level * max</c> gets <see cref="UpperColor"/>. Defaults to 0.5, which puts the 8-bit levels from
    /// 128 up in the upper class. Ignored by the other methods.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 0 and 1.</exception>
    public double Level
    {
        get;
        init
        {
            if (!(value is >= 0 and <= 1))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The level must be between 0 and 1.");

            field = value;
        }
    } = 0.5;

    /// <summary>
    /// Gets the width and the height, in pixels, of the window of the adaptive methods: an odd number from 3 to 20,001.
    /// Defaults to 15. Choose a window larger than the strokes to keep (about twice the height of a line of text, for a
    /// document). Ignored by <see cref="ThresholdMethod.Otsu"/> and <see cref="ThresholdMethod.Fixed"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is even, less than 3 or greater than 20,001.</exception>
    public int WindowSize
    {
        get;
        init
        {
            if (value is < 3 or > MaxWindowSize || value % 2 == 0)
                throw new ArgumentOutOfRangeException(nameof(value), value, "The window size must be an odd number from 3 to 20,001.");

            field = value;
        }
    } = 15;

    /// <summary>
    /// Gets what <see cref="ThresholdMethod.Mean"/> subtracts from the mean of the window, a fraction of the sample range:
    /// the threshold is <c>mean - Offset * max</c>. Defaults to 0. A positive offset keeps the pixels slightly darker than
    /// their surroundings, such as the noise of a blank background, in the upper class. Ignored by the other methods.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between -1 and 1.</exception>
    public double Offset
    {
        get;
        init
        {
            if (!(value is >= -1 and <= 1))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The offset must be between -1 and 1.");

            field = value;
        }
    }

    /// <summary>
    /// Gets the sensitivity <c>k</c> of <see cref="ThresholdMethod.Sauvola"/>, from 0 to 1: the larger it is, the further
    /// below the mean the threshold of a window with little contrast falls. Defaults to 0.2. Ignored by the other methods.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 0 and 1.</exception>
    public double SauvolaK
    {
        get;
        init
        {
            if (!(value is >= 0 and <= 1))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The Sauvola sensitivity must be between 0 and 1.");

            field = value;
        }
    } = 0.2;

    /// <summary>
    /// Gets the dynamic range <c>R</c> of the standard deviation of <see cref="ThresholdMethod.Sauvola"/>, a fraction of the
    /// sample range: <c>R = SauvolaR * max</c>. Defaults to 0.5 (127.5 for 8-bit samples, the value of the original paper).
    /// Ignored by the other methods.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not greater than 0 and at most 1.</exception>
    public double SauvolaR
    {
        get;
        init
        {
            if (!(value is > 0 and <= 1))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The Sauvola dynamic range must be greater than 0 and at most 1.");

            field = value;
        }
    } = 0.5;

    /// <summary>
    /// Gets the pixels the windows of the adaptive methods read outside the frame. Defaults to
    /// <see cref="ConvolutionEdgeMode.Mirror"/>. With <see cref="ConvolutionEdgeMode.Zero"/>, the outside pixels count as
    /// black pixels of the window. Ignored by <see cref="ThresholdMethod.Otsu"/> and <see cref="ThresholdMethod.Fixed"/>.
    /// </summary>
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
    } = ConvolutionEdgeMode.Mirror;

    /// <summary>
    /// Gets the color of the pixels whose luminance is greater than their threshold, alpha included. Defaults to opaque
    /// white. The pixel format must store it exactly (see <see cref="ImageProcessingExtensions.Threshold(Image, ThresholdOptions?, CancellationToken)"/>).
    /// </summary>
    public Rgba64 UpperColor { get; init; } = new(ushort.MaxValue, ushort.MaxValue, ushort.MaxValue);

    /// <summary>
    /// Gets the color of the pixels whose luminance is not greater than their threshold, alpha included. Defaults to opaque
    /// black. The pixel format must store it exactly (see <see cref="ImageProcessingExtensions.Threshold(Image, ThresholdOptions?, CancellationToken)"/>).
    /// </summary>
    public Rgba64 LowerColor { get; init; } = new(0, 0, 0);

    /// <summary>
    /// Gets a value indicating whether <see cref="ThresholdMethod.Otsu"/> chooses one threshold from the histogram of every
    /// frame of an image and its poster frame together, so that the frames of an animation get the same threshold and do
    /// not flicker. Defaults to <see langword="false"/>: each frame gets the threshold of its own histogram. Ignored when one
    /// frame is binarized, and by the other methods.
    /// </summary>
    public bool ShareHistogramAcrossFrames { get; init; }
}
