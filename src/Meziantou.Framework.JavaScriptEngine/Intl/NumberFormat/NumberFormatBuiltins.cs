namespace Meziantou.Framework.JavaScriptEngine.Intl;

/// <summary>Intl.NumberFormat (https://tc39.es/ecma402/#numberformat-objects).</summary>
internal static class NumberFormatBuiltins
{
    private static readonly string[] RelevantExtensionKeys = ["nu"];

    private static readonly BuiltinMethodTable PrototypeMethods =
    [
        new("formatToParts", 1, FormatToParts),
        new("formatRange", 2, FormatRange),
        new("formatRangeToParts", 2, FormatRangeToParts),
        new("resolvedOptions", 0, ResolvedOptions),
    ];

    public static JsFunction Initialize(Intrinsics intrinsics, JsObject intl)
    {
        var realm = intrinsics.Realm;
        var prototype = intrinsics.IntlNumberFormatPrototype;
        var constructor = realm.CreateBuiltinFunction("NumberFormat", 0, Construct, isConstructor: true);
        constructor.DefineBuiltIn("prototype", prototype, PropertyFlags.None);
        realm.DefineMethod(constructor, "supportedLocalesOf", 1, SupportedLocalesOf);
        prototype.DefineBuiltIn("constructor", constructor);
        prototype.DefineBuiltIn(WellKnownSymbols.ToStringTag, "Intl.NumberFormat", PropertyFlags.Configurable);
        realm.DefineAccessor(prototype, "format", GetFormat, setter: null);
        realm.DefineMethods(prototype, PrototypeMethods);
        intl.DefineBuiltIn("NumberFormat", constructor);
        return constructor;
    }

    private static JsValue SupportedLocalesOf(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var requestedLocales = IntlOperations.CanonicalizeLocaleList(arguments.Length > 0 ? arguments[0] : default);
        return IntlOperations.SupportedLocales(function.Realm, requestedLocales, arguments.Length > 1 ? arguments[1] : default);
    }

    /// <summary>The locale data of the "nu" key: the default numbering system of the locale, then the other supported ones.</summary>
    internal static IReadOnlyList<string?> GetNumberingSystemLocaleData(string locale)
    {
        var data = IntlOperations.Data.FindLocale(locale);
        var defaultNumberingSystem = data.GetDefaultNumberingSystem();
        if (!LocaleInfo.IsAvailableNumberingSystem(defaultNumberingSystem))
        {
            defaultNumberingSystem = "latn";
        }

        var result = new List<string?>(LocaleInfo.NumberingSystems.Length + 1) { defaultNumberingSystem };
        foreach (var numberingSystem in LocaleInfo.NumberingSystems)
        {
            if (numberingSystem != defaultNumberingSystem)
            {
                result.Add(numberingSystem);
            }
        }

        return result;
    }

    /// <summary>Intl.NumberFormat([locales [, options]])</summary>
    private static JsValue Construct(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var prototype = ObjectOperations.GetPrototypeForConstruct(function, newTarget, static i => i.IntlNumberFormatPrototype);
        var numberFormat = new JsNumberFormatObject(prototype);
        Initialize(numberFormat, arguments.Length > 0 ? arguments[0] : default, arguments.Length > 1 ? arguments[1] : default);

        // ChainNumberFormat (the normative optional constructor mode)
        if (newTarget is null && thisValue.TryGetObject() is { } thisObject && ObjectOperations.OrdinaryHasInstance(function.Realm.Intrinsics.IntlNumberFormat, thisValue))
        {
            thisObject.DefinePropertyOrThrow(function.Realm.Intrinsics.IntlFallbackSymbol, PropertyDescriptor.Data(numberFormat, PropertyFlags.None));
            return thisObject;
        }

        return numberFormat;
    }

    /// <summary>Creates a NumberFormat with the default locale and options (new Intl.NumberFormat()).</summary>
    internal static JsNumberFormatObject CreateDefault(Intrinsics intrinsics)
    {
        var numberFormat = new JsNumberFormatObject(intrinsics.IntlNumberFormatPrototype);
        Initialize(numberFormat, JsValue.Undefined, JsValue.Undefined);
        return numberFormat;
    }

