using System.Collections.Concurrent;
using Meziantou.Framework.JavaScriptEngine.Intl.Data;
using Meziantou.Framework.JavaScriptEngine.TimeZones;

namespace Meziantou.Framework.JavaScriptEngine.Intl;

/// <summary>
/// The localized names of the time zones (UTS 35 "Using Time Zone Names"): the specific and generic names of the zones
/// and of their metazones, the generic location format and the localized GMT format. It follows ICU's TimeZoneFormat.
/// </summary>
internal static class TimeZoneNameFormatter
{
    // Whether a zone uses daylight saving time around a date (ICU kDstCheckRange)
    private const long DstCheckRange = 184L * 86_400;

    private static Dictionary<string, (string CldrId, string? Region)>? s_zones;
    private static Dictionary<string, int>? s_regionZoneCounts;
    private static readonly ConcurrentDictionary<string, string> TargetRegions = new(StringComparer.Ordinal);

    /// <summary>Formats the time zone field of a pattern (z, zzzz, O, OOOO, v, vvvv, V...).</summary>
    public static string Format(CldrLocale locale, string identifier, TemporalTimeZone timeZone, in DateTimeLocalTime time, char symbol, int count, string[]? digits)
    {
        var offsetSeconds = time.OffsetSeconds;
        var cldrId = timeZone.Zone is null ? null : GetCldrId(identifier);
        string? name = null;
        switch (symbol)
        {
            case 'z':
                if (cldrId is not null)
                {
                    name = GetSpecificName(locale, cldrId, time, isLong: count >= 4);
                }

                return name ?? FormatLocalizedGmt(locale, offsetSeconds, isShort: count < 4, digits);

            case 'v':
                if (cldrId is not null)
                {
                    name = GetGenericName(locale, cldrId, timeZone.Zone!, time, isLong: count >= 4);
                }

                return name ?? FormatLocalizedGmt(locale, offsetSeconds, isShort: count < 4, digits);

            case 'V':
                if (cldrId is not null && count >= 3)
                {
                    name = count == 3 ? GetExemplarCity(locale, cldrId) : GetGenericLocationName(locale, cldrId);
                }

                return name ?? FormatLocalizedGmt(locale, offsetSeconds, isShort: false, digits);

            case 'O':
                return FormatLocalizedGmt(locale, offsetSeconds, isShort: count < 4, digits);

            default:
                return FormatLocalizedGmt(locale, offsetSeconds, isShort: false, digits);
        }
    }

    /// <summary>Gets the CLDR identifier of a zone (the first alias of its BCP 47 type, e.g. "Asia/Kolkata" -> "Asia/Calcutta").</summary>
    public static string GetCldrId(string identifier)
    {
        var zones = GetZones();
        if (zones.TryGetValue(identifier, out var entry))
            return entry.CldrId;

        if (TimeZoneDatabase.Default.TryGetTimeZone(identifier, out var record) && zones.TryGetValue(record.PrimaryIdentifier, out entry))
            return entry.CldrId;

        return identifier;
    }

    private static string? GetRegion(string cldrId) => GetZones().TryGetValue(cldrId, out var entry) ? entry.Region : null;

    private static Dictionary<string, (string CldrId, string? Region)> GetZones()
    {
        if (Volatile.Read(ref s_zones) is { } cached)
            return cached;

        var result = new Dictionary<string, (string, string?)>(StringComparer.OrdinalIgnoreCase);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        if (CldrData.Default.Supplemental.GetSection("bcp47") is { } bcp47 && bcp47.TryGetNode("u/tz", out var node) && node.IsTable)
        {
            var table = node.GetTable();
            for (var i = 0; i < table.Count; i++)
            {
                var key = table.GetKey(i);
                if (key.StartsWith('_', StringComparison.Ordinal) || !table.GetValue(i).IsTable)
                    continue;

                var info = table.GetValue(i).GetTable();
                if (!info.TryGetString("_alias", out var aliases))
                    continue;

                string? region = null;
                if (info.TryGetString("_region", out var explicitRegion))
                {
                    region = explicitRegion;
                }
                else if (key.Length == 5)
                {
                    region = key[..2].ToUpperInvariant();
                }

                var names = aliases.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (names.Length == 0)
                    continue;

                var deprecated = info.TryGetString("_deprecated", out var value) && value is "true";
                foreach (var name in names)
                {
                    result.TryAdd(name, (names[0], region));
                }

                if (region is not null && !deprecated)
                {
                    counts[region] = counts.TryGetValue(region, out var count) ? count + 1 : 1;
                }
            }
        }

        Volatile.Write(ref s_regionZoneCounts, counts);
        return CldrData.InitializeOnce(ref s_zones, result);
    }

