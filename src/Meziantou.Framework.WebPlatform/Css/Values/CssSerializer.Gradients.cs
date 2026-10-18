using System.Text;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.Values;

// The canonical serialization of the gradients (https://drafts.csswg.org/css-images-4/#gradients)
internal static partial class CssSerializer
{
    /// <summary>
    /// Serializes a gradient in its canonical form: the direction, shape or angle, then the position, then the color
    /// interpolation method, which is omitted when it is the default one (https://drafts.csswg.org/css-images-4/#color-interpolation).
    /// Returns false when the function is not a gradient.
    /// </summary>
    private static bool TrySerializeGradient(StringBuilder builder, CssFunction function, string name, HashSet<string>? keywords)
    {
        var kind = name.StartsWith("repeating-", StringComparison.Ordinal) ? name["repeating-".Length..] : name;
        if (kind is not ("linear-gradient" or "radial-gradient" or "conic-gradient"))
            return false;

        var segments = SplitAtCommas(ValueMatcher.RemoveWhitespace(function.Value));
        if (segments.Count < 2 || segments.Exists(s => s.Count == 0))
            return false;

        var first = segments[0];
        var hasPrelude = kind == "conic-gradient"
            ? first.Exists(v => GetIdent(v) is "from" or "at" or "in")
            : !ValueMatcher.IsColor(Unraw(first[0]));

        var parts = new List<string>(3);
        if (hasPrelude)
        {
            var other = new List<CssComponentValue>();
            List<CssComponentValue>? position = null;
            string? interpolation = null;
            for (var i = 0; i < first.Count;)
            {
                switch (GetIdent(first[i]))
                {
                    case "in" when i + 1 < first.Count && GetIdent(first[i + 1]) is { } space:
                    {
                        i += 2;
                        string? hue = null;
                        if (i + 1 < first.Count && GetIdent(first[i]) is "shorter" or "longer" or "increasing" or "decreasing" && GetIdent(first[i + 1]) == "hue")
                        {
                            hue = GetIdent(first[i]);
                            i += 2;
                        }

                        interpolation = SerializeInterpolationMethod(space, hue, segments);
                        break;
                    }

                    case "at":
                        position = [];
                        i++;
                        while (i < first.Count && GetIdent(first[i]) != "in")
                        {
                            position.Add(first[i]);
                            i++;
                        }

                        break;

                    default:
                        other.Add(first[i]);
                        i++;
                        break;
                }
            }

            // ellipse is the default shape when there are two sizes
            if (kind == "radial-gradient" && other.Count == 3 && other.FindIndex(v => GetIdent(v) == "ellipse") is var ellipse and >= 0 && other.Count(v => v is CssPreservedToken { Type: CssTokenType.Dimension or CssTokenType.Percentage or CssTokenType.Number } or CssFunction) == 2)
            {
                other.RemoveAt(ellipse);
            }

            // The default gradient line, shape, size and position are omitted (as in the browsers): to bottom, ellipse
            // farthest-corner (a circle keeps its shape) and at center
            if (kind == "linear-gradient" && other.Count == 2 && GetIdent(other[0]) == "to" && GetIdent(other[1]) == "bottom")
            {
                other.Clear();
            }
            else if (kind == "radial-gradient" && other.TrueForAll(static v => GetIdent(v) is not null))
            {
                other.RemoveAll(static v => GetIdent(v) == "farthest-corner");
                if (other is [var shape] && GetIdent(shape) == "ellipse")
                {
                    other.Clear();
                }
            }

            if (position is not null && position.TrueForAll(static v => GetIdent(v) == "center"))
            {
                position = null;
            }

            if (other.Count > 0)
            {
                parts.Add(SerializeSequence(other, keywords));
            }

            if (position is not null)
            {
                parts.Add("at " + SerializeSequence(CanonicalizePosition(position), keywords));
            }

            if (interpolation is not null)
            {
                parts.Add(interpolation);
            }
        }

        builder.Append(name).Append('(');
        if (parts.Count > 0)
        {
            builder.AppendJoin(' ', parts).Append(", ");
        }

        for (var i = hasPrelude ? 1 : 0; i < segments.Count; i++)
        {
            if (i > (hasPrelude ? 1 : 0))
            {
                builder.Append(", ");
            }

            builder.Append(SerializeSequence(segments[i], keywords));
        }

        builder.Append(')');
        return true;
    }

