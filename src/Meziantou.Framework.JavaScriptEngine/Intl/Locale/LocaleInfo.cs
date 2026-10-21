using Meziantou.Framework.JavaScriptEngine.Intl.Data;
using Meziantou.Framework.JavaScriptEngine.TimeZones;

namespace Meziantou.Framework.JavaScriptEngine.Intl;

/// <summary>The data of the locales used by Intl.Locale (getCalendars, getHourCycles, getWeekInfo...) and Intl.supportedValuesOf.</summary>
internal static class LocaleInfo
{
    /// <summary>The calendars of AvailableCalendars (the ones of Intl.Era-monthcode), sorted.</summary>
    public static readonly string[] AvailableCalendars =
    [
        "buddhist", "chinese", "coptic", "dangi", "ethioaa", "ethiopic", "gregory", "hebrew", "indian", "islamic-civil",
        "islamic-tbla", "islamic-umalqura", "iso8601", "japanese", "persian", "roc",
    ];

    /// <summary>The simple units sanctioned by ECMA-402 (IsSanctionedSingleUnitIdentifier), sorted.</summary>
    public static readonly string[] SanctionedUnits =
    [
        "acre", "bit", "byte", "celsius", "centimeter", "day", "degree", "fahrenheit", "fluid-ounce", "foot", "gallon",
        "gigabit", "gigabyte", "gram", "hectare", "hour", "inch", "kilobit", "kilobyte", "kilogram", "kilometer", "liter",
        "megabit", "megabyte", "meter", "microsecond", "mile", "mile-scandinavian", "milliliter", "millimeter",
        "millisecond", "minute", "month", "nanosecond", "ounce", "percent", "petabyte", "pound", "second", "stone",
        "terabit", "terabyte", "week", "yard", "year",
    ];

    private static string[]? s_numberingSystems;
    private static string[]? s_collations;
    private static string[]? s_currencies;
    private static Dictionary<string, List<string>>? s_regionTimeZones;

    private static CldrData Data => CldrData.Default;

    /// <summary>Gets the numbering systems with simple digit mappings (the numeric numbering systems of CLDR), sorted.</summary>
    public static string[] NumberingSystems => Volatile.Read(ref s_numberingSystems) ?? CldrData.InitializeOnce(ref s_numberingSystems, [.. Data.Supplemental.GetNumericNumberingSystems()]);

    /// <summary>Gets the collation types of all the locales, without "standard" and "search", sorted.</summary>
    public static string[] Collations => Volatile.Read(ref s_collations) ?? CldrData.InitializeOnce(ref s_collations, ComputeCollations());

    /// <summary>Gets the ISO 4217 currency codes known by the data, sorted.</summary>
    public static string[] Currencies => Volatile.Read(ref s_currencies) ?? CldrData.InitializeOnce(ref s_currencies, ComputeCurrencies());

    /// <summary>Gets the BCP 47 identifier of a CLDR calendar ("gregorian" -> "gregory").</summary>
    public static string ToBcp47Calendar(string calendar) => calendar switch
    {
        "gregorian" => "gregory",
        "ethiopic-amete-alem" => "ethioaa",
        _ => calendar,
    };

    /// <summary>Gets the BCP 47 identifier of a CLDR collation type ("phonebook" -> "phonebk", "traditional" -> "trad").</summary>
    public static string ToBcp47Collation(string type) => type.StartsWith('_', StringComparison.Ordinal) ? type : LocaleCanonicalizer.CanonicalizeUnicodeValue("co", type);

    public static bool IsAvailableCalendar(string calendar) => Array.BinarySearch(AvailableCalendars, calendar, StringComparer.Ordinal) >= 0;

    public static bool IsAvailableNumberingSystem(string numberingSystem) => Array.BinarySearch(NumberingSystems, numberingSystem, StringComparer.Ordinal) >= 0;

    private static string[] ComputeCollations()
    {
        var collation = Data.Collation;
        var result = new SortedSet<string>(StringComparer.Ordinal) { "emoji", "eor" };
        foreach (var locale in collation.TailoredLocales)
        {
            if (collation.GetOwnTailorings(locale) is not { } table)
                continue;

            for (var i = 0; i < table.Count; i++)
            {
                var key = ToBcp47Collation(table.GetKey(i));
                if (key is not ("_default" or "standard" or "search") && LanguageTag.IsUnicodeType(key))
                {
                    result.Add(key);
                }
            }
        }

        return [.. result];
    }

