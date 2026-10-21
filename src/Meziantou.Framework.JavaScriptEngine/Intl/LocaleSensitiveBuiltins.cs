namespace Meziantou.Framework.JavaScriptEngine.Intl;

/// <summary>
/// The locale-sensitive methods of ECMA-262 that ECMA-402 replaces (https://tc39.es/ecma402/#locale-sensitive-functions):
/// Number.prototype.toLocaleString, BigInt.prototype.toLocaleString and Array.prototype.toLocaleString.
/// </summary>
internal static class LocaleSensitiveBuiltins
{
    private static readonly PropertyKey ToLocaleStringKey = new("toLocaleString");

    public static void Initialize(Intrinsics intrinsics)
    {
        var realm = intrinsics.Realm;
        realm.DefineMethod(intrinsics.NumberPrototype, "toLocaleString", 0, NumberToLocaleString);
        realm.DefineMethod(intrinsics.BigIntPrototype, "toLocaleString", 0, BigIntToLocaleString);
        realm.DefineMethod(intrinsics.ArrayPrototype, "toLocaleString", 0, ArrayToLocaleString);
    }

    private static JsNumberFormatObject CreateNumberFormat(JsBuiltinFunction function, ReadOnlySpan<JsValue> arguments)
    {
        var intrinsics = function.Realm.Intrinsics;
        var locales = arguments.Length > 0 ? arguments[0] : default;
        var options = arguments.Length > 1 ? arguments[1] : default;
        if (locales.IsUndefined && options.IsUndefined)
            return intrinsics.DefaultNumberFormat;

        var constructor = intrinsics.IntlNumberFormat;
        return (JsNumberFormatObject)JsOperations.Construct(constructor, [locales, options], constructor).TryGetObject()!;
    }

    /// <summary>Number.prototype.toLocaleString([locales [, options]])</summary>
    private static JsValue NumberToLocaleString(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var x = NumberBuiltins.ThisNumberValue(thisValue, "toLocaleString");
        var numberFormat = CreateNumberFormat(function, arguments);
        return NumberFormatter.Format(numberFormat, IntlDecimal.FromDouble(x));
    }

    /// <summary>BigInt.prototype.toLocaleString([locales [, options]])</summary>
    private static JsValue BigIntToLocaleString(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var x = BigIntBuiltins.ThisBigIntValue(thisValue, "toLocaleString");
        var numberFormat = CreateNumberFormat(function, arguments);
        return NumberFormatter.Format(numberFormat, IntlDecimal.FromBigInteger(x));
    }

    /// <summary>Array.prototype.toLocaleString([locales [, options]])</summary>
    private static JsValue ArrayToLocaleString(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var locales = arguments.Length > 0 ? arguments[0] : default;
        var options = arguments.Length > 1 ? arguments[1] : default;
        var array = JsOperations.ToObject(thisValue);
        var length = JsOperations.LengthOfArrayLike(array);
        var result = new StringBuilder();
        for (long k = 0; k < length; k++)
        {
            if (k > 0)
            {
                // The implementation-defined list-separator String
                StringOperations.CheckLength((long)result.Length + 1);
                result.Append(',');
            }

            var nextElement = ArrayOperations.GetElement(array, k);
            if (!nextElement.IsNullOrUndefined)
            {
                var s = JsOperations.ToString(JsOperations.Invoke(nextElement, ToLocaleStringKey, [locales, options]));
                StringOperations.CheckLength((long)result.Length + s.Length);
                result.Append(s);
            }
        }

        return result.ToString();
    }
}
