using Meziantou.Framework.WebPlatform.Css.Syntax;

namespace Meziantou.Framework.WebPlatform.Css.Values;

// Anchor positioning (https://drafts.csswg.org/css-anchor-position-1/)
internal static partial class CssPropertyRules
{
    /// <summary>
    /// https://drafts.csswg.org/css-anchor-position-1/#position-area: a &lt;position-area&gt; of two identical keywords is
    /// equivalent to the single keyword, which is its canonical form (e.g. start start is start). The value is a list of
    /// comma-separated &lt;position-area&gt; (position-area or position-try-fallbacks).
    /// </summary>
    private static string? CanonicalizePositionAreas(CssPropertyDefinition property, List<CssComponentValue> significant)
    {
        var result = new List<CssComponentValue>(significant.Count);
        var changed = false;
        var start = 0;
        for (var i = 0; i <= significant.Count; i++)
        {
            if (i < significant.Count && significant[i] is not CssPreservedToken { Type: CssTokenType.Comma })
                continue;

            if (significant[start..i] is [CssPreservedToken { Type: CssTokenType.Ident } first, CssPreservedToken { Type: CssTokenType.Ident } second] && string.Equals(first.Token.Value, second.Token.Value, StringComparison.OrdinalIgnoreCase))
            {
                result.Add(first);
                changed = true;
            }
            else if (significant[start..i] is [CssPreservedToken { Type: CssTokenType.Ident } keyword1, CssPreservedToken { Type: CssTokenType.Ident } keyword2]
                && (keyword1.Token.IsIdent("span-all") ? keyword2 : keyword2.Token.IsIdent("span-all") ? keyword1 : null) is { } unambiguous && !IsAmbiguousPositionAreaKeyword(unambiguous.Token.Value))
            {
                // "If only a single keyword is given, it behaves as if the second keyword is span-all if the given keyword is
                // unambiguous about its axis": the single keyword is the canonical form
                result.Add(unambiguous);
                changed = true;
            }
            else
            {
                result.AddRange(significant[start..i]);
            }

            if (i < significant.Count)
            {
                result.Add(significant[i]);
            }

            start = i + 1;
        }

        if (!changed)
            return null;

        var ordered = CssPropertyRegistry.GetGrammar(property) is { } grammar ? CssCanonicalOrder.Reorder(property, grammar, result) ?? result : result;
        return CssSerializer.SerializeValue(CssShorthands.InsertSpaces(ordered), property);
    }

    /// <summary>
    /// Returns whether a &lt;position-area&gt; keyword does not specify its axis (https://drafts.csswg.org/css-anchor-position-1/#position-area-syntax):
    /// center, span-all, and the start/end keywords that do not name the block or inline axis.
    /// </summary>
    internal static bool IsAmbiguousPositionAreaKeyword(string keyword) => Infra.InfraAscii.ToAsciiLowercase(keyword) is "center" or "span-all" or "start" or "end"
        or "self-start" or "self-end" or "span-start" or "span-end" or "span-self-start" or "span-self-end";

    /// <summary>
    /// https://drafts.csswg.org/css-inline-3/#valdef-alignment-baseline-text-before-edge: the SVG 1.1 values text-before-edge and
    /// text-after-edge are legacy value aliases of text-top and text-bottom (converted at parse time).
    /// </summary>
    private static string? CanonicalizeLegacyBaseline(List<CssComponentValue> significant)
    {
        if (significant is not [CssPreservedToken { Type: CssTokenType.Ident } keyword])
            return null;

        return keyword.Token.IsIdent("text-before-edge") ? "text-top" : keyword.Token.IsIdent("text-after-edge") ? "text-bottom" : null;
    }

