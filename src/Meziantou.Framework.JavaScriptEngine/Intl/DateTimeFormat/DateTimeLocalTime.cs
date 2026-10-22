using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.InteropServices;
using Meziantou.Framework.JavaScriptEngine.Intl.Data;
using Meziantou.Framework.JavaScriptEngine.TimeZones;

namespace Meziantou.Framework.JavaScriptEngine.Intl;

/// <summary>
/// The ToLocalTime record of ECMA-402: the fields of an instant in a time zone and a calendar. The month is the index of
/// the CLDR month names of the calendar (for the Hebrew calendar, 6 is Adar I and 7 is Adar or Adar II).
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal struct DateTimeLocalTime
{
    /// <summary>Gets or sets the UTC instant, in seconds since the epoch (to find the metazone).</summary>
    public long EpochSeconds { get; set; }

    /// <summary>Gets or sets the offset of the time zone, in seconds.</summary>
    public int OffsetSeconds { get; set; }

    public bool InDst { get; set; }

    /// <summary>Gets or sets the day of the week, 1 (Monday) to 7 (Sunday).</summary>
    public int Weekday { get; set; }

    /// <summary>Gets or sets the index of the era in the CLDR data of the calendar, or -1 when the calendar has no era.</summary>
    public int Era { get; set; }

    /// <summary>Gets or sets whether <see cref="Era"/> is an era of the Gregorian calendar (the eras a calendar inherits: calendarData inheritEras).</summary>
    public bool IsGregorianEra { get; set; }

    /// <summary>Gets or sets the year of the era (or the year of the calendar when it has no era).</summary>
    public long Year { get; set; }

    /// <summary>Gets or sets the arithmetic year of the calendar.</summary>
    public long ExtendedYear { get; set; }

    /// <summary>Gets or sets the related ISO year (chinese and dangi), or null.</summary>
    public long? RelatedYear { get; set; }

    /// <summary>Gets or sets the year of the sexagenary cycle, 1 to 60 (chinese and dangi), or null.</summary>
    public int? CyclicYear { get; set; }

    public int Month { get; set; }

    /// <summary>Gets or sets the number of the month displayed by the numeric formats (the ordinal month for the Hebrew calendar).</summary>
    public int NumericMonth { get; set; }

    /// <summary>Gets or sets whether the month is a leap month (chinese and dangi).</summary>
    public bool IsLeapMonth { get; set; }

    /// <summary>Gets or sets whether the year is a leap year (the name of Adar II in the Hebrew calendar).</summary>
    public bool IsLeapYear { get; set; }

    public int Day { get; set; }

    public int Hour { get; set; }

    public int Minute { get; set; }

    public int Second { get; set; }

    public int Millisecond { get; set; }

    /// <summary>The value compared by formatRange for the date fields (era, year, month, day).</summary>
    public readonly (int Era, long Year, int Month, bool Leap, int Day) DateKey => (Era, ExtendedYear, Month, IsLeapMonth, Day);
}

/// <summary>Computes the ToLocalTime records.</summary>
internal static class DateTimeLocalTimeCalculator
{
    private const long NanosecondsPerMillisecond = 1_000_000;
    private const long MillisecondsPerDay = 86_400_000;

    private static readonly ConcurrentDictionary<string, Dictionary<string, int>> EraIndexes = new(StringComparer.Ordinal);
    private static (long StartDays, int Index)[]? s_japaneseEras;

