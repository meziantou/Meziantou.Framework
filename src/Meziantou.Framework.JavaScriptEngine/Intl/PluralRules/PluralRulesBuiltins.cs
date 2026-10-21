using Meziantou.Framework.JavaScriptEngine.Intl.Data;

namespace Meziantou.Framework.JavaScriptEngine.Intl;

/// <summary>An Intl.PluralRules instance: an ordinary object with the [[InitializedPluralRules]] internal slot.</summary>
internal sealed class JsPluralRulesObject(JsObject prototype) : JsObject(prototype, ObjectClass.Intl)
{
    public string Locale { get; set; } = "";

    /// <summary>[[Type]]: "cardinal" or "ordinal".</summary>
    public string Type { get; set; } = "cardinal";

    /// <summary>[[Notation]]: "standard", "scientific", "engineering" or "compact".</summary>
    public string Notation { get; set; } = "standard";

    /// <summary>[[CompactDisplay]]: "short" or "long".</summary>
    public string CompactDisplay { get; set; } = "short";

    public DigitOptions Digits { get; } = new();

    public CldrLocale DataLocale { get; set; } = null!;

    public Data.PluralRules Rules { get; set; } = null!;
}

/// <summary>Intl.PluralRules (https://tc39.es/ecma402/#pluralrules-objects).</summary>
internal static class PluralRulesBuiltins
{
    public static void Initialize(Intrinsics intrinsics, JsObject intl)
    {
        var realm = intrinsics.Realm;
        var prototype = intrinsics.IntlPluralRulesPrototype;
        var constructor = realm.CreateBuiltinFunction("PluralRules", 0, Construct, isConstructor: true);
        constructor.DefineBuiltIn("prototype", prototype, PropertyFlags.None);
        realm.DefineMethod(constructor, "supportedLocalesOf", 1, SupportedLocalesOf);
        prototype.DefineBuiltIn("constructor", constructor);
        prototype.DefineBuiltIn(WellKnownSymbols.ToStringTag, "Intl.PluralRules", PropertyFlags.Configurable);
        realm.DefineMethod(prototype, "resolvedOptions", 0, ResolvedOptions);
        realm.DefineMethod(prototype, "select", 1, Select);
        realm.DefineMethod(prototype, "selectRange", 2, SelectRange);
        intl.DefineBuiltIn("PluralRules", constructor);
    }

    /// <summary>[[AvailableLocales]] of PluralRules: the locales of the data and the locales that have plural rules.</summary>
    private static bool IsAvailable(string locale)
    {
        if (IntlOperations.IsAvailableLocale(locale))
            return true;

        return IntlOperations.Data.Supplemental.GetSection("plurals") is { } plurals && plurals.TryGetValue(locale, out _);
    }

    private static JsValue SupportedLocalesOf(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var requestedLocales = IntlOperations.CanonicalizeLocaleList(arguments.Length > 0 ? arguments[0] : default);
        return IntlOperations.SupportedLocales(function.Realm, requestedLocales, arguments.Length > 1 ? arguments[1] : default, IsAvailable);
    }

    /// <summary>Intl.PluralRules([locales [, options]])</summary>
    private static JsValue Construct(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (newTarget is null)
            return ThrowHelper.ThrowTypeError<JsValue>("Constructor Intl.PluralRules requires 'new'");

        var prototype = ObjectOperations.GetPrototypeForConstruct(function, newTarget, static i => i.IntlPluralRulesPrototype);
        var pluralRules = new JsPluralRulesObject(prototype);
        var requestedLocales = IntlOperations.CanonicalizeLocaleList(arguments.Length > 0 ? arguments[0] : default);
        var options = IntlOperations.CoerceOptionsToObject(arguments.Length > 1 ? arguments[1] : default);
        _ = IntlOperations.GetStringOption(options, "localeMatcher", ["lookup", "best fit"], "best fit");
        pluralRules.Type = IntlOperations.GetStringOption(options, "type", ["cardinal", "ordinal"], "cardinal")!;
        pluralRules.Notation = IntlOperations.GetStringOption(options, "notation", ["standard", "scientific", "engineering", "compact"], "standard")!;
        pluralRules.CompactDisplay = IntlOperations.GetStringOption(options, "compactDisplay", ["short", "long"], "short")!;
        pluralRules.Digits.Set(options, 0, 3, pluralRules.Notation);

        var r = IntlOperations.ResolveLocale(requestedLocales, [], static _ => null, static (_, _) => [], IsAvailable);
        pluralRules.Locale = r.Locale;
        pluralRules.DataLocale = IntlOperations.Data.FindLocale(r.DataLocale);
        pluralRules.Rules = IntlOperations.Data.Supplemental.GetPluralRules(r.DataLocale, pluralRules.Type is "ordinal" ? PluralRuleType.Ordinal : PluralRuleType.Cardinal);
        return pluralRules;
    }

