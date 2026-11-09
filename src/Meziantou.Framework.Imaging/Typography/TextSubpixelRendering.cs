namespace Meziantou.Framework.Imaging.Typography;

/// <summary>
/// How the glyphs of a text are antialiased when it is filled (<see cref="TextLayoutOptions.SubpixelRendering"/>): with
/// one coverage per pixel, or with one per color of the pixel, for the stripes of red, green and blue of an LCD screen.
/// </summary>
public enum TextSubpixelRendering
{
    /// <summary>One coverage per pixel, the same for its colors: grayscale antialiasing.</summary>
    None = 0,

    /// <summary>One coverage per color, for a screen whose pixels are red, green and blue stripes from left to right.</summary>
    Rgb = 1,

    /// <summary>One coverage per color, for a screen whose pixels are blue, green and red stripes from left to right.</summary>
    Bgr = 2,
}
