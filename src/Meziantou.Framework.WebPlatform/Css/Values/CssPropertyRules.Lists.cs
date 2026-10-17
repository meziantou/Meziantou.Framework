using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.Values;

// Lists and counters (https://drafts.csswg.org/css-lists-3/, https://drafts.csswg.org/css-counter-styles-3/)
internal static partial class CssPropertyRules
{
    /// <summary>
    /// https://drafts.csswg.org/css-lists-3/#propdef-counter-reset: the omitted integer of a counter is serialized (1 for
    /// counter-increment, 0 for counter-reset and counter-set), except for a reversed counter, whose initial value depends on
    /// the number of list items.
    /// </summary>
    private static string? CanonicalizeCounters(List<CssComponentValue> significant, string defaultValue)
    {
        if (significant is [var none] && IsIdent(none, "none"))
            return null;

        var parts = new List<string>(significant.Count * 2);
        for (var i = 0; i < significant.Count; i++)
        {
            var value = significant[i];
            parts.Add(CssSerializer.SerializeValue([value], property: null));
            if (IsCounterValue(value))
                continue;

            var reversed = value is CssFunction function && InfraAscii.EqualsIgnoreAsciiCase(function.Name, "reversed");
            if (!reversed && (i + 1 == significant.Count || !IsCounterValue(significant[i + 1])))
            {
                parts.Add(defaultValue);
            }
        }

        return string.Join(' ', parts);

        // An <integer>: a number or a math function (the names are identifiers or reversed())
        static bool IsCounterValue(CssComponentValue value) => value is CssPreservedToken { Type: CssTokenType.Number } || (value is CssFunction function && !InfraAscii.EqualsIgnoreAsciiCase(function.Name, "reversed"));
    }

    /// <summary>
    /// https://drafts.csswg.org/css-counter-styles-3/#symbols-function: the alphabetic and numeric systems of symbols() need
    /// at least two symbols (list-style-type, list-style, and the counter() and counters() of content). The symbols are
    /// strings: like browsers, the &lt;image&gt; symbols of the grammar are not supported.
    /// </summary>
    private static bool IsValidSymbols(CssPropertyDefinition property, List<CssComponentValue> significant)
    {
        foreach (var value in significant)
        {
            if (value is not CssFunction function)
                continue;

            var arguments = ValueMatcher.RemoveWhitespace(function.Value);
            if (!InfraAscii.EqualsIgnoreAsciiCase(function.Name, "symbols"))
            {
                if (!IsValidSymbols(property, arguments))
                    return false;

                continue;
            }

            var symbols = arguments is [CssPreservedToken { Type: CssTokenType.Ident }, .. var rest] ? rest : arguments;
            if (symbols.Exists(symbol => symbol is not CssPreservedToken { Type: CssTokenType.String }))
                return false;

            if (arguments is [var type, ..] && (IsIdent(type, "alphabetic") || IsIdent(type, "numeric")) && symbols.Count < 2)
                return false;
        }

        return true;
    }

    /// <summary>
    /// https://drafts.csswg.org/css-lists-3/#list-style-property: a none keyword sets list-style-image and list-style-type when
    /// they are not otherwise set by the shorthand (none is none none).
    /// </summary>
    private static Dictionary<string, string>? ExpandListStyle(CssPropertyDefinition shorthand, List<CssComponentValue> significant)
    {
        string? position = null;
        string? image = null;
        string? type = null;
        var nones = 0;
        foreach (var value in significant)
        {
            if (IsIdent(value, "none"))
            {
                nones++;
            }
            else if (position is null && (IsIdent(value, "inside") || IsIdent(value, "outside")))
            {
                position = InfraAscii.ToAsciiLowercase(((CssPreservedToken)value).Token.Value);
            }
            else if (image is null && ParseLonghand("list-style-image", value) is { } imageValue)
            {
                image = imageValue;
            }
            else if (type is null && ParseLonghand("list-style-type", value) is { } typeValue)
            {
                type = typeValue;
            }
            else
            {
                return null;
            }
        }

        if (nones > 0)
        {
            if (image is null && type is null)
            {
                image = type = "none";
            }
            else if (nones == 1 && image is null)
            {
                image = "none";
            }
            else if (nones == 1 && type is null)
            {
                type = "none";
            }
            else
            {
                return null;
            }
        }

        return new(StringComparer.Ordinal)
        {
            ["list-style-position"] = position ?? "outside",
            ["list-style-image"] = image ?? "none",
            ["list-style-type"] = type ?? "disc",
        };

        static string? ParseLonghand(string name, CssComponentValue value)
            => CssShorthands.ParseValue(name, [value], originalText: null) is [{ ShorthandName: null } longhand] ? longhand.Value : null;
    }

    /// <summary>
    /// The initial values are omitted (outside when all the values are initial), and a none keyword stands for both
    /// list-style-image and list-style-type. The position is kept when the type is a counter style named like a position (outside outside).
    /// </summary>
    private static string? SerializeListStyle(CssPropertyDefinition shorthand, Dictionary<string, string> lookup)
    {
        if (!lookup.TryGetValue("list-style-position", out var position) || !lookup.TryGetValue("list-style-image", out var image) || !lookup.TryGetValue("list-style-type", out var type))
            return null;

        var parts = new List<string>(3);
        if (position != "outside" || type is "inside" or "outside")
        {
            parts.Add(position);
        }

        if (image != "none")
        {
            parts.Add(image);
        }

        if (type != "disc")
        {
            parts.Add(type);
        }

        return parts.Count == 0 ? "outside" : string.Join(' ', parts);
    }
}
