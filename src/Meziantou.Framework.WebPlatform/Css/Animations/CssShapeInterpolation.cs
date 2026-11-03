using System.Globalization;
using System.Text;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Svg;

namespace Meziantou.Framework.WebPlatform.Css.Animations;

/// <summary>
/// The interpolation of the shape() function (https://drafts.csswg.org/css-shapes-2/#interpolating-shape) that the generic
/// interpolation of the values of the shape functions does not cover: a path() interpolated with a shape() is converted to the
/// equivalent shape() (its relative commands are <c>by</c> commands), and the options of the arcs are interpolated (the
/// direction and the size of an arc are booleans, set when their interpolated value is greater than 0, as the flags of the
/// arcs of path(); the rotation is an angle).
/// </summary>
internal static class CssShapeInterpolation
{
    private const string SweepFunction = "--sweep";
    private const string SizeFunction = "--size";

    /// <summary>Whether two computed values are a shape() and a path(), or two shape() with arcs.</summary>
    public static bool Applies(string a, string b)
    {
        var shapeA = IsShape(a);
        var shapeB = IsShape(b);
        if (shapeA && shapeB)
            return a.Contains("arc ", StringComparison.Ordinal) || b.Contains("arc ", StringComparison.Ordinal);

        return (shapeA && IsPath(b)) || (shapeB && IsPath(a));
    }

    /// <summary>Returns the equivalent of a computed shape() or path() whose arcs have all their options (see <see cref="Restore"/>).</summary>
    public static string? Normalize(string value)
    {
        var values = CssGenericInterpolation.Parse(value);
        if (values is not [CssFunction function, .. var rest])
            return null;

        string? shape;
        if (InfraAscii.EqualsIgnoreAsciiCase(function.Name, "path"))
        {
            shape = CssPath.TryParse(function, out var fillRule, out var commands) ? ConvertPath(fillRule, commands) : null;
        }
        else if (InfraAscii.EqualsIgnoreAsciiCase(function.Name, "shape"))
        {
            shape = NormalizeShape(function);
        }
        else
        {
            return null;
        }

        if (shape is null)
            return null;

        return rest.Count == 0 ? shape : shape + " " + Join(rest);
    }

    /// <summary>Restores the options of the arcs of an interpolated shape() normalized by <see cref="Normalize"/>, and computes it.</summary>
    public static string Restore(string property, string value)
    {
        var values = CssGenericInterpolation.Parse(value);
        var builder = new StringBuilder();
        for (var i = 0; i < values.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(' ');
            }

            builder.Append(values[i] is CssFunction { Name: "shape" } shape ? RestoreShape(shape) : CssSerializer.SerializeValue([values[i]], property: null));
        }

