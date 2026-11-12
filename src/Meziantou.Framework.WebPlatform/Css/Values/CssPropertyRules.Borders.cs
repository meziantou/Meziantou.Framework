using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.Values;

// Borders, shadows, outlines and user interface (https://drafts.csswg.org/css-backgrounds-3/, https://drafts.csswg.org/css-ui-4/)
internal static partial class CssPropertyRules
{
    /// <summary>
    /// The shadows of box-shadow (https://drafts.csswg.org/css-backgrounds-3/#box-shadow), in the canonical order of the
    /// browsers: the color, the lengths (the unitless zeros are lengths), then inset.
    /// </summary>
    private static string? CanonicalizeBoxShadow(List<CssComponentValue> significant)
    {
        if (significant is [CssPreservedToken { Type: CssTokenType.Ident } none] && none.Token.IsIdent("none"))
            return "none";

        return string.Join(", ", SplitAtCommas(significant).Select(shadow => string.Join(' ', OrderShadow(shadow.ConvertAll(value => value is CssPreservedToken { Type: CssTokenType.Number, Token.NumericValue: 0 } ? "0px" : CssSerializer.SerializeValue([value], property: null))))));
    }

    /// <summary>
    /// The computed value of box-shadow: each shadow has a color (currentcolor when omitted), four lengths (the omitted blur
    /// radius and spread distance are 0px, the negative blur radius is clamped) and inset when it is inner. The value is the
    /// generic computed value.
    /// </summary>
    public static string ComputeBoxShadow(string value)
    {
        if (value == "none")
            return value;

        var shadows = new List<string>();
        foreach (var shadow in SplitAtCommas(ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(value))))
        {
            var parts = OrderShadow(shadow.ConvertAll(component => component is CssPreservedToken { Type: CssTokenType.Number, Token.NumericValue: 0 } ? "0px" : CssSerializer.SerializeValue([component], property: null)));
            var inset = parts.Remove("inset");
            if (parts.Count == 0 || IsShadowLength(parts[0]))
            {
                parts.Insert(0, "currentcolor");
            }

            while (parts.Count < 5)
            {
                parts.Add("0px");
            }

            if (parts[3].StartsWith('-', StringComparison.Ordinal))
            {
                parts[3] = "0px";
            }

            if (inset)
            {
                parts.Add("inset");
            }

            shadows.Add(string.Join(' ', parts));
        }

