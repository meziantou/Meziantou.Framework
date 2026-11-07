using Meziantou.Framework.WebPlatform.Css.Values;

namespace Meziantou.Framework.WebPlatform.Css.Animations;

/// <summary>
/// Expands the shortened computed values of some properties to their full form before they are combined (e.g. the omitted
/// z component of transform-origin), and shortens the result again like the computed value of the reference value.
/// </summary>
internal static class CssValueNormalizer
{
    public static string Normalize(string property, string value)
    {
        switch (property)
        {
            case "transform-origin":
                return CountComponents(value) == 2 ? value + " 0px" : value;

            case "letter-spacing" or "word-spacing" when value == "normal":
                return "0px";

            case "outline-color" when value == "auto":
                return "currentcolor";

            case "border-top-left-radius" or "border-top-right-radius" or "border-bottom-right-radius" or "border-bottom-left-radius"
                or "border-start-start-radius" or "border-start-end-radius" or "border-end-start-radius" or "border-end-end-radius":
            case "border-spacing":
                return CountComponents(value) == 1 ? value + " " + value : value;

            // https://drafts.csswg.org/css-text-decor-4/#text-decoration-inset-property: a single value is both insets
            case "text-decoration-inset" when value != "auto":
                return CountComponents(value) == 1 ? value + " " + value : value;

            case "border-image-slice" or "border-image-width" or "border-image-outset" or "mask-border-slice" or "mask-border-width" or "mask-border-outset":
                return ExpandSides(value);

            // https://drafts.csswg.org/css-fonts-4/#font-style-prop: normal animates as oblique 0deg
            case "font-style":
                return value switch
                {
                    "normal" => "oblique 0deg",
                    "oblique" => "oblique 14deg",
                    _ => value,
                };

            // https://drafts.fxtf.org/motion-1/#offset-rotate-property: auto is auto 0deg
            case "offset-rotate" when value == "auto":
                return "auto 0deg";

            // https://drafts.csswg.org/css-shapes/#basic-shape-interpolation: "The values in the shape functions interpolate as
            // a simple list": the omitted offsets and radii of inset() and the omitted rounding of polygon() are restored
            case "clip-path" or "shape-outside" or "offset-path" when value.StartsWith("inset(", StringComparison.Ordinal) || value.StartsWith("polygon(", StringComparison.Ordinal):
                return ExpandBasicShape(value);

            default:
                return value;
        }
    }

    /// <summary>
    /// Expands an inset() to its four offsets and its eight radii (<c>inset(t r b l round tl tr br bl / tl tr br bl)</c>) and
    /// gives a polygon() its rounding (<c>round 0px</c>), keeping what follows the function (its reference box).
    /// </summary>
    private static string ExpandBasicShape(string value)
    {
        var components = CssGenericInterpolation.Parse(value);
        if (components is not [Syntax.CssFunction function, ..])
            return value;

        var arguments = CssGenericInterpolation.Parse(CssSerializer.SerializeValue(function.Value, property: null));
        string expanded;
        if (function.Name.Equals("inset", StringComparison.OrdinalIgnoreCase))
        {
            var round = arguments.FindIndex(static argument => argument is Syntax.CssPreservedToken { Type: Syntax.CssTokenType.Ident } ident && ident.Token.IsIdent("round"));
            var offsets = ExpandFour(Serialize(round < 0 ? arguments : arguments[..round]));
            var radii = round < 0 ? [] : arguments[(round + 1)..];
            var slash = radii.FindIndex(static argument => argument is Syntax.CssPreservedToken token && token.Token.IsDelim('/'));
            var horizontal = ExpandFour(round < 0 ? ["0px"] : Serialize(slash < 0 ? radii : radii[..slash]));
            var vertical = slash < 0 ? horizontal : ExpandFour(Serialize(radii[(slash + 1)..]));
            if (offsets is null || horizontal is null || vertical is null)
                return value;

            expanded = "inset(" + string.Join(' ', offsets) + " round " + string.Join(' ', horizontal) + " / " + string.Join(' ', vertical) + ")";
        }
        else
        {
            // polygon( <'fill-rule'>? [ round <length> ]? , <vertices> )
            var comma = arguments.FindIndex(static argument => argument is Syntax.CssPreservedToken { Type: Syntax.CssTokenType.Comma });
            var hasPrefix = comma > 0 && arguments[0] is Syntax.CssPreservedToken { Type: Syntax.CssTokenType.Ident };
            var prefix = hasPrefix ? Serialize(arguments[..comma]) : [];
            if (prefix.Contains("round"))
                return value;

            prefix.Add("round 0px");
            var builder = new System.Text.StringBuilder("polygon(").AppendJoin(' ', prefix).Append(',');
            foreach (var argument in hasPrefix ? arguments[(comma + 1)..] : arguments)
            {
                if (argument is Syntax.CssPreservedToken { Type: Syntax.CssTokenType.Comma })
                {
                    builder.Append(',');
                }
                else
                {
                    builder.Append(' ').Append(CssSerializer.SerializeValue([argument], property: null));
                }
            }

            expanded = builder.Append(')').ToString();
        }

        return components.Count == 1 ? expanded : expanded + " " + CssSerializer.SerializeValue(components[1..], property: null);

        static List<string> Serialize(List<Syntax.CssComponentValue> values) => values.ConvertAll(static component => CssSerializer.SerializeValue([component], property: null));

        // The 1 to 4 values of the sides (top, right, bottom, left) or of the corners
        static string[]? ExpandFour(List<string> parts)
        {
            if (parts.Count is 0 or > 4)
                return null;

            var first = parts[0];
            var second = parts.Count > 1 ? parts[1] : first;
            return [first, second, parts.Count > 2 ? parts[2] : first, parts.Count > 3 ? parts[3] : second];
        }
    }

