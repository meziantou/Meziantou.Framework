using Meziantou.Framework.JavaScriptEngine.Intl.Data;

namespace Meziantou.Framework.JavaScriptEngine.Intl;

/// <summary>An Intl.RelativeTimeFormat instance: an ordinary object with the [[InitializedRelativeTimeFormat]] internal slot.</summary>
internal sealed class JsRelativeTimeFormatObject(JsObject prototype) : JsObject(prototype, ObjectClass.Intl)
{
    public string Locale { get; set; } = "";

    public CldrLocale DataLocale { get; set; } = null!;

    public string NumberingSystem { get; set; } = "latn";

    /// <summary>[[Style]]: "long", "short" or "narrow".</summary>
    public string Style { get; set; } = "long";

    /// <summary>[[Numeric]]: "always" or "auto".</summary>
    public string Numeric { get; set; } = "always";

    /// <summary>[[NumberFormat]]</summary>
    public JsNumberFormatObject NumberFormat { get; set; } = null!;
}

/// <summary>A part of a formatted relative time: the parts of the number have the unit.</summary>
internal readonly record struct RelativeTimePart(string Type, string Value, string? Unit);

/// <summary>Intl.RelativeTimeFormat (https://tc39.es/ecma402/#relativetimeformat-objects).</summary>
internal static class RelativeTimeFormatBuiltins
{
    private static readonly string[] RelevantExtensionKeys = ["nu"];

    public static JsFunction Initialize(Intrinsics intrinsics, JsObject intl, JsObject prototype)
    {
        var realm = intrinsics.Realm;
        var constructor = realm.CreateBuiltinFunction("RelativeTimeFormat", 0, Construct, isConstructor: true);
        constructor.DefineBuiltIn("prototype", prototype, PropertyFlags.None);
        realm.DefineMethod(constructor, "supportedLocalesOf", 1, SupportedLocalesOf);
        prototype.DefineBuiltIn("constructor", constructor);
        prototype.DefineBuiltIn(WellKnownSymbols.ToStringTag, "Intl.RelativeTimeFormat", PropertyFlags.Configurable);
        realm.DefineMethod(prototype, "format", 2, Format);
        realm.DefineMethod(prototype, "formatToParts", 2, FormatToParts);
        realm.DefineMethod(prototype, "resolvedOptions", 0, ResolvedOptions);
        intl.DefineBuiltIn("RelativeTimeFormat", constructor);
        return constructor;
    }

    private static JsValue SupportedLocalesOf(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var requestedLocales = IntlOperations.CanonicalizeLocaleList(arguments.Length > 0 ? arguments[0] : default);
        return IntlOperations.SupportedLocales(function.Realm, requestedLocales, arguments.Length > 1 ? arguments[1] : default);
    }

    /// <summary>Intl.RelativeTimeFormat([locales [, options]])</summary>
    private static JsValue Construct(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (newTarget is null)
            return ThrowHelper.ThrowTypeError<JsValue>("Constructor Intl.RelativeTimeFormat requires 'new'");

        var prototype = ObjectOperations.GetPrototypeForConstruct(function, newTarget, static i => i.IntlRelativeTimeFormatPrototype);
        var relativeTimeFormat = new JsRelativeTimeFormatObject(prototype);
        var requestedLocales = IntlOperations.CanonicalizeLocaleList(arguments.Length > 0 ? arguments[0] : default);
        var options = IntlOperations.CoerceOptionsToObject(arguments.Length > 1 ? arguments[1] : default);
        _ = IntlOperations.GetStringOption(options, "localeMatcher", ["lookup", "best fit"], "best fit");
        var numberingSystem = IntlOperations.GetStringOption(options, "numberingSystem", [], fallback: null);
        if (numberingSystem is not null && !LanguageTag.IsUnicodeType(numberingSystem))
            ThrowHelper.ThrowRangeError("Invalid numberingSystem : " + numberingSystem);

        var r = IntlOperations.ResolveLocale(requestedLocales, RelevantExtensionKeys, _ => numberingSystem, static (locale, _) => NumberFormatBuiltins.GetNumberingSystemLocaleData(locale));
        relativeTimeFormat.Locale = r.Locale;
        relativeTimeFormat.DataLocale = IntlOperations.Data.FindLocale(r.DataLocale);
        relativeTimeFormat.NumberingSystem = r.Get("nu") ?? "latn";
        relativeTimeFormat.Style = IntlOperations.GetStringOption(options, "style", ["long", "short", "narrow"], "long")!;
        relativeTimeFormat.Numeric = IntlOperations.GetStringOption(options, "numeric", ["always", "auto"], "always")!;

        var realm = function.Realm;
        var numberFormatOptions = new JsObject(prototype: null);
        numberFormatOptions.CreateDataPropertyOrThrow("numberingSystem", relativeTimeFormat.NumberingSystem);
        var numberFormat = new JsNumberFormatObject(realm.Intrinsics.IntlNumberFormatPrototype);
        NumberFormatBuiltins.Initialize(numberFormat, relativeTimeFormat.Locale, numberFormatOptions);
        relativeTimeFormat.NumberFormat = numberFormat;
        return relativeTimeFormat;
    }

    private static JsRelativeTimeFormatObject ThisRelativeTimeFormat(JsValue thisValue, string method)
    {
        if (thisValue.Cell is JsRelativeTimeFormatObject relativeTimeFormat)
            return relativeTimeFormat;

        return ThrowHelper.ThrowTypeError<JsRelativeTimeFormatObject>($"Intl.RelativeTimeFormat.prototype.{method} called on an incompatible receiver");
    }