    private static string[] ComputeCurrencies()
    {
        var result = new SortedSet<string>(StringComparer.Ordinal);
        var locale = Data.GetLocale("en");
        if (locale?.GetOwnTable(CldrCategory.Currencies) is { } table)
        {
            for (var i = 0; i < table.Count; i++)
            {
                var key = table.GetKey(i);
                if (key.Length == 3 && char.IsAsciiLetterUpper(key[0]) && char.IsAsciiLetterUpper(key[1]) && char.IsAsciiLetterUpper(key[2]))
                {
                    result.Add(key);
                }
            }
        }

        if (Data.Supplemental.GetSection("currencyData") is { } currencyData && currencyData.TryGetNode("fractions", out var node) && node.IsTable)
        {
            var fractions = node.GetTable();
            for (var i = 0; i < fractions.Count; i++)
            {
                var key = fractions.GetKey(i);
                if (key.Length == 3 && key is not "DEFAULT")
                {
                    result.Add(key);
                }
            }
        }

        return [.. result];
    }

    /// <summary>Gets the language id of a locale (its extensions are ignored).</summary>
    public static LanguageId GetLanguageId(LanguageTag tag) => new(tag.Language, tag.Script, tag.Region, string.Join('-', tag.Variants));

    /// <summary>
    /// The region to use for the regional preferences of a locale (RegionPreference): the "rg" override when the data
    /// has it, else the region subtag, else the region of the "sd" subdivision, else the likely region, else "001".
    /// </summary>
    public static string GetRegionPreference(LanguageTag tag, Func<string, bool> hasData)
    {
        if (GetRegionOverride(tag) is { } regionOverride && hasData(regionOverride))
            return regionOverride;

        if (tag.Region.Length > 0)
            return tag.Region;

        if (tag.GetKeyword("sd") is { Length: >= 3 } subdivision && LanguageTag.IsRegionSubtag(subdivision.AsSpan(0, 2)))
            return subdivision[..2].ToUpperInvariant();

        var likely = Data.Supplemental.LikelySubtags;
        if (likely.TryAddLikelySubtags(new LanguageId(tag.Language, tag.Script, ""), out var maximized) && maximized.HasRegion)
            return maximized.Region;

        return "001";
    }

    // The region of a "rg" keyword ("gbzzzz" -> "GB")
    private static string? GetRegionOverride(LanguageTag tag)
    {
        if (tag.GetKeyword("rg") is { Length: 6 } value && value.EndsWith("zzzz", StringComparison.Ordinal) && LanguageTag.IsRegionSubtag(value.AsSpan(0, 2)))
            return value[..2].ToUpperInvariant();

        return null;
    }

    /// <summary>CalendarsOfLocale: the preferred calendars of the region of the locale.</summary>
    public static List<string> GetCalendars(LanguageTag tag)
    {
        var supplemental = Data.Supplemental;
        var section = supplemental.GetSection("calendarPreferenceData");
        var region = GetRegionPreference(tag, region => section is not null && section.TryGetString(region, out _));
        var result = new List<string>();
        foreach (var calendar in supplemental.GetCalendarPreferences(region))
        {
            var name = ToBcp47Calendar(calendar);
            if (IsAvailableCalendar(name) && !result.Contains(name))
            {
                result.Add(name);
            }
        }

        if (result.Count == 0)
        {
            result.Add("gregory");
        }

        return result;
    }

    /// <summary>CollationsOfLocale: the collation types of the locale, without "standard" and "search", sorted.</summary>
    public static List<string> GetCollations(LanguageTag tag)
    {
        var result = new SortedSet<string>(StringComparer.Ordinal) { "emoji", "eor" };
        var id = GetLanguageId(tag);
        if (!id.IsUnknownLanguage && Data.Locales.FindLocale(id) is { IsRoot: false })
        {
            foreach (var cldrType in Data.Collation.GetCollationTypes(id))
            {
                var type = ToBcp47Collation(cldrType);
                if (type is not ("standard" or "search") && LanguageTag.IsUnicodeType(type))
                {
                    result.Add(type);
                }
            }
        }

        return [.. result];
    }

    /// <summary>HourCyclesOfLocale: the preferred hour cycle of the locale.</summary>
    public static List<string> GetHourCycles(LanguageTag tag)
    {
        var supplemental = Data.Supplemental;
        var region = GetRegionPreference(tag, region => supplemental.GetTimeData(region) is not null);
        var timeData = supplemental.GetTimeData(tag.Language + "-" + region) ?? supplemental.GetTimeData(region) ?? supplemental.GetTimeData("001");
        // The preferred hour cycle only, as ICU (the allowed ones are for the skeletons of the formats)
        var result = new List<string>();
        if (timeData is not null)
        {
            Add(timeData.Preferred);
        }

        if (result.Count == 0)
        {
            result.Add("h23");
        }

        return result;

        void Add(string symbol)
        {
            var hourCycle = symbol.Length == 0 ? null : symbol[0] switch
            {
                'h' => "h12",
                'H' => "h23",
                'K' => "h11",
                'k' => "h24",
                _ => null,
            };

            if (hourCycle is not null && !result.Contains(hourCycle))
            {
                result.Add(hourCycle);
            }
        }
    }