        return string.Join(", ", shadows);
    }

    /// <summary>
    /// The computed value of box-shadow-offset (https://drafts.csswg.org/css-borders-4/#box-shadow-offset): a single length
    /// sets both offsets, so a pair of equal offsets is serialized as one length.
    /// </summary>
    public static string ComputeBoxShadowOffset(string value)
    {
        return string.Join(", ", SplitAtCommas(ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(value))).Select(item =>
        {
            var offsets = item.ConvertAll(component => CssSerializer.SerializeValue([component], property: null));
            return offsets is [var horizontal, var vertical] && horizontal == vertical ? horizontal : string.Join(' ', offsets);
        }));
    }

    private static List<string> OrderShadow(List<string> parts)
    {
        var lengths = parts.FindAll(IsShadowLength);
        var result = new List<string>(parts.Count);
        result.AddRange(parts.Where(part => part != "inset" && !IsShadowLength(part)));
        result.AddRange(lengths);
        if (parts.Contains("inset"))
        {
            result.Add("inset");
        }

        return result;
    }

    private static bool IsShadowLength(string part) => part.Length > 0 && (char.IsAsciiDigit(part[0]) || part[0] is '-' or '+' or '.' || CssCalc.IsMathFunction(part.Split('(')[0]));

    private static List<List<CssComponentValue>> SplitAtCommas(List<CssComponentValue> values)
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

    /// <summary>
    /// border-radius: the horizontal radii, then the vertical radii after a slash (https://drafts.csswg.org/css-backgrounds-3/#border-radius).
    /// The side shorthands (border-top-radius, border-block-start-radius...) have the radii of their two corners
    /// (https://drafts.csswg.org/css-borders-4/#corner-sizing).
    /// </summary>
    private static Dictionary<string, string>? ExpandBorderRadius(CssPropertyDefinition shorthand, List<CssComponentValue> significant)
    {
        var corners = CssPropertyRegistry.GetLonghandsRecursive(shorthand);
        if (ExpandRadii(significant, corners.Count) is not { } radii)
            return null;

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < corners.Count; i++)
        {
            result[corners[i]] = radii[i];
        }

        return result;
    }

    /// <summary>
    /// Returns the radius of each corner from the radii of a shorthand: the horizontal radii, then the vertical radii after a
    /// slash. The radius of a single corner is one or two lengths, or a horizontal and a vertical length separated by a slash
    /// (&lt;border-radius&gt;).
    /// </summary>
    private static string[]? ExpandRadii(List<CssComponentValue> values, int count)
    {
        var slash = values.FindIndex(value => value is CssPreservedToken { Type: CssTokenType.Delim } delim && delim.Token.IsDelim('/'));
        var horizontalValues = values[..(slash < 0 ? values.Count : slash)].ConvertAll(SerializeRadius);
        var verticalValues = slash < 0 ? null : values[(slash + 1)..].ConvertAll(SerializeRadius);
        if (count == 1)
        {
            if (verticalValues is null && horizontalValues.Count == 2)
            {
                verticalValues = [horizontalValues[1]];
                horizontalValues.RemoveAt(1);
            }

            if (horizontalValues.Count != 1 || verticalValues is not (null or { Count: 1 }))
                return null;
        }

        var horizontal = ExpandCornerValues(horizontalValues, count);
        var vertical = verticalValues is null ? horizontal : ExpandCornerValues(verticalValues, count);
        if (horizontal is null || vertical is null)
            return null;

        var result = new string[count];
        for (var i = 0; i < count; i++)
        {
            result[i] = horizontal[i] == vertical[i] ? horizontal[i] : horizontal[i] + " " + vertical[i];
        }

        return result;

        static string SerializeRadius(CssComponentValue value) => value is CssPreservedToken { Type: CssTokenType.Number, Token.NumericValue: 0 } ? "0px" : CssSerializer.SerializeValue([value], property: null);
    }

    /// <summary>
    /// Serializes the radii of corners as border-radius: the horizontal radii, then the vertical radii after a slash when they
    /// differ. The radius of a single corner is its value.
    /// </summary>
    private static string? SerializeRadii(string[] radii)
    {
        if (radii.Length == 1)
            return radii[0];

        var horizontal = new string[radii.Length];
        var vertical = new string[radii.Length];
        for (var i = 0; i < radii.Length; i++)
        {
            var parts = Cascade.CssValueComputer.SplitComponents(radii[i]);
            if (parts.Length is < 1 or > 2)
                return null;

            horizontal[i] = parts[0];
            vertical[i] = parts.Length == 2 ? parts[1] : parts[0];
        }

        var h = SerializeCornerValues(horizontal);
        var v = SerializeCornerValues(vertical);
        return h == v ? h : h + " / " + v;
    }

    /// <summary>
    /// The corner shorthands (https://drafts.csswg.org/css-borders-4/#corner-shorthands): <c>corner</c> is
    /// <c>&lt;'corner-shape'&gt; || &lt;'border-radius'&gt;</c>, <c>corner-top</c> is
    /// <c>&lt;'corner-top-shape'&gt; || &lt;'border-top-radius'&gt;</c> and <c>corner-top-left</c> is
    /// <c>&lt;'corner-top-left-shape'&gt; || &lt;'border-top-left-radius'&gt;</c>: the omitted component is reset to its
    /// initial value (no radius, round). The grammar ensures that the shapes and the radii are not interleaved.
    /// </summary>
    private static Dictionary<string, string>? ExpandCorner(CssPropertyDefinition shorthand, List<CssComponentValue> significant)
    {
        var (radiusLonghands, shapeLonghands) = GetCornerLonghands(shorthand);
        var count = radiusLonghands.Count;
        if (shapeLonghands.Count != count)
            return null;

        var shapes = new List<string>(count);
        var radii = new List<CssComponentValue>(significant.Count);
        foreach (var value in significant)
        {
            switch (value)
            {
                case CssPreservedToken { Type: CssTokenType.Ident } ident:
                    shapes.Add(InfraAscii.ToAsciiLowercase(ident.Token.Value));
                    break;

                case CssFunction function when InfraAscii.EqualsIgnoreAsciiCase(function.Name, "superellipse"):
                    shapes.Add(CssSerializer.SerializeValue([value], property: null));
                    break;

                default:
                    radii.Add(value);
                    break;
            }
        }

        var cornerShapes = shapes.Count == 0 ? [.. Enumerable.Repeat("round", count)] : ExpandCornerValues(shapes, count);
        var cornerRadii = radii.Count == 0 ? [.. Enumerable.Repeat("0px", count)] : ExpandRadii(radii, count);
        if (cornerShapes is null || cornerRadii is null)
            return null;

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < count; i++)
        {
            result[radiusLonghands[i]] = cornerRadii[i];
            result[shapeLonghands[i]] = cornerShapes[i];
        }

        return result;
    }

    /// <summary>
    /// Returns the value of each corner from the values of a corner shorthand: the four corners take the values as the sides
    /// of margin (top left, top right, bottom right, bottom left), the two corners of a side take one or two values.
    /// </summary>
    private static string[]? ExpandCornerValues(List<string> values, int count)
    {
        if (values.Count < 1 || values.Count > count)
            return null;

        if (count == 1)
            return [values[0]];

        if (count == 2)
            return [values[0], values.Count > 1 ? values[1] : values[0]];

        var top = values[0];
        var right = values.Count > 1 ? values[1] : top;
        var bottom = values.Count > 2 ? values[2] : top;
        var left = values.Count > 3 ? values[3] : right;
        return [top, right, bottom, left];
    }

    /// <summary>
    /// Serializes a corner shorthand: the shapes first, omitted when every corner is round, then the radii (as border-radius),
    /// omitted when no corner has a radius (0px) and a shape is not round.
    /// </summary>
    private static string? SerializeCorner(CssPropertyDefinition shorthand, Dictionary<string, string> lookup)
    {
        var (radiusLonghands, shapeLonghands) = GetCornerLonghands(shorthand);
        var count = radiusLonghands.Count;
        if (shapeLonghands.Count != count)
            return null;

        var radii = new string[count];
        var shapes = new string[count];
        for (var i = 0; i < count; i++)
        {
            if (!lookup.TryGetValue(radiusLonghands[i], out var radius) || !lookup.TryGetValue(shapeLonghands[i], out var shape))
                return null;

            radii[i] = radius;
            shapes[i] = shape;
        }

        if (SerializeRadii(radii) is not { } radiiValue)
            return null;

        if (Array.TrueForAll(shapes, shape => shape is "round" or "superellipse(1)"))
            return radiiValue;

        var shapesValue = SerializeCornerValues(shapes);
        return Array.TrueForAll(radii, radius => radius is "0px" or "0") ? shapesValue : shapesValue + " " + radiiValue;
    }

    /// <summary>
    /// Returns the values of the longhands of a corner shorthand where the components that its serialization omits have the
    /// values that its expansion sets back: the shapes are round when they all are (superellipse(1) when computed), and the
    /// radii are 0px when they all are zero (0 when specified). Returns the values when the property is not a corner shorthand.
    /// </summary>
    public static Dictionary<string, string> GetOmittedCornerLonghands(CssPropertyDefinition shorthand, Dictionary<string, string> lookup)
    {
        if (!ShorthandRules.TryGetValue(shorthand.Name, out var rules) || rules.Expand != ExpandCorner)
            return lookup;

        var (radiusLonghands, shapeLonghands) = GetCornerLonghands(shorthand);
        var allRound = shapeLonghands.TrueForAll(name => lookup.TryGetValue(name, out var shape) && shape is "round" or "superellipse(1)");
        var allZero = radiusLonghands.TrueForAll(name => lookup.TryGetValue(name, out var radius) && radius is "0px" or "0");
        if (!allRound && !allZero)
            return lookup;

        var result = new Dictionary<string, string>(lookup, StringComparer.Ordinal);
        if (allRound)
        {
            foreach (var name in shapeLonghands)
            {
                result[name] = "round";
            }
        }

        if (allZero)
        {
            foreach (var name in radiusLonghands)
            {
                result[name] = "0px";
            }
        }

        return result;
    }

    /// <summary>The shortest list of values of the corners (the reverse of <see cref="ExpandCornerValues"/>).</summary>
    private static string SerializeCornerValues(string[] values)
    {
        if (values.Length == 1)
            return values[0];

        if (values.Length == 2)
            return values[0] == values[1] ? values[0] : values[0] + " " + values[1];

        if (values[1] == values[3])
        {
            if (values[0] == values[2])
                return values[0] == values[1] ? values[0] : values[0] + " " + values[1];

            return values[0] + " " + values[1] + " " + values[2];
        }

        return string.Join(' ', values);
    }

    /// <summary>Returns the radius longhands and the shape longhands of a corner shorthand, in the order of its corners.</summary>
    private static (List<string> Radii, List<string> Shapes) GetCornerLonghands(CssPropertyDefinition shorthand)
    {
        var radii = new List<string>(4);
        var shapes = new List<string>(4);
        foreach (var longhand in CssPropertyRegistry.GetLonghandsRecursive(shorthand))
        {
            (longhand.EndsWith("-shape", StringComparison.Ordinal) ? shapes : radii).Add(longhand);
        }

        return (radii, shapes);
    }

    private static string? SerializeBorderRadius(CssPropertyDefinition shorthand, Dictionary<string, string> lookup)
    {
        var corners = CssPropertyRegistry.GetLonghandsRecursive(shorthand);
        var radii = new string[corners.Count];
        for (var i = 0; i < corners.Count; i++)
        {
            if (!lookup.TryGetValue(corners[i], out var corner))
                return null;

            radii[i] = corner;
        }

        return SerializeRadii(radii);
    }

    /// <summary>will-change (https://drafts.csswg.org/css-will-change/#will-change): the identifiers of the list cannot be auto, will-change, none, all or a CSS-wide keyword.</summary>
    private static bool IsValidWillChange(CssPropertyDefinition property, List<CssComponentValue> significant)
    {
        foreach (var value in significant)
        {
            if (value is CssPreservedToken { Type: CssTokenType.Ident } ident && InfraAscii.ToAsciiLowercase(ident.Token.Value) is var name
                && (name is "will-change" or "none" or "all" || (name == "auto" && significant.Count > 1)))
                return false;
        }

        return true;
    }

    /// <summary>
    /// cursor (https://drafts.csswg.org/css-ui-4/#cursor): each image is a url() or an image-set() (or a light-dark() of
    /// them), not a gradient, followed by no hotspot or two numbers, then a comma; the list ends with a keyword.
    /// </summary>
    private static bool IsValidCursor(CssPropertyDefinition property, List<CssComponentValue> significant)
    {
        var items = SplitAtCommas(significant);
        if (items[^1] is not [CssPreservedToken { Type: CssTokenType.Ident }])
            return false;

        foreach (var item in items.Take(items.Count - 1))
        {
            if (item.Count is not (1 or 3) || !IsCursorImage(item[0]) || item.Skip(1).Any(hotspot => hotspot is not (CssPreservedToken { Type: CssTokenType.Number } or CssFunction)))
                return false;
        }

        return true;

        static bool IsCursorImage(CssComponentValue value) => value switch
        {
            CssPreservedToken { Type: CssTokenType.Url } => true,
            CssFunction function => InfraAscii.ToAsciiLowercase(function.Name) switch
            {
                "url" or "src" or "image-set" or "-webkit-image-set" => true,
                "light-dark" => SplitAtCommas(function.Value) is [[var light], [var dark]] && IsCursorImage(light) && IsCursorImage(dark),
                _ => false,
            },
            _ => false,
        };
    }

    /// <summary>The computed value of caret-color (https://drafts.csswg.org/css-ui-4/#caret-color): the second value is omitted when it is auto.</summary>
    public static string ComputeCaretColor(string value)
    {
        var parts = Cascade.CssValueComputer.SplitComponents(value);
        if (parts.Length == 2 && parts[1] == "auto")
        {
            parts = [parts[0]];
        }

        return string.Join(' ', parts);
    }

    /// <summary>
    /// The computed value of the corner-*-shape longhands (https://drafts.csswg.org/css-borders-4/#corner-shape-value): the
    /// keywords compute to their equivalent superellipse().
    /// </summary>
    public static string ComputeCornerShape(string value) => value switch
    {
        "round" => "superellipse(1)",
        "scoop" => "superellipse(-1)",
        "bevel" => "superellipse(0)",
        "notch" => "superellipse(-infinity)",
        "square" => "superellipse(infinity)",
        "squircle" => "superellipse(2)",
        _ => value,
    };

    /// <summary>cursor: the strings of image-set() are URLs (https://drafts.csswg.org/css-images-4/#image-set-notation).</summary>
    private static string? CanonicalizeCursor(List<CssComponentValue> significant)
    {
        var builder = new System.Text.StringBuilder();
        var first = true;
        foreach (var value in significant)
        {
            if (value is CssPreservedToken { Type: CssTokenType.Comma })
            {
                builder.Append(", ");
                first = true;
                continue;
            }

            if (!first)
            {
                builder.Append(' ');
            }

            first = false;
            builder.Append(SerializeCursorComponent(value, inImageSet: false));
        }

        return builder.ToString();

        static string SerializeCursorComponent(CssComponentValue value, bool inImageSet)
        {
            if (inImageSet && value is CssPreservedToken { Type: CssTokenType.String } url)
                return "url(" + CssSerializer.SerializeValue([value], property: null) + ")";

            if (value is not CssFunction function || InfraAscii.ToAsciiLowercase(function.Name) is not ("image-set" or "light-dark" or "-webkit-image-set") and var name)
                return CssSerializer.SerializeValue([value], property: null);

            var isImageSet = InfraAscii.ToAsciiLowercase(function.Name) != "light-dark";
            var arguments = SplitAtCommas(function.Value).Select(argument => string.Join(' ', argument.Select(component => SerializeCursorComponent(component, isImageSet))));
            return InfraAscii.ToAsciiLowercase(function.Name) + "(" + string.Join(", ", arguments) + ")";
        }
    }
}

