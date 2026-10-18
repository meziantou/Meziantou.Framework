using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.Values;

// Basic shapes (https://drafts.csswg.org/css-shapes/#basic-shape-functions) of clip-path, shape-outside and offset-path, and
// the motion path properties (https://drafts.csswg.org/motion-1/)
internal static partial class CssPropertyRules
{
    private static readonly GrammarNode AngleGrammar = ValueGrammarParser.Parse("<angle>");

    private static string SerializeShapeComponent(CssComponentValue value, CssPropertyDefinition? property) => value is CssPreservedToken { Type: CssTokenType.Ident } ident
        ? InfraAscii.ToAsciiLowercase(ident.Token.Value)
        : CssSerializer.SerializeValue([value], property);

    /// <summary>A &lt;length-percentage&gt; (or a keyword), whose unitless zero is serialized as a length (0px).</summary>
    private static string SerializeLengthPercentage(CssComponentValue value, CssPropertyDefinition? property) => value is CssPreservedToken { Type: CssTokenType.Number } number && number.Token.NumericValue == 0
        ? "0px"
        : SerializeShapeComponent(value, property);

    /// <summary>A radius, which is not negative: the result of a math function is clamped to 0 in the computed value.</summary>
    private static string SerializeRadius(CssComponentValue value, CssPropertyDefinition? property, bool computed) => computed && value is CssPreservedToken { Type: CssTokenType.Dimension or CssTokenType.Percentage } numeric && numeric.Token.NumericValue < 0
        ? "0px"
        : SerializeLengthPercentage(value, property);

    private static bool IsZero(string value) => value is "0px" or "0%" or "0";

    /// <summary>The 4 values of the sides or corners of a box from 1 to 4 values (https://drafts.csswg.org/css-box-4/#margin-shorthand).</summary>
    private static string[] ExpandSides(List<string> values) => values.Count switch
    {
        1 => [values[0], values[0], values[0], values[0]],
        2 => [values[0], values[1], values[0], values[1]],
        3 => [values[0], values[1], values[2], values[1]],
        _ => [values[0], values[1], values[2], values[3]],
    };

    /// <summary>The shortest list of values that sets the 4 sides or corners of a box.</summary>
    private static string SerializeSides(string[] sides)
    {
        var count = 4;
        if (sides[3] == sides[1])
        {
            count = 3;
            if (sides[2] == sides[0])
            {
                count = 2;
                if (sides[1] == sides[0])
                {
                    count = 1;
                }
            }
        }

        return string.Join(' ', sides.Take(count));
    }

    /// <summary>
    /// The rounded corners of inset(), rect() and xywh() (https://drafts.csswg.org/css-shapes/#funcdef-basic-shape-inset):
    /// the shortest form of the &lt;'border-radius'&gt;, without the vertical radii when they are the horizontal radii, and
    /// omitted when all the radii are zero.
    /// </summary>
    private static string SerializeRound(List<CssComponentValue>? radii, CssPropertyDefinition? property, bool computed)
    {
        if (radii is null)
            return "";

        var slash = radii.FindIndex(value => value is CssPreservedToken token && token.Token.IsDelim('/'));
        var horizontal = ExpandSides([.. (slash < 0 ? radii : radii[..slash]).Select(value => SerializeRadius(value, property, computed))]);
        var vertical = slash < 0 ? horizontal : ExpandSides([.. radii[(slash + 1)..].Select(value => SerializeRadius(value, property, computed))]);
        if (horizontal.All(IsZero) && vertical.All(IsZero))
            return "";

        var result = " round " + SerializeSides(horizontal);
        return vertical.SequenceEqual(horizontal, StringComparer.Ordinal) ? result : result + " / " + SerializeSides(vertical);
    }

    /// <summary>Splits the values at a keyword (e.g. the round of inset()); the second part is null when the keyword is absent.</summary>
    private static (List<CssComponentValue> Before, List<CssComponentValue>? After) SplitAtKeyword(List<CssComponentValue> values, string keyword)
    {
        var index = values.FindIndex(value => IsIdent(value, keyword));
        return index < 0 ? (values, null) : (values[..index], values[(index + 1)..]);
    }

