using Meziantou.Framework.JavaScriptEngine.Intl.Data;

namespace Meziantou.Framework.JavaScriptEngine.Intl;

/// <summary>An Intl.DisplayNames instance: an ordinary object with the [[InitializedDisplayNames]] internal slot.</summary>
internal sealed class JsDisplayNamesObject(JsObject prototype) : JsObject(prototype, ObjectClass.Intl)
{
    public string Locale { get; set; } = "";

    public CldrLocale DataLocale { get; set; } = null!;

    /// <summary>[[Style]]: "narrow", "short" or "long".</summary>
    public string Style { get; set; } = "long";

    /// <summary>[[Type]]: "language", "region", "script", "currency", "calendar" or "dateTimeField".</summary>
    public string Type { get; set; } = "";

    /// <summary>[[Fallback]]: "code" or "none".</summary>
    public string Fallback { get; set; } = "code";

    /// <summary>[[LanguageDisplay]]: "dialect" or "standard"; null when the type is not "language".</summary>
    public string? LanguageDisplay { get; set; }
}

/// <summary>Intl.DisplayNames (https://tc39.es/ecma402/#intl-displaynames-objects).</summary>
internal static class DisplayNamesBuiltins
{
    private static readonly BuiltinMethodTable PrototypeMethods =
    [
        new("of", 1, Of),
        new("resolvedOptions", 0, ResolvedOptions),
    ];

    public static JsFunction Initialize(Intrinsics intrinsics, JsObject intl, JsObject prototype)
    {
        var realm = intrinsics.Realm;
        var constructor = realm.CreateBuiltinFunction("DisplayNames", 2, Construct, isConstructor: true);
        constructor.DefineBuiltIn("prototype", prototype, PropertyFlags.None);
        realm.DefineMethod(constructor, "supportedLocalesOf", 1, SupportedLocalesOf);
        prototype.DefineBuiltIn("constructor", constructor);
        prototype.DefineBuiltIn(WellKnownSymbols.ToStringTag, "Intl.DisplayNames", PropertyFlags.Configurable);
        realm.DefineMethods(prototype, PrototypeMethods);
        intl.DefineBuiltIn("DisplayNames", constructor);
        return constructor;
    }

    private static JsValue SupportedLocalesOf(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var requestedLocales = IntlOperations.CanonicalizeLocaleList(arguments.Length > 0 ? arguments[0] : default);
        return IntlOperations.SupportedLocales(function.Realm, requestedLocales, arguments.Length > 1 ? arguments[1] : default);
    }

    /// <summary>Intl.DisplayNames(locales, options)</summary>
    private static JsValue Construct(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        if (newTarget is null)
            return ThrowHelper.ThrowTypeError<JsValue>("Constructor Intl.DisplayNames requires 'new'");

        var prototype = ObjectOperations.GetPrototypeForConstruct(function, newTarget, static i => i.IntlDisplayNamesPrototype);
        var displayNames = new JsDisplayNamesObject(prototype);
        var requestedLocales = IntlOperations.CanonicalizeLocaleList(arguments.Length > 0 ? arguments[0] : default);
        var optionsValue = arguments.Length > 1 ? arguments[1] : default;
        if (optionsValue.IsUndefined)
            ThrowHelper.ThrowTypeError("Intl.DisplayNames requires an options object");

        var options = IntlOperations.GetOptionsObject(optionsValue);
        _ = IntlOperations.GetStringOption(options, "localeMatcher", ["lookup", "best fit"], "best fit");
        var r = IntlOperations.ResolveLocale(requestedLocales, [], static _ => null, static (_, _) => []);
        displayNames.Style = IntlOperations.GetStringOption(options, "style", ["narrow", "short", "long"], "long")!;
        var type = IntlOperations.GetStringOption(options, "type", ["language", "region", "script", "currency", "calendar", "dateTimeField"], fallback: null);
        if (type is null)
            ThrowHelper.ThrowTypeError("The type option is required");

        displayNames.Type = type;
        displayNames.Fallback = IntlOperations.GetStringOption(options, "fallback", ["code", "none"], "code")!;
        displayNames.Locale = r.Locale;
        displayNames.DataLocale = IntlOperations.Data.FindLocale(r.DataLocale);
        var languageDisplay = IntlOperations.GetStringOption(options, "languageDisplay", ["dialect", "standard"], "dialect")!;
        if (type is "language")
        {
            displayNames.LanguageDisplay = languageDisplay;
        }

        return displayNames;
    }