    /// <summary>NumberingSystemsOfLocale: the default numbering system of the locale.</summary>
    public static string GetDefaultNumberingSystem(LanguageTag tag)
    {
        var locale = Data.Locales.FindLocale(GetLanguageId(tag));
        var numberingSystem = locale.GetDefaultNumberingSystem();
        return IsAvailableNumberingSystem(numberingSystem) ? numberingSystem : "latn";
    }

    /// <summary>TimeZonesOfLocale: the primary time zone identifiers of a region, sorted.</summary>
    public static List<string> GetTimeZones(string region)
    {
        var map = Volatile.Read(ref s_regionTimeZones) ?? CldrData.InitializeOnce(ref s_regionTimeZones, ComputeRegionTimeZones());
        if (!map.TryGetValue(region, out var zones))
            return [];

        return [.. zones];
    }

    // bcp47/u/tz: the identifiers are "<region><city>" (e.g. "frpar"), or have a "_region"; "_alias" lists the IANA names
    private static Dictionary<string, List<string>> ComputeRegionTimeZones()
    {
        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var database = TimeZoneDatabase.Default;
        if (Data.Supplemental.GetSection("bcp47") is not { } bcp47 || !bcp47.TryGetNode("u/tz", out var node) || !node.IsTable)
            return result;

        var table = node.GetTable();
        for (var i = 0; i < table.Count; i++)
        {
            var key = table.GetKey(i);
            if (key.StartsWith('_', StringComparison.Ordinal) || !table.GetValue(i).IsTable)
                continue;

            var info = table.GetValue(i).GetTable();
            if (info.TryGetString("_deprecated", out var deprecated) && deprecated is "true")
                continue;

            if (!info.TryGetString("_alias", out var aliases))
                continue;

            string region;
            if (info.TryGetString("_region", out var explicitRegion))
            {
                region = explicitRegion;
            }
            else if (key.Length == 5)
            {
                region = key[..2].ToUpperInvariant();
            }
            else
            {
                continue;
            }

            var alias = aliases.Split(' ')[0];
            if (!database.TryGetTimeZone(alias, out var zone))
                continue;

            if (!result.TryGetValue(region, out var zones))
            {
                zones = [];
                result.Add(region, zones);
            }

            if (!zones.Contains(zone.PrimaryIdentifier))
            {
                zones.Add(zone.PrimaryIdentifier);
            }
        }

        foreach (var zones in result.Values)
        {
            zones.Sort(StringComparer.Ordinal);
        }

        return result;
    }

    /// <summary>TextDirectionOfLocale: "rtl" or "ltr" from the script of the locale (or of its likely subtags), or null when unknown.</summary>
    public static string? GetTextDirection(LanguageTag tag)
    {
        var script = tag.Script;
        if (script.Length == 0)
        {
            var likely = Data.Supplemental.LikelySubtags;
            if (likely.TryAddLikelySubtags(new LanguageId(tag.Language, "", tag.Region), out var maximized))
            {
                script = maximized.Script;
            }
        }

        if (script.Length == 0)
            return null;

        return Data.Supplemental.GetString("scriptMetadata", script) switch
        {
            "YES" => "rtl",
            "NO" => "ltr",
            _ => null,
        };
    }

    /// <summary>WeekInfoOfLocale: the first day (1 = Monday to 7 = Sunday) and the weekend days.</summary>
    public static (int FirstDay, List<int> Weekend) GetWeekInfo(LanguageTag tag, string? firstDayOfWeek)
    {
        var supplemental = Data.Supplemental;
        var section = supplemental.GetSection("weekData");
        var region = GetRegionPreference(tag, region => section is not null && section.TryGetNode("firstDay/" + region, out _));
        var data = supplemental.GetWeekData(region);
        var firstDay = data.FirstDay;
        if (firstDayOfWeek is not null && WeekdayToNumber(firstDayOfWeek) is { } day)
        {
            firstDay = day;
        }

        var weekend = new List<int>();
        for (var d = data.WeekendStart; ; d = (d % 7) + 1)
        {
            weekend.Add(d);
            if (d == data.WeekendEnd || weekend.Count == 7)
                break;
        }

        weekend.Sort();
        return (firstDay, weekend);
    }

    /// <summary>WeekdayToUValue(fw): "0" to "7" become "sun" to "sat" (0 and 7 are Sunday); other values are unchanged.</summary>
    public static string WeekdayToUValue(string value) => value switch
    {
        "0" or "7" => "sun",
        "1" => "mon",
        "2" => "tue",
        "3" => "wed",
        "4" => "thu",
        "5" => "fri",
        "6" => "sat",
        _ => value,
    };

    /// <summary>WeekdayUValueToNumber(fw)</summary>
    public static int? WeekdayToNumber(string value) => value switch
    {
        "mon" => 1,
        "tue" => 2,
        "wed" => 3,
        "thu" => 4,
        "fri" => 5,
        "sat" => 6,
        "sun" => 7,
        _ => null,
    };
}
