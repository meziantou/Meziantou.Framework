namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>Number (https://tc39.es/ecma262/#sec-number-objects).</summary>
internal static class NumberBuiltins
{
    public const double MaxSafeInteger = 9007199254740991;

    public static JsFunction Initialize(Intrinsics intrinsics)
    {
        var realm = intrinsics.Realm;
        var prototype = intrinsics.NumberPrototype;
        var constructor = realm.CreateBuiltinFunction("Number", 1, Construct, isConstructor: true);
        constructor.DefineBuiltIn("prototype", prototype, PropertyFlags.None);
        prototype.DefineBuiltIn("constructor", constructor);

        constructor.DefineBuiltIn("EPSILON", Math.Pow(2, -52), PropertyFlags.None);
        constructor.DefineBuiltIn("MAX_SAFE_INTEGER", MaxSafeInteger, PropertyFlags.None);
        constructor.DefineBuiltIn("MAX_VALUE", double.MaxValue, PropertyFlags.None);
        constructor.DefineBuiltIn("MIN_SAFE_INTEGER", -MaxSafeInteger, PropertyFlags.None);
        constructor.DefineBuiltIn("MIN_VALUE", double.Epsilon, PropertyFlags.None);
        constructor.DefineBuiltIn("NaN", double.NaN, PropertyFlags.None);
        constructor.DefineBuiltIn("NEGATIVE_INFINITY", double.NegativeInfinity, PropertyFlags.None);
        constructor.DefineBuiltIn("POSITIVE_INFINITY", double.PositiveInfinity, PropertyFlags.None);
        realm.DefineMethod(constructor, "isFinite", 1, IsFinite);
        realm.DefineMethod(constructor, "isInteger", 1, IsInteger);
        realm.DefineMethod(constructor, "isNaN", 1, IsNaN);
        realm.DefineMethod(constructor, "isSafeInteger", 1, IsSafeInteger);
        constructor.DefineBuiltIn("parseFloat", intrinsics.ParseFloat);
        constructor.DefineBuiltIn("parseInt", intrinsics.ParseInt);

        realm.DefineMethod(prototype, "toExponential", 1, ToExponential);
        realm.DefineMethod(prototype, "toFixed", 1, ToFixed);
        realm.DefineMethod(prototype, "toLocaleString", 0, ToLocaleString);
        realm.DefineMethod(prototype, "toPrecision", 1, ToPrecision);
        realm.DefineMethod(prototype, "toString", 1, ToString);
        realm.DefineMethod(prototype, "valueOf", 0, ValueOf);

        intrinsics.AddGlobal("Number", constructor);
        return constructor;
    }

    /// <summary>Number(value)</summary>
    private static JsValue Construct(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        double n;
        if (arguments.Length > 0)
        {
            var prim = JsOperations.ToNumeric(arguments[0]);
            n = prim.IsBigInt ? BigIntOperations.ToNumber(prim.AsBigInt()) : prim.NumberValue;
        }
        else
        {
            n = 0;
        }

        if (newTarget is null)
            return n;

        var prototype = ObjectOperations.GetPrototypeForConstruct(function, newTarget, static intrinsics => intrinsics.NumberPrototype);
        return new JsPrimitiveObject(prototype, ObjectClass.Number, n);
    }

    /// <summary>ThisNumberValue(value)</summary>
    internal static double ThisNumberValue(JsValue value, string methodName)
    {
        if (value.IsNumber)
            return value.NumberValue;

        if (value.Cell is JsPrimitiveObject { Class: ObjectClass.Number } obj)
            return obj.PrimitiveValue.NumberValue;

        return ThrowHelper.ThrowTypeError<double>($"Number.prototype.{methodName} requires that 'this' be a Number");
    }

    private static JsValue IsFinite(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var number = arguments.Length > 0 ? arguments[0] : default;
        return number.IsNumber && double.IsFinite(number.NumberValue);
    }

    private static JsValue IsInteger(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var number = arguments.Length > 0 ? arguments[0] : default;
        return number.IsNumber && IsIntegralNumber(number.NumberValue);
    }

    private static JsValue IsNaN(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var number = arguments.Length > 0 ? arguments[0] : default;
        return number.IsNumber && double.IsNaN(number.NumberValue);
    }

    private static JsValue IsSafeInteger(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var number = arguments.Length > 0 ? arguments[0] : default;
        return number.IsNumber && IsIntegralNumber(number.NumberValue) && Math.Abs(number.NumberValue) <= MaxSafeInteger;
    }

    /// <summary>IsIntegralNumber(argument) for a number.</summary>
    internal static bool IsIntegralNumber(double value) => double.IsFinite(value) && Math.Truncate(value) == value;

    /// <summary>Number.prototype.toExponential(fractionDigits)</summary>
    private static JsValue ToExponential(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var x = ThisNumberValue(thisValue, "toExponential");
        var fractionDigits = arguments.Length > 0 ? arguments[0] : default;
        var f = JsOperations.ToIntegerOrInfinity(fractionDigits);
        if (!double.IsFinite(x))
            return NumberFormatting.ToString(x);

        if (f is < 0 or > 100)
            return ThrowHelper.ThrowRangeError<JsValue>("toExponential() argument must be between 0 and 100");

        return NumberFormatting.ToExponential(x, fractionDigits.IsUndefined ? null : (int)f);
    }

    /// <summary>Number.prototype.toFixed(fractionDigits)</summary>
    private static JsValue ToFixed(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var x = ThisNumberValue(thisValue, "toFixed");
        var f = JsOperations.ToIntegerOrInfinity(arguments.Length > 0 ? arguments[0] : default);
        if (f is < 0 or > 100 || double.IsInfinity(f))
            return ThrowHelper.ThrowRangeError<JsValue>("toFixed() digits argument must be between 0 and 100");

        return NumberFormatting.ToFixed(x, (int)f);
    }

    /// <summary>Number.prototype.toLocaleString(): without ECMA-402, the same as toString().</summary>
    private static JsValue ToLocaleString(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var x = ThisNumberValue(thisValue, "toLocaleString");
        return NumberFormatting.ToString(x);
    }

    /// <summary>Number.prototype.toPrecision(precision)</summary>
    private static JsValue ToPrecision(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var x = ThisNumberValue(thisValue, "toPrecision");
        var precision = arguments.Length > 0 ? arguments[0] : default;
        if (precision.IsUndefined)
            return NumberFormatting.ToString(x);

        var p = JsOperations.ToIntegerOrInfinity(precision);
        if (!double.IsFinite(x))
            return NumberFormatting.ToString(x);

        if (p is < 1 or > 100)
            return ThrowHelper.ThrowRangeError<JsValue>("toPrecision() argument must be between 1 and 100");

        return NumberFormatting.ToPrecision(x, (int)p);
    }

    /// <summary>Number.prototype.toString(radix)</summary>
    private static JsValue ToString(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var x = ThisNumberValue(thisValue, "toString");
        var radix = arguments.Length > 0 ? arguments[0] : default;
        var radixMV = radix.IsUndefined ? 10 : JsOperations.ToIntegerOrInfinity(radix);
        if (radixMV is < 2 or > 36)
            return ThrowHelper.ThrowRangeError<JsValue>("toString() radix must be between 2 and 36");

        return NumberFormatting.ToString(x, (int)radixMV);
    }

    /// <summary>Number.prototype.valueOf()</summary>
    private static JsValue ValueOf(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        return ThisNumberValue(thisValue, "valueOf");
    }
}
