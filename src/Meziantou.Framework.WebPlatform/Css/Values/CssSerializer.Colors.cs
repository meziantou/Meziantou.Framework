using System.Text;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.Values;

// The serialization of the colors that are not resolved when they are specified: color-mix() and the relative colors
internal static partial class CssSerializer
{
    /// <summary>
    /// Serializes a relative color (https://drafts.csswg.org/css-color-5/#serial-relative-color): rgba() and hsla() are
    /// serialized as rgb() and hsl(), the origin color and the channels as specified values. Returns false when the
    /// function is not a relative color.
    /// </summary>
    private static bool TrySerializeRelativeColor(StringBuilder builder, CssFunction function, string name, HashSet<string>? keywords)
    {
        name = name switch
        {
            "rgba" => "rgb",
            "hsla" => "hsl",
            _ => name,
        };

        if (CssRelativeColor.GetChannelKeywords(name) is not { } channelKeywords || !CssRelativeColor.IsRelativeColorSyntax(function))
            return false;

        builder.Append(name).Append('(');
        var first = true;
        foreach (var value in ValueMatcher.RemoveWhitespace(function.Value))
        {
            if (value is CssPreservedToken slash && slash.Token.IsDelim('/'))
            {
                builder.Append(" /");
                continue;
            }

            if (!first)
            {
                builder.Append(' ');
            }

            first = false;
            if (value is CssFunction math && CssCalc.IsMathFunction(math.Name) && CssCalc.SerializeSpecifiedValue(math, channelKeywords) is { } calc)
            {
                builder.Append(calc);
            }
            else if (GetIdent(value) is { } ident && channelKeywords.Contains(ident))
            {
                builder.Append(ident);
            }
            else if (name == "color" && GetIdent(value) == "xyz")
            {
                // xyz is an alias of xyz-d65 (https://drafts.csswg.org/css-color-4/#predefined-xyz)
                builder.Append("xyz-d65");
            }
            else
            {
                SerializeComponentValue(builder, value, keywords, zeroIsLength: false);
            }
        }

        builder.Append(')');
        return true;
    }

    /// <summary>
    /// Serializes color-mix() (https://drafts.csswg.org/css-color-5/#serial-color-mix): the default interpolation method
    /// (in oklab) is omitted, each color is followed by its percentage. When the percentages are numbers (they are not math
    /// functions), the missing ones share what the specified ones leave of 100%, and they are all omitted when they are
    /// equal and sum to 100%. Returns false when the function is not a valid color-mix().
    /// </summary>
    private static bool TrySerializeColorMix(StringBuilder builder, CssFunction function, HashSet<string>? keywords)
    {
        var segments = SplitAtCommas(ValueMatcher.RemoveWhitespace(function.Value));
        if (segments.Exists(s => s.Count == 0))
            return false;

        string? interpolation = null;
        var start = 0;
        if (GetIdent(segments[0][0]) == "in")
        {
            var method = segments[0];
            if (method.Count < 2 || GetIdent(method[1]) is not { } space)
                return false;

            space = space == "xyz" ? "xyz-d65" : space;
            string? hue = null;
            if (method.Count == 4 && GetIdent(method[2]) is { } hueMethod && GetIdent(method[3]) == "hue")
            {
                hue = hueMethod == "shorter" ? null : hueMethod;
            }
            else if (method.Count != 2)
            {
                return false;
            }

            interpolation = space == "oklab" && hue is null ? null : hue is null ? "in " + space : "in " + space + " " + hue + " hue";
            start = 1;
        }

        var colors = new List<(CssComponentValue Color, CssComponentValue? Percentage)>();
        for (var i = start; i < segments.Count; i++)
        {
            var segment = segments[i];
            switch (segment)
            {
                case [var color] when ValueMatcher.IsColor(color):
                    colors.Add((color, null));
                    break;
                case [var color, var percentage] when ValueMatcher.IsColor(color) && !ValueMatcher.IsColor(percentage):
                    colors.Add((color, percentage));
                    break;
                case [var percentage, var color] when ValueMatcher.IsColor(color) && !ValueMatcher.IsColor(percentage):
                    colors.Add((color, percentage));
                    break;
                default:
                    return false;
            }
        }

        if (colors.Count == 0)
            return false;

        var percentages = colors.ConvertAll(c => c.Percentage is null ? null : SerializeSequence([c.Percentage], keywords));
        if (colors.TrueForAll(c => c.Percentage is null or CssPreservedToken { Type: CssTokenType.Percentage }))
        {
            percentages = NormalizeMixPercentages(colors.ConvertAll(c => (c.Percentage as CssPreservedToken)?.Token.NumericValue));
        }

        builder.Append("color-mix(");
        if (interpolation is not null)
        {
            builder.Append(interpolation).Append(", ");
        }

        for (var i = 0; i < colors.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(", ");
            }

            SerializeComponentValue(builder, colors[i].Color, keywords, zeroIsLength: false);

            if (percentages[i] is { } percentage)
            {
                builder.Append(' ').Append(percentage);
            }
        }

