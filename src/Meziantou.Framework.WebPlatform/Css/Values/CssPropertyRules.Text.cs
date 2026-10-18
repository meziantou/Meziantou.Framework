using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.Values;

// Text, inline layout and text decoration (https://drafts.csswg.org/css-text-4/, https://drafts.csswg.org/css-inline-3/, https://drafts.csswg.org/css-text-decor-4/)
internal static partial class CssPropertyRules
{
    /// <summary>The legacy keywords of white-space and the values of white-space-collapse and text-wrap-mode they set (https://drafts.csswg.org/css-text-4/#white-space-property).</summary>
    private static readonly (string Keyword, string Collapse, string Mode)[] WhiteSpaceKeywords =
    [
        ("normal", "collapse", "wrap"),
        ("pre", "preserve", "nowrap"),
        ("nowrap", "collapse", "nowrap"),
        ("pre-wrap", "preserve", "wrap"),
        ("break-spaces", "break-spaces", "wrap"),
        ("pre-line", "preserve-breaks", "wrap"),
    ];

    private static readonly HashSet<string> AlignmentBaselineKeywords = new(StringComparer.Ordinal) { "baseline", "text-bottom", "alphabetic", "ideographic", "middle", "central", "mathematical", "hanging", "text-top" };

    /// <summary>
    /// text-justify: distribute is a parse-time alias of inter-character (https://drafts.csswg.org/css-text-3/#valdef-text-justify-distribute).
    /// Returns null when the value does not contain it.
    /// </summary>
    private static string? CanonicalizeTextJustify(List<CssComponentValue> significant)
    {
        var keywords = InfraAscii.ToAsciiLowercase(Serialize(significant, separator: " ")).Split(' ');
        if (!keywords.Contains("distribute", StringComparer.Ordinal))
            return null;

        return keywords.Contains("no-compress", StringComparer.Ordinal) ? "inter-character no-compress" : "inter-character";
    }

    /// <summary>
    /// overflow-x, overflow-y (https://drafts.csswg.org/css-overflow-3/#valdef-overflow-overlay): <c>overlay</c> is a legacy value
    /// alias of <c>auto</c> (https://drafts.csswg.org/css-cascade-5/#legacy-value-alias: it is converted at parse time).
    /// </summary>
    private static string? CanonicalizeOverflow(List<CssComponentValue> significant)
        => significant is [CssPreservedToken { Type: CssTokenType.Ident } keyword] && keyword.Token.IsIdent("overlay") ? "auto" : null;

    /// <summary>
    /// ruby-overhang (https://drafts.csswg.org/css-ruby-1/#ruby-overhang): <c>none</c> is a legacy value alias of <c>spaces</c>
    /// (converted at parse time).
    /// </summary>
    private static string? CanonicalizeRubyOverhang(List<CssComponentValue> significant)
        => significant is [CssPreservedToken { Type: CssTokenType.Ident } keyword] && keyword.Token.IsIdent("none") ? "spaces" : null;

    /// <summary>
    /// hyphenate-limit-chars (https://drafts.csswg.org/css-text-4/#hyphenate-limit-chars-property): "If the third value is
    /// missing, it is the same as the second. If the second value is missing, then it is auto": the shortest serialization omits
    /// the third value when it equals the second, then the second when it is auto (<c>5 2 2</c> is <c>5 2</c>, <c>auto auto</c> is <c>auto</c>).
    /// </summary>
    private static string? CanonicalizeHyphenateLimitChars(List<CssComponentValue> significant)
    {
        var values = new List<string>(significant.Count);
        foreach (var value in significant)
        {
            values.Add(value is CssPreservedToken { Type: CssTokenType.Ident } ident ? InfraAscii.ToAsciiLowercase(ident.Token.Value) : Serialize([value], separator: " "));
        }

        return CanonicalizeHyphenateLimitChars(values);
    }

    /// <inheritdoc cref="CanonicalizeHyphenateLimitChars(List{CssComponentValue})"/>
    internal static string CanonicalizeHyphenateLimitChars(List<string> values)
    {
        if (values.Count == 3 && values[2] == values[1])
        {
            values.RemoveAt(2);
        }

        if (values.Count == 2 && values[1] == "auto")
        {
            values.RemoveAt(1);
        }

        return string.Join(' ', values);
    }

