using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.Values;

// Fragmentation (https://drafts.csswg.org/css-break-4/)
internal static partial class CssPropertyRules
{
    /// <summary>
    /// The legacy shorthands page-break-before, page-break-after and page-break-inside
    /// (https://drafts.csswg.org/css-break-4/#page-break-properties): they set break-before, break-after and break-inside, are
    /// never used to serialize a declaration block (https://drafts.csswg.org/css-cascade-5/#legacy-shorthand), and only
    /// represent the values of their grammar.
    /// </summary>
    public static bool IsLegacyShorthand(string name) => name is "page-break-before" or "page-break-after" or "page-break-inside";

    /// <summary>The value always of page-break-before and page-break-after is page, the other values are the same.</summary>
    private static Dictionary<string, string>? ExpandLegacyPageBreak(CssPropertyDefinition shorthand, List<CssComponentValue> significant)
    {
        if (significant is not [CssPreservedToken { Type: CssTokenType.Ident } ident])
            return null;

        var value = InfraAscii.ToAsciiLowercase(ident.Token.Value);
        return new Dictionary<string, string>(StringComparer.Ordinal) { [shorthand.Longhands[0]] = value == "always" ? "page" : value };
    }

    /// <summary>The values of break-before, break-after and break-inside that the legacy shorthand cannot represent (e.g. column) serialize as the empty string.</summary>
    private static string? SerializeLegacyPageBreak(CssPropertyDefinition shorthand, Dictionary<string, string> lookup)
    {
        if (!lookup.TryGetValue(shorthand.Longhands[0], out var value))
            return null;

        if (value == "page" && shorthand.Name != "page-break-inside")
            return "always";

        return CssPropertyRegistry.GetGrammar(shorthand) is { } grammar && ValueMatcher.Matches(grammar, CssParser.ParseListOfComponentValues(value)) ? value : null;
    }
}