    /// <summary>
    /// Serializes a &lt;color-interpolation-method&gt;; returns null when it is the default method: srgb when all the colors
    /// are legacy colors, oklab otherwise, and the shorter hue interpolation (https://drafts.csswg.org/css-color-4/#interpolation-space).
    /// </summary>
    private static string? SerializeInterpolationMethod(string space, string? hue, List<List<CssComponentValue>> segments)
    {
        if (space == "xyz")
        {
            space = "xyz-d65";
        }

        if (hue == "shorter")
        {
            hue = null;
        }

        if (hue is null && space == (AreLegacyColors(segments) ? "srgb" : "oklab"))
            return null;

        return hue is null ? "in " + space : "in " + space + " " + hue + " hue";
    }

    /// <summary>Returns whether all the colors of the color stops are legacy colors (https://drafts.csswg.org/css-color-4/#legacy-color-syntax).</summary>
    private static bool AreLegacyColors(List<List<CssComponentValue>> segments)
    {
        foreach (var segment in segments)
        {
            foreach (var raw in segment)
            {
                var value = Unraw(raw);
                if (!ValueMatcher.IsColor(value))
                    continue;

                var isLegacy = value switch
                {
                    CssPreservedToken { Type: CssTokenType.Hash or CssTokenType.Ident } => true,
                    CssFunction color => InfraAscii.ToAsciiLowercase(color.Name) is "rgb" or "rgba" or "hsl" or "hsla" or "hwb" && !color.Value.Exists(v => GetIdent(v) == "from"),
                    _ => false,
                };

                if (!isLegacy)
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Orders the components of a &lt;position&gt; horizontal first (https://drafts.csswg.org/css-values-4/#position-serialization):
    /// "bottom right" is "right bottom", "top 10px left 20px" is "left 20px top 10px".
    /// </summary>
    private static List<CssComponentValue> CanonicalizePosition(List<CssComponentValue> position)
    {
        switch (position.Count)
        {
            case 2 when GetIdent(position[0]) is "top" or "bottom" || GetIdent(position[1]) is "left" or "right":
                return [position[1], position[0]];

            case 4 when GetIdent(position[0]) is "top" or "bottom":
                return [position[2], position[3], position[0], position[1]];

            default:
                return position;
        }
    }

    /// <summary>
    /// Returns the component value of a computed value inserted in a value as text (a delim token whose value is the
    /// serialization, e.g. the computed colors of a gradient), or the value itself.
    /// </summary>
    private static CssComponentValue Unraw(CssComponentValue value)
    {
        if (value is CssPreservedToken { Type: CssTokenType.Delim, Token.Value: { Length: > 1 } text } && ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(text)) is [var parsed])
            return parsed;

        return value;
    }

    private static List<List<CssComponentValue>> SplitAtCommas(List<CssComponentValue> values)
    {
        var result = new List<List<CssComponentValue>> { new() };
        foreach (var value in values)
        {
            if (value is CssPreservedToken { Type: CssTokenType.Comma })
            {
                result.Add([]);
            }
            else
            {
                result[^1].Add(value);
            }
        }

        return result;
    }

    private static string? GetIdent(CssComponentValue value) => value is CssPreservedToken { Type: CssTokenType.Ident } ident ? InfraAscii.ToAsciiLowercase(ident.Token.Value) : null;

    /// <summary>Serializes component values (whitespace excluded) separated by a space.</summary>
    private static string SerializeSequence(List<CssComponentValue> values, HashSet<string>? keywords)
    {
        var builder = new StringBuilder();
        foreach (var value in values)
        {
            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            SerializeComponentValue(builder, value, keywords, zeroIsLength: false);
        }

        return builder.ToString();
    }
}
