using System.Collections.Concurrent;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.Values;

/// <summary>
/// An easing function (https://drafts.csswg.org/css-easing-2/#easing-functions): transforms an input progress value into an
/// output progress value. The input and output progress values may be outside of [0, 1].
/// </summary>
internal abstract class CssEasing
{
    private static readonly ConcurrentDictionary<string, CssEasing?> Cache = new(StringComparer.Ordinal);

    /// <summary>The linear easing function (linear, or linear(0, 1)): the output is the input.</summary>
    public static CssEasing Linear { get; } = new LinearKeywordEasing();

    /// <summary>Returns whether the function is the linear keyword, whose output is always its input.</summary>
    public bool IsLinear => ReferenceEquals(this, Linear);

    /// <summary>
    /// Returns the output progress value of the easing function. <paramref name="beforeFlag"/> is the before flag of the step
    /// easing functions (https://drafts.csswg.org/css-easing-2/#before-flag), set when the animation effect is in its before phase.
    /// </summary>
    public abstract double Evaluate(double inputProgress, bool beforeFlag = false);

    /// <summary>
    /// Parses an &lt;easing-function&gt; (e.g. a computed value of animation-timing-function). Returns null when the value is not a
    /// single valid easing function. The results are cached.
    /// </summary>
    public static CssEasing? Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (Cache.TryGetValue(value, out var result))
            return result;

        result = ParseCore(value);
        if (Cache.Count < 4096)
        {
            Cache.TryAdd(value, result);
        }

