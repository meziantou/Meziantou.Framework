namespace Meziantou.Framework.Imaging;

/// <summary>How an ICC color conversion brings the colors that the destination cannot reproduce into its gamut (<see cref="IccColorTransformOptions.GamutMapping"/>).</summary>
public enum GamutMapping
{
    /// <summary>
    /// Each channel of the destination is clipped to its range. In-gamut colors are exact, but an out-of-gamut color can
    /// change hue and lightness, and colors of different saturations can become the same.
    /// </summary>
    Clip = 0,

    /// <summary>
    /// The chroma of an out-of-gamut color is reduced toward the neutral axis, keeping its lightness and hue in Oklab,
    /// until the color is in the gamut of the destination or close enough to its clipped value, following the gamut
    /// mapping algorithm of CSS Color Module Level 4 (section 13.2): a binary search on the chroma that stops when the
    /// clipped color is less than a just noticeable difference (0.02 in Oklab) away. In-gamut colors are unchanged.
    /// </summary>
    /// <remarks>
    /// The mapping replaces the clipping of colorimetric conversions: it applies to a destination based on a matrix and
    /// tone curves whatever the intent (their conversion is colorimetric), and to a destination based on lookup tables
    /// with the relative and absolute colorimetric intents, where a color is in the gamut when its round trip through the
    /// colorimetric tables of the destination changes it by less than the same difference. The perceptual and saturation
    /// tables of a profile map the gamut as their author chose: they are used as they are. A grayscale destination has
    /// no chroma and is not mapped.
    /// </remarks>
    ReduceChroma = 1,
}
