using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>The abstract operations of ECMAScript (https://tc39.es/ecma262/#sec-abstract-operations).</summary>
internal static class JsOperations
{
    // ------------------------------------------------------------------------------------------------------------------
    // Testing and comparison (https://tc39.es/ecma262/#sec-testing-and-comparison-operations)
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>SameValue(x, y)</summary>
    internal static bool SameValue(JsValue x, JsValue y)
    {
        if (x.IsNumber)
        {
            if (!y.IsNumber)
                return false;

            var a = x.NumberValue;
            var b = y.NumberValue;
            if (double.IsNaN(a))
                return double.IsNaN(b);

            return a.Equals(b) && double.IsNegative(a) == double.IsNegative(b);
        }

        return SameValueNonNumber(x, y);
    }

    /// <summary>SameValueZero(x, y)</summary>
    internal static bool SameValueZero(JsValue x, JsValue y)
    {
        if (x.IsNumber)
        {
            if (!y.IsNumber)
                return false;

            var a = x.NumberValue;
            var b = y.NumberValue;
            return a == b || (double.IsNaN(a) && double.IsNaN(b));
        }

        return SameValueNonNumber(x, y);
    }

    private static bool SameValueNonNumber(JsValue x, JsValue y)
    {
        if (ReferenceEquals(x.Cell, y.Cell))
            return true;

        if (x.Kind != y.Kind)
            return false;

        return x.Kind switch
        {
            JsValueKind.String => x.AsJsString().Equals(y.AsJsString()),
            JsValueKind.BigInt => x.AsBigInt() == y.AsBigInt(),
            _ => false,
        };
    }

    /// <summary>IsStrictlyEqual(x, y): <c>===</c></summary>
    internal static bool IsStrictlyEqual(JsValue x, JsValue y)
    {
        if (x.IsNumber)
            return y.IsNumber && x.NumberValue == y.NumberValue;

        return SameValueNonNumber(x, y);
    }

    /// <summary>IsLooselyEqual(x, y): <c>==</c></summary>
    internal static bool IsLooselyEqual(JsValue x, JsValue y)
    {
        while (true)
        {
            var xKind = x.Kind;
            var yKind = y.Kind;
            if (xKind == yKind)
                return IsStrictlyEqual(x, y);

            if (x.IsNullOrUndefined && y.IsNullOrUndefined)
                return true;

            // [[IsHTMLDDA]] objects are loosely equal to null and undefined
            if (x.Cell is JsObject { IsHtmlDda: true } && y.IsNullOrUndefined)
                return true;

            if (y.Cell is JsObject { IsHtmlDda: true } && x.IsNullOrUndefined)
                return true;

            switch (xKind, yKind)
            {
                case (JsValueKind.Number, JsValueKind.String):
                    return x.NumberValue == StringToNumber(y.AsJsString());
                case (JsValueKind.String, JsValueKind.Number):
                    return StringToNumber(x.AsJsString()) == y.NumberValue;
                case (JsValueKind.BigInt, JsValueKind.String):
                    {
                        var n = StringToBigInt(y.AsString());
                        return n is not null && x.AsBigInt() == n.Value;
                    }

                case (JsValueKind.String, JsValueKind.BigInt):
                    (x, y) = (y, x);
                    continue;
                case (JsValueKind.Boolean, _):
                    x = x.AsBoolean() ? 1 : 0;
                    continue;
                case (_, JsValueKind.Boolean):
                    y = y.AsBoolean() ? 1 : 0;
                    continue;
                case (JsValueKind.String or JsValueKind.Number or JsValueKind.BigInt or JsValueKind.Symbol, JsValueKind.Object):
                    y = ToPrimitive(y, PreferredType.None);
                    continue;
                case (JsValueKind.Object, JsValueKind.String or JsValueKind.Number or JsValueKind.BigInt or JsValueKind.Symbol):
                    x = ToPrimitive(x, PreferredType.None);
                    continue;
                case (JsValueKind.BigInt, JsValueKind.Number):
                    return BigIntEqualsNumber(x.AsBigInt(), y.NumberValue);
                case (JsValueKind.Number, JsValueKind.BigInt):
                    return BigIntEqualsNumber(y.AsBigInt(), x.NumberValue);
                default:
                    return false;
            }
        }
    }

    internal static bool BigIntEqualsNumber(BigInteger bigint, double number)
    {
        if (double.IsNaN(number) || double.IsInfinity(number))
            return false;

        if (number != Math.Floor(number))
            return false;

        return bigint == new BigInteger(number);
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Type conversion (https://tc39.es/ecma262/#sec-type-conversion)
    // ------------------------------------------------------------------------------------------------------------------

    internal enum PreferredType
    {
        None,
        Number,
        String,
    }

    /// <summary>ToPrimitive(input, preferredType)</summary>
    internal static JsValue ToPrimitive(JsValue input, PreferredType preferredType = PreferredType.None)
    {
        if (input.Cell is not JsObject obj)
            return input;

        var exoticToPrim = GetMethod(input, new PropertyKey(WellKnownSymbols.ToPrimitive));
        if (exoticToPrim is not null)
        {
            var hint = preferredType switch
            {
                PreferredType.String => "string",
                PreferredType.Number => "number",
                _ => "default",
            };

            var result = exoticToPrim.Call(input, [hint]);
            if (result.IsObject)
            {
                ThrowHelper.ThrowTypeError("Cannot convert object to primitive value");
            }

            return result;
        }

        return OrdinaryToPrimitive(obj, preferredType == PreferredType.String ? PreferredType.String : PreferredType.Number);
    }

    /// <summary>OrdinaryToPrimitive(O, hint)</summary>
    internal static JsValue OrdinaryToPrimitive(JsObject obj, PreferredType hint)
    {
        var first = hint == PreferredType.String ? "toString" : "valueOf";
        var second = hint == PreferredType.String ? "valueOf" : "toString";
        foreach (var name in (ReadOnlySpan<string>)[first, second])
        {
            var method = obj.Get(new PropertyKey(name));
            if (method.Cell is JsObject { IsCallable: true } callable)
            {
                var result = callable.Call(new JsValue(obj), []);
                if (!result.IsObject)
                    return result;
            }
        }

        return ThrowHelper.ThrowTypeError<JsValue>("Cannot convert object to primitive value");
    }

    /// <summary>ToBoolean(argument)</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool ToBoolean(JsValue value)
    {
        if (value.IsNumber)
        {
            var number = value.NumberValue;
            return number != 0 && !double.IsNaN(number);
        }

        return value.Cell switch
        {
            null => false,
            BooleanCell boolean => boolean.Value,
            JsString str => !str.IsEmpty,
            JsBigInt bigint => !bigint.Value.IsZero,
            JsObject obj => !obj.IsHtmlDda,
            JsSymbol => true,
            _ => false,
        };
    }

    /// <summary>ToNumeric(value): a number, or a BigInt value.</summary>
    internal static JsValue ToNumeric(JsValue value)
    {
        if (value.IsNumber || value.IsBigInt)
            return value;

        var primitive = ToPrimitive(value, PreferredType.Number);
        if (primitive.IsBigInt)
            return primitive;

        return ToNumber(primitive);
    }

    /// <summary>ToNumber(argument)</summary>
    internal static double ToNumber(JsValue value)
    {
        if (value.IsNumber)
            return value.NumberValue;

        switch (value.Cell)
        {
            case null:
                return double.NaN;
            case NullCell:
                return 0;
            case BooleanCell boolean:
                return boolean.Value ? 1 : 0;
            case JsString str:
                return StringToNumber(str);
            case JsSymbol:
                ThrowHelper.ThrowTypeError("Cannot convert a Symbol value to a number");
                return 0;
            case JsBigInt:
                ThrowHelper.ThrowTypeError("Cannot convert a BigInt value to a number");
                return 0;
            default:
                return ToNumber(ToPrimitive(value, PreferredType.Number));
        }
    }

    internal static double StringToNumber(JsString value) => NumberParsing.StringToNumber(value.ToString());

    /// <summary>StringToBigInt(str), or null when the string is not a valid StringIntegerLiteral.</summary>
    internal static BigInteger? StringToBigInt(string value)
    {
        var span = NumberParsing.TrimWhiteSpace(value.AsSpan());
        if (span.Length == 0)
            return BigInteger.Zero;

        var radix = 10;
        if (span.Length > 2 && span[0] == '0' && span[1] is 'x' or 'X' or 'o' or 'O' or 'b' or 'B')
        {
            radix = span[1] switch
            {
                'x' or 'X' => 16,
                'o' or 'O' => 8,
                _ => 2,
            };

            span = span[2..];
            if (span.Length == 0)
                return null;
        }

        var negative = false;
        if (radix == 10 && (span[0] == '+' || span[0] == '-'))
        {
            negative = span[0] == '-';
            span = span[1..];
            if (span.Length == 0)
                return null;
        }

        var result = BigInteger.Zero;
        foreach (var c in span)
        {
            var digit = NumberParsing.GetDigitValue(c);
            if (digit < 0 || digit >= radix)
                return null;

            result = (result * radix) + digit;
        }

        return negative ? -result : result;
    }

    /// <summary>ToIntegerOrInfinity(argument)</summary>
    internal static double ToIntegerOrInfinity(JsValue value)
    {
        var number = ToNumber(value);
        if (double.IsNaN(number) || number == 0)
            return 0;

        if (double.IsInfinity(number))
            return number;

        return Math.Truncate(number) + 0.0;
    }

    /// <summary>ToInt32(argument)</summary>
    internal static int ToInt32(JsValue value) => value.IsNumber ? DoubleToInt32(value.NumberValue) : DoubleToInt32(ToNumber(value));

    /// <summary>ToUint32(argument)</summary>
    internal static uint ToUint32(JsValue value) => (uint)ToInt32(value);

    internal static int DoubleToInt32(double value)
    {
        if (value is >= int.MinValue and <= int.MaxValue)
            return (int)value;

        if (double.IsNaN(value) || double.IsInfinity(value))
            return 0;

        var truncated = Math.Truncate(value);
        var modulo = truncated % 4294967296.0;
        if (modulo < 0)
        {
            modulo += 4294967296.0;
        }

        return (int)(uint)modulo;
    }

    /// <summary>ToLength(argument)</summary>
    internal static double ToLength(JsValue value)
    {
        var length = ToIntegerOrInfinity(value);
        if (length <= 0)
            return 0;

        return Math.Min(length, 9007199254740991);
    }

    /// <summary>ToIndex(value)</summary>
    internal static double ToIndex(JsValue value)
    {
        var integer = ToIntegerOrInfinity(value);
        if (integer is < 0 or > 9007199254740991)
        {
            ThrowHelper.ThrowRangeError("Invalid index");
        }

        return integer;
    }

    /// <summary>ToString(argument)</summary>
    internal static string ToString(JsValue value) => ToJsString(value).ToString();

    /// <summary>ToString(argument), without flattening a string value.</summary>
    internal static JsString ToJsString(JsValue value)
    {
        if (value.IsNumber)
            return JsString.Create(NumberFormatting.ToString(value.NumberValue));

        switch (value.Cell)
        {
            case JsString str:
                return str;
            case null:
                return JsString.Create("undefined");
            case NullCell:
                return JsString.Create("null");
            case BooleanCell boolean:
                return JsString.Create(boolean.Value ? "true" : "false");
            case JsSymbol:
                return ThrowHelper.ThrowTypeError<JsString>("Cannot convert a Symbol value to a string");
            case JsBigInt bigint:
                return JsString.Create(bigint.Value.ToString(CultureInfo.InvariantCulture));
            default:
                return ToJsString(ToPrimitive(value, PreferredType.String));
        }
    }

    /// <summary>ToPropertyKey(argument)</summary>
    internal static PropertyKey ToPropertyKey(JsValue value)
    {
        if (value.IsNumber)
            return PropertyKey.FromDouble(value.NumberValue);

        if (value.Cell is JsString str)
            return new PropertyKey(str.ToString());

        var key = ToPrimitive(value, PreferredType.String);
        if (key.Cell is JsSymbol symbol)
            return new PropertyKey(symbol);

        return new PropertyKey(ToString(key));
    }

    /// <summary>ToObject(argument)</summary>
    internal static JsObject ToObject(JsValue value)
    {
        if (value.Cell is JsObject obj)
            return obj;

        if (value.IsNullOrUndefined)
            return ThrowHelper.ThrowTypeError<JsObject>("Cannot convert undefined or null to object");

        var realm = JsEngine.Current!.CurrentRealm!;
        return realm.Intrinsics.CreatePrimitiveWrapper(value);
    }

    /// <summary>RequireObjectCoercible(argument)</summary>
    internal static void RequireObjectCoercible(JsValue value, string? methodName = null)
    {
        if (value.IsNullOrUndefined)
        {
            ThrowHelper.ThrowTypeError(methodName is null ? "Cannot convert undefined or null to object" : methodName + " called on null or undefined");
        }
    }

    /// <summary>GetMethod(V, P): the function, or null when the property is undefined or null.</summary>
    internal static JsObject? GetMethod(JsValue value, in PropertyKey key)
    {
        var function = GetV(value, key);
        if (function.IsNullOrUndefined)
            return null;

        if (function.Cell is not JsObject { IsCallable: true } callable)
            return ThrowHelper.ThrowTypeError<JsObject>($"{key} is not a function");

        return callable;
    }

    /// <summary>GetV(V, P): the property of a value (a primitive value reads the properties of its prototype).</summary>
    internal static JsValue GetV(JsValue value, in PropertyKey key)
    {
        if (value.Cell is JsObject obj)
            return obj.Get(key, value);

        return ToObject(value).Get(key, value);
    }

    /// <summary>Call(F, V, argumentsList)</summary>
    internal static JsValue Call(JsValue function, JsValue thisValue, ReadOnlySpan<JsValue> arguments)
    {
        if (function.Cell is not JsObject { IsCallable: true } callable)
            return ThrowHelper.ThrowTypeError<JsValue>($"{DescribeForError(function)} is not a function");

        return callable.Call(thisValue, arguments);
    }

    /// <summary>Invoke(V, P, argumentsList)</summary>
    internal static JsValue Invoke(JsValue value, in PropertyKey key, ReadOnlySpan<JsValue> arguments)
    {
        var function = GetV(value, key);
        return Call(function, value, arguments);
    }

    /// <summary>Construct(F, argumentsList, newTarget)</summary>
    internal static JsValue Construct(JsObject constructor, ReadOnlySpan<JsValue> arguments, JsObject? newTarget = null)
    {
        return constructor.Construct(arguments, newTarget ?? constructor);
    }

    /// <summary>IsCallable(argument)</summary>
    internal static bool IsCallable(JsValue value) => value.Cell is JsObject { IsCallable: true };

    /// <summary>IsConstructor(argument)</summary>
    internal static bool IsConstructor(JsValue value) => value.Cell is JsObject { IsConstructor: true };

    /// <summary>The result of the typeof operator.</summary>
    internal static string TypeOf(JsValue value)
    {
        return value.Kind switch
        {
            JsValueKind.Undefined => "undefined",
            JsValueKind.Null => "object",
            JsValueKind.Boolean => "boolean",
            JsValueKind.Number => "number",
            JsValueKind.String => "string",
            JsValueKind.Symbol => "symbol",
            JsValueKind.BigInt => "bigint",
            _ => value.AsObject() switch
            {
                { IsHtmlDda: true } => "undefined",
                { IsCallable: true } => "function",
                _ => "object",
            },
        };
    }

    /// <summary>Describes a value in an error message without running JavaScript code.</summary>
    internal static string DescribeForError(JsValue value)
    {
        return value.Kind switch
        {
            JsValueKind.String => "\"" + value.AsString() + "\"",
            JsValueKind.Object => value.AsObject().IsCallable ? "function" : "object",
            _ => value.ToString(),
        };
    }

    /// <summary>LengthOfArrayLike(obj)</summary>
    internal static long LengthOfArrayLike(JsObject obj) => (long)ToLength(obj.Get(new PropertyKey("length")));

    /// <summary>IsArray(argument): true for an array, or a proxy whose target is an array.</summary>
    internal static bool IsArray(JsValue value)
    {
        if (value.Cell is not JsObject obj)
            return false;

        while (true)
        {
            if (obj.Class == ObjectClass.Array)
                return true;

            if (obj.Class != ObjectClass.Proxy)
                return false;

            obj = ((IProxyTarget)obj).GetProxyTargetOrThrow();
        }
    }
}
