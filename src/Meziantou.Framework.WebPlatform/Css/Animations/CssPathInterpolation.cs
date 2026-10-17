using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Svg;

namespace Meziantou.Framework.WebPlatform.Css.Animations;

/// <summary>
/// The interpolation of the d property (https://svgwg.org/svg2-draft/paths.html#PathElementDAttribute,
/// https://svgwg.org/specs/animations/#AnimationTypes): two path() functions whose path data have the same number and types of
/// segments are interpolated segment by segment (the computed values are absolute commands, so relative and absolute
/// commands can be interpolated); the arc flags are booleans (a value greater than 0 is 1). Anything else is discrete.
/// </summary>
internal static class CssPathInterpolation
{
    public static Func<double, string?>? Prepare(string from, string to)
    {
        if (Parse(from) is not { } a || Parse(to) is not { } b || !AreCompatible(a, b))
            return null;

        return progress => progress == 0 ? from : Combine(a, b, (x, y) => (float)CssGenericInterpolation.Lerp(x, y, progress));
    }

    public static string? Combine(string underlying, string value)
    {
        if (Parse(underlying) is not { } a || Parse(value) is not { } b || !AreCompatible(a, b))
            return null;

        return Combine(a, b, static (x, y) => x + y);
    }

    private static string Combine((string? FillRule, string? Box, List<SvgPathCommand> Commands) a, (string? FillRule, string? Box, List<SvgPathCommand> Commands) b, Func<float, float, float> combine)
    {
        var commands = new List<SvgPathCommand>(a.Commands.Count);
        for (var i = 0; i < a.Commands.Count; i++)
        {
            var commandA = a.Commands[i];
            var commandB = b.Commands[i];
            var values = new float[commandA.Values.Length];
            for (var j = 0; j < values.Length; j++)
            {
                values[j] = combine(commandA.Values[j], commandB.Values[j]);

                // The large-arc and sweep flags of an arc
                if (commandA.Type == 'A' && j is 3 or 4)
                {
                    values[j] = values[j] > 0 ? 1 : 0;
                }
            }

            commands.Add(new SvgPathCommand(commandA.Type, values));
        }

        var path = CssPath.Serialize(a.FillRule, commands);
        return a.Box is null ? path : path + " " + a.Box;
    }

    private static bool AreCompatible((string? FillRule, string? Box, List<SvgPathCommand> Commands) a, (string? FillRule, string? Box, List<SvgPathCommand> Commands) b)
    {
        if (a.FillRule != b.FillRule || a.Box != b.Box || a.Commands.Count != b.Commands.Count)
            return false;

        for (var i = 0; i < a.Commands.Count; i++)
        {
            if (a.Commands[i].Type != b.Commands[i].Type || a.Commands[i].Values.Length != b.Commands[i].Values.Length)
                return false;
        }

        return true;
    }

    /// <summary>Parses a path() and the reference box that may follow it (the &lt;geometry-box&gt; of clip-path, the &lt;shape-box&gt; of shape-outside, the &lt;coord-box&gt; of offset-path).</summary>
    private static (string? FillRule, string? Box, List<SvgPathCommand> Commands)? Parse(string value)
    {
        var values = CssGenericInterpolation.Parse(value);
        string? box = null;
        if (values is [CssFunction, CssPreservedToken { Type: CssTokenType.Ident } ident])
        {
            box = ident.Token.Value.ToLowerInvariant();
            values = [values[0]];
        }

        if (values is not [CssFunction function] || !CssPath.TryParse(function, out var fillRule, out var commands))
            return null;

        return (fillRule, box, SvgPathData.ToAbsolute(commands));
    }
}
