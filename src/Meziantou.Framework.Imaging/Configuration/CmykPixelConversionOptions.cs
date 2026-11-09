using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging;

/// <summary>
/// Immutable options of the color conversion between CMYK pixels (<see cref="Cmyk32"/>, <see cref="Cmyk64"/>) and the
/// other pixel formats: see <see cref="PixelConversionOptions.CmykConversion"/>.
/// </summary>
/// <remarks>
/// <para>
/// From CMYK, the colors are converted with an <see cref="IccColorTransform"/> from the CMYK profile of the image, or from
/// <see cref="CmykProfile"/> when the image has none, to <see cref="RgbProfile"/>, which labels the converted pixels. When
/// neither profile exists, the device CMYK formulas of <see cref="Cmyk"/> are used (CSS Color 5 <c>device-cmyk()</c>), and
/// the converted pixels have no profile.
/// </para>
/// <para>
/// To CMYK, the colors are converted from the RGB profile of the image, or from <see cref="RgbProfile"/> when the image has
/// none, to <see cref="CmykProfile"/>, which labels the converted pixels. Without <see cref="CmykProfile"/>, the device CMYK
/// formulas of <see cref="Cmyk"/> are used (the black is what the three inks share), and the converted pixels have no
/// profile. CMYK has no alpha: non-opaque pixels need <see cref="PixelConversionOptions.BackgroundColor"/>.
/// </para>
/// <para>
/// The device formulas do not describe what a press prints: they are a convenience for previews and for files whose
/// printing condition is unknown. Use a CMYK profile to prepare colors for print.
/// </para>
/// </remarks>
public sealed class CmykPixelConversionOptions
{
    /// <summary>
    /// Gets the default options: the ICC profile of the image when it has one, otherwise the device CMYK formulas; sRGB on
    /// the RGB side; media-relative colorimetric intent with black point compensation.
    /// </summary>
    public static CmykPixelConversionOptions Default { get; } = new();

    /// <summary>
    /// Gets the CMYK profile of the CMYK side when the image has none: the source of a conversion from CMYK pixels without
    /// profile, and the destination of a conversion to CMYK pixels. <see langword="null"/> (the default) uses the device
    /// CMYK formulas instead.
    /// </summary>
    /// <exception cref="ArgumentException">The profile does not declare the CMYK color space.</exception>
    public IccProfile? CmykProfile
    {
        get;
        init
        {
            if (value is not null && value.ColorSpace != IccProfileColorSpace.Cmyk)
                throw new ArgumentException($"The CMYK profile must declare the CMYK color space, but it declares {value.ColorSpace}.", nameof(value));

            field = value;
        }
    }

    /// <summary>
    /// Gets the RGB profile of the RGB side when the image has none: the destination of a color-managed conversion from
    /// CMYK pixels, and the source of a color-managed conversion of RGB or gray pixels without profile to CMYK. Defaults to
    /// <see cref="IccProfile.Srgb"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The profile does not declare the RGB color space.</exception>
    public IccProfile RgbProfile
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            if (value.ColorSpace != IccProfileColorSpace.Rgb)
                throw new ArgumentException($"The RGB profile must declare the RGB color space, but it declares {value.ColorSpace}.", nameof(value));

            field = value;
        }
    } = IccProfile.Srgb;

    /// <summary>Gets the rendering intent and the black point compensation of the color-managed conversions. Defaults to <see cref="IccColorTransformOptions.Default"/>.</summary>
    /// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
    public IccColorTransformOptions Transform
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = IccColorTransformOptions.Default;
}