    private static string? GetZoneString(CldrLocale locale, string cldrId, string path) => locale.GetString(CldrCategory.TimeZoneNames, "zone/" + cldrId + "/" + path);

    private static string? GetMetazoneString(CldrLocale locale, string metazone, string path) => locale.GetString(CldrCategory.TimeZoneNames, "metazone/" + metazone + "/" + path);

    private static string? GetMetazone(string cldrId, long epochSeconds) => CldrData.Default.Supplemental.GetMetazone(cldrId, epochSeconds);

    // The specific name (standard or daylight) of the zone, else of its metazone
    private static string? GetSpecificName(CldrLocale locale, string cldrId, in DateTimeLocalTime time, bool isLong)
    {
        var path = (isLong ? "long/" : "short/") + (time.InDst ? "daylight" : "standard");
        if (GetZoneString(locale, cldrId, path) is { } zoneName)
            return zoneName;

        if (GetMetazone(cldrId, time.EpochSeconds) is { } metazone && GetMetazoneString(locale, metazone, path) is { } metazoneName)
            return metazoneName;

        return null;
    }

    // TZGNCore::getDisplayName: the generic non-location name, else the generic location name
    private static string? GetGenericName(CldrLocale locale, string cldrId, NamedTimeZone zone, in DateTimeLocalTime time, bool isLong)
    {
        var width = isLong ? "long/" : "short/";
        if (GetZoneString(locale, cldrId, width + "generic") is { } zoneName)
            return zoneName;

        if (GetMetazone(cldrId, time.EpochSeconds) is { } metazone)
        {
            string? name = null;
            if (!time.InDst && !UsesDaylightSavingTimeAround(zone, time.EpochSeconds))
            {
                // A zone without daylight saving time around the date uses its standard name, when it differs from the generic one
                var standardName = GetZoneString(locale, cldrId, width + "standard") ?? GetMetazoneString(locale, metazone, width + "standard");
                if (standardName is not null && !string.Equals(standardName, GetMetazoneString(locale, metazone, width + "generic"), StringComparison.OrdinalIgnoreCase))
                {
                    name = standardName;
                }
            }

            if (name is null && GetMetazoneString(locale, metazone, width + "generic") is { } metazoneName)
            {
                // A zone whose offset differs from the one of the reference zone of the metazone uses a partial location name
                name = metazoneName;
                var goldenZone = CldrData.Default.Supplemental.GetMetazoneZone(metazone, GetTargetRegion(locale)) ?? CldrData.Default.Supplemental.GetMetazoneZone(metazone, "001");
                if (goldenZone is not null && goldenZone != cldrId && TimeZoneDatabase.Default.GetTimeZone(goldenZone) is { } golden)
                {
                    var type = golden.GetLocalTimeType(time.EpochSeconds);
                    if (type.OffsetSeconds != time.OffsetSeconds || type.IsDaylightSavingTime != time.InDst)
                    {
                        var location = GetLocationName(locale, cldrId);
                        var fallbackFormat = locale.GetString(CldrCategory.TimeZoneNames, "fallbackFormat") ?? "{1} ({0})";
                        name = fallbackFormat.Replace("{1}", metazoneName, StringComparison.Ordinal).Replace("{0}", location, StringComparison.Ordinal);
                    }
                }
            }

            if (name is not null)
                return name;
        }

        return GetGenericLocationName(locale, cldrId);
    }

    private static bool UsesDaylightSavingTimeAround(NamedTimeZone zone, long epochSeconds)
    {
        return zone.GetLocalTimeType(epochSeconds - DstCheckRange).IsDaylightSavingTime || zone.GetLocalTimeType(epochSeconds + DstCheckRange).IsDaylightSavingTime;
    }