    /// <summary>InitializeNumberFormat(numberFormat, locales, options)</summary>
    internal static void Initialize(JsNumberFormatObject numberFormat, JsValue locales, JsValue optionsValue)
    {
        var requestedLocales = IntlOperations.CanonicalizeLocaleList(locales);
        var options = IntlOperations.CoerceOptionsToObject(optionsValue);
        _ = IntlOperations.GetStringOption(options, "localeMatcher", ["lookup", "best fit"], "best fit");
        var numberingSystem = IntlOperations.GetStringOption(options, "numberingSystem", [], fallback: null);
        if (numberingSystem is not null && !LanguageTag.IsUnicodeType(numberingSystem))
            ThrowHelper.ThrowRangeError("Invalid numberingSystem : " + numberingSystem);

        var r = IntlOperations.ResolveLocale(requestedLocales, RelevantExtensionKeys, key => numberingSystem, static (locale, _) => GetNumberingSystemLocaleData(locale));
        numberFormat.Locale = r.Locale;
        numberFormat.DataLocale = IntlOperations.Data.FindLocale(r.DataLocale);
        numberFormat.NumberingSystem = r.Get("nu") ?? "latn";

        SetUnitOptions(numberFormat, options);
        var style = numberFormat.Style;
        var notation = IntlOperations.GetStringOption(options, "notation", ["standard", "scientific", "engineering", "compact"], "standard")!;
        numberFormat.Notation = notation;
        int mnfdDefault;
        int mxfdDefault;
        if (style is "currency" && notation is "standard")
        {
            var digits = IntlOperations.Data.Supplemental.GetCurrencyDigits(numberFormat.Currency!).Digits;
            mnfdDefault = digits;
            mxfdDefault = digits;
        }
        else
        {
            mnfdDefault = 0;
            mxfdDefault = style is "percent" ? 0 : 3;
        }

        numberFormat.Digits.Set(options, mnfdDefault, mxfdDefault, notation);
        var compactDisplay = IntlOperations.GetStringOption(options, "compactDisplay", ["short", "long"], "short")!;
        var defaultUseGrouping = "auto";
        if (notation is "compact")
        {
            numberFormat.CompactDisplay = compactDisplay;
            defaultUseGrouping = "min2";
        }

        numberFormat.UseGrouping = GetUseGroupingOption(options, defaultUseGrouping);
        numberFormat.SignDisplay = IntlOperations.GetStringOption(options, "signDisplay", ["auto", "never", "always", "exceptZero", "negative"], "auto")!;
    }

    // GetBooleanOrStringNumberFormatOption(options, "useGrouping", \u00AB "min2", "auto", "always", "true", "false" \u00BB, defaultUseGrouping)
    private static string? GetUseGroupingOption(JsObject options, string fallback)
    {
        var value = options.Get("useGrouping");
        if (value.IsUndefined)
            return fallback;

        if (value.IsBoolean && value.AsBoolean())
            return "always";

        if (!JsOperations.ToBoolean(value))
            return null;

        var text = JsOperations.ToString(value);
        return text switch
        {
            "min2" or "auto" or "always" => text,
            "true" or "false" => fallback,
            _ => ThrowHelper.ThrowRangeError<string>("Invalid useGrouping : " + text),
        };
    }

    /// <summary>SetNumberFormatUnitOptions(intlObj, options)</summary>
    private static void SetUnitOptions(JsNumberFormatObject numberFormat, JsObject options)
    {
        var style = IntlOperations.GetStringOption(options, "style", ["decimal", "percent", "currency", "unit"], "decimal")!;
        numberFormat.Style = style;
        var currency = IntlOperations.GetStringOption(options, "currency", [], fallback: null);
        if (currency is null)
        {
            if (style is "currency")
                ThrowHelper.ThrowTypeError("Currency code is required with currency style.");
        }
        else if (!IsWellFormedCurrencyCode(currency))
        {
            ThrowHelper.ThrowRangeError("Invalid currency code : " + currency);
        }

        var currencyDisplay = IntlOperations.GetStringOption(options, "currencyDisplay", ["code", "symbol", "narrowSymbol", "name"], "symbol")!;
        var currencySign = IntlOperations.GetStringOption(options, "currencySign", ["standard", "accounting"], "standard")!;
        var unit = IntlOperations.GetStringOption(options, "unit", [], fallback: null);
        if (unit is null)
        {
            if (style is "unit")
                ThrowHelper.ThrowTypeError("Unit is required with unit style.");
        }
        else if (!IsWellFormedUnitIdentifier(unit))
        {
            ThrowHelper.ThrowRangeError("Invalid unit argument : " + unit);
        }

        var unitDisplay = IntlOperations.GetStringOption(options, "unitDisplay", ["short", "narrow", "long"], "short")!;
        if (style is "currency")
        {
            numberFormat.Currency = currency!.ToUpperInvariant();
            numberFormat.CurrencyDisplay = currencyDisplay;
            numberFormat.CurrencySign = currencySign;
        }

        if (style is "unit")
        {
            numberFormat.Unit = unit;
            numberFormat.UnitDisplay = unitDisplay;
        }
    }

