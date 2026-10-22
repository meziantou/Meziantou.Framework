namespace Meziantou.Framework.JavaScriptEngine.Intl;

/// <summary>
/// The locale-sensitive methods of ECMA-262 that ECMA-402 replaces (https://tc39.es/ecma402/#locale-sensitive-functions):
/// Number.prototype.toLocaleString, BigInt.prototype.toLocaleString, Array.prototype.toLocaleString, the case
/// conversions of String.prototype and Temporal.Duration.prototype.toLocaleString.
/// </summary>
internal static class LocaleSensitiveBuiltins
{
    private static readonly PropertyKey ToLocaleStringKey = new("toLocaleString");

    private static readonly BuiltinMethodTable StringPrototypeMethods =
    [
        new("toLocaleLowerCase", 0, (function, thisValue, arguments, _) => TransformCase(thisValue, arguments, upper: false, "toLocaleLowerCase")),
        new("toLocaleUpperCase", 0, (function, thisValue, arguments, _) => TransformCase(thisValue, arguments, upper: true, "toLocaleUpperCase")),
    ];

    public static void Initialize(Intrinsics intrinsics)
    {
        var realm = intrinsics.Realm;
        realm.DefineMethod(intrinsics.NumberPrototype, "toLocaleString", 0, NumberToLocaleString);
        realm.DefineMethod(intrinsics.BigIntPrototype, "toLocaleString", 0, BigIntToLocaleString);
        realm.DefineMethod(intrinsics.ArrayPrototype, "toLocaleString", 0, ArrayToLocaleString);
        realm.DefineMethods(intrinsics.StringPrototype, StringPrototypeMethods);
    }

    /// <summary>Temporal.Duration.prototype.toLocaleString (the Temporal built-ins are created on first use).</summary>
    public static void InitializeTemporal(Intrinsics intrinsics) => intrinsics.Realm.DefineMethod(intrinsics.TemporalDurationPrototype, "toLocaleString", 0, DurationToLocaleString);

    /// <summary>The languages whose case mappings differ from the default ones (SpecialCasing.txt).</summary>
    private static bool HasLanguageSensitiveCaseMapping(string locale) => locale is "az" or "lt" or "tr";

    /// <summary>String.prototype.toLocaleLowerCase / toLocaleUpperCase (TransformCase, https://tc39.es/ecma402/#sec-transform-case).</summary>
    private static JsValue TransformCase(JsValue thisValue, ReadOnlySpan<JsValue> arguments, bool upper, string methodName)
    {
        if (thisValue.IsNullOrUndefined)
            return ThrowHelper.ThrowTypeError<JsValue>("String.prototype." + methodName + " called on null or undefined");

        var s = JsOperations.ToString(thisValue);
        var requestedLocales = IntlOperations.CanonicalizeLocaleList(arguments.Length > 0 ? arguments[0] : default);
        var requestedLocale = requestedLocales.Count > 0 ? requestedLocales[0] : IntlOperations.DefaultLocale(JsEngine.Current!);
        var noExtensionsLocale = IntlOperations.RemoveUnicodeExtension(requestedLocale, out _);
        var language = IntlOperations.LookupMatchingLocaleByPrefix(noExtensionsLocale, HasLanguageSensitiveCaseMapping);
        if (language is null)
            return upper ? StringOperations.ToUpperCase(s) : StringOperations.ToLowerCase(s);

        return upper ? UnicodeCaseMapping.ToUpper(s, language) : UnicodeCaseMapping.ToLower(s, language);
    }

    /// <summary>Temporal.Duration.prototype.toLocaleString([locales [, options]]): the format of an Intl.DurationFormat.</summary>
    private static JsValue DurationToLocaleString(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (thisValue.Cell is not JsDuration)
            return ThrowHelper.ThrowTypeError<JsValue>("Temporal.Duration.prototype.toLocaleString called on an object that is not a Temporal.Duration");

        var constructor = function.Realm.Intrinsics.IntlDurationFormat;
        var locales = arguments.Length > 0 ? arguments[0] : default;
        var options = arguments.Length > 1 ? arguments[1] : default;
        var formatter = JsOperations.Construct(constructor, [locales, options], constructor);
        return DurationFormatBuiltins.FormatDuration(formatter, thisValue);
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
