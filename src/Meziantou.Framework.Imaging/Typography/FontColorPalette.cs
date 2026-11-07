namespace Meziantou.Framework.Imaging.Typography;

/// <summary>A palette of a color font: the colors its color glyphs are painted with.</summary>
/// <remarks>
/// Every palette of a font has the same number of colors: a glyph names its colors by their position, and another
/// palette gives it other colors (<see cref="TextStyle.ColorPaletteIndex"/>). The colors are sRGB values with an
/// opacity; like the colors of a brush, they are drawn as they are, without color management.
/// </remarks>
public sealed class FontColorPalette
{
    internal FontColorPalette(Rgba32[] colors, bool isForLightBackground, bool isForDarkBackground)
    {
        Colors = colors;
        IsForLightBackground = isForLightBackground;
        IsForDarkBackground = isForDarkBackground;
    }

    /// <summary>Gets the colors of the palette.</summary>
    public IReadOnlyList<Rgba32> Colors { get; }

    /// <summary>Gets a value indicating whether the font says the palette is made for a light background.</summary>
    public bool IsForLightBackground { get; }

    /// <summary>Gets a value indicating whether the font says the palette is made for a dark background.</summary>
    public bool IsForDarkBackground { get; }
}