        return CssPropertyRules.ComputeShapeProperty(property, builder.ToString());
    }

    private static bool IsShape(string value) => value.StartsWith("shape(", StringComparison.Ordinal);

    private static bool IsPath(string value) => value.StartsWith("path(", StringComparison.Ordinal);

    /// <summary>Converts the commands of a path() to the equivalent shape() (https://drafts.csswg.org/css-shapes-2/#interpolating-shape).</summary>
    private static string ConvertPath(string? fillRule, List<SvgPathCommand> commands)
    {
        var builder = new StringBuilder("shape(");
        if (fillRule is "evenodd")
        {
            builder.Append("evenodd ");
        }

        for (var i = 0; i < commands.Count; i++)
        {
            var command = commands[i];
            var values = command.Values;
            var mode = char.IsLower(command.Type) ? "by " : "to ";
            if (i == 0)
            {
                if (char.ToUpperInvariant(command.Type) != 'M')
                    return builder.Append("from 0px 0px").ToString();

                builder.Append("from ").Append(Length(values[0])).Append(' ').Append(Length(values[1]));
                continue;
            }

            builder.Append(", ");
            switch (char.ToUpperInvariant(command.Type))
            {
                case 'M':
                    builder.Append("move ").Append(mode).Append(Point(values, 0));
                    break;

                case 'L':
                    builder.Append("line ").Append(mode).Append(Point(values, 0));
                    break;

                case 'H':
                    builder.Append("hline ").Append(mode).Append(Length(values[0]));
                    break;

                case 'V':
                    builder.Append("vline ").Append(mode).Append(Length(values[0]));
                    break;

                case 'C':
                    builder.Append("curve ").Append(mode).Append(Point(values, 4)).Append(" with ").Append(Point(values, 0)).Append(" / ").Append(Point(values, 2));
                    break;

                case 'Q':
                    builder.Append("curve ").Append(mode).Append(Point(values, 2)).Append(" with ").Append(Point(values, 0));
                    break;

                case 'S':
                    builder.Append("smooth ").Append(mode).Append(Point(values, 2)).Append(" with ").Append(Point(values, 0));
                    break;

                case 'T':
                    builder.Append("smooth ").Append(mode).Append(Point(values, 0));
                    break;

                case 'A':
                    builder.Append("arc ").Append(mode).Append(Point(values, 5)).Append(" of ").Append(Length(values[0])).Append(' ').Append(Length(values[1]))
                        .Append(" rotate ").Append(Number(values[2])).Append("deg ")
                        .Append(Flag(SweepFunction, values[4] != 0)).Append(' ').Append(Flag(SizeFunction, values[3] != 0));
                    break;

                default:
                    builder.Append("close");
                    break;
            }
        }

        return builder.Append(')').ToString();

        static string Point(float[] values, int index) => Length(values[index]) + " " + Length(values[index + 1]);
    }

    /// <summary>Writes the arcs of a shape() with all their options: two radii, a rotation, and the flags as numbers.</summary>
    private static string? NormalizeShape(CssFunction function)
    {
        var groups = Cascade.CssValueComputer.SplitArguments(function.Value);
        var commands = new List<string>(groups.Count);
        foreach (var group in groups)
        {
            if (group is not [var first, ..] || !IsIdent(first, "arc"))
            {
                commands.Add(Join(group));
                continue;
            }

            var of = group.FindIndex(value => IsIdent(value, "of"));
            if (of < 0)
                return null;

            var radii = new List<CssComponentValue>(2);
            var sweep = false;
            var large = false;
            string rotation = "0deg";
            for (var i = of + 1; i < group.Count; i++)
            {
                var value = group[i];
                if (IsIdent(value, "cw"))
                {
                    sweep = true;
                }
                else if (IsIdent(value, "large"))
                {
                    large = true;
                }
                else if (IsIdent(value, "rotate") && i + 1 < group.Count)
                {
                    i++;
                    rotation = CssSerializer.SerializeValue([group[i]], property: null);
                }
                else if (value is not CssPreservedToken { Type: CssTokenType.Ident })
                {
                    radii.Add(value);
                }
            }

            if (radii.Count is 0 or > 2)
                return null;

            commands.Add(Join(group[..(of + 1)]) + " " + Join(radii)
                + " rotate " + rotation + " " + Flag(SweepFunction, sweep) + " " + Flag(SizeFunction, large));
        }

        return "shape(" + string.Join(", ", commands) + ")";
    }

    /// <summary>
    /// Replaces the flags of the arcs of an interpolated shape() by their keywords (cw, large) when their value is greater than 0.
    /// </summary>
    private static string RestoreShape(CssFunction function)
    {
        var groups = Cascade.CssValueComputer.SplitArguments(function.Value);
        var commands = new List<string>(groups.Count);
        foreach (var group in groups)
        {
            var parts = new List<string>(group.Count);
            foreach (var value in group)
            {
                if (value is CssFunction { Name: SweepFunction or SizeFunction } flag)
                {
                    if (CssGenericInterpolation.Parse(CssSerializer.SerializeValue(flag.Value, property: null)) is [var number] && CssGenericInterpolation.TryGetNumber(number, out var x, out _) && x > 0)
                    {
                        parts.Add(flag.Name == SweepFunction ? "cw" : "large");
                    }

                    continue;
                }

                parts.Add(CssSerializer.SerializeValue([value], property: null));
            }

            commands.Add(string.Join(' ', parts));
        }

        return "shape(" + string.Join(", ", commands) + ")";
    }

    /// <summary>
    /// Returns two normalized shapes whose arcs have the same number of radii: an arc with one radius interpolated with an
    /// arc with two radii gets two radii (the single radius is both).
    /// </summary>
    public static (string A, string B) MatchRadii(string a, string b)
    {
        if (CssGenericInterpolation.Parse(a) is not [CssFunction { Name: "shape" } shapeA, ..] || CssGenericInterpolation.Parse(b) is not [CssFunction { Name: "shape" } shapeB, ..])
            return (a, b);

        var groupsA = Cascade.CssValueComputer.SplitArguments(shapeA.Value);
        var groupsB = Cascade.CssValueComputer.SplitArguments(shapeB.Value);
        if (groupsA.Count != groupsB.Count)
            return (a, b);

        var changed = false;
        for (var i = 0; i < groupsA.Count; i++)
        {
            if (groupsA[i] is not [var first, ..] || !IsIdent(first, "arc") || groupsB[i] is not [var other, ..] || !IsIdent(other, "arc"))
                continue;

            var countA = CountRadii(groupsA[i]);
            var countB = CountRadii(groupsB[i]);
            if (countA == 1 && countB == 2)
            {
                groupsA[i] = DuplicateRadius(groupsA[i]);
                changed = true;
            }
            else if (countA == 2 && countB == 1)
            {
                groupsB[i] = DuplicateRadius(groupsB[i]);
                changed = true;
            }
        }

        if (!changed)
            return (a, b);

        return (Replace(a, groupsA), Replace(b, groupsB));

        static int CountRadii(List<CssComponentValue> group)
        {
            var of = group.FindIndex(value => IsIdent(value, "of"));
            var count = 0;
            for (var i = of + 1; of >= 0 && i < group.Count && group[i] is not CssPreservedToken { Type: CssTokenType.Ident }; i++)
            {
                count++;
            }

            return count;
        }

        static List<CssComponentValue> DuplicateRadius(List<CssComponentValue> group)
        {
            var of = group.FindIndex(value => IsIdent(value, "of"));
            var result = new List<CssComponentValue>(group);
            result.Insert(of + 2, group[of + 1]);
            return result;
        }

        static string Replace(string value, List<List<CssComponentValue>> groups)
        {
            var rest = CssGenericInterpolation.Parse(value) is [_, .. var others] && others.Count > 0 ? " " + Join(others) : "";
            var commands = new List<string>(groups.Count);
            foreach (var group in groups)
            {
                commands.Add(Join(group));
            }

            return "shape(" + string.Join(", ", commands) + ")" + rest;
        }
    }

    private static bool IsIdent(CssComponentValue value, string name) => value is CssPreservedToken { Type: CssTokenType.Ident } ident && InfraAscii.EqualsIgnoreAsciiCase(ident.Token.Value, name);

    private static string Flag(string name, bool value) => name + "(" + (value ? "1" : "0") + ")";

    private static string Length(float value) => Number(value) + "px";

    // The shortest representation of the float (0.1f is 0.1, not 0.100000001)
    private static string Number(float value) => CssSerializer.SerializeNumber(double.Parse(value.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture));

    /// <summary>Serializes component values separated by a space (the whitespace was removed when they were split).</summary>
    private static string Join(IReadOnlyList<CssComponentValue> values)
    {
        var builder = new StringBuilder();
        foreach (var value in values)
        {
            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(CssSerializer.SerializeValue([value], property: null));
        }

        return builder.ToString();
    }
}
