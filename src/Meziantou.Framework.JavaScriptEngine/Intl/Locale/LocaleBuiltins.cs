using Meziantou.Framework.JavaScriptEngine.Intl.Data;

namespace Meziantou.Framework.JavaScriptEngine.Intl;

/// <summary>Intl.Locale (https://tc39.es/ecma402/#locale-objects).</summary>
internal static class LocaleBuiltins
{
    /// <summary>[[LocaleExtensionKeys]] of %Intl.Locale%.</summary>
    private static readonly string[] LocaleExtensionKeys = ["ca", "co", "fw", "hc", "kf", "kn", "nu"];

    public static JsFunction Initialize(Intrinsics intrinsics, JsObject intl)
    {
        var realm = intrinsics.Realm;
        var prototype = intrinsics.IntlLocalePrototype;
        var constructor = realm.CreateBuiltinFunction("Locale", 1, Construct, isConstructor: true);
        constructor.DefineBuiltIn("prototype", prototype, PropertyFlags.None);
        prototype.DefineBuiltIn("constructor", constructor);
        prototype.DefineBuiltIn(WellKnownSymbols.ToStringTag, "Intl.Locale", PropertyFlags.Configurable);

        realm.DefineMethod(prototype, "maximize", 0, Maximize);
        realm.DefineMethod(prototype, "minimize", 0, Minimize);
        realm.DefineMethod(prototype, "toString", 0, ToString);
        realm.DefineMethod(prototype, "getCalendars", 0, GetCalendars);
        realm.DefineMethod(prototype, "getCollations", 0, GetCollations);
        realm.DefineMethod(prototype, "getHourCycles", 0, GetHourCycles);
        realm.DefineMethod(prototype, "getNumberingSystems", 0, GetNumberingSystems);
        realm.DefineMethod(prototype, "getTimeZones", 0, GetTimeZones);
        realm.DefineMethod(prototype, "getTextInfo", 0, GetTextInfo);
        realm.DefineMethod(prototype, "getWeekInfo", 0, GetWeekInfo);

        realm.DefineAccessor(prototype, "baseName", GetBaseName, setter: null);
        realm.DefineAccessor(prototype, "calendar", static (f, t, a, n) => ToValue(ThisLocale(t, "calendar").Calendar), setter: null);
        realm.DefineAccessor(prototype, "caseFirst", static (f, t, a, n) => ToValue(ThisLocale(t, "caseFirst").CaseFirst), setter: null);
        realm.DefineAccessor(prototype, "collation", static (f, t, a, n) => ToValue(ThisLocale(t, "collation").Collation), setter: null);
        realm.DefineAccessor(prototype, "firstDayOfWeek", static (f, t, a, n) => ToValue(ThisLocale(t, "firstDayOfWeek").FirstDayOfWeek), setter: null);
        realm.DefineAccessor(prototype, "hourCycle", static (f, t, a, n) => ToValue(ThisLocale(t, "hourCycle").HourCycle), setter: null);
        realm.DefineAccessor(prototype, "numeric", static (f, t, a, n) => ThisLocale(t, "numeric").Numeric, setter: null);
        realm.DefineAccessor(prototype, "numberingSystem", static (f, t, a, n) => ToValue(ThisLocale(t, "numberingSystem").NumberingSystem), setter: null);
        realm.DefineAccessor(prototype, "language", static (f, t, a, n) => GetTag(t, "language").Language, setter: null);
        realm.DefineAccessor(prototype, "script", static (f, t, a, n) => ToSubtagValue(GetTag(t, "script").Script), setter: null);
        realm.DefineAccessor(prototype, "region", static (f, t, a, n) => ToSubtagValue(GetTag(t, "region").Region), setter: null);
        realm.DefineAccessor(prototype, "variants", static (f, t, a, n) => GetTag(t, "variants") is { Variants.Count: > 0 } tag ? string.Join('-', tag.Variants) : JsValue.Undefined, setter: null);

        intl.DefineBuiltIn("Locale", constructor);
        return constructor;
    }