    /// <summary>
    /// A &lt;position&gt; (https://drafts.csswg.org/css-values-4/#position-serialization): the horizontal component first and
    /// a single value completed with center (e.g. top is center top, bottom 10% right 20% is right 20% bottom 10%). The computed
    /// value is a pair of offsets from the top left corner (e.g. right top is 100% 0%).
    /// </summary>
    private static string SerializePosition(List<CssComponentValue> values, bool computed)
    {
        var position = CssBackgrounds.ParsePosition(values) is { } parsed ? parsed.X + " " + parsed.Y : string.Join(' ', values.Select(value => SerializeLengthPercentage(value, property: null)));
        if (!computed)
            return position;

        // A zero length is removed from a sum (calc(75% + 0px) is 75%)
        var offsets = Cascade.CssValueComputer.SplitComponents(Cascade.CssValueComputer.ComputePositionLayer(position));
        for (var i = 0; i < offsets.Length; i++)
        {
            if (offsets[i].StartsWith("calc(", StringComparison.Ordinal) && ParseLengthPercentage(offsets[i]) is { HasPercentage: true, Pixels: 0 } sum)
            {
                offsets[i] = CssSerializer.SerializeNumber(sum.Percentage) + "%";
            }
        }

        return string.Join(' ', offsets);
    }

    /// <summary>The canonical &lt;position&gt; of a longhand (e.g. mask-position: top is center top); the keywords of the property are kept.</summary>
    private static string? CanonicalizePositionList(List<CssComponentValue> significant)
    {
        if (significant is [CssPreservedToken { Type: CssTokenType.Ident } keyword] && InfraAscii.ToAsciiLowercase(keyword.Token.Value) is "auto" or "normal" or "none")
            return null;

        // A single logical keyword of an axis is completed with center (x-start is x-start center)
        if (significant is [CssPreservedToken { Type: CssTokenType.Ident } single] && InfraAscii.ToAsciiLowercase(single.Token.Value) is "x-start" or "x-end" or "y-start" or "y-end")
        {
            var lower = InfraAscii.ToAsciiLowercase(single.Token.Value);
            return lower[0] == 'x' ? lower + " center" : "center " + lower;
        }

        // The other logical keywords (x-start, block-end...) keep their specified form
        if (significant.Exists(value => value is CssPreservedToken { Type: CssTokenType.Ident } ident && InfraAscii.ToAsciiLowercase(ident.Token.Value) is { } name
            && (name.StartsWith("x-", StringComparison.Ordinal) || name.StartsWith("y-", StringComparison.Ordinal) || name.StartsWith("block-", StringComparison.Ordinal) || name.StartsWith("inline-", StringComparison.Ordinal) || name is "start" or "end")))
            return null;

        return string.Join(", ", Cascade.CssValueComputer.SplitArguments(significant).Select(layer => SerializePosition(layer, computed: false)));
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Length-percentage arithmetic
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Parses a computed &lt;length-percentage&gt;: a percentage, a length in px, or a sum of both (calc(10% + 5px)). A zero
    /// length (0px) has no length term.
    /// </summary>
    private static (double Percentage, double Pixels, bool HasPercentage, bool HasPixels)? ParseLengthPercentage(string value)
    {
        var values = ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(value));
        var isCalc = values is [CssFunction function] && InfraAscii.EqualsIgnoreAsciiCase(function.Name, "calc");
        var terms = isCalc ? ValueMatcher.RemoveWhitespace(((CssFunction)values[0]).Value) : values;
        if (terms.Count % 2 == 0)
            return null;

        double percentage = 0;
        double pixels = 0;
        var hasPercentage = false;
        var hasPixels = false;
        var sign = 1;
        for (var i = 0; i < terms.Count; i++)
        {
            if (i % 2 == 1)
            {
                if (terms[i] is not CssPreservedToken { Type: CssTokenType.Delim } operation || !(operation.Token.IsDelim('+') || operation.Token.IsDelim('-')))
                    return null;

                sign = operation.Token.IsDelim('-') ? -1 : 1;
                continue;
            }

            switch (terms[i])
            {
                case CssPreservedToken { Type: CssTokenType.Percentage } term:
                    percentage += sign * term.Token.NumericValue;
                    hasPercentage = true;
                    break;
                case CssPreservedToken { Type: CssTokenType.Dimension } term when InfraAscii.EqualsIgnoreAsciiCase(term.Token.Value, "px"):
                    pixels += sign * term.Token.NumericValue;
                    hasPixels = isCalc || term.Token.NumericValue != 0;
                    break;
                case CssPreservedToken { Type: CssTokenType.Number } term when term.Token.NumericValue == 0:
                    break;
                default:
                    return null;
            }
        }

        return (percentage, pixels, hasPercentage, hasPixels);
    }