    private static JsDisplayNamesObject ThisDisplayNames(JsValue thisValue, string method)
    {
        if (thisValue.Cell is JsDisplayNamesObject displayNames)
            return displayNames;

        return ThrowHelper.ThrowTypeError<JsDisplayNamesObject>($"Intl.DisplayNames.prototype.{method} called on an incompatible receiver");
    }

    /// <summary>Intl.DisplayNames.prototype.resolvedOptions()</summary>
    private static JsValue ResolvedOptions(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var displayNames = ThisDisplayNames(thisValue, "resolvedOptions");
        var options = new JsObject(function.Realm.Intrinsics.ObjectPrototype);
        options.CreateDataPropertyOrThrow("locale", displayNames.Locale);
        options.CreateDataPropertyOrThrow("style", displayNames.Style);
        options.CreateDataPropertyOrThrow("type", displayNames.Type);
        options.CreateDataPropertyOrThrow("fallback", displayNames.Fallback);
        if (displayNames.LanguageDisplay is { } languageDisplay)
        {
            options.CreateDataPropertyOrThrow("languageDisplay", languageDisplay);
        }

        return options;
    }

    /// <summary>Intl.DisplayNames.prototype.of(code)</summary>
    private static JsValue Of(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var displayNames = ThisDisplayNames(thisValue, "of");
        var code = JsOperations.ToString(arguments.Length > 0 ? arguments[0] : default);
        code = CanonicalCodeForDisplayNames(displayNames.Type, code);
        var substitute = displayNames.Fallback is "code";
        var names = new DisplayNamesData(displayNames.DataLocale, displayNames.Style is not "long");
        var result = displayNames.Type switch
        {
            "language" => names.GetLocaleDisplayName(code, displayNames.LanguageDisplay is "dialect", substitute),
            "region" => names.GetRegionName(code),
            "script" => names.GetScriptName(code),
            "currency" => names.GetCurrencyName(code),
            "calendar" => names.GetCalendarName(code),
            _ => names.GetDateTimeFieldName(code, displayNames.Style),
        };

        if (result is not null)
            return result;

        if (substitute)
            return code;

        return JsValue.Undefined;
    }

    /// <summary>CanonicalCodeForDisplayNames(type, code)</summary>
    internal static string CanonicalCodeForDisplayNames(string type, string code)
    {
        switch (type)
        {
            case "language":
                {
                    var tag = LanguageTag.Parse(code);
                    if (tag is null || tag.HasUnicodeExtension || tag.HasTransformedExtension || tag.OtherExtensions.Count > 0 || tag.PrivateUse is not null)
                        ThrowHelper.ThrowRangeError("Invalid language code: " + code);

                    tag.Canonicalize();
                    return tag.ToString();
                }

            case "region":
                if (!LanguageTag.IsRegionSubtag(code))
                    ThrowHelper.ThrowRangeError("Invalid region code: " + code);

                return code.ToUpperInvariant();

            case "script":
                if (!LanguageTag.IsScriptSubtag(code))
                    ThrowHelper.ThrowRangeError("Invalid script code: " + code);

                return LanguageTag.ToTitle(code);

            case "calendar":
                if (!LanguageTag.IsUnicodeType(code))
                    ThrowHelper.ThrowRangeError("Invalid calendar code: " + code);

                return LocaleCanonicalizer.CanonicalizeUnicodeValue("ca", code);

            case "dateTimeField":
                if (code is not ("era" or "year" or "quarter" or "month" or "weekOfYear" or "weekday" or "day" or "dayPeriod" or "hour" or "minute" or "second" or "timeZoneName"))
                    ThrowHelper.ThrowRangeError("Invalid dateTimeField code: " + code);

                return code;

            default:
                if (!NumberFormatBuiltins.IsWellFormedCurrencyCode(code))
                    ThrowHelper.ThrowRangeError("Invalid currency code: " + code);

                return code.ToUpperInvariant();
        }
    }
}
