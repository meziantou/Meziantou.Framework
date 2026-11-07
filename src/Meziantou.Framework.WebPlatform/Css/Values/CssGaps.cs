using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.Values;

/// <summary>
/// The shorthands of the gap decorations (https://drafts.csswg.org/css-gaps/): the column-rule and row-rule lists of
/// &lt;gap-rule&gt; (with repeat()), the *-rule-inset family (an omitted value copies its pair), and the bidirectional
/// shorthands (rule, rule-color, rule-inset...), which set the column and row longhands to the same value.
/// </summary>
internal static class CssGaps
{
    private static readonly string[] RuleDefaults = ["medium", "none", "currentcolor"];

    public static bool IsShorthand(CssPropertyDefinition shorthand) => IsBidirectional(shorthand) || IsRuleList(shorthand.Name) || IsInset(shorthand.Name);

    /// <summary>The shorthands whose longhands are the column-* and row-* versions of a property (rule, rule-color, rule-inset...).</summary>
    private static bool IsBidirectional(CssPropertyDefinition shorthand) => shorthand.Name.StartsWith("rule", StringComparison.Ordinal) && shorthand.Longhands is [var column, var row] && column == "column-" + shorthand.Name && row == "row-" + shorthand.Name;

    private static bool IsRuleList(string name) => name is "column-rule" or "row-rule";

    private static bool IsInset(string name) => name.StartsWith("column-rule-inset", StringComparison.Ordinal) || name.StartsWith("row-rule-inset", StringComparison.Ordinal);

    // ----------------------------------------------------------------------------------------------------------------
    // Expansion
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>Expands a gap decoration shorthand, or returns null when the value is invalid.</summary>
    public static List<CssLonghandValue>? Expand(CssPropertyDefinition shorthand, List<CssComponentValue> values, List<CssComponentValue> significant)
    {
        if (IsBidirectional(shorthand))
        {
            // The value is parsed as the value of the column property, and applies to the row property too
            if (CssShorthands.ParseValue(shorthand.Longhands[0], values, originalText: null) is not { } column)
                return null;

            return [.. column, .. column.Select(v => v with { Name = "row-" + v.Name["column-".Length..] })];
        }

        if (IsRuleList(shorthand.Name))
            return ExpandRuleList(shorthand, significant);

        return ExpandInset(shorthand, significant);
    }

    /// <summary>
    /// https://drafts.csswg.org/css-gaps/#column-row-rule: each &lt;gap-rule&gt; (or repeat()) of the list sets the item of
    /// the width, style and color lists; the omitted components are their initial value.
    /// </summary>
    private static List<CssLonghandValue>? ExpandRuleList(CssPropertyDefinition shorthand, List<CssComponentValue> significant)
    {
        var lists = new[] { new List<string>(), new List<string>(), new List<string>() };
        foreach (var item in SplitAtComma(significant))
        {
            if (item is [CssFunction function] && InfraAscii.EqualsIgnoreAsciiCase(function.Name, "repeat"))
            {
                var arguments = SplitAtComma(ValueMatcher.RemoveWhitespace(function.Value));
                if (arguments.Count < 2 || arguments[0] is not [var count])
                    return null;

                var inner = new[] { new List<string>(), new List<string>(), new List<string>() };
                foreach (var rule in arguments.Skip(1))
                {
                    if (ParseRule(shorthand, rule) is not { } parsed)
                        return null;

                    for (var i = 0; i < 3; i++)
                    {
                        inner[i].Add(parsed[i]);
                    }
                }

                var countText = CssSerializer.SerializeValue([count], property: null);
                for (var i = 0; i < 3; i++)
                {
                    lists[i].Add("repeat(" + InfraAscii.ToAsciiLowercase(countText) + ", " + string.Join(", ", inner[i]) + ")");
                }
            }
            else
            {
                if (ParseRule(shorthand, item) is not { } parsed)
                    return null;

                for (var i = 0; i < 3; i++)
                {
                    lists[i].Add(parsed[i]);
                }
            }
        }

        return [.. shorthand.Longhands.Select((longhand, i) => new CssLonghandValue(longhand, string.Join(", ", lists[i])))];
    }