    private static JsValue ToValue(string? value) => value is null ? JsValue.Undefined : value;

    private static JsValue ToSubtagValue(string value) => value.Length == 0 ? JsValue.Undefined : value;

    private static JsLocaleObject ThisLocale(JsValue thisValue, string method)
    {
        if (thisValue.Cell is JsLocaleObject locale)
            return locale;

        return ThrowHelper.ThrowTypeError<JsLocaleObject>($"Intl.Locale.prototype.{method} called on an incompatible receiver");
    }

    private static LanguageTag GetTag(JsValue thisValue, string method) => LanguageTag.Parse(ThisLocale(thisValue, method).Locale)!;

    private static JsValue GetBaseName(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget) => GetTag(thisValue, "baseName").BaseName;

    /// <summary>Intl.Locale(tag [, options])</summary>
    private static JsValue Construct(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (newTarget is null)
            return ThrowHelper.ThrowTypeError<JsValue>("Constructor Intl.Locale requires 'new'");

        var prototype = ObjectOperations.GetPrototypeForConstruct(function, newTarget, static i => i.IntlLocalePrototype);
        var locale = new JsLocaleObject(prototype);
        var tagArgument = arguments.Length > 0 ? arguments[0] : default;
        var optionsArgument = arguments.Length > 1 ? arguments[1] : default;
        if (!tagArgument.IsString && !tagArgument.IsObject)
            ThrowHelper.ThrowTypeError("First argument to Intl.Locale constructor can't be empty or missing");

        var tag = tagArgument.Cell is JsLocaleObject other ? other.Locale : JsOperations.ToString(tagArgument);
        var options = IntlOperations.CoerceOptionsToObject(optionsArgument);
        var parsed = LanguageTag.Parse(tag) ?? ThrowHelper.ThrowRangeError<LanguageTag>("Incorrect locale information provided");
        parsed.Canonicalize();
        UpdateLanguageId(parsed, options);

        var calendar = IntlOperations.GetStringOption(options, "calendar", [], fallback: null);
        if (calendar is not null && !LanguageTag.IsUnicodeType(calendar))
            ThrowHelper.ThrowRangeError("Invalid calendar : " + calendar);

        var collation = IntlOperations.GetStringOption(options, "collation", [], fallback: null);
        if (collation is not null && !LanguageTag.IsUnicodeType(collation))
            ThrowHelper.ThrowRangeError("Invalid collation : " + collation);

        var firstDayOfWeek = IntlOperations.GetStringOption(options, "firstDayOfWeek", [], fallback: null);
        if (firstDayOfWeek is not null)
        {
            firstDayOfWeek = LocaleInfo.WeekdayToUValue(firstDayOfWeek);
            if (!LanguageTag.IsUnicodeType(firstDayOfWeek))
                ThrowHelper.ThrowRangeError("Invalid firstDayOfWeek : " + firstDayOfWeek);
        }

        var hourCycle = IntlOperations.GetStringOption(options, "hourCycle", ["h11", "h12", "h23", "h24"], fallback: null);
        var caseFirst = IntlOperations.GetStringOption(options, "caseFirst", ["upper", "lower", "false"], fallback: null);
        var numeric = IntlOperations.GetBooleanOption(options, "numeric", fallback: null);
        var numberingSystem = IntlOperations.GetStringOption(options, "numberingSystem", [], fallback: null);
        if (numberingSystem is not null && !LanguageTag.IsUnicodeType(numberingSystem))
            ThrowHelper.ThrowRangeError("Invalid numberingSystem : " + numberingSystem);

        // MakeLocaleRecord(tag, opt, localeExtensionKeys)
        var values = new string?[LocaleExtensionKeys.Length];
        for (var i = 0; i < LocaleExtensionKeys.Length; i++)
        {
            var key = LocaleExtensionKeys[i];
            var value = parsed.GetKeyword(key);
            var overrideValue = key switch
            {
                "ca" => calendar,
                "co" => collation,
                "fw" => firstDayOfWeek,
                "hc" => hourCycle,
                "kf" => caseFirst,
                "kn" => numeric is null ? null : numeric.Value ? "true" : "false",
                _ => numberingSystem,
            };

            if (overrideValue is not null)
            {
                value = LocaleCanonicalizer.CanonicalizeUnicodeValue(key, overrideValue);
                parsed.SetKeyword(key, value);
            }

            values[i] = value;
        }

        parsed.Canonicalize();
        locale.Locale = parsed.ToString();
        locale.Calendar = values[0];
        locale.Collation = values[1];
        locale.FirstDayOfWeek = values[2];
        locale.HourCycle = values[3];
        locale.CaseFirst = values[4];
        locale.Numeric = values[5] is "true" or "";
        locale.NumberingSystem = values[6];
        return locale;
    }