// border-image (https://drafts.csswg.org/css-backgrounds-3/#border-image)
internal static partial class CssPropertyRules
{
    private static readonly (string Name, string Initial)[] BorderImageLonghands =
    [
        ("border-image-source", "none"),
        ("border-image-slice", "100%"),
        ("border-image-width", "1"),
        ("border-image-outset", "0"),
        ("border-image-repeat", "stretch"),
    ];

    /// <summary>
    /// border-image: the source, the slices followed by the widths and the outsets after slashes, and the repeat keywords,
    /// in any order (the slashes follow the slices).
    /// </summary>
    private static Dictionary<string, string>? ExpandBorderImage(CssPropertyDefinition shorthand, List<CssComponentValue> significant)
    {
        var parts = new Dictionary<string, List<CssComponentValue>>(StringComparer.Ordinal);
        var i = 0;
        while (i < significant.Count)
        {
            var value = significant[i];
            if (IsRepeatKeyword(value))
            {
                if (!parts.TryAdd("border-image-repeat", []))
                    return null;

                while (i < significant.Count && IsRepeatKeyword(significant[i]))
                {
                    parts["border-image-repeat"].Add(significant[i++]);
                }
            }
            else if (value is CssPreservedToken { Type: CssTokenType.Number or CssTokenType.Percentage } || IsIdent(value, "fill") || (value is CssFunction function && CssCalc.IsMathFunction(function.Name)))
            {
                if (!parts.TryAdd("border-image-slice", []))
                    return null;

                while (i < significant.Count && significant[i] is not CssPreservedToken { Type: CssTokenType.Delim } && !IsRepeatKeyword(significant[i]) && !IsImage(significant[i]))
                {
                    parts["border-image-slice"].Add(significant[i++]);
                }

                // / width, / / outset, / width / outset
                for (var slash = 0; slash < 2 && i < significant.Count && significant[i] is CssPreservedToken { Type: CssTokenType.Delim } delim && delim.Token.IsDelim('/'); slash++)
                {
                    i++;
                    var values = new List<CssComponentValue>();
                    while (i < significant.Count && significant[i] is not CssPreservedToken { Type: CssTokenType.Delim } && !IsRepeatKeyword(significant[i]) && !IsImage(significant[i]))
                    {
                        values.Add(significant[i++]);
                    }

                    if (values.Count > 0)
                    {
                        parts[slash == 0 ? "border-image-width" : "border-image-outset"] = values;
                    }
                    else if (slash == 1 || i >= significant.Count || significant[i] is not CssPreservedToken { Type: CssTokenType.Delim })
                    {
                        // An empty value is only allowed for the width, followed by the outset
                        return null;
                    }
                }
            }
            else if (!parts.TryAdd("border-image-source", [significant[i++]]))
            {
                return null;
            }
        }

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, initial) in BorderImageLonghands)
        {
            if (!parts.TryGetValue(name, out var values))
            {
                result[name] = initial;
            }
            else if (CssShorthands.ParseValue(name, CssShorthands.InsertSpaces(values), originalText: null) is [{ ShorthandName: null } longhand])
            {
                result[name] = longhand.Value;
            }
            else
            {
                return null;
            }
        }

        return result;

        static bool IsRepeatKeyword(CssComponentValue value) => value is CssPreservedToken { Type: CssTokenType.Ident } ident && InfraAscii.ToAsciiLowercase(ident.Token.Value) is "stretch" or "repeat" or "round" or "space";

        static bool IsImage(CssComponentValue value) => value is CssPreservedToken { Type: CssTokenType.Url } || IsIdent(value, "none") || (value is CssFunction function && !CssCalc.IsMathFunction(function.Name));
    }

    private static string? SerializeBorderImage(CssPropertyDefinition shorthand, Dictionary<string, string> lookup)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, _) in BorderImageLonghands)
        {
            if (!lookup.TryGetValue(name, out var value))
                return null;

            values[name] = value;
        }

        var parts = new List<string>();
        if (values["border-image-source"] != "none")
        {
            parts.Add(values["border-image-source"]);
        }

        var hasWidth = values["border-image-width"] != "1";
        var hasOutset = values["border-image-outset"] is not ("0" or "0px");
        if (values["border-image-slice"] != "100%" || hasWidth || hasOutset)
        {
            var slice = values["border-image-slice"];
            if (hasWidth || hasOutset)
            {
                slice += " / " + (hasWidth ? values["border-image-width"] + " " : "");
                slice += hasOutset ? "/ " + values["border-image-outset"] : "";
            }

            parts.Add(slice.TrimEnd());
        }

        if (values["border-image-repeat"] != "stretch")
        {
            parts.Add(values["border-image-repeat"]);
        }

        return parts.Count == 0 ? "none" : string.Join(' ', parts);
    }

    /// <summary>border-image-slice, border-image-width and border-image-outset: the sides in their shortest form (1 1 1 1 is 1), fill last.</summary>
    private static string? CanonicalizeBorderImageSides(List<CssComponentValue> significant)
    {
        var fill = significant.Exists(value => IsIdent(value, "fill"));
        var sides = significant.Where(value => !IsIdent(value, "fill")).Select(value => CssSerializer.SerializeValue([value], property: null)).ToList();
        if (sides.Count is < 1 or > 4)
            return null;

        var top = sides[0];
        var right = sides.Count > 1 ? sides[1] : top;
        var bottom = sides.Count > 2 ? sides[2] : top;
        var left = sides.Count > 3 ? sides[3] : right;
        string result;
        if (right == left)
        {
            result = top == bottom ? (top == right ? top : top + " " + right) : top + " " + right + " " + bottom;
        }
        else
        {
            result = string.Join(' ', top, right, bottom, left);
        }

        return fill ? result + " fill" : result;
    }

    /// <summary>border-image-repeat: the second keyword is omitted when it is equal to the first one.</summary>
    private static string? CanonicalizeBorderImageRepeat(List<CssComponentValue> significant)
    {
        var keywords = significant.ConvertAll(value => InfraAscii.ToAsciiLowercase(CssSerializer.SerializeValue([value], property: null)));
        return keywords is [var horizontal, var vertical] && horizontal == vertical ? horizontal : string.Join(' ', keywords);
    }
}
