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
    /// <para>
    /// The perceptual and saturation tables of a version 4 profile give black the lightness of the perceptual reference
    /// medium (L* = 3.1373), where version 2 tables and matrix-based profiles usually give it 0. The compensation maps
    /// one to the other; without it, nothing does: converting from a version 4 table to a version 2 or matrix-based
    /// profile turns black into a dark gray, and the reverse makes the darkest grays black. Some color management
    /// systems, such as LittleCMS, compensate a version 4 destination even when asked not to.
    /// </para>
    /// </remarks>
    public bool BlackPointCompensation { get; init; } = true;

    /// <summary>
    /// Gets how the colors that the destination cannot reproduce are brought into its gamut. Defaults to
    /// <see cref="Imaging.GamutMapping.Clip"/>, which clips each channel.
    /// </summary>
    /// <remarks>
    /// The mapping happens in the profile connection space, after black point compensation and the rescaling of the
    /// ICC-absolute colorimetric intent, right before the conversion to the device values of each destination profile
    /// (each profile of a sequence that is entered from the connection space). See <see cref="Imaging.GamutMapping"/>
    /// for the destinations it applies to.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="Imaging.GamutMapping"/>.</exception>
    public GamutMapping GamutMapping
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The gamut mapping is not valid.");

            field = value;
        }
    } = GamutMapping.Clip;

    /// <summary>
    /// Gets a value indicating whether <see cref="IccColorTransform.Convert(ReadOnlySpan{float}, Span{float})"/> clips the
    /// samples to [0, 1]. Defaults to <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// With <see langword="false"/>, the floating-point samples keep the values outside [0, 1] that high dynamic range
    /// colors (light above the white) and wide-gamut colors (a color that the destination primaries reach with a negative
    /// component) have, at the source and at the destination. The tone curves are extended: in [0, 1] they are evaluated
    /// as usual, above 1 the function of a parametric curve continues and a sampled curve goes on with the slope of its
    /// last interval, and below 0 the curve is mirrored through its value at 0 (<c>-f(-x)</c> for a curve through 0, the
    /// extended transfer functions of CSS Color 4). The matrices have no range. A value that is not a number is read as 0.
    /// </para>
    /// <para>
    /// Only conversions made of tone curves and matrices can do so (matrix-based profiles, the RGB and grayscale profiles
    /// of displays and color spaces): a conversion through a lookup table (an <c>AToB</c> or <c>BToA</c> table, a profile
    /// connection space in CIELAB) and a gamut mapping other than <see cref="Imaging.GamutMapping.Clip"/> are refused by
    /// <see cref="IccColorTransform.Create(IccProfile, IccProfile, IccColorTransformOptions?)"/>.
    /// The 8-bit and 16-bit conversions always cover the whole range of their type, which the option does not change.
    /// </para>
    /// </remarks>
    public bool ClipFloatingPointSamples { get; init; } = true;

    /// <summary>
    /// Gets how high dynamic range samples are brought to the standard dynamic range of the destination profile, or
    /// <see langword="null"/> (the default) to refuse to convert them.
    /// </summary>
    /// <remarks>
    /// It applies to <see cref="ImageProcessingExtensions.ConvertColorProfile"/> on an image whose samples are PQ or HLG
    /// ones: an image without ICC profile whose CICP color description (<see cref="Metadata.ImageMetadata.Cicp"/>)
    /// declares the transfer characteristics 16 or 18. Such an image is rejected when this is <see langword="null"/>:
    /// compressing the highlights changes the look of the image, which is a choice. It is ignored for every other
    /// source, and by <see cref="IccColorTransform"/>, whose sources are ICC profiles.
    /// </remarks>
    public HdrToneMappingOptions? ToneMapping { get; init; }
}