    /// <summary>Shortens a combined value like the computed value of the property (e.g. "10px 10px" is "10px" for border-top-left-radius).</summary>
    public static string Shorten(string property, string value)
    {
        switch (property)
        {
            case "border-top-left-radius" or "border-top-right-radius" or "border-bottom-right-radius" or "border-bottom-left-radius"
                or "border-start-start-radius" or "border-start-end-radius" or "border-end-start-radius" or "border-end-end-radius"
                or "border-spacing" or "text-decoration-inset":
            {
                var parts = GetComponents(value);
                return parts.Count == 2 && parts[0] == parts[1] ? parts[0] : value;
            }

            case "border-image-slice" or "border-image-width" or "border-image-outset" or "mask-border-slice" or "mask-border-width" or "mask-border-outset":
                return ShortenSides(value);

            case "font-style" when value == "oblique 0deg":
                return "normal";

            case "transform-origin" when value.EndsWith(" 0px", StringComparison.Ordinal):
                return value[..^4];

            case "offset-rotate" when value == "auto 0deg":
                return "auto";

            // (the shortest form of the computed value of the shape)
            case "clip-path" or "shape-outside" or "offset-path" when value.StartsWith("inset(", StringComparison.Ordinal) || value.StartsWith("polygon(", StringComparison.Ordinal):
                return CssPropertyRules.ComputeShapeProperty(property, value);

            default:
                return value;
        }
    }

    /// <summary>Returns the serialized components of a value (a calc() is a single component).</summary>
    private static List<string> GetComponents(string value) => CssGenericInterpolation.Parse(value).ConvertAll(component => CssSerializer.SerializeValue([component], property: null));

    private static int CountComponents(string value) => CssGenericInterpolation.Parse(value).Count;

    /// <summary>Expands the 1 to 4 values of the sides to 4 values (top, right, bottom, left); the fill keyword is kept last.</summary>
    private static string ExpandSides(string value)
    {
        var parts = new List<string>();
        var fill = false;
        foreach (var component in CssGenericInterpolation.Parse(value))
        {
            var text = CssSerializer.SerializeValue([component], property: null);
            if (text == "fill")
            {
                fill = true;
            }
            else
            {
                parts.Add(text);
            }
        }

        if (parts.Count is 0 or > 4)
            return value;

        var top = parts[0];
        var right = parts.Count > 1 ? parts[1] : top;
        var bottom = parts.Count > 2 ? parts[2] : top;
        var left = parts.Count > 3 ? parts[3] : right;
        return top + " " + right + " " + bottom + " " + left + (fill ? " fill" : "");
    }

    private static string ShortenSides(string value)
    {
        var parts = GetComponents(value);
        var fill = parts.Remove("fill");
        if (parts.Count == 4)
        {
            if (parts[3] == parts[1])
            {
                parts.RemoveAt(3);
                if (parts[2] == parts[0])
                {
                    parts.RemoveAt(2);
                    if (parts[1] == parts[0])
                    {
                        parts.RemoveAt(1);
                    }
                }
            }
        }

        return string.Join(' ', parts) + (fill ? " fill" : "");
    }
}
