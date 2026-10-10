using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging;

/// <summary>Immutable options of an ICC color conversion (<see cref="IccColorTransform"/>).</summary>
public sealed class IccColorTransformOptions
{
    /// <summary>Gets the default options: media-relative colorimetric intent with black point compensation.</summary>
    public static IccColorTransformOptions Default { get; } = new();

    /// <summary>
    /// Gets the rendering intent. Defaults to <see cref="IccRenderingIntent.RelativeColorimetric"/>.
    /// </summary>
    /// <remarks>
    /// The intent selects the conversion tables of table-based profiles; a profile that has no table for the intent uses
    /// its perceptual table, then its matrix and tone curves. Matrix-based profiles (most RGB and grayscale profiles) give
    /// the same result for every intent except <see cref="IccRenderingIntent.AbsoluteColorimetric"/>, which uses the
    /// colorimetric conversion and rescales the colors by the media white points of the two profiles (the illuminant of
    /// the profile connection space for a display profile and for a profile without media white point).
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="IccRenderingIntent"/>.</exception>
    public IccRenderingIntent Intent
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The rendering intent is not valid.");

            field = value;
        }
    } = IccRenderingIntent.RelativeColorimetric;

    /// <summary>
    /// Gets a value indicating whether the black point of the source is mapped to the black point of the destination, so
    /// that shadow detail is kept when the destination cannot reproduce the black of the source. Defaults to
    /// <see langword="true"/>. It has no effect with <see cref="IccRenderingIntent.AbsoluteColorimetric"/>.
    /// </summary>
    /// <remarks>
    /// The compensation scales CIEXYZ toward the white point, following the algorithm published by Adobe and
    /// standardized as ISO 18619. The black point of a profile is estimated from its conversion tables or tone curves;
    /// when both are black, as between two display profiles, nothing changes. A table-based destination that cannot be
    /// converted back to the profile connection space has no measurable black point and is not compensated.
    /// </remarks>
    public bool BlackPointCompensation { get; init; } = true;
}