    // The region of the locale, or its likely region
    private static string GetTargetRegion(CldrLocale locale)
    {
        return TargetRegions.GetOrAdd(locale.Name, static name =>
        {
            if (LanguageId.TryParse(name, out var id))
            {
                if (id.HasRegion)
                    return id.Region;

                if (CldrData.Default.Supplemental.LikelySubtags.TryAddLikelySubtags(id, out var maximized) && maximized.HasRegion)
                    return maximized.Region;
            }

            return "001";
        });
    }

    // The generic location format: "{0} Time" with the country when the zone is the only one of its country, else the city
    private static string? GetGenericLocationName(CldrLocale locale, string cldrId)
    {
        var region = GetRegion(cldrId);
        if (region is null or "001" or "ZZ")
            return null;

        var regionFormat = locale.GetString(CldrCategory.TimeZoneNames, "regionFormat") ?? "{0}";
        return regionFormat.Replace("{0}", GetLocationName(locale, cldrId), StringComparison.Ordinal);
    }

    private static string GetLocationName(CldrLocale locale, string cldrId)
    {
        var region = GetRegion(cldrId);
        if (region is not (null or "001" or "ZZ"))
        {
            GetZones();
            var counts = Volatile.Read(ref s_regionZoneCounts);
            var isPrimary = (counts is not null && counts.TryGetValue(region, out var count) && count == 1)
                || CldrData.Default.Supplemental.GetPrimaryZone(region) == cldrId;
            if (isPrimary && locale.GetString(CldrCategory.DisplayNames, "territories/" + region) is { } country)
                return country;
        }

        return GetExemplarCity(locale, cldrId);
    }

    private static string GetExemplarCity(CldrLocale locale, string cldrId)
    {
        if (GetZoneString(locale, cldrId, "exemplarCity") is { } city)
            return city;

        var index = cldrId.LastIndexOf('/', StringComparison.Ordinal);
        return (index >= 0 ? cldrId[(index + 1)..] : cldrId).Replace('_', ' ');
    }

    /// <summary>
    /// The localized GMT format: the gmtFormat with the offset in the hourFormat (long), or with the hours without padding
    /// and the minutes only when they are not zero (short). As ICU, a zero offset is "GMT+0" and not the gmtZeroFormat.
    /// </summary>
    public static string FormatLocalizedGmt(CldrLocale locale, int offsetSeconds, bool isShort, string[]? digits)
    {
        var gmtFormat = locale.GetString(CldrCategory.TimeZoneNames, "gmtFormat") ?? "GMT{0}";
        var hourFormat = locale.GetString(CldrCategory.TimeZoneNames, "hourFormat") ?? "+HH:mm;-HH:mm";
        var separator = hourFormat.IndexOf(';', StringComparison.Ordinal);
        var pattern = offsetSeconds >= 0 ? (separator >= 0 ? hourFormat[..separator] : hourFormat) : (separator >= 0 ? hourFormat[(separator + 1)..] : "-" + hourFormat.TrimStart('+'));
        var absolute = Math.Abs(offsetSeconds);
        var hours = absolute / 3600;
        var minutes = absolute / 60 % 60;
        var seconds = absolute % 60;
        var result = new StringBuilder();
        var hoursEnd = 0;
        var i = 0;
        while (i < pattern.Length)
        {
            var c = pattern[i];
            var start = i;
            while (i < pattern.Length && pattern[i] == c)
            {
                i++;
            }

            var length = i - start;
            switch (c)
            {
                case 'H':
                    AppendNumber(result, hours, isShort ? 1 : length, digits);
                    hoursEnd = result.Length;
                    break;

                case 'm':
                    if (isShort && minutes == 0 && seconds == 0)
                    {
                        // Remove the separator before the minutes
                        while (result.Length > hoursEnd)
                        {
                            result.Length--;
                        }
                    }
                    else
                    {
                        AppendNumber(result, minutes, 2, digits);
                        if (seconds != 0)
                        {
                            result.Append(':');
                            AppendNumber(result, seconds, 2, digits);
                        }
                    }

                    break;

                default:
                    result.Append(c, length);
                    break;
            }
        }

        return gmtFormat.Replace("{0}", result.ToString(), StringComparison.Ordinal);
    }

    private static void AppendNumber(StringBuilder result, int value, int minimumDigits, string[]? digits)
    {
        var text = value.ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(minimumDigits, '0');
        foreach (var c in text)
        {
            result.Append(digits is { Length: 10 } ? digits[c - '0'] : c);
        }
    }
}