    /// <summary>ToLocalTime(epochNs, calendar, timeZone)</summary>
    public static DateTimeLocalTime ToLocalTime(Int128 epochNanoseconds, string calendar, TemporalTimeZone timeZone)
    {
        var epochMilliseconds = (long)FloorDivide(epochNanoseconds, NanosecondsPerMillisecond);
        var epochSeconds = CalendarMath.FloorDivide(epochMilliseconds, 1000);
        int offsetSeconds;
        var inDst = false;
        if (timeZone.Zone is { } zone)
        {
            var type = zone.GetLocalTimeType(epochSeconds);
            offsetSeconds = type.OffsetSeconds;
            inDst = type.IsDaylightSavingTime;

            // A negative daylight saving time (e.g. Europe/Dublin, whose winter time is the "daylight" time of the
            // database) is the standard time, as in the rearguard format of the database that ICU uses
            var other = zone.GetLocalTimeType(epochSeconds + (182L * 86_400));
            if (other.IsDaylightSavingTime == type.IsDaylightSavingTime)
            {
                other = zone.GetLocalTimeType(epochSeconds - (182L * 86_400));
            }

            if (other.IsDaylightSavingTime != type.IsDaylightSavingTime)
            {
                if (type.IsDaylightSavingTime && type.OffsetSeconds < other.OffsetSeconds)
                {
                    inDst = false;
                }
                else if (!type.IsDaylightSavingTime && type.OffsetSeconds > other.OffsetSeconds)
                {
                    inDst = true;
                }
            }
        }
        else
        {
            offsetSeconds = timeZone.OffsetMinutes * 60;
        }

        var localMilliseconds = epochMilliseconds + (offsetSeconds * 1000L);
        var days = CalendarMath.FloorDivide(localMilliseconds, MillisecondsPerDay);
        var timeOfDay = (int)(localMilliseconds - (days * MillisecondsPerDay));
        var result = new DateTimeLocalTime
        {
            EpochSeconds = epochSeconds,
            OffsetSeconds = offsetSeconds,
            InDst = inDst,
            Hour = timeOfDay / 3_600_000,
            Minute = timeOfDay / 60_000 % 60,
            Second = timeOfDay / 1000 % 60,
            Millisecond = timeOfDay % 1000,
            Weekday = (int)CalendarMath.FloorModulo(days + 3, 7) + 1,
        };

        CalendarMath.CivilFromDays(days, out var isoYear, out var isoMonth, out var isoDay);
        SetDate(ref result, calendar, new IsoDate(isoYear, isoMonth, isoDay), days);
        return result;
    }

    private static Int128 FloorDivide(Int128 value, Int128 divisor)
    {
        var quotient = value / divisor;
        if (value % divisor != 0 && (value < 0) != (divisor < 0))
        {
            quotient--;
        }

        return quotient;
    }

    private static void SetDate(ref DateTimeLocalTime result, string calendar, IsoDate isoDate, long epochDays)
    {
        if (calendar is "gregory" or "iso8601" || !TemporalCalendars.TryGet(calendar, out var temporalCalendar))
        {
            result.Era = isoDate.Year <= 0 ? 0 : 1;
            result.Year = isoDate.Year <= 0 ? 1 - isoDate.Year : isoDate.Year;
            result.ExtendedYear = isoDate.Year;
            result.Month = isoDate.Month;
            result.NumericMonth = isoDate.Month;
            result.Day = isoDate.Day;
            return;
        }

        var date = temporalCalendar.IsoToDate(isoDate);
        result.ExtendedYear = date.Year;
        result.Day = date.Day;
        result.IsLeapYear = date.InLeapYear;
        _ = TemporalParser.TryParseMonthCode(date.MonthCode, out var monthNumber, out var isLeapMonth);
        result.Month = monthNumber;
        result.NumericMonth = calendar is "hebrew" ? date.Month : monthNumber;
        result.Era = -1;
        result.Year = date.EraYear ?? date.Year;
        switch (calendar)
        {
            case "chinese" or "dangi":
                result.IsLeapMonth = isLeapMonth;
                result.RelatedYear = date.Year;
                result.CyclicYear = (int)CalendarMath.FloorModulo(date.Year - 4, 60) + 1;
                result.Year = result.CyclicYear.Value;
                break;

            case "hebrew":
                // The CLDR months: 6 is Adar I (M05L), 7 is Adar (Adar II in a leap year), 8 (Nisan) to 13 (Elul)
                result.Month = isLeapMonth ? 6 : monthNumber >= 6 ? monthNumber + 1 : monthNumber;
                result.Era = GetEraIndex(calendar, date.Era);
                break;

            case "japanese":
                SetJapaneseEra(ref result, date, epochDays);
                break;

            default:
                result.Era = GetEraIndex(calendar, date.Era);
                break;
        }
    }