    /// <summary>
    /// Returns 100% minus the computed &lt;length-percentage&gt;s, simplified (e.g. 100% - 10% is 90%, 100% - 20px is
    /// calc(100% - 20px), 100% - calc(100% - 20px) is calc(0% + 20px)).
    /// </summary>
    internal static string SubtractFromFull(params string[] values)
    {
        double percentage = 100;
        double pixels = 0;
        var hasPixels = false;
        foreach (var value in values)
        {
            if (ParseLengthPercentage(value) is not { } term)
                return "calc(100% - " + string.Join(" - ", values) + ")";

            percentage -= term.Percentage;
            pixels -= term.Pixels;
            hasPixels |= term.HasPixels;
        }

        if (!hasPixels)
            return CssSerializer.SerializeNumber(percentage) + "%";

        return "calc(" + CssSerializer.SerializeNumber(percentage) + "% " + (pixels < 0 ? "- " + CssSerializer.SerializeNumber(-pixels) : "+ " + CssSerializer.SerializeNumber(pixels)) + "px)";
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Basic shapes
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The specified or computed value of clip-path, shape-outside and offset-path when it contains a basic shape
    /// (https://drafts.csswg.org/css-shapes/#basic-shape-serialization): the shape comes first, the default reference box
    /// (border-box, margin-box for shape-outside) is omitted, the arguments that have their default value are omitted, and
    /// the zero lengths have a unit. In the computed value, the positions are offsets from the top left corner, and rect()
    /// and xywh() are equivalent inset()s. Returns null when the value is not a shape.
    /// </summary>
    private static string? SerializeShapeValue(string propertyName, List<CssComponentValue> values, bool computed)
    {
        var property = CssPropertyRegistry.Get(propertyName);
        string? shape = null;
        string? box = null;
        foreach (var value in values)
        {
            if (shape is null && value is CssFunction function && SerializeShape(property, function, computed) is { } serialized)
            {
                shape = serialized;
            }
            else if (box is null && value is CssPreservedToken { Type: CssTokenType.Ident } ident && !ident.Token.IsIdent("none"))
            {
                box = InfraAscii.ToAsciiLowercase(ident.Token.Value);
            }
            else
            {
                return null;
            }
        }

        if (shape is null)
            return null;

        var defaultBox = propertyName == "shape-outside" ? "margin-box" : "border-box";
        return box is null || box == defaultBox ? shape : shape + " " + box;
    }

    /// <summary>Computes clip-path, shape-outside or offset-path from its generic computed value (see <see cref="SerializeShapeValue"/>).</summary>
    public static string ComputeShapeProperty(string propertyName, string value) => SerializeShapeValue(propertyName, ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(value)), computed: true) ?? value;

    /// <summary>
    /// The specified value of border-shape: its shapes are serialized as the basic shapes of the other properties
    /// (https://drafts.csswg.org/css-shapes/#basic-shape-serialization), the reference boxes are kept as specified. Returns
    /// null when the value has no shape (none).
    /// </summary>
    private static string? SerializeBorderShape(List<CssComponentValue> values)
    {
        var property = CssPropertyRegistry.Get("border-shape");
        var parts = new List<string>(values.Count);
        var hasShape = false;
        foreach (var value in values)
        {
            if (value is CssFunction function && SerializeShape(property, function, computed: false) is { } shape)
            {
                parts.Add(shape);
                hasShape = true;
            }
            else
            {
                parts.Add(CssSerializer.SerializeValue([value], property));
            }
        }

        return hasShape ? string.Join(' ', parts) : null;
    }