    /// <summary>IsWellFormedCurrencyCode(currency): three ASCII letters.</summary>
    internal static bool IsWellFormedCurrencyCode(string currency) => currency.Length == 3 && char.IsAsciiLetter(currency[0]) && char.IsAsciiLetter(currency[1]) && char.IsAsciiLetter(currency[2]);

    /// <summary>IsWellFormedUnitIdentifier(unitIdentifier)</summary>
    internal static bool IsWellFormedUnitIdentifier(string unit)
    {
        if (IsSanctionedSingleUnitIdentifier(unit))
            return true;

        var per = unit.IndexOf("-per-", StringComparison.Ordinal);
        if (per < 0 || unit.IndexOf("-per-", per + 1, StringComparison.Ordinal) >= 0)
            return false;

        return IsSanctionedSingleUnitIdentifier(unit[..per]) && IsSanctionedSingleUnitIdentifier(unit[(per + 5)..]);
    }

    private static bool IsSanctionedSingleUnitIdentifier(string unit) => Array.BinarySearch(LocaleInfo.SanctionedUnits, unit, StringComparer.Ordinal) >= 0;

    /// <summary>UnwrapNumberFormat(nf) then RequireInternalSlot([[InitializedNumberFormat]])</summary>
    private static JsNumberFormatObject UnwrapNumberFormat(JsBuiltinFunction function, JsValue value, string method)
    {
        if (value.TryGetObject() is not { } obj)
            return ThrowHelper.ThrowTypeError<JsNumberFormatObject>($"Intl.NumberFormat.prototype.{method} called on an incompatible receiver");

        if (obj is not JsNumberFormatObject && ObjectOperations.OrdinaryHasInstance(function.Realm.Intrinsics.IntlNumberFormat, value))
        {
            value = obj.Get(function.Realm.Intrinsics.IntlFallbackSymbol);
        }

        return ThisNumberFormat(value, method);
    }

    private static JsNumberFormatObject ThisNumberFormat(JsValue thisValue, string method)
    {
        if (thisValue.Cell is JsNumberFormatObject numberFormat)
            return numberFormat;

        return ThrowHelper.ThrowTypeError<JsNumberFormatObject>($"Intl.NumberFormat.prototype.{method} called on an incompatible receiver");
    }

    /// <summary>ToIntlMathematicalValue(value)</summary>
    internal static IntlDecimal ToIntlMathematicalValue(JsValue value)
    {
        if (value.IsNumber)
            return IntlDecimal.FromNumber(value.NumberValue);

        var primitive = JsOperations.ToPrimitive(value, JsOperations.PreferredType.Number);
        if (primitive.IsBigInt)
            return IntlDecimal.FromBigInteger(primitive.AsBigInt());

        if (primitive.IsString)
            return IntlDecimal.Parse(JsOperations.ToString(primitive));

        return IntlDecimal.FromNumber(JsOperations.ToNumber(primitive));
    }

    /// <summary>get Intl.NumberFormat.prototype.format</summary>
    private static JsValue GetFormat(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var numberFormat = UnwrapNumberFormat(function, thisValue, "format");
        if (numberFormat.BoundFormat is null)
        {
            var bound = function.Realm.CreateBuiltinFunction("", 1, static (f, t, a, n) =>
            {
                var nf = (JsNumberFormatObject)f.State!;
                var x = ToIntlMathematicalValue(a.Length > 0 ? a[0] : default);
                return NumberFormatter.Format(nf, x);
            });
            bound.State = numberFormat;
            numberFormat.BoundFormat = bound;
        }

        return numberFormat.BoundFormat;
    }

    /// <summary>Intl.NumberFormat.prototype.formatToParts(value)</summary>
    private static JsValue FormatToParts(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var numberFormat = ThisNumberFormat(thisValue, "formatToParts");
        var x = ToIntlMathematicalValue(arguments.Length > 0 ? arguments[0] : default);
        var parts = NumberFormatter.PartitionNumberPattern(numberFormat, x).ToList();
        return CreatePartsArray(function.Realm, parts);
    }

    internal static JsArray CreatePartsArray(JsRealm realm, List<NumberPart> parts)
    {
        var items = new JsValue[parts.Count];
        for (var i = 0; i < parts.Count; i++)
        {
            var o = new JsObject(realm.Intrinsics.ObjectPrototype);
            o.CreateDataPropertyOrThrow("type", parts[i].Type);
            o.CreateDataPropertyOrThrow("value", parts[i].Value);
            items[i] = o;
        }

        return JsArray.Create(realm, items);
    }