    // The eras of the Japanese calendar before Meiji 6 are the eras of CLDR (as ICU), from their first day. The dates
    // before the first era of CLDR (Meiji since CLDR 49, Taika before) use the Gregorian eras (inheritEras).
    private static void SetJapaneseEra(ref DateTimeLocalTime result, CalendarDate date, long epochDays)
    {
        if (date.Era is "ce" or "bce")
        {
            var eras = GetJapaneseEras();
            for (var i = eras.Length - 1; i >= 0; i--)
            {
                if (epochDays >= eras[i].StartDays)
                {
                    result.Era = eras[i].Index;
                    result.Year = date.Year - CalendarMath.YearFromDays(eras[i].StartDays) + 1;
                    return;
                }
            }

            result.Era = GetEraIndex("gregory", date.Era);
            result.IsGregorianEra = true;
            return;
        }

        result.Era = GetEraIndex("japanese", date.Era);
    }

    private static (long StartDays, int Index)[] GetJapaneseEras()
    {
        if (Volatile.Read(ref s_japaneseEras) is { } cached)
            return cached;

        var result = new List<(long, int)>();
        if (CldrData.Default.Supplemental.GetSection("calendarData") is { } section && section.TryGetNode("japanese/eras", out var node) && node.IsTable)
        {
            var table = node.GetTable();
            for (var i = 0; i < table.Count; i++)
            {
                if (!table.GetValue(i).IsTable || !table.GetValue(i).GetTable().TryGetString("_start", out var start))
                    continue;

                if (!int.TryParse(table.GetKey(i), NumberStyles.None, CultureInfo.InvariantCulture, out var index))
                    continue;

                // "645-6-19", "-660-2-11"
                var negative = start.StartsWith('-', StringComparison.Ordinal);
                var parts = (negative ? start[1..] : start).Split('-');
                if (parts.Length != 3)
                    continue;

                var year = long.Parse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture) * (negative ? -1 : 1);
                var month = int.Parse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture);
                var day = int.Parse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture);
                result.Add((CalendarMath.DaysFromCivil(year, month, day), index));
            }
        }

        result.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        return CldrData.InitializeOnce(ref s_japaneseEras, [.. result]);
    }

    /// <summary>Gets the index of an era in the CLDR data of a calendar from its code (supplemental calendarData eras: _code, _aliases).</summary>
    public static int GetEraIndex(string calendar, string? eraCode)
    {
        if (eraCode is null)
            return -1;

        var map = EraIndexes.GetOrAdd(calendar, static calendar => LoadEraIndexes(calendar));
        return map.TryGetValue(eraCode, out var index) ? index : -1;
    }

    private static Dictionary<string, int> LoadEraIndexes(string calendar)
    {
        var cldrCalendar = calendar switch
        {
            "gregory" => "gregorian",
            "ethioaa" => "ethiopic-amete-alem",
            _ => calendar,
        };

        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        if (CldrData.Default.Supplemental.GetSection("calendarData") is { } section && section.TryGetNode(cldrCalendar + "/eras", out var node) && node.IsTable)
        {
            var table = node.GetTable();
            for (var i = 0; i < table.Count; i++)
            {
                if (!table.GetValue(i).IsTable || !int.TryParse(table.GetKey(i), NumberStyles.None, CultureInfo.InvariantCulture, out var index))
                    continue;

                var era = table.GetValue(i).GetTable();
                if (era.TryGetString("_code", out var code))
                {
                    result.TryAdd(code, index);
                }

                if (era.TryGetString("_aliases", out var aliases))
                {
                    foreach (var alias in aliases.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    {
                        result.TryAdd(alias, index);
                    }
                }
            }
        }

        return result;
    }
}
