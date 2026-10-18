using System.Collections.Frozen;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.Values;

/// <summary>The syntax of the relative colors (https://drafts.csswg.org/css-color-5/#relative-colors), e.g. rgb(from red r calc(g * 2) b).</summary>
internal static class CssRelativeColor
{
    [Flags]
    private enum ChannelKinds
    {
        Number = 1,
        Percentage = 2,
        Angle = 4,
    }

    private const ChannelKinds NumberOrPercentage = ChannelKinds.Number | ChannelKinds.Percentage;
    private const ChannelKinds Hue = ChannelKinds.Number | ChannelKinds.Angle;

    private static readonly FrozenSet<string> RgbSpaces = FrozenSet.Create(StringComparer.Ordinal,
        "srgb", "srgb-linear", "display-p3", "display-p3-linear", "a98-rgb", "prophoto-rgb", "rec2020", "rec2100-pq", "rec2100-hlg", "rec2100-linear");

    private static readonly FrozenSet<string> XyzSpaces = FrozenSet.Create(StringComparer.Ordinal, "xyz", "xyz-d50", "xyz-d65");

    private static readonly FrozenSet<string> RgbKeywords = FrozenSet.Create(StringComparer.Ordinal, "r", "g", "b", "alpha");
    private static readonly FrozenSet<string> XyzKeywords = FrozenSet.Create(StringComparer.Ordinal, "x", "y", "z", "alpha");

