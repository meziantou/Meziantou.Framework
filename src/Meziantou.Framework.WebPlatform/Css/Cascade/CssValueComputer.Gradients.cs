using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.Cascade;

// The computed values of the gradients (https://drafts.csswg.org/css-images-4/#gradients)
internal static partial class CssValueComputer
{
    /// <summary>
    /// Computes the gradients of a computed value: the position of radial-gradient() and conic-gradient() is a pair of offsets
    /// (the keywords are percentages, e.g. at right center is at 100% 50%), and the default values are omitted (at center,
    /// farthest-corner, the ellipse shape without size, from 0deg). The interpolation method follows the position.
    /// </summary>
    private static string ComputeGradients(string value)
    {
        if (!value.Contains("gradient(", StringComparison.OrdinalIgnoreCase))
            return value;

        var values = CssParser.ParseListOfComponentValues(value);
        var changed = false;
        var result = new System.Text.StringBuilder();
        foreach (var component in values)
        {
            if (component is CssFunction function && ComputeGradient(function) is { } gradient)
            {
                result.Append(gradient);
                changed = true;
            }
            else
            {
                result.Append(CssSerializer.SerializeValue([component], property: null));
            }
        }

        return changed ? result.ToString() : value;
    }

    private static string? ComputeGradient(CssFunction function)
    {
        var name = InfraAscii.ToAsciiLowercase(function.Name);
        var kind = name.StartsWith("repeating-", StringComparison.Ordinal) ? name["repeating-".Length..] : name;
        if (kind is not ("linear-gradient" or "radial-gradient" or "conic-gradient"))
            return null;

        var segments = new List<List<CssComponentValue>> { new() };
        foreach (var argument in ValueMatcher.RemoveWhitespace(function.Value))
        {
            if (argument is CssPreservedToken { Type: CssTokenType.Comma })
            {
                segments.Add([]);
            }
            else
            {
                segments[^1].Add(argument);
            }
        }

        var first = segments[0];
        var hasPrelude = first.Count > 0 && (kind == "conic-gradient"
            ? first.Exists(v => GetIdent(v) is "from" or "at" or "in")
            : !ValueMatcher.IsColor(first[0]));

        // https://drafts.csswg.org/css-images-4/#color-stop-syntax: a color stop with two positions is two color stops with
        // the same color (red 10% 20% is red 10%, red 20%), as in the browsers
        var stops = new List<string>(segments.Count);
        var hasDoubleStop = false;
        foreach (var segment in hasPrelude ? segments.Skip(1) : segments)
        {
            if (segment is [var color, var position1, var position2] && ValueMatcher.IsColor(color))
            {
                var serializedColor = CssSerializer.SerializeValue([color], property: null);
                stops.Add(serializedColor + " " + SerializeAngle(position1));
                stops.Add(serializedColor + " " + SerializeAngle(position2));
                hasDoubleStop = true;
            }
            else
            {
                stops.Add(string.Join(' ', segment.Select(SerializeAngle)));
            }
        }

        // linear-gradient(): the horizontal side of a corner comes first (to top right is to right top)
        if (kind == "linear-gradient")
        {
            if (first is [var to, var side1, var side2] && GetIdent(to) == "to" && GetIdent(side1) is "top" or "bottom" && segments.Count >= 2)
            {
                first = [to, side2, side1];
            }
            else if (!hasDoubleStop)
            {
                return null;
            }

            return name + "(" + string.Join(", ", hasPrelude ? stops.Prepend(string.Join(' ', first.Select(v => CssSerializer.SerializeValue([v], property: null)))) : stops) + ")";
        }

        if (!hasPrelude)
            return hasDoubleStop ? name + "(" + string.Join(", ", stops) + ")" : null;

        if (segments.Count < 2)
            return null;

        var other = new List<string>();
        string? position = null;
        string? interpolation = null;
        for (var i = 0; i < first.Count;)
        {
            switch (GetIdent(first[i]))
            {
                case "at":
                {
                    var parts = new List<string>();
                    i++;
                    while (i < first.Count && GetIdent(first[i]) != "in")
                    {
                        parts.Add(CssSerializer.SerializeValue([first[i]], property: null));
                        i++;
                    }

                    position = ComputePositionLayer(string.Join(' ', parts));
                    break;
                }

                case "in":
                    interpolation = string.Join(' ', first[i..].Select(v => CssSerializer.SerializeValue([v], property: null)));
                    i = first.Count;
                    break;

                case "from" when i + 1 < first.Count:
                {
                    var angle = SerializeAngle(first[i + 1]);
                    if (angle is not ("0deg" or "0"))
                    {
                        other.Add("from " + angle);
                    }

                    i += 2;
                    break;
                }

                default:
                    other.Add(CssSerializer.SerializeValue([first[i]], property: null));
                    i++;
                    break;
            }
        }

        // https://drafts.csswg.org/css-images-3/#radial-gradients: farthest-corner and the ellipse shape (without explicit
        // sizes) are the defaults
        other.Remove("farthest-corner");
        if (other is ["ellipse"])
        {
            other.Clear();
        }

        // The radii are non-negative (the math functions are clamped), and a single radius is a circle
        for (var i = 0; i < other.Count; i++)
        {
            if (other[i].StartsWith('-', StringComparison.Ordinal) && other[i].EndsWith("px", StringComparison.Ordinal))
            {
                other[i] = "0px";
            }
        }

        if (other is ["circle", var radius] && radius is not ("closest-side" or "closest-corner" or "farthest-side"))
        {
            other.RemoveAt(0);
        }

        if (position == "50% 50%")
        {
            position = null;
        }

        var prelude = new List<string>(3);
        if (other.Count > 0)
        {
            prelude.Add(string.Join(' ', other));
        }

        if (position is not null)
        {
            prelude.Add("at " + position);
        }

        if (interpolation is not null)
        {
            prelude.Add(interpolation);
        }

        return name + "(" + string.Join(", ", prelude.Count > 0 ? stops.Prepend(string.Join(' ', prelude)) : stops) + ")";

        // The angles converted from other units (e.g. 1.5708rad is 90.0002deg) have at most 6 significant digits, like the browsers
        static string SerializeAngle(CssComponentValue value) => value is CssPreservedToken { Type: CssTokenType.Dimension, Token: var token } && InfraAscii.EqualsIgnoreAsciiCase(token.Value, "deg")
            ? CssColor.FormatNumber(token.NumericValue) + "deg"
            : CssSerializer.SerializeValue([value], property: null);

        static string? GetIdent(CssComponentValue value) => value is CssPreservedToken { Type: CssTokenType.Ident } ident ? InfraAscii.ToAsciiLowercase(ident.Token.Value) : null;
    }
}
