using System.Collections.Concurrent;
using Meziantou.Framework.JavaScriptEngine.Intl.Data;

namespace Meziantou.Framework.JavaScriptEngine.Intl;

/// <summary>An Intl.Collator instance: an ordinary object with the [[InitializedCollator]] internal slot.</summary>
internal sealed class JsCollatorObject(JsObject prototype) : JsObject(prototype, ObjectClass.Intl)
{
    public string Locale { get; set; } = "";

    /// <summary>[[Usage]]: "sort" or "search".</summary>
    public string Usage { get; set; } = "sort";

    /// <summary>[[Sensitivity]]: "base", "accent", "case" or "variant".</summary>
    public string Sensitivity { get; set; } = "variant";

    public bool IgnorePunctuation { get; set; }

    /// <summary>[[Collation]]: "default" or a collation type.</summary>
    public string Collation { get; set; } = "default";

    public bool Numeric { get; set; }

    /// <summary>[[CaseFirst]]: "upper", "lower" or "false".</summary>
    public string CaseFirst { get; set; } = "false";

    public JsFunction? BoundCompare { get; set; }

    /// <summary>Gets or sets the comparer of the strings (the collation of the locale with the settings).</summary>
    public StringCollator Collator { get; set; } = null!;
}

/// <summary>Intl.Collator (https://tc39.es/ecma402/#collator-objects) and String.prototype.localeCompare.</summary>
internal static class CollatorBuiltins
{
    private static readonly string[] RelevantExtensionKeys = ["co", "kf", "kn"];
    private static readonly string?[] SearchCollations = [null];
    private static readonly string?[] NumericValues = ["false", "true"];
    private static readonly ConcurrentDictionary<string, CollatorLocaleData> LocaleDataCache = new(StringComparer.Ordinal);

    public static JsFunction Initialize(Intrinsics intrinsics, JsObject intl)
    {
        var realm = intrinsics.Realm;
        var prototype = intrinsics.IntlCollatorPrototype;
        var constructor = realm.CreateBuiltinFunction("Collator", 0, Construct, isConstructor: true);
        constructor.DefineBuiltIn("prototype", prototype, PropertyFlags.None);
        realm.DefineMethod(constructor, "supportedLocalesOf", 1, SupportedLocalesOf);
        prototype.DefineBuiltIn("constructor", constructor);
        prototype.DefineBuiltIn(WellKnownSymbols.ToStringTag, "Intl.Collator", PropertyFlags.Configurable);
        realm.DefineAccessor(prototype, "compare", GetCompare, setter: null);
        realm.DefineMethod(prototype, "resolvedOptions", 0, ResolvedOptions);
        intl.DefineBuiltIn("Collator", constructor);

        realm.DefineMethod(intrinsics.StringPrototype, "localeCompare", 1, LocaleCompare);
        return constructor;
    }

    /// <summary>[[AvailableLocales]] of Collator: the locales of the data and the locales that have collation tailorings (e.g. bs-Cyrl).</summary>
    private static bool IsAvailable(string locale)
    {
        if (IntlOperations.IsAvailableLocale(locale))
            return true;

        return locale is not ("und" or "root") && IntlOperations.Data.Collation.GetOwnTailorings(locale) is not null;
    }

    private static JsValue SupportedLocalesOf(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var requestedLocales = IntlOperations.CanonicalizeLocaleList(arguments.Length > 0 ? arguments[0] : default);
        return IntlOperations.SupportedLocales(function.Realm, requestedLocales, arguments.Length > 1 ? arguments[1] : default, IsAvailable);
    }

    /// <summary>Intl.Collator([locales [, options]])</summary>
    private static JsValue Construct(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        // The collator is a constructor that can be called as a function: the active function is then the new target
        newTarget ??= function;
        var prototype = ObjectOperations.GetPrototypeForConstruct(function, newTarget, static i => i.IntlCollatorPrototype);
        var collator = new JsCollatorObject(prototype);
        Initialize(collator, arguments.Length > 0 ? arguments[0] : default, arguments.Length > 1 ? arguments[1] : default);
        return collator;
    }