    /// <summary>
    /// The computed value of border-shape (https://drafts.csswg.org/css-borders-4/#border-shape): the default reference
    /// boxes are omitted (half-border-box for a single shape, border-box for the outer shape and padding-box for the inner
    /// shape), and two identical shapes are serialized as one.
    /// </summary>
    public static string ComputeBorderShape(string value)
    {
        var property = CssPropertyRegistry.Get("border-shape");
        var shapes = new List<(string Shape, string? Box)>();
        foreach (var component in ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(value)))
        {
            if (component is CssFunction function && SerializeShape(property, function, computed: true) is { } shape)
            {
                shapes.Add((shape, null));
            }
            else if (component is CssPreservedToken { Type: CssTokenType.Ident } ident && shapes.Count > 0 && shapes[^1].Box is null)
            {
                shapes[^1] = (shapes[^1].Shape, InfraAscii.ToAsciiLowercase(ident.Token.Value));
            }
            else
            {
                return value;
            }
        }

        if (shapes is [var outer, var inner])
        {
            var outerBox = outer.Box ?? "border-box";
            var innerBox = inner.Box ?? "padding-box";
            if (outer.Shape != inner.Shape || outerBox != innerBox)
                return Serialize(outer.Shape, outerBox, "border-box") + " " + Serialize(inner.Shape, innerBox, "padding-box");

            shapes = [(outer.Shape, outerBox)];
        }

        return shapes is [var single] ? Serialize(single.Shape, single.Box, "half-border-box") : value;