    /// <summary>
    /// https://drafts.csswg.org/css-anchor-position-1/#position-visibility: anchors-valid and anchors-visible are legacy value
    /// aliases of anchor-valid and anchor-visible (https://drafts.csswg.org/css-cascade-5/#legacy-value-alias: they are
    /// converted at parse time), serialized in the order of the grammar.
    /// </summary>
    private static string? CanonicalizePositionVisibility(List<CssComponentValue> significant)
    {
        if (significant is [CssPreservedToken { Type: CssTokenType.Ident } single] && single.Token.IsIdent("always"))
            return null;

        var valid = false;
        var visible = false;
        var noOverflow = false;
        foreach (var value in significant)
        {
            if (value is not CssPreservedToken { Type: CssTokenType.Ident } keyword)
                return null;

            if (keyword.Token.IsIdent("anchor-valid") || keyword.Token.IsIdent("anchors-valid"))
            {
                valid = true;
            }
            else if (keyword.Token.IsIdent("anchor-visible") || keyword.Token.IsIdent("anchors-visible"))
            {
                visible = true;
            }
            else
            {
                noOverflow = true;
            }
        }

        var parts = new List<string>(3);
        if (valid)
        {
            parts.Add("anchor-valid");
        }

        if (visible)
        {
            parts.Add("anchor-visible");
        }

        if (noOverflow)
        {
            parts.Add("no-overflow");
        }

        return string.Join(' ', parts);
    }

    /// <summary>
    /// https://drafts.csswg.org/css-anchor-position-1/#position-area-computed: "the two keywords indicating the selected tracks in
    /// each axis, with the long (block-start) and short (start) logical keywords treated as equivalent. It serializes in the order
    /// given in the grammar, with the logical keywords serialized in their short forms" (as in the browsers: a single keyword stands
    /// for itself and span-all, a keyword with span-all is serialized alone, and an ambiguous keyword with span-all names its axis).
    /// </summary>
    internal static string ComputePositionArea(string value)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
            return value;

        var (first, second) = (parts[0], parts[1]);
        if (first == second)
            return first;

        var firstAxis = GetPositionAreaAxis(first);
        var secondAxis = GetPositionAreaAxis(second);
        if (first == "span-all" || second == "span-all")
        {
            var (keyword, axis, isFirst) = first == "span-all" ? (second, secondAxis, false) : (first, firstAxis, true);
            return axis switch
            {
                // Both keywords are ambiguous: the first one refers to the block axis, the second one to the inline axis
                PositionAreaAxis.Ambiguous => InsertAxis(keyword, isFirst ? "block-" : "inline-"),
                PositionAreaAxis.SelfAmbiguous => InsertAxis(keyword, isFirst ? "self-block-" : "self-inline-"),
                PositionAreaAxis.Center => value,
                _ => keyword,
            };
        }

        // The logical keywords in their short forms, block axis first
        if (firstAxis is PositionAreaAxis.Block or PositionAreaAxis.SelfBlock && secondAxis is PositionAreaAxis.Inline or PositionAreaAxis.SelfInline or PositionAreaAxis.Center
            || firstAxis == PositionAreaAxis.Center && secondAxis is PositionAreaAxis.Block or PositionAreaAxis.SelfBlock)
        {
            return firstAxis == PositionAreaAxis.Center ? Join(Shorten(second), first) : Join(Shorten(first), Shorten(second));
        }

        if (firstAxis is PositionAreaAxis.Inline or PositionAreaAxis.SelfInline && secondAxis is PositionAreaAxis.Block or PositionAreaAxis.SelfBlock or PositionAreaAxis.Center
            || firstAxis == PositionAreaAxis.Center && secondAxis is PositionAreaAxis.Inline or PositionAreaAxis.SelfInline)
        {
            return firstAxis == PositionAreaAxis.Center ? Join(first, Shorten(second)) : secondAxis == PositionAreaAxis.Center ? Join(second, Shorten(first)) : Join(Shorten(second), Shorten(first));
        }

        // The physical keywords: the horizontal one first
        if (firstAxis == PositionAreaAxis.Vertical && secondAxis is PositionAreaAxis.Horizontal or PositionAreaAxis.Center || firstAxis == PositionAreaAxis.Center && secondAxis == PositionAreaAxis.Horizontal)
            return second + " " + first;

        return value;

