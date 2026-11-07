using System.Text;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.Values;

/// <summary>
/// The colors parameterized by headroom (https://drafts.csswg.org/css-color-hdr-1/#hdr-color-function):
/// color-hdr([ &lt;color&gt; &amp;&amp; &lt;number [0,∞]&gt;? ]#{2}). The actual color depends on the HDR headroom of the display,
/// which is not exposed: the computed value keeps the function with its computed colors. The parsing and the serialization
/// are not fully defined (https://github.com/w3c/csswg-drafts/issues/13597): an omitted headroom is 0 (as in the CSSWG
/// discussion that added the function), the two headrooms must not be the same (the function is invalid when they are known
/// at parse time), and the headroom follows its color in the serialization.
/// </summary>
internal static class CssHdrColor
{
    /// <summary>A color and its headroom (null when it is omitted).</summary>
    public readonly record struct Stop(CssComponentValue Color, CssComponentValue? Headroom);

    /// <summary>Returns the two stops of color-hdr(), or null when the arguments are not two colors with optional headrooms.</summary>
    public static Stop[]? GetStops(CssFunction function)
    {
        var arguments = Cascade.CssValueComputer.SplitArguments(function.Value);
        if (arguments.Count != 2)
            return null;

        var stops = new Stop[2];
        for (var i = 0; i < 2; i++)
        {
            Stop? stop = arguments[i] switch
            {
                [var color] when ValueMatcher.IsColor(color) => new Stop(color, null),
                [var color, var headroom] when ValueMatcher.IsColor(color) && IsHeadroom(headroom) => new Stop(color, headroom),
                [var headroom, var color] when ValueMatcher.IsColor(color) && IsHeadroom(headroom) => new Stop(color, headroom),
                _ => null,
            };

            if (stop is null)
                return null;

            stops[i] = stop.Value;
        }

        return stops;
    }

    /// <summary>Returns whether color-hdr() is valid: two colors whose headrooms are not the same (when they are known at parse time).</summary>
    public static bool IsValid(CssFunction function)
    {
        if (GetStops(function) is not [var first, var second])
            return false;

        return EvaluateHeadroom(first.Headroom, CssCalcContext.Default) is not { } h1 || EvaluateHeadroom(second.Headroom, CssCalcContext.Default) is not { } h2 || h1 != h2;
    }

    /// <summary>
    /// Returns the value of a headroom (0 when it is omitted), or null when it is a math function that cannot be resolved in
    /// the context. A math function is clamped to the range of &lt;number [0,∞]&gt; (https://drafts.csswg.org/css-values-4/#calc-range).
    /// </summary>
    public static double? EvaluateHeadroom(CssComponentValue? headroom, CssCalcContext context) => headroom switch
    {
        null => 0,
        CssPreservedToken { Type: CssTokenType.Number } number => number.Token.NumericValue,
        CssFunction math when CssCalc.Evaluate(math, context) is { IsNumeric: true, Unit: "" } result => double.IsNaN(result.Value) ? 0 : Math.Max(0, result.Value),
        _ => null,
    };

    /// <summary>Serializes color-hdr() with the serialized colors and headrooms (a headroom follows its color).</summary>
    public static string Serialize(IReadOnlyList<(string Color, string? Headroom)> stops)
    {
        var builder = new StringBuilder("color-hdr(");
        for (var i = 0; i < stops.Count; i++)
        {
            builder.Append(i > 0 ? ", " : "").Append(stops[i].Color);
            if (stops[i].Headroom is { } headroom)
            {
                builder.Append(' ').Append(headroom);
            }
        }

        return builder.Append(')').ToString();
    }

    // <number [0,∞]>: a non-negative number, or a math function that resolves to a number
    private static bool IsHeadroom(CssComponentValue value) => value switch
    {
        CssPreservedToken { Type: CssTokenType.Number } number => number.Token.NumericValue >= 0,
        CssFunction math when CssCalc.IsMathFunction(math.Name) => CssCalc.Evaluate(math, new CssCalcContext { KeepRelativeUnits = true }) is { TypeUnit: "" },
        _ => false,
    };
}