    internal static void Initialize(JsCollatorObject collator, JsValue locales, JsValue optionsValue)
    {
        var requestedLocales = IntlOperations.CanonicalizeLocaleList(locales);
        var options = IntlOperations.CoerceOptionsToObject(optionsValue);
        var usage = IntlOperations.GetStringOption(options, "usage", ["sort", "search"], "sort")!;
        collator.Usage = usage;
        _ = IntlOperations.GetStringOption(options, "localeMatcher", ["lookup", "best fit"], "best fit");
        var collation = IntlOperations.GetStringOption(options, "collation", [], fallback: null);
        if (collation is not null && !LanguageTag.IsUnicodeType(collation))
            ThrowHelper.ThrowRangeError("Invalid collation : " + collation);

        var numericOption = IntlOperations.GetBooleanOption(options, "numeric", fallback: null);
        var numeric = numericOption is null ? null : numericOption.Value ? "true" : "false";
        var caseFirst = IntlOperations.GetStringOption(options, "caseFirst", ["upper", "lower", "false"], fallback: null);

        var isSearch = usage is "search";
        var r = IntlOperations.ResolveLocale(
            requestedLocales,
            RelevantExtensionKeys,
            key => key switch
            {
                "co" => collation,
                "kn" => numeric,
                _ => caseFirst,
            },
            (locale, key) => GetLocaleData(locale, key, isSearch),
            IsAvailable);

        collator.Locale = r.Locale;
        collator.Collation = r.Get("co") ?? "default";
        collator.Numeric = r.Get("kn") is "true";
        collator.CaseFirst = r.Get("kf") ?? "false";

        // The default sensitivity of the search locale data is "variant" too
        collator.Sensitivity = IntlOperations.GetStringOption(options, "sensitivity", ["base", "accent", "case", "variant"], "variant")!;
        var tailoring = GetCollation(r.DataLocale, isSearch ? "search" : collator.Collation);
        collator.IgnorePunctuation = IntlOperations.GetBooleanOption(options, "ignorePunctuation", tailoring.IsShiftedByDefault)!.Value;

        var caseFirstSetting = collator.CaseFirst switch
        {
            "upper" => CollationCaseFirst.Upper,
            "lower" => CollationCaseFirst.Lower,
            _ => CollationCaseFirst.Off,
        };

        collator.Collator = tailoring.GetCollator(CollatorSettings.FromSensitivity(collator.Sensitivity, caseFirstSetting, collator.IgnorePunctuation, collator.Numeric));
    }

    /// <summary>The [[SortLocaleData]] and [[SearchLocaleData]] of a locale.</summary>
    private static string?[] GetLocaleData(string locale, string key, bool isSearch)
    {
        return key switch
        {
            "co" => isSearch ? SearchCollations : GetLocaleData(locale).Collations,
            "kn" => NumericValues,
            _ => GetLocaleData(locale).CaseFirst,
        };
    }

    private static CollatorLocaleData GetLocaleData(string locale)
    {
        return LocaleDataCache.GetOrAdd(locale, static locale =>
        {
            var collations = LocaleInfo.GetCollations(LanguageTag.Parse(locale)!);
            var co = new string?[collations.Count + 1];
            for (var i = 0; i < collations.Count; i++)
            {
                co[i + 1] = collations[i];
            }

            var type = IntlOperations.Data.Collation.GetDefaultCollation(LanguageId.Parse(locale));
            var defaultCollation = TailoredCollation.Get(locale, type) ?? TailoredCollation.Get(locale, "standard") ?? TailoredCollation.Root;
            string?[] caseFirst = defaultCollation.CaseFirst switch
            {
                CollationCaseFirst.Upper => ["upper", "false", "lower"],
                CollationCaseFirst.Lower => ["lower", "false", "upper"],
                _ => ["false", "lower", "upper"],
            };

            return new CollatorLocaleData(co, caseFirst, defaultCollation);
        });
    }

    /// <summary>Gets the default collation of a locale (e.g. pinyin for zh).</summary>
    private static TailoredCollation GetDefaultCollation(string locale) => GetLocaleData(locale).DefaultCollation;

    /// <summary>The locale data of a locale: the values of "co" and "kf" (the default first) and the default collation.</summary>
    private sealed record CollatorLocaleData(string?[] Collations, string?[] CaseFirst, TailoredCollation DefaultCollation);