    /// <summary>
    /// Returns the canonical serialization of a &lt;text-edge&gt; (https://drafts.csswg.org/css-inline-3/#typedef-text-edge): the
    /// second keyword is omitted when it is the one implied by the first keyword alone (the same keyword, or text after cap and
    /// ex), and text alphabetic is alphabetic. Returns null when the value is not a &lt;text-edge&gt; of two keywords.
    /// </summary>
    internal static string? CanonicalizeTextEdge(List<CssComponentValue> significant)
    {
        if (significant is not [CssPreservedToken { Type: CssTokenType.Ident } first, CssPreservedToken { Type: CssTokenType.Ident } second])
            return null;

        var over = InfraAscii.ToAsciiLowercase(first.Token.Value);
        var under = InfraAscii.ToAsciiLowercase(second.Token.Value);
        if (over == under || (over is "cap" or "ex" && under == "text"))
            return over;

        return over == "text" && under == "alphabetic" ? under : over + " " + under;
    }

    /// <summary>
    /// text-box (https://drafts.csswg.org/css-inline-3/#text-box-shorthand): normal sets none and auto; "omitting the text-box-trim
    /// value sets it to trim-both (not the initial value), while omitting the text-box-edge value sets it to auto".
    /// </summary>
    private static Dictionary<string, string>? ExpandTextBox(CssPropertyDefinition shorthand, List<CssComponentValue> significant)
    {
        if (significant is [CssPreservedToken { Type: CssTokenType.Ident } single] && InfraAscii.ToAsciiLowercase(single.Token.Value) == "normal")
            return new(StringComparer.Ordinal) { ["text-box-trim"] = "none", ["text-box-edge"] = "auto" };

        var trim = "trim-both";
        var edge = new List<CssComponentValue>();
        foreach (var value in significant)
        {
            var keyword = value is CssPreservedToken { Type: CssTokenType.Ident } ident ? InfraAscii.ToAsciiLowercase(ident.Token.Value) : null;
            if (keyword is "none" or "trim-start" or "trim-end" or "trim-both")
            {
                trim = keyword;
            }
            else
            {
                edge.Add(value);
            }
        }

        var edgeValue = edge.Count == 0 ? "auto" : CanonicalizeTextEdge(edge) ?? InfraAscii.ToAsciiLowercase(Serialize(edge, separator: " "));
        return new(StringComparer.Ordinal) { ["text-box-trim"] = trim, ["text-box-edge"] = edgeValue };
    }

    private static string? SerializeTextBox(CssPropertyDefinition shorthand, Dictionary<string, string> lookup)
    {
        if (!lookup.TryGetValue("text-box-trim", out var trim) || !lookup.TryGetValue("text-box-edge", out var edge))
            return null;

        if (trim == "none" && edge == "auto")
            return "normal";

        if (edge == "auto")
            return trim;

        return trim == "trim-both" ? edge : trim + " " + edge;
    }

    /// <summary>white-space: a legacy keyword, or &lt;'white-space-collapse'&gt; || &lt;'text-wrap-mode'&gt; (the omitted values are collapse and wrap).</summary>
    private static Dictionary<string, string>? ExpandWhiteSpace(CssPropertyDefinition shorthand, List<CssComponentValue> significant)
    {
        var collapse = "collapse";
        var mode = "wrap";
        foreach (var value in significant)
        {
            var keyword = value is CssPreservedToken { Type: CssTokenType.Ident } ident ? InfraAscii.ToAsciiLowercase(ident.Token.Value) : null;
            if (significant.Count == 1 && Array.Find(WhiteSpaceKeywords, k => k.Keyword == keyword) is { Keyword: not null } legacy)
            {
                (collapse, mode) = (legacy.Collapse, legacy.Mode);
            }
            else if (keyword is "wrap" or "nowrap")
            {
                mode = keyword;
            }
            else if (keyword is not null)
            {
                collapse = keyword;
            }
        }

        return new(StringComparer.Ordinal) { ["white-space-collapse"] = collapse, ["text-wrap-mode"] = mode };
    }