        builder.Append(')');
        return true;
    }

    /// <summary>
    /// Serializes color-hdr() (https://drafts.csswg.org/css-color-hdr-1/#hdr-color-function): each color is followed by its
    /// headroom, when it is specified. Returns false when the function is not a valid color-hdr().
    /// </summary>
    private static bool TrySerializeHdrColor(StringBuilder builder, CssFunction function, HashSet<string>? keywords)
    {
        if (!CssHdrColor.IsValid(function) || CssHdrColor.GetStops(function) is not { } stops)
            return false;

        var serialized = new List<(string Color, string? Headroom)>(stops.Length);
        foreach (var stop in stops)
        {
            var color = new StringBuilder();
            SerializeComponentValue(color, stop.Color, keywords, zeroIsLength: false);
            serialized.Add((color.ToString(), stop.Headroom is null ? null : SerializeSequence([stop.Headroom], keywords)));
        }

        builder.Append(CssHdrColor.Serialize(serialized));
        return true;
    }

    /// <summary>
    /// Normalizes the percentages of color-mix() and palette-mix() when they are numbers: the missing ones share what the
    /// specified ones leave of 100%, and they are all omitted when they are equal and sum to 100% (or when the only one is 100%).
    /// </summary>
    private static List<string?> NormalizeMixPercentages(List<double?> values)
    {
        var missing = values.Count(v => v is null);
        if (missing > 0 && missing < values.Count)
        {
            var remaining = Math.Max(0, 100 - values.Sum(v => v ?? 0)) / missing;
            values = values.ConvertAll(v => (double?)(v ?? remaining));
        }

        if (values.Exists(v => v is null))
            return values.ConvertAll(_ => (string?)null);

        var equal = values.TrueForAll(v => v == values[0]);
        var omit = values.Count == 1 ? values[0] == 100 : equal && Math.Abs(values.Sum(v => v!.Value) - 100) < 1e-9;
        return omit ? values.ConvertAll(_ => (string?)null) : values.ConvertAll(v => (string?)(SerializeNumber(v!.Value) + "%"));
    }

    /// <summary>
    /// Serializes palette-mix() (https://drafts.csswg.org/css-fonts-5/#typedef-font-palette-palette-mix): the default
    /// interpolation method (in oklab) and hue interpolation method (shorter) are omitted, the percentage follows the
    /// palette. When the percentages are numbers (they are not math functions, which are simplified in the computed
    /// value), the missing ones share what the specified ones leave of 100%, and they are all omitted when they are equal
    /// and sum to 100%. Returns false when the function is not a valid palette-mix().
    /// </summary>
    private static bool TrySerializePaletteMix(StringBuilder builder, CssFunction function, HashSet<string>? keywords)
    {
        var segments = SplitAtCommas(ValueMatcher.RemoveWhitespace(function.Value));
        if (segments.Exists(s => s.Count == 0))
            return false;

        string? interpolation = null;
        var start = 0;
        if (GetIdent(segments[0][0]) == "in")
        {
            var method = segments[0];
            if (method.Count < 2 || GetIdent(method[1]) is not { } space)
                return false;

            space = space == "xyz" ? "xyz-d65" : space;
            string? hue = null;
            if (method.Count == 4 && GetIdent(method[2]) is { } hueMethod && GetIdent(method[3]) == "hue")
            {
                hue = hueMethod == "shorter" ? null : hueMethod;
            }
            else if (method.Count != 2)
            {
                return false;
            }

            interpolation = space == "oklab" && hue is null ? null : hue is null ? "in " + space : "in " + space + " " + hue + " hue";
            start = 1;
        }

        static bool IsPercentage(CssComponentValue value) => value is CssPreservedToken { Type: CssTokenType.Percentage } || (value is CssFunction math && CssCalc.IsMathFunction(math.Name)) || value is CssPreservedToken { Type: CssTokenType.Delim, Token.Value.Length: > 1 }; // a computed math function

        var palettes = new List<CssComponentValue>();
        var percentages = new List<string?>();
        for (var i = start; i < segments.Count; i++)
        {
            switch (segments[i])
            {
                case [var palette] when !IsPercentage(palette):
                    palettes.Add(palette);
                    percentages.Add(null);
                    break;
                case [var first, var second] when IsPercentage(first) != IsPercentage(second):
                    palettes.Add(IsPercentage(first) ? second : first);
                    percentages.Add(SerializeSequence([IsPercentage(first) ? first : second], keywords));
                    break;
                default:
                    return false;
            }
        }

        if (palettes.Count == 0)
            return false;

        // The percentages that are numbers are normalized
        var values = percentages.ConvertAll(p => p is not null && p.EndsWith('%', StringComparison.Ordinal) && double.TryParse(p.AsSpan(0, p.Length - 1), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number) ? number : (double?)null);
        if (percentages.Select((p, i) => p is null || values[i] is not null).All(b => b))
        {
            percentages = NormalizeMixPercentages(values);
        }

        builder.Append("palette-mix(");
        if (interpolation is not null)
        {
            builder.Append(interpolation).Append(", ");
        }

        for (var i = 0; i < palettes.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(", ");
            }

            SerializeComponentValue(builder, palettes[i], keywords, zeroIsLength: false);
            if (percentages[i] is { } percentage)
            {
                builder.Append(' ').Append(percentage);
            }
        }

        builder.Append(')');
        return true;
    }

    /// <summary>Serializes color-layers() (https://drafts.csswg.org/css-color-6/#color-layers): the normal blend mode is omitted.</summary>
    private static bool TrySerializeColorLayers(StringBuilder builder, CssFunction function, HashSet<string>? keywords)
    {
        var segments = SplitAtCommas(ValueMatcher.RemoveWhitespace(function.Value));
        if (segments.Exists(s => s.Count != 1))
            return false;

        if (segments.Count > 1 && GetIdent(segments[0][0]) == "normal")
        {
            segments.RemoveAt(0);
        }

        builder.Append("color-layers(");
        for (var i = 0; i < segments.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(", ");
            }

            SerializeComponentValue(builder, segments[i][0], keywords, zeroIsLength: false);
        }

        builder.Append(')');
        return true;
    }
}