    private static JsPluralRulesObject ThisPluralRules(JsValue thisValue, string method)
    {
        if (thisValue.Cell is JsPluralRulesObject pluralRules)
            return pluralRules;

        return ThrowHelper.ThrowTypeError<JsPluralRulesObject>($"Intl.PluralRules.prototype.{method} called on an incompatible receiver");
    }

    /// <summary>Intl.PluralRules.prototype.resolvedOptions()</summary>
    private static JsValue ResolvedOptions(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var pluralRules = ThisPluralRules(thisValue, "resolvedOptions");
        var realm = function.Realm;
        var options = new JsObject(realm.Intrinsics.ObjectPrototype);
        var digits = pluralRules.Digits;
        options.CreateDataPropertyOrThrow("locale", pluralRules.Locale);
        options.CreateDataPropertyOrThrow("type", pluralRules.Type);
        options.CreateDataPropertyOrThrow("notation", pluralRules.Notation);
        if (pluralRules.Notation is "compact")
        {
            options.CreateDataPropertyOrThrow("compactDisplay", pluralRules.CompactDisplay);
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

        options.CreateDataPropertyOrThrow("pluralCategories", IntlOperations.CreateArray(realm, pluralRules.Rules.Categories));
        options.CreateDataPropertyOrThrow("roundingIncrement", digits.RoundingIncrement);
        options.CreateDataPropertyOrThrow("roundingMode", digits.RoundingMode);
        options.CreateDataPropertyOrThrow("roundingPriority", digits.ComputedRoundingPriority);
        options.CreateDataPropertyOrThrow("trailingZeroDisplay", digits.TrailingZeroDisplay);
        return options;
    }

    /// <summary>Intl.PluralRules.prototype.select(value)</summary>
    private static JsValue Select(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var pluralRules = ThisPluralRules(thisValue, "select");
        var n = JsOperations.ToNumber(arguments.Length > 0 ? arguments[0] : default);
        return ResolvePlural(pluralRules, n).Category;
    }

    /// <summary>Intl.PluralRules.prototype.selectRange(start, end)</summary>
    private static JsValue SelectRange(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var pluralRules = ThisPluralRules(thisValue, "selectRange");
        var start = arguments.Length > 0 ? arguments[0] : default;
        var end = arguments.Length > 1 ? arguments[1] : default;
        if (start.IsUndefined || end.IsUndefined)
            ThrowHelper.ThrowTypeError("start and end are required");

        var x = JsOperations.ToNumber(start);
        var y = JsOperations.ToNumber(end);
        if (double.IsNaN(x) || double.IsNaN(y))
            ThrowHelper.ThrowRangeError("start and end must not be NaN");

        var xp = ResolvePlural(pluralRules, x);
        var yp = ResolvePlural(pluralRules, y);
        if (xp.FormattedString == yp.FormattedString)
            return xp.Category;

        return SelectRange(pluralRules.Rules, xp.Category, yp.Category);
    }

    /// <summary>PluralRuleSelectRange: the category of a range from the pluralRanges data of CLDR, else "other" (as ICU).</summary>
    internal static string SelectRange(Data.PluralRules rules, string start, string end)
    {
        var supplemental = IntlOperations.Data.Supplemental;
        var key = rules.Locale;
        while (true)
        {
            if (supplemental.GetPluralRange(key, start, end) is { } result)
                return result;

            var separator = key.LastIndexOf('-', StringComparison.Ordinal);
            if (separator < 0)
                break;

            key = key[..separator];
        }

        return "other";
    }

    /// <summary>ResolvePlural(pluralRules, n)</summary>
    internal static (string Category, string FormattedString) ResolvePlural(JsPluralRulesObject pluralRules, double n)
    {
        if (!double.IsFinite(n))
            return ("other", NumberFormatting.ToString(n));

        var x = IntlDecimal.FromDouble(n);
        var (exponent, _) = Notation.ComputeExponent(pluralRules.Digits, pluralRules.Notation, pluralRules.DataLocale, "latn", pluralRules.CompactDisplay, currency: false, x);
        var formatted = pluralRules.Digits.FormatNumericToString(x.ScaleByPowerOfTen(-exponent));
        var category = SelectCategory(pluralRules.Rules, formatted.FormattedString, exponent);
        return (category, formatted.FormattedString);
    }

    /// <summary>PluralRuleSelect: the category of a formatted number (digits, '.', without sign) with the exponent of its notation.</summary>
    internal static string SelectCategory(Data.PluralRules rules, string formatted, int exponent)
    {
        var text = exponent > 0 ? formatted + "c" + exponent.ToString(System.Globalization.CultureInfo.InvariantCulture) : formatted;
        if (!PluralOperands.TryParse(text, out var operands))
            return "other";

        return rules.Select(operands);
    }
}