    private static (IntlDecimal X, IntlDecimal Y) GetRangeArguments(ReadOnlySpan<JsValue> arguments)
    {
        var start = arguments.Length > 0 ? arguments[0] : default;
        var end = arguments.Length > 1 ? arguments[1] : default;
        if (start.IsUndefined || end.IsUndefined)
            ThrowHelper.ThrowTypeError("start and end are required");

        var x = ToIntlMathematicalValue(start);
        var y = ToIntlMathematicalValue(end);
        return (x, y);
    }

    /// <summary>Intl.NumberFormat.prototype.formatRange(start, end)</summary>
    private static JsValue FormatRange(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var numberFormat = ThisNumberFormat(thisValue, "formatRange");
        var (x, y) = GetRangeArguments(arguments);
        var builder = new StringBuilder();
        foreach (var (part, _) in NumberFormatter.PartitionNumberRangePattern(numberFormat, x, y))
        {
            builder.Append(part.Value);
        }

        return builder.ToString();
    }

    /// <summary>Intl.NumberFormat.prototype.formatRangeToParts(start, end)</summary>
    private static JsValue FormatRangeToParts(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var numberFormat = ThisNumberFormat(thisValue, "formatRangeToParts");
        var (x, y) = GetRangeArguments(arguments);
        var parts = NumberFormatter.PartitionNumberRangePattern(numberFormat, x, y);
        var realm = function.Realm;
        var items = new JsValue[parts.Count];
        for (var i = 0; i < parts.Count; i++)
        {
            var o = new JsObject(realm.Intrinsics.ObjectPrototype);
            o.CreateDataPropertyOrThrow("type", parts[i].Part.Type);
            o.CreateDataPropertyOrThrow("value", parts[i].Part.Value);
            o.CreateDataPropertyOrThrow("source", parts[i].Source);
            items[i] = o;
        }

        return JsArray.Create(realm, items);
    }

    /// <summary>Intl.NumberFormat.prototype.resolvedOptions()</summary>
    private static JsValue ResolvedOptions(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var nf = UnwrapNumberFormat(function, thisValue, "resolvedOptions");
        var options = new JsObject(function.Realm.Intrinsics.ObjectPrototype);
        var digits = nf.Digits;
        options.CreateDataPropertyOrThrow("locale", nf.Locale);
        options.CreateDataPropertyOrThrow("numberingSystem", nf.NumberingSystem);
        options.CreateDataPropertyOrThrow("style", nf.Style);
        if (nf.Style is "currency")
        {
            options.CreateDataPropertyOrThrow("currency", nf.Currency!);
            options.CreateDataPropertyOrThrow("currencyDisplay", nf.CurrencyDisplay);
            options.CreateDataPropertyOrThrow("currencySign", nf.CurrencySign);
        }

        if (nf.Style is "unit")
        {
            options.CreateDataPropertyOrThrow("unit", nf.Unit!);
            options.CreateDataPropertyOrThrow("unitDisplay", nf.UnitDisplay);
        }

        options.CreateDataPropertyOrThrow("minimumIntegerDigits", digits.MinimumIntegerDigits);
        if (digits.HasFractionDigits)
        {
            options.CreateDataPropertyOrThrow("minimumFractionDigits", digits.MinimumFractionDigits);
            options.CreateDataPropertyOrThrow("maximumFractionDigits", digits.MaximumFractionDigits);
        }

        if (digits.HasSignificantDigits)
        {
            options.CreateDataPropertyOrThrow("minimumSignificantDigits", digits.MinimumSignificantDigits);
            options.CreateDataPropertyOrThrow("maximumSignificantDigits", digits.MaximumSignificantDigits);
        }

        options.CreateDataPropertyOrThrow("useGrouping", nf.UseGrouping is null ? JsValue.False : nf.UseGrouping);
        options.CreateDataPropertyOrThrow("notation", nf.Notation);
        if (nf.Notation is "compact")
        {
            options.CreateDataPropertyOrThrow("compactDisplay", nf.CompactDisplay);
        }

        options.CreateDataPropertyOrThrow("signDisplay", nf.SignDisplay);
        options.CreateDataPropertyOrThrow("roundingIncrement", digits.RoundingIncrement);
        options.CreateDataPropertyOrThrow("roundingMode", digits.RoundingMode);
        options.CreateDataPropertyOrThrow("roundingPriority", digits.ComputedRoundingPriority);
        options.CreateDataPropertyOrThrow("trailingZeroDisplay", digits.TrailingZeroDisplay);
        return options;
    }
}
