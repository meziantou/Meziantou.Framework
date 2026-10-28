namespace Meziantou.Framework.Imaging;

/// <summary>
/// The arc of the hue circle along which two hues are interpolated, as the <c>hue-interpolation-method</c> of CSS Color 4
/// (section 12.4) selects it.
/// </summary>
public enum HueInterpolation
{
    /// <summary>The shorter arc, at most 180 degrees. This is the default, as in CSS.</summary>
    Shorter = 0,

    /// <summary>The longer arc, at least 180 degrees.</summary>
    Longer = 1,

    /// <summary>The arc along which the hue increases from the first color to the second.</summary>
    Increasing = 2,

    /// <summary>The arc along which the hue decreases from the first color to the second.</summary>
    Decreasing = 3,
}
