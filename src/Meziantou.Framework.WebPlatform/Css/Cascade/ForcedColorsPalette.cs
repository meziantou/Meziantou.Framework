using System.Collections.Frozen;
using System.Drawing;
using System.Globalization;
using System.Runtime.CompilerServices;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Css.Cascade;

/// <summary>
/// The color palette of the forced colors mode of an environment (https://drafts.csswg.org/css-color-adjust-1/#forced): the
/// computed values of the system colors of a <see cref="ForcedColorsTheme"/>, and the user preferences that the palette implies.
/// </summary>
internal sealed class ForcedColorsPalette
{
    private static readonly ConditionalWeakTable<ForcedColorsTheme, ForcedColorsPalette> Palettes = [];

    private ForcedColorsPalette(ForcedColorsTheme theme)
    {
        Colors = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["accentcolor"] = Serialize(theme.AccentColor),
            ["accentcolortext"] = Serialize(theme.AccentColorText),
            ["activetext"] = Serialize(theme.ActiveText),
            ["buttonborder"] = Serialize(theme.ButtonBorder),
            ["buttonface"] = Serialize(theme.ButtonFace),
            ["buttontext"] = Serialize(theme.ButtonText),
            ["canvas"] = Serialize(theme.Canvas),
            ["canvastext"] = Serialize(theme.CanvasText),
            ["field"] = Serialize(theme.Field),
            ["fieldtext"] = Serialize(theme.FieldText),
            ["graytext"] = Serialize(theme.GrayText),
            ["highlight"] = Serialize(theme.Highlight),
            ["highlighttext"] = Serialize(theme.HighlightText),
            ["linktext"] = Serialize(theme.LinkText),
            ["mark"] = Serialize(theme.Mark),
            ["marktext"] = Serialize(theme.MarkText),
            ["selecteditem"] = Serialize(theme.SelectedItem),
            ["selecteditemtext"] = Serialize(theme.SelectedItemText),
            ["visitedtext"] = Serialize(theme.VisitedText),
        }.ToFrozenDictionary(StringComparer.Ordinal);

        // "if the UA determines, based on Lab lightness, that the Canvas color is clearly either dark (L < 33%) or light
        // (L > 67%), then it must match the appropriate value of the prefers-color-scheme media query"; the preference of the
        // user is kept between the thresholds
        var canvas = GetRelativeLuminance(theme.Canvas);
        var lightness = canvas > 216.0 / 24389 ? (116 * Math.Cbrt(canvas)) - 16 : 24389.0 / 27 * canvas;
        PrefersColorScheme = lightness < 33 ? "dark" : lightness > 67 ? "light" : null;

        // https://drafts.csswg.org/mediaqueries-5/#forced-colors: "the user agent must also match one of prefers-contrast: more
        // or prefers-contrast: less if it can determine that the forced color palette chosen by the user has a particularly
        // high or low contrast, and must make prefers-contrast: custom match otherwise". As in Chromium, the contrast is the
        // WCAG contrast ratio of the text on the canvas: high from 7 (the enhanced contrast of WCAG), low up to 2.5
        var text = GetRelativeLuminance(theme.CanvasText);
        var ratio = (Math.Max(canvas, text) + 0.05) / (Math.Min(canvas, text) + 0.05);
        PrefersContrast = ratio >= 7 ? "more" : ratio <= 2.5 ? "less" : "custom";
    }

    /// <summary>The computed values of the system colors, by lowercase keyword (the deprecated system colors are mapped by the caller).</summary>
    public FrozenDictionary<string, string> Colors { get; }

    /// <summary>The preferred color scheme that the canvas color of the palette implies (<c>light</c> or <c>dark</c>), or null when it is neither clearly light nor dark.</summary>
    public string? PrefersColorScheme { get; }

    /// <summary>The value of the <c>prefers-contrast</c> media feature: <c>more</c>, <c>less</c> or <c>custom</c>.</summary>
    public string PrefersContrast { get; }

    /// <summary>Returns the palette of the forced colors mode of a viewport, or null when the mode is not active.</summary>
    public static ForcedColorsPalette? Get(ViewportOptions viewport)
    {
        if (!viewport.ForcedColors)
            return null;

        var theme = viewport.ForcedColorsTheme ?? (viewport.PreferredColorScheme == PreferredColorScheme.Dark ? ForcedColorsTheme.Dark : ForcedColorsTheme.Light);
        return Palettes.GetValue(theme, static theme => new ForcedColorsPalette(theme));
    }

    private static string Serialize(Color color) => string.Create(CultureInfo.InvariantCulture, $"rgb({color.R}, {color.G}, {color.B})");

    /// <summary>Returns the relative luminance of an sRGB color (https://www.w3.org/TR/WCAG21/#dfn-relative-luminance).</summary>
    private static double GetRelativeLuminance(Color color)
    {
        return (0.2126 * Linear(color.R)) + (0.7152 * Linear(color.G)) + (0.0722 * Linear(color.B));

        static double Linear(byte value)
        {
            var channel = value / 255.0;
            return channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
        }
    }
}