    /// <summary>Intl.RelativeTimeFormat.prototype.format(value, unit)</summary>
    private static JsValue Format(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var relativeTimeFormat = ThisRelativeTimeFormat(thisValue, "format");
        var value = JsOperations.ToNumber(arguments.Length > 0 ? arguments[0] : default);
        var unit = JsOperations.ToString(arguments.Length > 1 ? arguments[1] : default);
        var parts = PartitionRelativeTimePattern(relativeTimeFormat, value, unit);
        var builder = new StringBuilder();
        foreach (var part in parts)
        {
            builder.Append(part.Value);
        }

        return builder.ToString();
    }

    /// <summary>Intl.RelativeTimeFormat.prototype.formatToParts(value, unit)</summary>
    private static JsValue FormatToParts(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var relativeTimeFormat = ThisRelativeTimeFormat(thisValue, "formatToParts");
        var value = JsOperations.ToNumber(arguments.Length > 0 ? arguments[0] : default);
        var unit = JsOperations.ToString(arguments.Length > 1 ? arguments[1] : default);
        var parts = PartitionRelativeTimePattern(relativeTimeFormat, value, unit);
        var realm = function.Realm;
        var items = new JsValue[parts.Count];
        for (var i = 0; i < parts.Count; i++)
        {
            var o = new JsObject(realm.Intrinsics.ObjectPrototype);
            o.CreateDataPropertyOrThrow("type", parts[i].Type);
            o.CreateDataPropertyOrThrow("value", parts[i].Value);
            if (parts[i].Unit is { } partUnit)
            {
                o.CreateDataPropertyOrThrow("unit", partUnit);
            }

            items[i] = o;
        }

        return JsArray.Create(realm, items);
    }

    /// <summary>Intl.RelativeTimeFormat.prototype.resolvedOptions()</summary>
    private static JsValue ResolvedOptions(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var relativeTimeFormat = ThisRelativeTimeFormat(thisValue, "resolvedOptions");
        var options = new JsObject(function.Realm.Intrinsics.ObjectPrototype);
        options.CreateDataPropertyOrThrow("locale", relativeTimeFormat.Locale);
        options.CreateDataPropertyOrThrow("style", relativeTimeFormat.Style);
        options.CreateDataPropertyOrThrow("numeric", relativeTimeFormat.Numeric);
        options.CreateDataPropertyOrThrow("numberingSystem", relativeTimeFormat.NumberingSystem);
        return options;
    }

    /// <summary>SingularRelativeTimeUnit(unit): the singular unit, or null when the unit is not valid.</summary>
    internal static string? SingularRelativeTimeUnit(string unit)
    {
        return unit switch
        {
            "second" or "seconds" => "second",
            "minute" or "minutes" => "minute",
            "hour" or "hours" => "hour",
            "day" or "days" => "day",
            "week" or "weeks" => "week",
            "month" or "months" => "month",
            "quarter" or "quarters" => "quarter",
            "year" or "years" => "year",
            _ => null,
        };
    }

    /// <summary>PartitionRelativeTimePattern(relativeTimeFormat, value, unit)</summary>
    internal static List<RelativeTimePart> PartitionRelativeTimePattern(JsRelativeTimeFormatObject relativeTimeFormat, double value, string unit)
    {
        if (!double.IsFinite(value))
            ThrowHelper.ThrowRangeError("Invalid value: " + NumberFormatting.ToString(value));

        var singular = SingularRelativeTimeUnit(unit) ?? ThrowHelper.ThrowRangeError<string>("Invalid unit argument: " + unit);
        var locale = relativeTimeFormat.DataLocale;
        var field = relativeTimeFormat.Style is "long" ? singular : singular + "-" + relativeTimeFormat.Style;
        if (relativeTimeFormat.Numeric is "auto")
        {
            var valueString = NumberFormatting.ToString(value);
            if (locale.GetString(CldrCategory.Fields, field + "/relative-type-" + valueString) is { } relative)
                return [new RelativeTimePart("literal", relative, Unit: null)];
        }

        string tense;
        if (value < 0 || (value == 0 && double.IsNegative(value)))
        {
            tense = "past";
            value = -value;
        }
        else
        {
            tense = "future";
        }

        var numberFormat = relativeTimeFormat.NumberFormat;
        var x = IntlDecimal.FromDouble(value);
        var formatted = NumberFormatter.PartitionNumberPattern(numberFormat, x).ToList();
        var category = PluralRulesBuiltins.SelectCategory(numberFormat.PluralRules, numberFormat.Digits.FormatNumericToString(x).FormattedString, 0);
        var prefix = field + "/relativeTime-type-" + tense + "/relativeTimePattern-count-";
        var pattern = locale.GetString(CldrCategory.Fields, prefix + category) ?? locale.GetString(CldrCategory.Fields, prefix + "other") ?? "{0}";
        return MakePartsList(pattern, singular, formatted);
    }

    /// <summary>MakePartsList(pattern, unit, parts)</summary>
    private static List<RelativeTimePart> MakePartsList(string pattern, string unit, List<NumberPart> parts)
    {
        var result = new List<RelativeTimePart>();
        var index = pattern.IndexOf("{0}", StringComparison.Ordinal);
        if (index < 0)
        {
            result.Add(new RelativeTimePart("literal", pattern, Unit: null));
            return result;
        }

        if (index > 0)
        {
            result.Add(new RelativeTimePart("literal", pattern[..index], Unit: null));
        }

        foreach (var part in parts)
        {
            result.Add(new RelativeTimePart(part.Type, part.Value, unit));
        }

        if (index + 3 < pattern.Length)
        {
            result.Add(new RelativeTimePart("literal", pattern[(index + 3)..], Unit: null));
        }

        return result;
    }
}
