using Meziantou.Framework.JavaScriptEngine.TimeZones;

namespace Meziantou.Framework.JavaScriptEngine.Intl;

/// <summary>The Intl object (https://tc39.es/ecma402/#intl-object).</summary>
internal static class IntlBuiltins
{
    private static readonly BuiltinMethodTable IntlMethods =
    [
        new("getCanonicalLocales", 1, GetCanonicalLocales),
        new("supportedValuesOf", 1, SupportedValuesOf),
    ];

    public static JsObject Initialize(Intrinsics intrinsics)
    {
        var realm = intrinsics.Realm;
        var intl = new JsObject(intrinsics.ObjectPrototype);
        intl.DefineBuiltIn(WellKnownSymbols.ToStringTag, "Intl", PropertyFlags.Configurable);
        realm.DefineMethods(intl, IntlMethods);
        intrinsics.AddGlobal("Intl", intl);
        return intl;
    }

    /// <summary>Intl.getCanonicalLocales(locales)</summary>
    private static JsValue GetCanonicalLocales(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var list = IntlOperations.CanonicalizeLocaleList(arguments.Length > 0 ? arguments[0] : default);
        return IntlOperations.CreateArray(function.Realm, list);
    }

    /// <summary>Intl.supportedValuesOf(key)</summary>
    private static JsValue SupportedValuesOf(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var key = JsOperations.ToString(arguments.Length > 0 ? arguments[0] : default);
        IReadOnlyList<string> list = key switch
        {
            "calendar" => LocaleInfo.AvailableCalendars,
            "collation" => LocaleInfo.Collations,
            "currency" => LocaleInfo.Currencies,
            "numberingSystem" => LocaleInfo.NumberingSystems,
            "timeZone" => GetTimeZones(),
            "unit" => LocaleInfo.SanctionedUnits,
            _ => ThrowHelper.ThrowRangeError<IReadOnlyList<string>>("Invalid key : " + key),
        };

        return IntlOperations.CreateArray(function.Realm, list);
    }

    // AvailablePrimaryTimeZoneIdentifiers, sorted
    private static List<string> GetTimeZones()
    {
        var database = TimeZoneDatabase.Default;
        var result = new List<string>();
        foreach (var identifier in database.AvailableIdentifiers)
        {
            if (database.TryGetTimeZone(identifier, out var zone) && string.Equals(zone.PrimaryIdentifier, identifier, StringComparison.Ordinal))
            {
                result.Add(identifier);
            }
        }

        result.Sort(StringComparer.Ordinal);
        return result;
    }
}