        static string Join(string block, string inline) => block == inline ? block : block + " " + inline;

        static string Shorten(string keyword) => keyword.Replace("block-", "", StringComparison.Ordinal).Replace("inline-", "", StringComparison.Ordinal);

        static string InsertAxis(string keyword, string axis)
        {
            // start -> block-start, span-self-end -> span-self-block-end
            var span = keyword.StartsWith("span-", StringComparison.Ordinal) ? "span-" : "";
            var rest = keyword[span.Length..];
            return rest.StartsWith("self-", StringComparison.Ordinal) ? span + axis + rest[5..] : span + axis + rest;
        }
    }

    /// <summary>
    /// https://drafts.csswg.org/css-anchor-position-1/#position-try-fallbacks: the &lt;position-area&gt; entries compute as the
    /// position-area property (e.g. self-block-start span-self-inline-end is self-start span-self-end).
    /// </summary>
    internal static string ComputePositionTryFallbacks(string value)
    {
        if (value == "none")
            return value;

        var entries = value.Split(',', StringSplitOptions.TrimEntries);
        var changed = false;
        for (var i = 0; i < entries.Length; i++)
        {
            var keywords = entries[i].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (keywords.Length is 0 or > 2 || Array.Exists(keywords, static keyword => !IsPositionAreaKeyword(keyword)))
                continue;

            var computed = ComputePositionArea(entries[i]);
            if (computed != entries[i])
            {
                entries[i] = computed;
                changed = true;
            }
        }

        return changed ? string.Join(", ", entries) : value;
    }

    /// <summary>Returns whether a keyword is a &lt;position-area&gt; keyword, as opposed to a &lt;dashed-ident&gt; or a &lt;try-tactic&gt; of position-try-fallbacks.</summary>
    private static bool IsPositionAreaKeyword(string keyword)
    {
        if (keyword is "center" or "span-all" or "left" or "right" or "top" or "bottom" or "start" or "end" or "self-start" or "self-end")
            return true;

        var name = keyword.StartsWith("span-", StringComparison.Ordinal) ? keyword[5..] : keyword;
        name = name.StartsWith("self-", StringComparison.Ordinal) ? name[5..] : name;
        return name is "left" or "right" or "top" or "bottom" or "start" or "end"
            or "x-start" or "x-end" or "y-start" or "y-end" or "block-start" or "block-end" or "inline-start" or "inline-end";
    }

    private enum PositionAreaAxis
    {
        Center,
        SpanAll,
        Horizontal,
        Vertical,
        Block,
        Inline,
        SelfBlock,
        SelfInline,
        Ambiguous,
        SelfAmbiguous,
    }

    private static PositionAreaAxis GetPositionAreaAxis(string keyword)
    {
        if (keyword == "center")
            return PositionAreaAxis.Center;

        if (keyword == "span-all")
            return PositionAreaAxis.SpanAll;

        var name = keyword.StartsWith("span-", StringComparison.Ordinal) ? keyword[5..] : keyword;
        return name switch
        {
            "left" or "right" => PositionAreaAxis.Horizontal,
            "top" or "bottom" => PositionAreaAxis.Vertical,
            "start" or "end" => PositionAreaAxis.Ambiguous,
            "self-start" or "self-end" => PositionAreaAxis.SelfAmbiguous,
            _ when name.StartsWith("x-", StringComparison.Ordinal) || name.StartsWith("self-x-", StringComparison.Ordinal) => PositionAreaAxis.Horizontal,
            _ when name.StartsWith("y-", StringComparison.Ordinal) || name.StartsWith("self-y-", StringComparison.Ordinal) => PositionAreaAxis.Vertical,
            _ when name.StartsWith("block-", StringComparison.Ordinal) => PositionAreaAxis.Block,
            _ when name.StartsWith("inline-", StringComparison.Ordinal) => PositionAreaAxis.Inline,
            _ when name.StartsWith("self-block-", StringComparison.Ordinal) => PositionAreaAxis.SelfBlock,
            _ => PositionAreaAxis.SelfInline,
        };
    }
}