    /// <summary>UpdateLanguageId(tag, options)</summary>
    private static void UpdateLanguageId(LanguageTag tag, JsObject options)
    {
        var language = IntlOperations.GetStringOption(options, "language", [], tag.Language)!;
        if (!LanguageTag.IsLanguageSubtag(language))
            ThrowHelper.ThrowRangeError("Invalid language : " + language);

        var script = IntlOperations.GetStringOption(options, "script", [], tag.Script.Length > 0 ? tag.Script : null);
        if (script is not null && !LanguageTag.IsScriptSubtag(script))
            ThrowHelper.ThrowRangeError("Invalid script : " + script);

        var region = IntlOperations.GetStringOption(options, "region", [], tag.Region.Length > 0 ? tag.Region : null);
        if (region is not null && !LanguageTag.IsRegionSubtag(region))
            ThrowHelper.ThrowRangeError("Invalid region : " + region);

        var variants = IntlOperations.GetStringOption(options, "variants", [], tag.Variants.Count > 0 ? string.Join('-', tag.Variants) : null);
        List<string>? variantSubtags = null;
        if (variants is not null)
        {
            if (variants.Length == 0)
                ThrowHelper.ThrowRangeError("Invalid variants : " + variants);

            variantSubtags = [];
            foreach (var variant in variants.ToLowerInvariant().Split('-'))
            {
                if (!LanguageTag.IsVariantSubtag(variant))
                    ThrowHelper.ThrowRangeError("Invalid variants : " + variants);

                if (variantSubtags.Contains(variant))
                    ThrowHelper.ThrowRangeError("Duplicate variants : " + variants);

                variantSubtags.Add(variant);
            }
        }

        tag.Language = language.ToLowerInvariant();
        tag.Script = script is null ? "" : LanguageTag.ToTitle(script);
        tag.Region = region is null ? "" : region.ToUpperInvariant();
        tag.Variants.Clear();
        if (variantSubtags is not null)
        {
            tag.Variants.AddRange(variantSubtags);
        }
    }

    private static JsLocaleObject CreateLocale(JsRealm realm, string tag)
    {
        var constructor = realm.Intrinsics.IntlLocale;
        return (JsLocaleObject)JsOperations.Construct(constructor, [tag], constructor).TryGetObject()!;
    }

    /// <summary>Intl.Locale.prototype.maximize()</summary>
    private static JsValue Maximize(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var locale = ThisLocale(thisValue, "maximize");
        var tag = LanguageTag.Parse(locale.Locale)!;
        var likely = CldrData.Default.Supplemental.LikelySubtags;
        if (likely.TryAddLikelySubtags(new LanguageId(tag.Language, tag.Script, tag.Region), out var maximized))
        {
            tag.Language = maximized.Language;
            tag.Script = maximized.Script;
            tag.Region = maximized.Region;
        }

        return CreateLocale(function.Realm, tag.ToString());
    }

    /// <summary>Intl.Locale.prototype.minimize()</summary>
    private static JsValue Minimize(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var locale = ThisLocale(thisValue, "minimize");
        var tag = LanguageTag.Parse(locale.Locale)!;
        var likely = CldrData.Default.Supplemental.LikelySubtags;
        var minimized = likely.RemoveLikelySubtags(new LanguageId(tag.Language, tag.Script, tag.Region), favorRegion: true);
        tag.Language = minimized.Language;
        tag.Script = minimized.Script;
        tag.Region = minimized.Region;
        return CreateLocale(function.Realm, tag.ToString());
    }