    /// <summary>Gets the collation of a locale for "default", "search", or a BCP 47 collation type.</summary>
    private static TailoredCollation GetCollation(string locale, string collation)
    {
        if (collation is "default")
            return GetDefaultCollation(locale);

        if (collation is "search")
            return TailoredCollation.Get(locale, "search") ?? GetDefaultCollation(locale);

        foreach (var type in IntlOperations.Data.Collation.GetCollationTypes(LanguageId.Parse(locale)))
        {
            if (LocaleInfo.ToBcp47Collation(type) == collation && TailoredCollation.Get(locale, type) is { } result)
                return result;
        }

        return GetDefaultCollation(locale);
    }

    private static JsCollatorObject ThisCollator(JsValue thisValue, string method)
    {
        if (thisValue.Cell is JsCollatorObject collator)
            return collator;

        return ThrowHelper.ThrowTypeError<JsCollatorObject>($"Intl.Collator.prototype.{method} called on an incompatible receiver");
    }

    /// <summary>get Intl.Collator.prototype.compare</summary>
    private static JsValue GetCompare(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var collator = ThisCollator(thisValue, "compare");
        if (collator.BoundCompare is null)
        {
            var bound = function.Realm.CreateBuiltinFunction("", 2, static (f, t, a, n) =>
            {
                var c = (JsCollatorObject)f.State!;
                var x = JsOperations.ToString(a.Length > 0 ? a[0] : default);
                var y = JsOperations.ToString(a.Length > 1 ? a[1] : default);
                return CompareStrings(c, x, y);
            });
            bound.State = collator;
            collator.BoundCompare = bound;
        }

        return collator.BoundCompare;
    }

    /// <summary>CompareStrings(collator, x, y)</summary>
    internal static int CompareStrings(JsCollatorObject collator, string x, string y) => Math.Sign(collator.Collator.Compare(x, y));

    /// <summary>Intl.Collator.prototype.resolvedOptions()</summary>
    private static JsValue ResolvedOptions(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var collator = ThisCollator(thisValue, "resolvedOptions");
        var options = new JsObject(function.Realm.Intrinsics.ObjectPrototype);
        options.CreateDataPropertyOrThrow("locale", collator.Locale);
        options.CreateDataPropertyOrThrow("usage", collator.Usage);
        options.CreateDataPropertyOrThrow("sensitivity", collator.Sensitivity);
        options.CreateDataPropertyOrThrow("ignorePunctuation", collator.IgnorePunctuation);
        options.CreateDataPropertyOrThrow("collation", collator.Collation);
        options.CreateDataPropertyOrThrow("numeric", collator.Numeric);
        options.CreateDataPropertyOrThrow("caseFirst", collator.CaseFirst);
        return options;
    }

    /// <summary>String.prototype.localeCompare(that [, locales [, options]])</summary>
    private static JsValue LocaleCompare(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (thisValue.IsNullOrUndefined)
            ThrowHelper.ThrowTypeError("String.prototype.localeCompare called on null or undefined");

        var s = JsOperations.ToString(thisValue);
        var thatValue = JsOperations.ToString(arguments.Length > 0 ? arguments[0] : default);
        var locales = arguments.Length > 1 ? arguments[1] : default;
        var options = arguments.Length > 2 ? arguments[2] : default;
        var intrinsics = function.Realm.Intrinsics;
        JsCollatorObject collator;
        if (locales.IsUndefined && options.IsUndefined)
        {
            collator = intrinsics.DefaultCollator;
        }
        else if (locales.IsString && options.IsUndefined)
        {
            // Creating a collator from a string has no observable effect: the collators of the locales are reused
            collator = intrinsics.GetLocaleCompareCollator(JsOperations.ToString(locales));
        }
        else
        {
            collator = new JsCollatorObject(intrinsics.IntlCollatorPrototype);
            Initialize(collator, locales, options);
        }

        return CompareStrings(collator, s, thatValue);
    }

    /// <summary>Creates the collator of the default locale and options, used by String.prototype.localeCompare without arguments.</summary>
    internal static JsCollatorObject CreateDefault(Intrinsics intrinsics)
    {
        var collator = new JsCollatorObject(intrinsics.IntlCollatorPrototype);
        Initialize(collator, default, default);
        return collator;
    }
}