    /// <summary>Parses a &lt;gap-rule&gt; (&lt;line-width&gt; || &lt;line-style&gt; || &lt;color&gt;) into its width, style and color.</summary>
    private static string[]? ParseRule(CssPropertyDefinition shorthand, List<CssComponentValue> rule)
    {
        var result = new string?[3];
        var types = new[] { "<line-width>", "<line-style>", "<color>" };
        foreach (var component in rule)
        {
            var found = false;
            for (var i = 0; i < 3 && !found; i++)
            {
                if (result[i] is null && CssPropertyRegistry.GetTypeGrammar(types[i]) is { } grammar && ValueMatcher.Matches(grammar, [component]))
                {
                    result[i] = CssSerializer.SerializeValue([component], CssPropertyRegistry.Get(shorthand.Longhands[i]));
                    found = true;
                }
            }

            if (!found)
                return null;
        }

        return [.. result.Select((value, i) => value ?? RuleDefaults[i])];
    }

    /// <summary>
    /// https://drafts.csswg.org/css-gaps/#insets: *-rule-inset-cap and *-rule-inset-junction set the start and end insets
    /// (an omitted end is the start), *-rule-inset-start and *-rule-inset-end set the cap and junction insets, and
    /// *-rule-inset sets the cap insets, then the junction insets (omitted: the cap insets).
    /// </summary>
    private static List<CssLonghandValue>? ExpandInset(CssPropertyDefinition shorthand, List<CssComponentValue> significant)
    {
        var parts = new List<List<CssComponentValue>> { new() };
        foreach (var value in significant)
        {
            if (value is CssPreservedToken slash && slash.Token.IsDelim('/'))
            {
                parts.Add([]);
            }
            else
            {
                parts[^1].Add(value);
            }
        }

        var longhand = CssPropertyRegistry.Get(shorthand.Longhands[0]);
        var grammar = CssPropertyRegistry.GetTypeGrammar("<inset-value>");
        var insets = new List<List<string>>();
        foreach (var part in parts)
        {
            if (part.Count is 0 or > 2 || grammar is null || !part.TrueForAll(v => ValueMatcher.Matches(grammar, [v])))
                return null;

            insets.Add([.. part.Select(v => CssSerializer.SerializeValue([v], longhand))]);
        }

        string[] values;
        if (shorthand.Name.EndsWith("-start", StringComparison.Ordinal) || shorthand.Name.EndsWith("-end", StringComparison.Ordinal))
        {
            if (insets is not [[var single]])
                return null;

            values = [single, single];
        }
        else if (shorthand.Longhands.Length == 2)
        {
            if (insets is not [var pair])
                return null;

            values = [pair[0], pair.Count > 1 ? pair[1] : pair[0]];
        }
        else
        {
            if (insets.Count > 2)
                return null;

            var cap = insets[0];
            var junction = insets.Count > 1 ? insets[1] : cap;
            values = [cap[0], cap.Count > 1 ? cap[1] : cap[0], junction[0], junction.Count > 1 ? junction[1] : junction[0]];
        }

        return [.. shorthand.Longhands.Select((name, i) => new CssLonghandValue(name, values[i]))];
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Serialization
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>Serializes a gap decoration shorthand from its longhands, or returns null when it cannot represent them.</summary>
    public static string? Serialize(CssPropertyDefinition shorthand, Dictionary<string, string> lookup)
    {
        if (IsBidirectional(shorthand))
        {
            var column = SerializeProperty(shorthand.Longhands[0], lookup);
            var row = SerializeProperty(shorthand.Longhands[1], lookup);
            return column is not null && column == row ? column : null;
        }

        var values = shorthand.Longhands.Select(l => lookup[l]).ToArray();
        if (IsRuleList(shorthand.Name))
            return SerializeRuleList(values);

        if (shorthand.Name.EndsWith("-start", StringComparison.Ordinal) || shorthand.Name.EndsWith("-end", StringComparison.Ordinal))
            return values[0] == values[1] ? values[0] : null;

        if (values.Length == 2)
            return values[0] == values[1] ? values[0] : values[0] + " " + values[1];

        return values.All(v => v == values[0]) ? values[0] : values[0] + " " + values[1] + " / " + values[2] + " " + values[3];
    }

    private static string? SerializeProperty(string name, Dictionary<string, string> lookup)
    {
        if (CssPropertyRegistry.Get(name) is not { } property)
            return null;

        if (!property.IsShorthand)
            return lookup.GetValueOrDefault(name);

        return CssShorthands.Serialize(property, longhand => lookup.TryGetValue(longhand, out var value) ? new CssLonghandValue(longhand, value) : null) is { Length: > 0 } serialized ? serialized : null;
    }

    /// <summary>Zips the width, style and color lists, which must have the same structure (the same items and repeat() counts).</summary>
    private static string? SerializeRuleList(string[] longhands)
    {
        var lists = longhands.Select(ParseList).ToArray();
        if (lists.Any(l => l.Count != lists[0].Count))
            return null;

        var items = new List<string>();
        for (var i = 0; i < lists[0].Count; i++)
        {
            var (count, width) = lists[0][i];
            var (styleCount, style) = lists[1][i];
            var (colorCount, color) = lists[2][i];
            if (styleCount != count || colorCount != count || style.Count != width.Count || color.Count != width.Count)
                return null;

            var rules = width.Select((_, j) => SerializeRule(width[j], style[j], color[j])).ToList();
            items.Add(count is null ? rules[0] : "repeat(" + count + ", " + string.Join(", ", rules) + ")");
        }

        return string.Join(", ", items);

        static string SerializeRule(string width, string style, string color)
        {
            var parts = new List<string>(3);
            if (width != RuleDefaults[0])
            {
                parts.Add(width);
            }

            if (style != RuleDefaults[1])
            {
                parts.Add(style);
            }

            if (color != RuleDefaults[2])
            {
                parts.Add(color);
            }

            return parts.Count == 0 ? width : string.Join(' ', parts);
        }
    }

    /// <summary>Parses a list of values and repeat() into items: (null, [value]) or (count, values).</summary>
    private static List<(string? Count, List<string> Values)> ParseList(string value)
    {
        var result = new List<(string?, List<string>)>();
        foreach (var item in SplitAtComma(ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(value))))
        {
            if (item is [CssFunction function] && InfraAscii.EqualsIgnoreAsciiCase(function.Name, "repeat"))
            {
                var arguments = SplitAtComma(ValueMatcher.RemoveWhitespace(function.Value));
                result.Add((CssSerializer.SerializeValue(arguments[0], property: null), [.. arguments.Skip(1).Select(a => CssSerializer.SerializeValue(InsertSpaces(a), property: null))]));
            }
            else
            {
                result.Add((null, [CssSerializer.SerializeValue(InsertSpaces(item), property: null)]));
            }
        }

        return result;
    }

    private static List<List<CssComponentValue>> SplitAtComma(List<CssComponentValue> values)
    {
        var result = new List<List<CssComponentValue>> { new() };
        foreach (var value in values)
        {
            if (value is CssPreservedToken { Type: CssTokenType.Comma })
            {
                result.Add([]);
            }
            else if (value is not CssPreservedToken { Type: CssTokenType.Whitespace })
            {
                result[^1].Add(value);
            }
        }

        return result;
    }

    private static List<CssComponentValue> InsertSpaces(List<CssComponentValue> values)
    {
        var result = new List<CssComponentValue>(values.Count * 2);
        foreach (var value in values)
        {
            if (result.Count > 0)
            {
                result.Add(new CssPreservedToken(new CssToken { Type = CssTokenType.Whitespace, Value = " " }));
            }

            result.Add(value);
        }

        return result;
    }
}
