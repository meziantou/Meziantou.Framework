using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.Values;

// Fonts (https://drafts.csswg.org/css-fonts-4/)
internal static partial class CssPropertyRules
{
    /// <summary>The percentages of the &lt;font-width-css3&gt; keywords (https://drafts.csswg.org/css-fonts-4/#font-width-prop).</summary>
    private static readonly Dictionary<double, string> FontWidthKeywords = new()
    {
        [50] = "ultra-condensed",
        [62.5] = "extra-condensed",
        [75] = "condensed",
        [87.5] = "semi-condensed",
        [100] = "normal",
        [112.5] = "semi-expanded",
        [125] = "expanded",
        [150] = "extra-expanded",
        [200] = "ultra-expanded",
    };

    /// <summary>
    /// Returns the longhands of the font shorthand with the font width replaced by its &lt;font-width-css3&gt; keyword when it is
    /// the percentage of a keyword (also in a calc(), as in the browsers: WPT css/css-fonts/font-shorthand-serialization-font-stretch.html),
    /// or null: the shorthand can only represent the keywords.
    /// </summary>
    public static Dictionary<string, string>? GetFontWidthKeywordLonghands(CssPropertyDefinition shorthand, Dictionary<string, string> lookup)
    {
        if (shorthand.Name != "font")
            return null;

        foreach (var name in (ReadOnlySpan<string>)["font-width", "font-stretch"])
        {
            if (!lookup.TryGetValue(name, out var value))
                continue;

            var text = value.StartsWith("calc(", StringComparison.Ordinal) && value.EndsWith(')', StringComparison.Ordinal) ? value[5..^1].Trim() : value;
            if (!text.EndsWith('%', StringComparison.Ordinal) || !double.TryParse(text.AsSpan(0, text.Length - 1), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var percentage)
                || !FontWidthKeywords.TryGetValue(percentage, out var keyword))
            {
                return null;
            }

            return new Dictionary<string, string>(lookup, StringComparer.Ordinal) { [name] = keyword };
        }

        return null;
    }

    /// <summary>The keywords of font-synthesis and the longhand they set to auto (oblique-only sets font-synthesis-style to oblique-only).</summary>
    private static readonly (string Keyword, string Longhand)[] FontSynthesisKeywords =
    [
        ("weight", "font-synthesis-weight"),
        ("style", "font-synthesis-style"),
        ("oblique-only", "font-synthesis-style"),
        ("small-caps", "font-synthesis-small-caps"),
        ("position", "font-synthesis-position"),
    ];

    /// <summary>
    /// https://drafts.csswg.org/css-fonts-4/#font-synthesis: the listed kinds of synthesis are auto (oblique-only for
    /// oblique-only), the others are none.
    /// </summary>
    private static Dictionary<string, string>? ExpandFontSynthesis(CssPropertyDefinition shorthand, List<CssComponentValue> significant)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["font-synthesis-weight"] = "none",
            ["font-synthesis-style"] = "none",
            ["font-synthesis-small-caps"] = "none",
            ["font-synthesis-position"] = "none",
        };

        if (significant is [var none] && IsIdent(none, "none"))
            return result;

        foreach (var value in significant)
        {
            if (value is not CssPreservedToken { Type: CssTokenType.Ident } ident)
                return null;

            var keyword = InfraAscii.ToAsciiLowercase(ident.Token.Value);
            var index = Array.FindIndex(FontSynthesisKeywords, entry => entry.Keyword == keyword);
            if (index < 0)
                return null;

            result[FontSynthesisKeywords[index].Longhand] = keyword == "oblique-only" ? "oblique-only" : "auto";
        }

        return result;
    }

    private static string? SerializeFontSynthesis(CssPropertyDefinition shorthand, Dictionary<string, string> lookup)
    {
        var keywords = new List<string>(4);
        foreach (var (keyword, longhand) in FontSynthesisKeywords)
        {
            if (!lookup.TryGetValue(longhand, out var value))
                return null;

            if (value == (keyword == "oblique-only" ? "oblique-only" : "auto"))
            {
                keywords.Add(keyword);
            }
        }

        return keywords.Count == 0 ? "none" : string.Join(' ', keywords);
    }

    /// <summary>
    /// https://drafts.csswg.org/css-fonts-4/#font-language-override-prop: the string is an OpenType language system tag of
    /// 1 to 4 ASCII characters (padded with spaces).
    /// </summary>
    private static bool IsValidFontLanguageOverride(CssPropertyDefinition property, List<CssComponentValue> significant)
        => significant is not [CssPreservedToken { Type: CssTokenType.String } tag] || (tag.Token.Value.Length is >= 1 and <= 4 && tag.Token.Value.All(c => c is >= ' ' and <= '~'));

    /// <summary>
    /// Returns whether a value that matches the grammar of a descriptor satisfies the constraints defined in prose:
    /// https://drafts.csswg.org/css-fonts-4/#override-color: the colors of override-colors are absolute (no currentcolor,
    /// no system colors, no light-dark(), also in the functions that combine colors, e.g. color-mix()).
    /// </summary>
    public static bool IsValidDescriptor(CssDescriptorDefinition descriptor, List<CssComponentValue> significant)
        => descriptor is not { AtRule: "@font-palette-values", Name: "override-colors" } || !ContainsNonAbsoluteColor(significant);

    private static bool ContainsNonAbsoluteColor(List<CssComponentValue> values)
    {
        foreach (var value in values)
        {
            switch (value)
            {
                case CssPreservedToken { Type: CssTokenType.Ident } ident when ident.Token.IsIdent("currentcolor") || CssColor.IsSystemColor(ident.Token.Value):
                    return true;
                case CssFunction function when InfraAscii.EqualsIgnoreAsciiCase(function.Name, "light-dark") || ContainsNonAbsoluteColor(function.Value):
                    return true;
            }
        }

        return false;
    }
}
