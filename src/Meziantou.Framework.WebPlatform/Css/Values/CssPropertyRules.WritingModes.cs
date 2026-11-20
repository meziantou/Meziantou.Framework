using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.Values;

// Writing modes (https://drafts.csswg.org/css-writing-modes-4/)
internal static partial class CssPropertyRules
{
    /// <summary>
    /// https://drafts.csswg.org/css-writing-modes-4/#glyph-orientation: <c>auto</c> sets <c>text-orientation: mixed</c>,
    /// <c>0deg</c> and <c>0</c> set <c>upright</c>, <c>90deg</c> and <c>90</c> set <c>sideways</c>.
    /// </summary>
    private static Dictionary<string, string>? ExpandGlyphOrientationVertical(CssPropertyDefinition shorthand, List<CssComponentValue> significant)
        => GetGlyphOrientationVerticalTextOrientation(significant) is { } value ? new Dictionary<string, string>(StringComparer.Ordinal) { [shorthand.Longhands[0]] = value } : null;

    /// <summary>The value of text-orientation set by a value of glyph-orientation-vertical, or null when the value is invalid (another angle or number).</summary>
    private static string? GetGlyphOrientationVerticalTextOrientation(List<CssComponentValue> significant) => significant switch
    {
        [CssPreservedToken { Type: CssTokenType.Ident } ident] when InfraAscii.EqualsIgnoreAsciiCase(ident.Value, "auto") => "mixed",
        [CssPreservedToken { Type: CssTokenType.Number, NumericValue: 0, IsInteger: true }] => "upright",
        [CssPreservedToken { Type: CssTokenType.Number, NumericValue: 90, IsInteger: true }] => "sideways",
        [CssPreservedToken { Type: CssTokenType.Dimension, NumericValue: 0 } zero] when InfraAscii.EqualsIgnoreAsciiCase(zero.Value, "deg") => "upright",
        [CssPreservedToken { Type: CssTokenType.Dimension, NumericValue: 90 } ninety] when InfraAscii.EqualsIgnoreAsciiCase(ninety.Value, "deg") => "sideways",
        _ => null,
    };

    /// <summary>The value of text-orientation as a value of glyph-orientation-vertical (the angles in degrees), or the empty string for the values it cannot represent.</summary>
    private static string? SerializeGlyphOrientationVertical(CssPropertyDefinition shorthand, Dictionary<string, string> lookup)
    {
        if (!lookup.TryGetValue(shorthand.Longhands[0], out var value))
            return null;

        return value switch
        {
            "mixed" => "auto",
            "upright" => "0deg",
            "sideways" => "90deg",
            _ => null,
        };
    }
}