        return result;
    }

    private static CssEasing? ParseCore(string value)
    {
        var significant = ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(value));
        if (significant is not [var component])
            return null;

        if (component is CssPreservedToken { Type: CssTokenType.Ident } ident)
        {
            return InfraAscii.ToAsciiLowercase(ident.Token.Value) switch
            {
                "linear" => Linear,
                "ease" => new CubicBezierEasing(0.25, 0.1, 0.25, 1),
                "ease-in" => new CubicBezierEasing(0.42, 0, 1, 1),
                "ease-out" => new CubicBezierEasing(0, 0, 0.58, 1),
                "ease-in-out" => new CubicBezierEasing(0.42, 0, 0.58, 1),
                "step-start" => new StepsEasing(1, StepPosition.JumpStart),
                "step-end" => new StepsEasing(1, StepPosition.JumpEnd),
                _ => null,
            };
        }

        if (component is not CssFunction function)
            return null;

        var arguments = ValueMatcher.SplitAtCommas(ValueMatcher.RemoveWhitespace(function.Value));
        switch (InfraAscii.ToAsciiLowercase(function.Name))
        {
            case "cubic-bezier":
                {
                    if (arguments.Count != 4)
                        return null;

                    Span<double> values = stackalloc double[4];
                    for (var i = 0; i < 4; i++)
                    {
                        if (arguments[i] is not [var argument] || ReadNumber(argument) is not { } number)
                            return null;

                        values[i] = number;
                    }

                    // The x values must be in [0, 1] (math functions are clamped)
                    if ((arguments[0] is [CssPreservedToken] && values[0] is < 0 or > 1) || (arguments[2] is [CssPreservedToken] && values[2] is < 0 or > 1))
                        return null;

                    return new CubicBezierEasing(Math.Clamp(values[0], 0, 1), values[1], Math.Clamp(values[2], 0, 1), values[3]);
                }

            case "steps":
                {
                    if (arguments.Count is < 1 or > 2 || arguments[0] is not [var countArgument] || ReadNumber(countArgument) is not { } count)
                        return null;

                    var position = StepPosition.JumpEnd;
                    if (arguments.Count == 2)
                    {
                        if (arguments[1] is not [CssPreservedToken { Type: CssTokenType.Ident } positionIdent])
                            return null;

                        switch (InfraAscii.ToAsciiLowercase(positionIdent.Token.Value))
                        {
                            case "jump-start":
                            case "start":
                                position = StepPosition.JumpStart;
                                break;
                            case "jump-end":
                            case "end":
                                position = StepPosition.JumpEnd;
                                break;
                            case "jump-none":
                                position = StepPosition.JumpNone;
                                break;
                            case "jump-both":
                                position = StepPosition.JumpBoth;
                                break;
                            default:
                                return null;
                        }
                    }

                    // The number of steps is an integer (math functions are rounded), at least 1 (2 with jump-none)
                    var steps = Math.Floor(count + 0.5);
                    if (countArgument is CssPreservedToken && (count != steps || count < (position == StepPosition.JumpNone ? 2 : 1)))
                        return null;

                    steps = Math.Max(steps, position == StepPosition.JumpNone ? 2 : 1);
                    return new StepsEasing((int)Math.Min(steps, int.MaxValue), position);
                }

            case "linear":
                return ParseLinear(arguments);

            default:
                return null;
        }
    }

    /// <summary>
    /// Parses the arguments of linear() (https://drafts.csswg.org/css-easing-2/#linear-easing-function-parsing) into its
    /// control points, following the steps of the specification.
    /// </summary>
    private static LinearEasing? ParseLinear(List<List<CssComponentValue>> arguments)
    {
        var outputs = new List<double>();
        var inputs = new List<double?>();
        foreach (var argument in arguments)
        {
            double? output = null;
            var percentages = new List<double>(2);
            foreach (var component in argument)
            {
                if (component is CssPreservedToken { Type: CssTokenType.Percentage } percentage && percentages.Count < 2)
                {
                    percentages.Add(percentage.Token.NumericValue / 100);
                }
                else if (output is null && ReadNumber(component) is { } number)
                {
                    output = number;
                }
                else if (component is CssFunction math && CssCalc.Evaluate(math, CssCalcContext.Default) is { IsNumeric: true, Unit: "%" } calc && percentages.Count < 2)
                {
                    percentages.Add(calc.Value / 100);
                }
                else
                {
                    return null;
                }
            }

            if (output is null)
                return null;

            if (percentages.Count == 0)
            {
                outputs.Add(output.Value);
                inputs.Add(null);
            }

            foreach (var percentage in percentages)
            {
                outputs.Add(output.Value);
                inputs.Add(percentage);
            }
        }

        if (outputs.Count < 2)
            return null;

        inputs[0] ??= 0;
        inputs[^1] ??= 1;

        var largest = double.NegativeInfinity;
        for (var i = 0; i < inputs.Count; i++)
        {
            if (inputs[i] is { } input)
            {
                largest = Math.Max(largest, input);
                inputs[i] = largest;
            }
        }

        for (var i = 1; i < inputs.Count - 1; i++)
        {
            if (inputs[i] is not null)
                continue;

            var end = i;
            while (inputs[end] is null)
            {
                end++;
            }

            var start = inputs[i - 1]!.Value;
            var step = (inputs[end]!.Value - start) / (end - i + 1);
            for (var j = i; j < end; j++)
            {
                inputs[j] = start + (step * (j - i + 1));
            }
        }

        var points = new (double Input, double Output)[outputs.Count];
        for (var i = 0; i < points.Length; i++)
        {
            points[i] = (inputs[i]!.Value, outputs[i]);
        }

        return new LinearEasing(points);
    }

    private static double? ReadNumber(CssComponentValue component)
    {
        if (component is CssPreservedToken { Type: CssTokenType.Number } number)
            return number.Token.NumericValue;

        if (component is CssFunction function && CssCalc.Evaluate(function, CssCalcContext.Default) is { IsResolved: true, Unit: "" } calc)
            return calc.Value;

        return null;
    }

    private sealed class LinearKeywordEasing : CssEasing
    {
        public override double Evaluate(double inputProgress, bool beforeFlag = false) => inputProgress;
    }

    /// <summary>https://drafts.csswg.org/css-easing-2/#linear-easing-function-output</summary>
    private sealed class LinearEasing((double Input, double Output)[] points) : CssEasing
    {
        public override double Evaluate(double inputProgress, bool beforeFlag = false)
        {
            // The index of the last point whose input is less than or equal to the input progress, or 0
            var pointAIndex = 0;
            for (var i = 0; i < points.Length; i++)
            {
                if (points[i].Input <= inputProgress)
                {
                    pointAIndex = i;
                }
            }

            if (pointAIndex == points.Length - 1)
            {
                pointAIndex--;
            }

            var pointA = points[pointAIndex];
            var pointB = points[pointAIndex + 1];
            if (pointA.Input == pointB.Input)
                return pointB.Output;

            var progressBetweenPoints = (inputProgress - pointA.Input) / (pointB.Input - pointA.Input);
            return pointA.Output + (progressBetweenPoints * (pointB.Output - pointA.Output));
        }
    }

    /// <summary>https://drafts.csswg.org/css-easing-2/#cubic-bezier-algo</summary>
    private sealed class CubicBezierEasing : CssEasing
    {
        private const double Epsilon = 1e-12;

        private readonly double _x1;
        private readonly double _y1;
        private readonly double _x2;
        private readonly double _y2;

        // The polynomial coefficients (the curve starts at (0, 0) and ends at (1, 1))
        private readonly double _ax;
        private readonly double _bx;
        private readonly double _cx;
        private readonly double _ay;
        private readonly double _by;
        private readonly double _cy;

        public CubicBezierEasing(double x1, double y1, double x2, double y2)
        {
            _x1 = x1;
            _y1 = y1;
            _x2 = x2;
            _y2 = y2;
            _cx = 3 * x1;
            _bx = (3 * (x2 - x1)) - _cx;
            _ax = 1 - _cx - _bx;
            _cy = 3 * y1;
            _by = (3 * (y2 - y1)) - _cy;
            _ay = 1 - _cy - _by;
        }

        public override double Evaluate(double inputProgress, bool beforeFlag = false)
        {
            // Outside of [0, 1], the curve is extended with the tangent at the closest endpoint (the degenerate cases follow the browsers)
            if (inputProgress < 0)
            {
                if (_x1 > 0)
                    return _y1 / _x1 * inputProgress;

                if (_y1 == 0 && _x2 > 0)
                    return _y2 / _x2 * inputProgress;

                return _y1 == 0 && _y2 == 0 ? inputProgress : 0;
            }

            if (inputProgress > 1)
            {
                if (_x2 < 1)
                    return 1 + ((_y2 - 1) / (_x2 - 1) * (inputProgress - 1));

                if (_y2 == 1 && _x1 < 1)
                    return 1 + ((_y1 - 1) / (_x1 - 1) * (inputProgress - 1));

                return _y2 == 1 && _y1 == 1 ? inputProgress : 1;
            }

            return SampleCurveY(SolveCurveX(inputProgress));
        }

        private double SampleCurveX(double t) => ((((_ax * t) + _bx) * t) + _cx) * t;

        private double SampleCurveY(double t) => ((((_ay * t) + _by) * t) + _cy) * t;

        private double SampleCurveDerivativeX(double t) => (((3 * _ax * t) + (2 * _bx)) * t) + _cx;

        /// <summary>Finds the parameter t of the curve for the x value, with Newton's method, then by bisection.</summary>
        private double SolveCurveX(double x)
        {
            var t = x;
            for (var i = 0; i < 16; i++)
            {
                var error = SampleCurveX(t) - x;
                if (Math.Abs(error) < Epsilon)
                    return t;

                var derivative = SampleCurveDerivativeX(t);
                if (Math.Abs(derivative) < 1e-6)
                    break;

                t -= error / derivative;
            }

            var low = 0d;
            var high = 1d;
            t = x;
            while (low < high)
            {
                var value = SampleCurveX(t);
                if (Math.Abs(value - x) < Epsilon)
                    return t;

                if (x > value)
                {
                    low = t;
                }
                else
                {
                    high = t;
                }

                var next = (low + high) / 2;
                if (next == t)
                    break;

                t = next;
            }

            return t;
        }
    }

    private enum StepPosition
    {
        JumpStart,
        JumpEnd,
        JumpNone,
        JumpBoth,
    }

    /// <summary>https://drafts.csswg.org/css-easing-2/#step-easing-algo</summary>
    private sealed class StepsEasing(int steps, StepPosition position) : CssEasing
    {
        public override double Evaluate(double inputProgress, bool beforeFlag = false)
        {
            var currentStep = Math.Floor(inputProgress * steps);
            if (position is StepPosition.JumpStart or StepPosition.JumpBoth)
            {
                currentStep++;
            }

            if (beforeFlag && (inputProgress * steps) % 1 == 0)
            {
                currentStep--;
            }

            if (inputProgress >= 0 && currentStep < 0)
            {
                currentStep = 0;
            }

            var jumps = position switch
            {
                StepPosition.JumpNone => steps - 1,
                StepPosition.JumpBoth => steps + 1,
                _ => steps,
            };

            if (inputProgress <= 1 && currentStep > jumps)
            {
                currentStep = jumps;
            }

            return currentStep / jumps;
        }
    }
}
