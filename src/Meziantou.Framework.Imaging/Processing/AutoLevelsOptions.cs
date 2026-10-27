namespace Meziantou.Framework.Imaging;

/// <summary>
/// Immutable options of a contrast stretch (<see cref="ImageProcessingExtensions.AutoLevels(Image, AutoLevelsOptions?, CancellationToken)"/>).
/// </summary>
/// <remarks>
/// <para>
/// The stretch finds the low level <c>lo</c>, the smallest level <c>v</c> such that more than
/// <see cref="LowPercentile"/> percent of the counted samples are at most <c>v</c>, and the high level <c>hi</c>, the
/// largest level <c>v</c> such that more than <c>100 - </c><see cref="HighPercentile"/> percent of them are at least
/// <c>v</c>. A level <c>v</c> then becomes <c>round((v - lo) * max / (hi - lo))</c>, clamped to the sample range, where
/// <c>max</c> is the largest sample value (255 or 65,535). With the default percentiles, <c>lo</c> and <c>hi</c> are the
/// darkest and the brightest levels present. When <c>hi</c> is not above <c>lo</c>, nothing changes.
/// </para>
/// <para>
/// The histograms have one bin per sample level: 256 for 8-bit pixel formats, 65,536 for 16-bit ones. Fully transparent
/// pixels are not counted, but they are remapped like the others.
/// </para>
/// </remarks>
public sealed class AutoLevelsOptions
{
    /// <summary>Gets the default options: the luminance of each frame is stretched from its darkest to its brightest level.</summary>
    public static AutoLevelsOptions Default { get; } = new();

    /// <summary>
    /// Gets the share of the samples, in percent, that may be darker than the low level and become 0. Defaults to 0. It must
    /// be less than <see cref="HighPercentile"/>, which the operation checks.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not at least 0 and less than 100.</exception>
    public double LowPercentile
    {
        get;
        init
        {
            if (!(value is >= 0 and < 100))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The low percentile must be at least 0 and less than 100.");

            field = value;
        }
    }

    /// <summary>
    /// Gets the percentile of the high level: <c>100 - HighPercentile</c> percent of the samples may be brighter than it and
    /// become the largest sample value. Defaults to 100.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not greater than 0 and at most 100.</exception>
    public double HighPercentile
    {
        get;
        init
        {
            if (!(value is > 0 and <= 100))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The high percentile must be greater than 0 and at most 100.");

            field = value;
        }
    } = 100;

    /// <summary>
    /// Gets the samples that are stretched. Defaults to <see cref="HistogramColorMode.Luminance"/>, which keeps the hues;
    /// <see cref="HistogramColorMode.PerChannel"/> stretches each channel from its own levels, which also removes a color cast.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="HistogramColorMode"/>.</exception>
    public HistogramColorMode ColorMode
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The color mode is not valid.");

            field = value;
        }
    }

    /// <summary>
    /// Gets a value indicating whether every frame of an image, and its poster frame, is stretched with the levels of all of
    /// them together, so that the frames of an animation get the same mapping and do not flicker. Defaults to
    /// <see langword="false"/>: each frame is stretched alone. Ignored when one frame is stretched.
    /// </summary>
    public bool ShareHistogramAcrossFrames { get; init; }
}