        static string Serialize(string shape, string? box, string defaultBox) => box is null || box == defaultBox ? shape : shape + " " + box;
    }

    private static string? SerializeShape(CssPropertyDefinition? property, CssFunction function, bool computed)
    {
        var arguments = ValueMatcher.RemoveWhitespace(function.Value);
        return InfraAscii.ToAsciiLowercase(function.Name) switch
        {
            "inset" => SerializeInset(arguments, property, computed),
            "rect" or "xywh" when computed => ComputeRectangle(InfraAscii.ToAsciiLowercase(function.Name), arguments, property),
            "rect" or "xywh" => SerializeRectangle(InfraAscii.ToAsciiLowercase(function.Name), arguments, property),
            "circle" or "ellipse" => SerializeEllipse(InfraAscii.ToAsciiLowercase(function.Name), arguments, property, computed),
            "polygon" => SerializePolygon(function, property),
            "ray" => SerializeRay(arguments, property, computed),
            "shape" => SerializeShapeFunction(function, property, computed),
            "path" => CssSerializer.SerializeValue([function], property),
            _ => null,
        };
    }

    /// <summary>inset(): the shortest form of the insets (https://drafts.csswg.org/css-shapes/#funcdef-basic-shape-inset).</summary>
    private static string SerializeInset(List<CssComponentValue> arguments, CssPropertyDefinition? property, bool computed)
    {
        var (insets, radii) = SplitAtKeyword(arguments, "round");
        return "inset(" + SerializeSides(ExpandSides([.. insets.Select(value => SerializeLengthPercentage(value, property))])) + SerializeRound(radii, property, computed) + ")";
    }

    private static string SerializeRectangle(string name, List<CssComponentValue> arguments, CssPropertyDefinition? property)
    {
        var (edges, radii) = SplitAtKeyword(arguments, "round");
        return name + "(" + string.Join(' ', edges.Select(value => SerializeLengthPercentage(value, property))) + SerializeRound(radii, property, computed: false) + ")";
    }

    /// <summary>
    /// https://drafts.csswg.org/css-shapes/#basic-shape-computed-values: rect() and xywh() compute to the equivalent inset()
    /// (the auto edges of rect() are the edges of the reference box).
    /// </summary>
    private static string ComputeRectangle(string name, List<CssComponentValue> arguments, CssPropertyDefinition? property)
    {
        var (edges, radii) = SplitAtKeyword(arguments, "round");
        var values = edges.Select(value => SerializeLengthPercentage(value, property)).ToList();
        if (values.Count != 4)
            return name + "(" + string.Join(' ', values) + SerializeRound(radii, property, computed: true) + ")";

        string[] insets;
        if (name == "rect")
        {
            insets =
            [
                values[0] == "auto" ? "0%" : values[0],
                values[1] == "auto" ? "0%" : SubtractFromFull(values[1]),
                values[2] == "auto" ? "0%" : SubtractFromFull(values[2]),
                values[3] == "auto" ? "0%" : values[3],
            ];
        }
        else
        {
            // xywh(x y width height)
            insets = [values[1], SubtractFromFull(values[0], values[2]), SubtractFromFull(values[1], values[3]), values[0]];
        }

        return "inset(" + SerializeSides(insets) + SerializeRound(radii, property, computed: true) + ")";
    }

    /// <summary>circle() and ellipse(): the default radius closest-side is omitted (https://drafts.csswg.org/css-shapes/#funcdef-basic-shape-circle).</summary>
    private static string SerializeEllipse(string name, List<CssComponentValue> arguments, CssPropertyDefinition? property, bool computed)
    {
        var (radii, position) = SplitAtKeyword(arguments, "at");
        var parts = radii.Select(value => SerializeRadius(value, property, computed)).ToList();
        if (parts.TrueForAll(radius => radius == "closest-side"))
        {
            parts.Clear();
        }

        if (position is not null)
        {
            parts.Add("at " + SerializePosition(position, computed));
        }

        return name + "(" + string.Join(' ', parts) + ")";
    }

    /// <summary>polygon(): the default fill rule nonzero and a zero rounding radius are omitted (https://drafts.csswg.org/css-shapes/#funcdef-basic-shape-polygon).</summary>
    private static string SerializePolygon(CssFunction function, CssPropertyDefinition? property)
    {
        var groups = Cascade.CssValueComputer.SplitArguments(function.Value);
        var parts = new List<string>(groups.Count);
        for (var i = 0; i < groups.Count; i++)
        {
            var group = groups[i];
            if (i == 0 && group is [CssPreservedToken { Type: CssTokenType.Ident }, ..])
            {
                var options = new List<string>(2);
                var (rule, round) = SplitAtKeyword(group, "round");
                if (rule is [var fillRule] && !IsIdent(fillRule, "nonzero"))
                {
                    options.Add(SerializeShapeComponent(fillRule, property));
                }

                if (round is [var radius] && SerializeLengthPercentage(radius, property) is var serializedRadius && !IsZero(serializedRadius))
                {
                    options.Add("round " + serializedRadius);
                }

                if (options.Count > 0)
                {
                    parts.Add(string.Join(' ', options));
                }

                continue;
            }

            parts.Add(string.Join(' ', group.Select(value => SerializeLengthPercentage(value, property))));
        }

        return "polygon(" + string.Join(", ", parts) + ")";
    }

    /// <summary>
    /// ray(): the angle, the size (without the default closest-side), contain and the position, in this order
    /// (https://drafts.csswg.org/motion-1/#ray-function).
    /// </summary>
    private static string SerializeRay(List<CssComponentValue> arguments, CssPropertyDefinition? property, bool computed)
    {
        string? angle = null;
        string? size = null;
        var contain = false;
        List<CssComponentValue>? position = null;
        foreach (var argument in arguments)
        {
            if (position is not null)
            {
                // The position ends at the angle or at a keyword of ray()
                var isRayKeyword = argument is CssPreservedToken { Type: CssTokenType.Ident } && InfraAscii.ToAsciiLowercase(((CssPreservedToken)argument).Token.Value) is "contain" or "sides" or "closest-side" or "closest-corner" or "farthest-side" or "farthest-corner";
                if (!isRayKeyword && !ValueMatcher.Matches(AngleGrammar, [argument]))
                {
                    position.Add(argument);
                    continue;
                }
            }

            if (IsIdent(argument, "at"))
            {
                position = [];
            }
            else if (IsIdent(argument, "contain"))
            {
                contain = true;
            }
            else if (argument is CssPreservedToken { Type: CssTokenType.Ident })
            {
                size = SerializeShapeComponent(argument, property);
            }
            else
            {
                angle = SerializeShapeComponent(argument, property);
            }
        }

        var parts = new List<string>(4);
        if (angle is not null)
        {
            parts.Add(angle);
        }

        if (size is not null and not "closest-side")
        {
            parts.Add(size);
        }

        if (contain)
        {
            parts.Add("contain");
        }

        if (position is not null)
        {
            parts.Add("at " + SerializePosition(position, computed));
        }

        return "ray(" + string.Join(' ', parts) + ")";
    }

    /// <summary>
    /// shape() (https://drafts.csswg.org/css-shapes-2/#shape-function): the default fill rule nonzero and the default arc
    /// options (small, ccw, rotate 0deg) are omitted, the other ones are in the order of the grammar (the sweep, the size, the
    /// rotation); the positions of the control points are canonical, and their default reference is omitted (<c>from origin</c>
    /// for a <c>to</c> command, <c>from start</c> for a <c>by</c> command); in the computed value, the end points and the
    /// control points are offsets from the top left corner, and so are the keywords of <c>hline</c> and <c>vline</c>.
    /// </summary>
    private static string SerializeShapeFunction(CssFunction function, CssPropertyDefinition? property, bool computed)
    {
        var groups = Cascade.CssValueComputer.SplitArguments(function.Value);
        var commands = new List<string>(groups.Count);
        for (var groupIndex = 0; groupIndex < groups.Count; groupIndex++)
        {
            var group = groups[groupIndex];
            var parts = new List<string>(group.Count);
            var isArc = group is [var first, ..] && IsIdent(first, "arc");
            var hasEndPoint = group is [var command, ..] && !IsIdent(command, "hline") && !IsIdent(command, "vline");
            var isAbsolute = group is [_, var mode, ..] && IsIdent(mode, "to");
            for (var i = 0; i < group.Count; i++)
            {
                var value = group[i];
                if (IsIdent(value, "nonzero"))
                    continue;

                if (isArc && IsIdent(value, "of"))
                {
                    // arc: of <radii> [ cw | ccw ]? [ large | small ]? [ rotate <angle> ]?, in any order
                    parts.Add("of");
                    string? sweep = null;
                    string? size = null;
                    string? rotation = null;
                    for (i++; i < group.Count; i++)
                    {
                        if (IsIdent(group[i], "cw"))
                        {
                            sweep = "cw";
                        }
                        else if (IsIdent(group[i], "large"))
                        {
                            size = "large";
                        }
                        else if (IsIdent(group[i], "rotate") && i + 1 < group.Count)
                        {
                            i++;
                            rotation = group[i] is CssPreservedToken { Type: CssTokenType.Number or CssTokenType.Dimension } angle && angle.Token.NumericValue == 0 ? null : "rotate " + SerializeLengthPercentage(group[i], property);
                        }
                        else if (!IsIdent(group[i], "ccw") && !IsIdent(group[i], "small"))
                        {
                            parts.Add(SerializeLengthPercentage(group[i], property));
                        }
                    }

                    foreach (var option in (ReadOnlySpan<string?>)[sweep, size, rotation])
                    {
                        if (option is not null)
                        {
                            parts.Add(option);
                        }
                    }

                    break;
                }

                if (hasEndPoint && IsIdent(value, "with"))
                {
                    // curve and smooth: with <control-point> [ / <control-point> ]?
                    parts.Add("with");
                    var start = i + 1;
                    for (var j = start; j <= group.Count; j++)
                    {
                        if (j < group.Count && group[j] is not CssPreservedToken { Type: CssTokenType.Delim, Token.Value: "/" })
                            continue;

                        if (start > i + 1)
                        {
                            parts.Add("/");
                        }

                        parts.Add(SerializeControlPoint(group[start..j], isAbsolute, property, computed));
                        start = j + 1;
                    }

                    break;
                }

                if (!hasEndPoint && computed && i == 2 && isAbsolute && value is CssPreservedToken { Type: CssTokenType.Ident } keyword)
                {
                    // hline and vline: the keywords of the axis are percentages
                    parts.Add(InfraAscii.ToAsciiLowercase(keyword.Token.Value) switch
                    {
                        "left" or "top" => "0%",
                        "center" => "50%",
                        "right" or "bottom" => "100%",
                        var other => other,
                    });
                    continue;
                }

                parts.Add(SerializeLengthPercentage(value, property));

                // The start point (from <position>) and the end point of a command (to <position>, which ends at a keyword of the command)
                if ((groupIndex == 0 && IsIdent(value, "from")) || (i == 1 && hasEndPoint && IsIdent(value, "to")))
                {
                    var end = i + 1;
                    while (end < group.Count && !(group[end] is CssPreservedToken { Type: CssTokenType.Ident } option && InfraAscii.ToAsciiLowercase(option.Token.Value) is "with" or "of" or "cw" or "ccw" or "large" or "small" or "rotate"))
                    {
                        end++;
                    }

                    if (end > i + 1)
                    {
                        parts.Add(SerializePosition(group[(i + 1)..end], computed));
                        i = end - 1;
                    }
                }
            }

            commands.Add(string.Join(' ', parts));
        }

        return "shape(" + string.Join(", ", commands) + ")";
    }

    /// <summary>
    /// A control point of a curve of shape() (https://drafts.csswg.org/css-shapes-2/#typedef-shape-control-point-anchor): a
    /// position, or a coordinate pair relative to the start of the command, to its end or to the origin of the reference box
    /// (<c>from start | end | origin</c>: the default is the origin for a <c>to</c> command and the start for a <c>by</c>
    /// command, and it is omitted).
    /// </summary>
    private static string SerializeControlPoint(List<CssComponentValue> values, bool isAbsolute, CssPropertyDefinition? property, bool computed)
    {
        if (values is [.., var from, CssPreservedToken { Type: CssTokenType.Ident } reference] && IsIdent(from, "from"))
        {
            var coordinates = string.Join(' ', values.GetRange(0, values.Count - 2).Select(value => SerializeLengthPercentage(value, property)));
            var anchor = InfraAscii.ToAsciiLowercase(reference.Token.Value);
            return anchor == (isAbsolute ? "origin" : "start") ? coordinates : coordinates + " from " + anchor;
        }

        // The keywords of a position are only valid for a command that ends at a position
        return isAbsolute ? SerializePosition(values, computed) : string.Join(' ', values.Select(value => SerializeLengthPercentage(value, property)));
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Motion path (https://drafts.csswg.org/motion-1/)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// https://drafts.csswg.org/motion-1/#offset-rotate-property: auto or reverse first, and a zero angle after them is
    /// omitted (auto 0deg is auto).
    /// </summary>
    private static string? CanonicalizeOffsetRotate(List<CssComponentValue> significant)
    {
        var property = CssPropertyRegistry.Get("offset-rotate");
        string? keyword = null;
        CssComponentValue? angle = null;
        foreach (var value in significant)
        {
            if (value is CssPreservedToken { Type: CssTokenType.Ident })
            {
                keyword = SerializeShapeComponent(value, property);
            }
            else
            {
                angle = value;
            }
        }

        if (angle is null)
            return keyword;

        var serializedAngle = SerializeShapeComponent(angle, property);
        if (keyword is null)
            return serializedAngle;

        return angle is CssPreservedToken { Type: CssTokenType.Dimension or CssTokenType.Number } zero && zero.Token.NumericValue == 0 ? keyword : keyword + " " + serializedAngle;
    }

    /// <summary>https://drafts.csswg.org/motion-1/#offset-rotate-property: reverse is auto 180deg (the angle is added to 180deg).</summary>
    public static string ComputeOffsetRotate(string value)
    {
        if (!value.StartsWith("reverse", StringComparison.Ordinal))
            return value;

        var parts = Cascade.CssValueComputer.SplitComponents(value);
        double angle = 0;
        if (parts.Length == 2 && !(parts[1].EndsWith("deg", StringComparison.Ordinal) && Cascade.CssValueComputer.TryParseNumber(parts[1][..^3], out angle)))
            return value;

        return "auto " + CssSerializer.SerializeNumber(angle + 180) + "deg";
    }
}
