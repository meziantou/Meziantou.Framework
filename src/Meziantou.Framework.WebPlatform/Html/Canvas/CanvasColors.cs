using System.Globalization;
using System.Text.RegularExpressions;
using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Rendering;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The colors of the canvas: the parsing of the CSS colors of the styles (https://html.spec.whatwg.org/multipage/canvas.html#fill-and-stroke-styles) and their serialization.</summary>
internal static partial class CanvasColors
{
    /// <summary>A parsed color: its serialization (https://html.spec.whatwg.org/multipage/canvas.html#serialisation-of-a-color) and its value.</summary>
    internal readonly record struct ParsedColor(string Serialization, DisplayColor Color, CssColor CssColor);

    /// <summary>
    /// Parses a CSS color (https://drafts.csswg.org/css-color/#parse-a-css-color-value), or returns null when the string is not a
    /// color. <c>currentcolor</c> is the computed color of the canvas element when it is connected, else opaque black. The
    /// color to paint is CSS gamut mapped to the color space of the canvas (https://drafts.csswg.org/css-color-4/#css-gamut-mapping).
    /// </summary>
    public static ParsedColor? Parse(string value, Element? element, RenderingColorSpace gamut = RenderingColorSpace.Srgb, bool preserveRange = false)
    {
        if (value.Contains("currentcolor", StringComparison.OrdinalIgnoreCase))
        {
            var currentColor = element is { IsConnected: true } ? element.NodeDocument.GetStyleEngine().GetStyle(element).GetComputedValue("color") : "black";
            value = CurrentColorRegex().Replace(value, currentColor);
        }

        if (CssValueComputer.ParseCssColorValue(value) is not { } color || (preserveRange ? DisplayColor.FromCssUnmapped(color) : DisplayColor.FromCss(color, gamut)) is not { } displayColor)
            return null;

        return new ParsedColor(Serialize(color, IsLegacySyntax(value)), displayColor, color);
    }

    /// <summary>Whether a color is written with a legacy color syntax (<c>rgb()</c>, <c>rgba()</c>, <c>hsl()</c>, <c>hsla()</c>, <c>hwb()</c>), whose missing components are zero.</summary>
    private static bool IsLegacySyntax(string value)
        => ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(value)) is [CssFunction function]
            && InfraAscii.ToAsciiLowercase(function.Name) is "rgb" or "rgba" or "hsl" or "hsla" or "hwb"
            && (ValueMatcher.RemoveWhitespace(function.Value) is not [CssPreservedToken { Type: CssTokenType.Ident } from, ..] || !InfraAscii.EqualsIgnoreAsciiCase(from.Token.Value, "from"));

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/canvas.html#serialisation-of-a-color: the sRGB colors are serialized as
    /// <c>#rrggbb</c> when they are opaque, else as <c>rgba(r, g, b, a)</c>; the other colors as their CSS serialization.
    /// </summary>
    public static string Serialize(CssColor color, bool legacySyntax = false)
    {
        double r, g, b;
        if (color.LegacyRgb)
        {
            (r, g, b) = (color.Channels[0] ?? 0, color.Channels[1] ?? 0, color.Channels[2] ?? 0);
        }
        else if (legacySyntax && color.Function == "srgb")
        {
            // The sRGB colors of the legacy syntaxes are serialized as legacy colors (their missing components are zero)
            (r, g, b) = ((color.Channels[0] ?? 0) * 255, (color.Channels[1] ?? 0) * 255, (color.Channels[2] ?? 0) * 255);
        }
        else
        {
            return color.Serialize();
        }

        var alpha = Math.Clamp(color.Alpha ?? 0, 0, 1);
        if (alpha < 1)
            return string.Create(CultureInfo.InvariantCulture, $"rgba({ToByte(r)}, {ToByte(g)}, {ToByte(b)}, {CssColor.FormatNumber(alpha)})");

        return string.Create(CultureInfo.InvariantCulture, $"#{ToByte(r):x2}{ToByte(g):x2}{ToByte(b):x2}");

        static int ToByte(double value) => (int)Math.Clamp(Math.Round(Math.Round(value, 6, MidpointRounding.AwayFromZero), MidpointRounding.AwayFromZero), 0, 255);
    }

    [GeneratedRegex(@"(?<![\w-])currentcolor(?![\w-])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex CurrentColorRegex();
}