    /// <summary>Intl.Locale.prototype.toString()</summary>
    private static JsValue ToString(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget) => ThisLocale(thisValue, "toString").Locale;

    /// <summary>Intl.Locale.prototype.getCalendars()</summary>
    private static JsValue GetCalendars(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var locale = ThisLocale(thisValue, "getCalendars");
        if (locale.Calendar is not null)
            return IntlOperations.CreateArray(function.Realm, [locale.Calendar]);

        return IntlOperations.CreateArray(function.Realm, LocaleInfo.GetCalendars(LanguageTag.Parse(locale.Locale)!));
    }

    /// <summary>Intl.Locale.prototype.getCollations()</summary>
    private static JsValue GetCollations(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var locale = ThisLocale(thisValue, "getCollations");
        if (locale.Collation is not null)
            return IntlOperations.CreateArray(function.Realm, [locale.Collation]);

        return IntlOperations.CreateArray(function.Realm, LocaleInfo.GetCollations(LanguageTag.Parse(locale.Locale)!));
    }

    /// <summary>Intl.Locale.prototype.getHourCycles()</summary>
    private static JsValue GetHourCycles(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var locale = ThisLocale(thisValue, "getHourCycles");
        if (locale.HourCycle is not null)
            return IntlOperations.CreateArray(function.Realm, [locale.HourCycle]);

        return IntlOperations.CreateArray(function.Realm, LocaleInfo.GetHourCycles(LanguageTag.Parse(locale.Locale)!));
    }

    /// <summary>Intl.Locale.prototype.getNumberingSystems()</summary>
    private static JsValue GetNumberingSystems(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var locale = ThisLocale(thisValue, "getNumberingSystems");
        if (locale.NumberingSystem is not null)
            return IntlOperations.CreateArray(function.Realm, [locale.NumberingSystem]);

        return IntlOperations.CreateArray(function.Realm, [LocaleInfo.GetDefaultNumberingSystem(LanguageTag.Parse(locale.Locale)!)]);
    }

    /// <summary>Intl.Locale.prototype.getTimeZones()</summary>
    private static JsValue GetTimeZones(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var locale = ThisLocale(thisValue, "getTimeZones");
        var tag = LanguageTag.Parse(locale.Locale)!;
        if (tag.Region.Length == 0)
            return JsValue.Undefined;

        return IntlOperations.CreateArray(function.Realm, LocaleInfo.GetTimeZones(tag.Region));
    }

    /// <summary>Intl.Locale.prototype.getTextInfo()</summary>
    private static JsValue GetTextInfo(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var locale = ThisLocale(thisValue, "getTextInfo");
        var info = new JsObject(function.Realm.Intrinsics.ObjectPrototype);
        var direction = LocaleInfo.GetTextDirection(LanguageTag.Parse(locale.Locale)!);
        info.CreateDataPropertyOrThrow("direction", direction is null ? JsValue.Undefined : direction);
        return info;
    }

    /// <summary>Intl.Locale.prototype.getWeekInfo()</summary>
    private static JsValue GetWeekInfo(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var locale = ThisLocale(thisValue, "getWeekInfo");
        var (firstDay, weekend) = LocaleInfo.GetWeekInfo(LanguageTag.Parse(locale.Locale)!, locale.FirstDayOfWeek);
        var info = new JsObject(function.Realm.Intrinsics.ObjectPrototype);
        info.CreateDataPropertyOrThrow("firstDay", firstDay);
        var weekendValues = new JsValue[weekend.Count];
        for (var i = 0; i < weekendValues.Length; i++)
        {
            weekendValues[i] = weekend[i];
        }

        info.CreateDataPropertyOrThrow("weekend", JsArray.Create(function.Realm, weekendValues));
        return info;
    }
}
