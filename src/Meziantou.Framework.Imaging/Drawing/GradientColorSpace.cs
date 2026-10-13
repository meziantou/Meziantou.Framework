namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>The numeric space in which a gradient mixes the colors of its stops.</summary>
/// <remarks>
/// The space only changes the colors between two stops: at a stop, and beyond the first and the last ones, the color is
/// the color of the stop, exactly, in every space.
/// </remarks>
public enum GradientColorSpace
{
    /// <summary>
    /// The stored (encoded, gamma-compressed) sample values are interpolated directly, in the pixel format of the frame.
    /// This is the default, and what CSS and SVG do by default; the middle of a gradient between two bright colors is
    /// darker than either.
    /// </summary>
    Encoded = 0,

    /// <summary>
    /// The colors of the stops are taken as sRGB, converted to linear light, interpolated, and converted back: the
    /// mixture light makes. The middle of a gradient from black to white is the gray 188, not 128.
    /// </summary>
    LinearSrgb = 1,

    /// <summary>
    /// The colors of the stops are taken as sRGB and interpolated in Oklab, a space in which equal distances are equal
    /// perceived differences: the lightness changes evenly, and the gradient goes through no dull or dark band.
    /// </summary>
    Oklab = 2,
}
