using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;

namespace Meziantou.Framework.WebPlatform.Css.Animations;

/// <summary>
/// The interpolation of font-palette (https://drafts.csswg.org/css-fonts-4/#font-palette-prop, animation type: by computed
/// value): the value between two palettes is the palette-mix() of the palettes in oklab, with the progress as the
/// percentage of the second one. The progress is clamped to [0, 1] (the percentages of palette-mix() are between 0% and
/// 100%), where the value is one of the palettes; the palettes are not additive.
/// </summary>
internal static class CssFontPaletteInterpolation
{
    public static Func<double, string?> Prepare(string from, string to)
    {
        var property = CssPropertyRegistry.Get("font-palette");
        return progress =>
        {
            if (!(progress > 0))
                return from;

            if (progress >= 1)
                return to;

            // Serialized as the computed value is (the default color space is omitted, as the percentages of an even mix)
            var percentage = progress * 100;
            var value = string.Create(CultureInfo.InvariantCulture, $"palette-mix(in oklab, {from} {CssSerializer.SerializeNumber(100 - percentage)}%, {to} {CssSerializer.SerializeNumber(percentage)}%)");
            return CssSerializer.SerializeValue(CssParser.ParseListOfComponentValues(value), property);
        };
    }
}