    private static string? SerializeWhiteSpace(CssPropertyDefinition shorthand, Dictionary<string, string> lookup)
    {
        if (!lookup.TryGetValue("white-space-collapse", out var collapse) || !lookup.TryGetValue("text-wrap-mode", out var mode))
            return null;

        if (Array.Find(WhiteSpaceKeywords, k => k.Collapse == collapse && k.Mode == mode) is { Keyword: { } keyword })
            return keyword;

        return mode == "wrap" ? collapse : collapse + " " + mode;
    }

    /// <summary>
    /// vertical-align (https://drafts.csswg.org/css-inline-3/#transverse-alignment): first / last set baseline-source, the
    /// baseline keywords set alignment-baseline, and the other values set baseline-shift; the omitted values are auto,
    /// baseline and 0.
    /// </summary>
    private static Dictionary<string, string>? ExpandVerticalAlign(CssPropertyDefinition shorthand, List<CssComponentValue> significant)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal) { ["baseline-source"] = "auto", ["alignment-baseline"] = "baseline", ["baseline-shift"] = "0px" };
        foreach (var value in significant)
        {
            var keyword = value is CssPreservedToken { Type: CssTokenType.Ident } ident ? InfraAscii.ToAsciiLowercase(ident.Token.Value) : null;
            var name = keyword switch
            {
                "first" or "last" => "baseline-source",
                not null when AlignmentBaselineKeywords.Contains(keyword) => "alignment-baseline",
                _ => "baseline-shift",
            };

            result[name] = keyword ?? (CssShorthands.ParseValue(name, [value], originalText: null) is [{ ShorthandName: null } longhand] ? longhand.Value : CssSerializer.SerializeValue([value], property: null));
        }

        return result;
    }

    private static string? SerializeVerticalAlign(CssPropertyDefinition shorthand, Dictionary<string, string> lookup)
    {
        if (!lookup.TryGetValue("baseline-source", out var source) || !lookup.TryGetValue("alignment-baseline", out var alignment) || !lookup.TryGetValue("baseline-shift", out var shift))
            return null;

        var parts = new List<string>(3);
        if (source != "auto")
        {
            parts.Add(source);
        }

        if (alignment != "baseline")
        {
            parts.Add(alignment);
        }

        if (shift is not ("0" or "0px"))
        {
            parts.Add(shift);
        }

        return parts.Count == 0 ? "baseline" : string.Join(' ', parts);
    }

    /// <summary>text-emphasis-position: over or under first, and the default right side is omitted (https://drafts.csswg.org/css-text-decor-4/#text-emphasis-position-property).</summary>
    public static string CanonicalizeTextEmphasisPosition(string value)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
            return value;

        var (vertical, horizontal) = parts[0] is "over" or "under" ? (parts[0], parts[1]) : (parts[1], parts[0]);
        return horizontal == "right" ? vertical : vertical + " " + horizontal;
    }

    /// <summary>
    /// The computed value of text-emphasis-style (https://drafts.csswg.org/css-text-decor-4/#text-emphasis-style-property):
    /// the omitted shape is circle in horizontal writing modes and sesame in vertical ones, and filled is omitted.
    /// </summary>
    public static string ComputeTextEmphasisStyle(string value, bool horizontal)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (parts is not ([_] or [_, _]) || parts.Exists(part => part.StartsWith('"', StringComparison.Ordinal)) || parts is ["none"])
            return value;

        if (!parts.Exists(part => part is not ("filled" or "open")))
        {
            parts.Add(horizontal ? "circle" : "sesame");
        }

        parts.Remove("filled");
        return string.Join(' ', parts);
    }

    /// <summary>text-decoration-inset: the second inset is omitted when it is equal to the first one.</summary>
    private static string? CanonicalizeTextDecorationInset(List<CssComponentValue> significant)
    {
        var parts = significant.ConvertAll(value => value is CssPreservedToken { Type: CssTokenType.Number, Token.NumericValue: 0 } ? "0px" : CssSerializer.SerializeValue([value], property: null));
        return parts is [var start, var end] && start == end ? start : string.Join(' ', parts);
    }
}
