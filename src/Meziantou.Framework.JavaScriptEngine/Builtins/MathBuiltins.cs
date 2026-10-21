using System.Numerics;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>The Math object (https://tc39.es/ecma262/#sec-math-object).</summary>
internal static class MathBuiltins
{
    public static JsObject Initialize(Intrinsics intrinsics)
    {
        var realm = intrinsics.Realm;
        var math = new JsObject(intrinsics.ObjectPrototype);

        math.DefineBuiltIn("E", Math.E, PropertyFlags.None);
        math.DefineBuiltIn("LN10", 2.302585092994046, PropertyFlags.None);
        math.DefineBuiltIn("LN2", 0.6931471805599453, PropertyFlags.None);
        math.DefineBuiltIn("LOG10E", 0.4342944819032518, PropertyFlags.None);
        math.DefineBuiltIn("LOG2E", 1.4426950408889634, PropertyFlags.None);
        math.DefineBuiltIn("PI", Math.PI, PropertyFlags.None);
        math.DefineBuiltIn("SQRT1_2", 0.7071067811865476, PropertyFlags.None);
        math.DefineBuiltIn("SQRT2", 1.4142135623730951, PropertyFlags.None);
        math.DefineBuiltIn(WellKnownSymbols.ToStringTag, "Math", PropertyFlags.Configurable);

        DefineUnary(realm, math, "abs", Math.Abs);
        DefineUnary(realm, math, "acos", Math.Acos);
        DefineUnary(realm, math, "acosh", Math.Acosh);
        DefineUnary(realm, math, "asin", Math.Asin);
        DefineUnary(realm, math, "asinh", Math.Asinh);
        DefineUnary(realm, math, "atan", Math.Atan);
        DefineUnary(realm, math, "atanh", Math.Atanh);
        realm.DefineMethod(math, "atan2", 2, Atan2);
        DefineUnary(realm, math, "cbrt", Math.Cbrt);
        DefineUnary(realm, math, "ceil", Math.Ceiling);
        realm.DefineMethod(math, "clz32", 1, Clz32);
        DefineUnary(realm, math, "cos", Math.Cos);
        DefineUnary(realm, math, "cosh", Math.Cosh);
        DefineUnary(realm, math, "exp", Math.Exp);
        DefineUnary(realm, math, "expm1", Expm1);
        DefineUnary(realm, math, "floor", Math.Floor);
        DefineUnary(realm, math, "fround", static x => (float)x);
        DefineUnary(realm, math, "f16round", F16Round);
        realm.DefineMethod(math, "hypot", 2, Hypot);
        realm.DefineMethod(math, "imul", 2, Imul);
        DefineUnary(realm, math, "log", Math.Log);
        DefineUnary(realm, math, "log1p", Log1p);
        DefineUnary(realm, math, "log10", Math.Log10);
        DefineUnary(realm, math, "log2", Math.Log2);
        realm.DefineMethod(math, "max", 2, Max);
        realm.DefineMethod(math, "min", 2, Min);
        realm.DefineMethod(math, "pow", 2, Pow);
        realm.DefineMethod(math, "random", 0, Random);
        DefineUnary(realm, math, "round", Round);
        DefineUnary(realm, math, "sign", Sign);
        DefineUnary(realm, math, "sin", Math.Sin);
        DefineUnary(realm, math, "sinh", Math.Sinh);
        DefineUnary(realm, math, "sqrt", Math.Sqrt);
        realm.DefineMethod(math, "sumPrecise", 1, SumPrecise);
        DefineUnary(realm, math, "tan", Math.Tan);
        DefineUnary(realm, math, "tanh", Math.Tanh);
        DefineUnary(realm, math, "trunc", Math.Truncate);

        intrinsics.AddGlobal("Math", math);
        return math;
    }

    /// <summary>Defines a function that converts its argument with ToNumber and applies an operation to it.</summary>
    private static void DefineUnary(JsRealm realm, JsObject math, string name, Func<double, double> operation)
    {
        realm.DefineMethod(math, name, 1, (_, _, arguments, _) => operation(JsOperations.ToNumber(arguments.Length > 0 ? arguments[0] : default)));
    }

    private static JsValue Atan2(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var y = JsOperations.ToNumber(arguments.Length > 0 ? arguments[0] : default);
        var x = JsOperations.ToNumber(arguments.Length > 1 ? arguments[1] : default);
        return Math.Atan2(y, x);
    }

    private static JsValue Clz32(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var n = JsOperations.ToUint32(arguments.Length > 0 ? arguments[0] : default);
        return BitOperations.LeadingZeroCount(n);
    }

    private static JsValue Imul(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var a = JsOperations.ToInt32(arguments.Length > 0 ? arguments[0] : default);
        var b = JsOperations.ToInt32(arguments.Length > 1 ? arguments[1] : default);
        return unchecked(a * b);
    }

    private static JsValue Pow(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var @base = JsOperations.ToNumber(arguments.Length > 0 ? arguments[0] : default);
        var exponent = JsOperations.ToNumber(arguments.Length > 1 ? arguments[1] : default);
        return NumberOperations.Exponentiate(@base, exponent);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Math.random is not a cryptographically secure generator")]
    private static JsValue Random(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        return System.Random.Shared.NextDouble();
    }

    /// <summary>Math.max(...args): all the arguments are converted before the result is computed.</summary>
    private static JsValue Max(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var result = double.NegativeInfinity;
        foreach (var argument in arguments)
        {
            var number = JsOperations.ToNumber(argument);
            if (double.IsNaN(number) || double.IsNaN(result))
            {
                result = double.NaN;
            }
            else if (number > result || (number == 0 && result == 0 && !double.IsNegative(number)))
            {
                result = number;
            }
        }

        return result;
    }

    /// <summary>Math.min(...args): all the arguments are converted before the result is computed.</summary>
    private static JsValue Min(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var result = double.PositiveInfinity;
        foreach (var argument in arguments)
        {
            var number = JsOperations.ToNumber(argument);
            if (double.IsNaN(number) || double.IsNaN(result))
            {
                result = double.NaN;
            }
            else if (number < result || (number == 0 && result == 0 && double.IsNegative(number)))
            {
                result = number;
            }
        }

        return result;
    }

    /// <summary>Math.hypot(...args): Infinity wins over NaN; the sum of squares is scaled and compensated.</summary>
    private static JsValue Hypot(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (arguments.Length == 0)
            return 0;

        Span<double> numbers = arguments.Length <= 16 ? stackalloc double[arguments.Length] : new double[arguments.Length];
        for (var i = 0; i < arguments.Length; i++)
        {
            numbers[i] = JsOperations.ToNumber(arguments[i]);
        }

        var max = 0.0;
        var hasNaN = false;
        foreach (var number in numbers)
        {
            if (double.IsInfinity(number))
                return double.PositiveInfinity;

            if (double.IsNaN(number))
            {
                hasNaN = true;
            }
            else
            {
                max = Math.Max(max, Math.Abs(number));
            }
        }

        if (hasNaN)
            return double.NaN;

        if (max == 0)
            return 0;

        var sum = 0.0;
        var compensation = 0.0;
        foreach (var number in numbers)
        {
            var scaled = number / max;
            var summand = (scaled * scaled) - compensation;
            var preliminary = sum + summand;
            compensation = (preliminary - sum) - summand;
            sum = preliminary;
        }

        return Math.Sqrt(sum) * max;
    }

    /// <summary>Math.round(x): the closest integer, ties toward +Infinity; -0 for the numbers in [-0.5, -0].</summary>
    internal static double Round(double x)
    {
        if (!double.IsFinite(x) || Math.Truncate(x) == x)
            return x;

        var floor = Math.Floor(x);
        var result = x - floor >= 0.5 ? floor + 1 : floor;
        return result == 0 && x < 0 ? -0.0 : result;
    }

    private static double Sign(double x)
    {
        if (double.IsNaN(x) || x == 0)
            return x;

        return x > 0 ? 1 : -1;
    }

    /// <summary>Math.f16round(x): the closest IEEE 754 binary16 value (rounded directly from the double).</summary>
    private static double F16Round(double x) => (double)(Half)x;

    /// <summary>Math.expm1(x), accurate for small x.</summary>
    internal static double Expm1(double x)
    {
        if (double.IsNaN(x) || x == 0 || double.IsPositiveInfinity(x))
            return x;

        if (double.IsNegativeInfinity(x))
            return -1;

        var u = Math.Exp(x);
        if (u == 1)
            return x;

        var um1 = u - 1;
        if (um1 == -1 || double.IsPositiveInfinity(u))
            return um1;

        return um1 * x / Math.Log(u);
    }

    /// <summary>Math.log1p(x), accurate for small x.</summary>
    internal static double Log1p(double x)
    {
        if (double.IsNaN(x) || x == 0 || double.IsPositiveInfinity(x))
            return x;

        if (x == -1)
            return double.NegativeInfinity;

        if (x < -1)
            return double.NaN;

        var u = 1 + x;
        if (u == 1)
            return x;

        return Math.Log(u) * x / (u - 1);
    }

    /// <summary>Math.sumPrecise(items) (https://tc39.es/proposal-math-sum/): the exact sum of the numbers, rounded once.</summary>
    private static JsValue SumPrecise(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var items = arguments.Length > 0 ? arguments[0] : default;
        JsOperations.RequireObjectCoercible(items, "Math.sumPrecise");
        var iteratorRecord = IteratorOperations.GetIterator(items);

        var state = SumState.MinusZero;
        ExactSummation? sum = null;
        long count = 0;
        while (IteratorOperations.IteratorStepValue(iteratorRecord, out var next))
        {
            count++;
            if (count >= 9007199254740992)
            {
                var error = ThrowHelper.CreateException(ErrorType.RangeError, "Too many values to sum");
                IteratorOperations.IteratorCloseOnThrow(iteratorRecord);
                throw error;
            }

            if (!next.IsNumber)
            {
                var error = ThrowHelper.CreateException(ErrorType.TypeError, "Math.sumPrecise requires an iterable of numbers");
                IteratorOperations.IteratorCloseOnThrow(iteratorRecord);
                throw error;
            }

            if (state == SumState.NotANumber)
                continue;

            var n = next.NumberValue;
            if (double.IsNaN(n))
            {
                state = SumState.NotANumber;
            }
            else if (double.IsPositiveInfinity(n))
            {
                state = state == SumState.MinusInfinity ? SumState.NotANumber : SumState.PlusInfinity;
            }
            else if (double.IsNegativeInfinity(n))
            {
                state = state == SumState.PlusInfinity ? SumState.NotANumber : SumState.MinusInfinity;
            }
            else if (!(n == 0 && double.IsNegative(n)) && state is SumState.MinusZero or SumState.Finite)
            {
                state = SumState.Finite;
                sum ??= new ExactSummation();
                sum.Add(n);
            }
        }

        return state switch
        {
            SumState.NotANumber => double.NaN,
            SumState.PlusInfinity => double.PositiveInfinity,
            SumState.MinusInfinity => double.NegativeInfinity,
            SumState.MinusZero => -0.0,
            _ => sum!.GetResult(),
        };
    }

    private enum SumState
    {
        MinusZero,
        Finite,
        PlusInfinity,
        MinusInfinity,
        NotANumber,
    }
}