    /// <summary>The channel keywords of the relative color functions (color() depends on its color space: r, g, b or x, y, z).</summary>
    private static readonly FrozenDictionary<string, FrozenSet<string>> ChannelKeywords = new Dictionary<string, FrozenSet<string>>(StringComparer.Ordinal)
    {
        ["rgb"] = RgbKeywords,
        ["rgba"] = RgbKeywords,
        ["hsl"] = FrozenSet.Create(StringComparer.Ordinal, "h", "s", "l", "alpha"),
        ["hsla"] = FrozenSet.Create(StringComparer.Ordinal, "h", "s", "l", "alpha"),
        ["hwb"] = FrozenSet.Create(StringComparer.Ordinal, "h", "w", "b", "alpha"),
        ["lab"] = FrozenSet.Create(StringComparer.Ordinal, "l", "a", "b", "alpha"),
        ["oklab"] = FrozenSet.Create(StringComparer.Ordinal, "l", "a", "b", "alpha"),
        ["lch"] = FrozenSet.Create(StringComparer.Ordinal, "l", "c", "h", "alpha"),
        ["oklch"] = FrozenSet.Create(StringComparer.Ordinal, "l", "c", "h", "alpha"),

        // https://drafts.csswg.org/css-color-hdr-1/#ICtCp, https://drafts.csswg.org/css-color-hdr-1/#Jzazbz, https://drafts.csswg.org/css-color-hdr-1/#JzCzhz
        ["ictcp"] = FrozenSet.Create(StringComparer.Ordinal, "i", "ct", "cp", "alpha"),
        ["jzazbz"] = FrozenSet.Create(StringComparer.Ordinal, "j", "a", "b", "alpha"),
        ["jzczhz"] = FrozenSet.Create(StringComparer.Ordinal, "j", "c", "h", "alpha"),
        ["color"] = FrozenSet.Create(StringComparer.Ordinal, "r", "g", "b", "x", "y", "z", "alpha"),
        ["alpha"] = FrozenSet.Create(StringComparer.Ordinal, "alpha"),
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>Returns the channel keywords of a relative color function (rgb: r, g, b and alpha), or null when it is not a color function.</summary>
    public static FrozenSet<string>? GetChannelKeywords(string function) => ChannelKeywords.GetValueOrDefault(function);

    /// <summary>Returns whether the function is a relative color: its arguments start with "from".</summary>
    public static bool IsRelativeColorSyntax(CssFunction function)
    {
        foreach (var value in function.Value)
        {
            if (value is CssPreservedToken { Type: CssTokenType.Whitespace })
                continue;

            return value is CssPreservedToken { Type: CssTokenType.Ident } ident && InfraAscii.EqualsIgnoreAsciiCase(ident.Token.Value, "from");
        }

        return false;
    }

    /// <summary>Returns whether the relative color is valid: name( from &lt;color&gt; channels [ / alpha ]? ).</summary>
    public static bool IsValid(CssFunction function)
    {
        var name = InfraAscii.ToAsciiLowercase(function.Name);
        var values = ValueMatcher.RemoveWhitespace(function.Value);
        if (values.Count < 2 || !ValueMatcher.IsColor(values[1]))
            return false;

        var index = 2;
        ChannelKinds[] channels;
        FrozenSet<string>? keywords;
        if (name == "color")
        {
            // color(from <color> <colorspace> channels): the channel keywords depend on the color space
            if (index >= values.Count || values[index] is not CssPreservedToken { Type: CssTokenType.Ident } space)
                return false;

            var spaceName = InfraAscii.ToAsciiLowercase(space.Token.Value);
            index++;
            if (RgbSpaces.Contains(spaceName))
            {
                keywords = RgbKeywords;
            }
            else if (XyzSpaces.Contains(spaceName))
            {
                keywords = XyzKeywords;
            }
            else
            {
                return false;
            }

            channels = [NumberOrPercentage, NumberOrPercentage, NumberOrPercentage];
        }
        else
        {
            keywords = GetChannelKeywords(name);
            channels = name switch
            {
                "hsl" or "hsla" or "hwb" => [Hue, NumberOrPercentage, NumberOrPercentage],
                "lch" or "oklch" or "jzczhz" => [NumberOrPercentage, NumberOrPercentage, Hue],

                // https://drafts.csswg.org/css-color-5/#relative-alpha: alpha(from <color> [ / alpha ]?)
                "alpha" => [],
                _ => [NumberOrPercentage, NumberOrPercentage, NumberOrPercentage],
            };
        }

        if (keywords is null)
            return false;

        foreach (var kinds in channels)
        {
            if (index >= values.Count || !IsValidChannel(values[index], kinds, keywords))
                return false;

            index++;
        }

        // The alpha of alpha() is required
        if (index == values.Count)
            return name != "alpha";

        // [ / [ <alpha-value> | none ] ]?
        return index + 2 == values.Count && values[index] is CssPreservedToken { Token: var slash } && slash.IsDelim('/') && IsValidChannel(values[index + 1], NumberOrPercentage, keywords);
    }

    private static bool IsValidChannel(CssComponentValue value, ChannelKinds kinds, FrozenSet<string> keywords)
    {
        switch (value)
        {
            case CssPreservedToken { Token: var token }:
                return token.Type switch
                {
                    CssTokenType.Number => true,
                    CssTokenType.Percentage => kinds.HasFlag(ChannelKinds.Percentage),
                    CssTokenType.Dimension => kinds.HasFlag(ChannelKinds.Angle) && InfraAscii.ToAsciiLowercase(token.Value) is "deg" or "grad" or "rad" or "turn",
                    CssTokenType.Ident => InfraAscii.EqualsIgnoreAsciiCase(token.Value, "none") || keywords.Contains(InfraAscii.ToAsciiLowercase(token.Value)),
                    _ => false,
                };

            // https://drafts.csswg.org/css-values-5/#tree-counting: sibling-index() and sibling-count() are integers
            case CssFunction treeCounting when InfraAscii.ToAsciiLowercase(treeCounting.Name) is "sibling-index" or "sibling-count":
                return ValueMatcher.RemoveWhitespace(treeCounting.Value).Count == 0;

            case CssFunction math when CssCalc.IsMathFunction(math.Name):
            {
                // The channel keywords are numbers (e.g. calc(h + 1deg) is invalid); only the type of the tree counting functions matters
                if (CssCalc.Evaluate(math, new CssCalcContext { ChannelKeywords = keywords, ResolveTreeCountingFunction = static _ => 1 }) is not { } result)
                    return false;

                return result.TypeUnit switch
                {
                    "" => true,
                    "%" => kinds.HasFlag(ChannelKinds.Percentage),
                    "deg" => kinds.HasFlag(ChannelKinds.Angle),
                    _ => false,
                };
            }

            default:
                return false;
        }
    }
}
