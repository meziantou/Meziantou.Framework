using System.Collections.Concurrent;
using Meziantou.Framework.JavaScriptEngine.Intl.Data;

namespace Meziantou.Framework.JavaScriptEngine.Intl;

/// <summary>
/// The date data of a locale and a calendar (ca-gregorian.json or the calendar of ca-*.json): the names of the fields,
/// the patterns, and the pattern generator. The instances are cached and thread-safe.
/// </summary>
internal sealed class DateTimeLocaleData
{
    private static readonly ConcurrentDictionary<(string Locale, string Calendar), DateTimeLocaleData> Cache = new();

    private readonly string _prefix;
    private readonly CldrCategory _category;
    private readonly ConcurrentDictionary<string, string?> _strings = new(StringComparer.Ordinal);
    private DateTimePatternGenerator? _generator;
    private DateTimeLocaleData? _gregorian;

    private DateTimeLocaleData(CldrLocale locale, string calendar)
    {
        Locale = locale;
        Calendar = calendar;
        CldrCalendar = GetCldrCalendar(calendar);
        if (CldrCalendar is "gregorian")
        {
            _category = CldrCategory.Gregorian;
            _prefix = "";
        }
        else
        {
            _category = CldrCategory.Calendars;
            _prefix = CldrCalendar + "/";
        }
    }

    /// <summary>Gets the locale of the data.</summary>
    public CldrLocale Locale { get; }

    /// <summary>Gets the calendar (a BCP 47 calendar type, e.g. "gregory").</summary>
    public string Calendar { get; }

    /// <summary>Gets the CLDR calendar whose data is used (e.g. "gregorian", "ethiopic-amete-alem").</summary>
    public string CldrCalendar { get; }

    public DateTimePatternGenerator Generator => Volatile.Read(ref _generator) ?? CldrData.InitializeOnce(ref _generator, new DateTimePatternGenerator(this));

    /// <summary>Gets the data of the Gregorian calendar of the same locale.</summary>
    public DateTimeLocaleData Gregorian => CldrCalendar is "gregorian" ? this : (Volatile.Read(ref _gregorian) ?? CldrData.InitializeOnce(ref _gregorian, Get(Locale, "gregory")));

    public static DateTimeLocaleData Get(CldrLocale locale, string calendar) => Cache.GetOrAdd((locale.Name, calendar), static (key, locale) => new DateTimeLocaleData(locale, key.Calendar), locale);

    // The CLDR calendar of a BCP 47 calendar type; iso8601 uses the data of CLDR when the locale has it, else gregorian
    private static string GetCldrCalendar(string calendar) => calendar switch
    {
        "gregory" => "gregorian",
        "ethioaa" => "ethiopic-amete-alem",
        "islamicc" => "islamic-civil",
        "iso8601" => CldrData.Default.Locales.Root.Contains(CldrCategory.Calendars, "iso8601/dateFormats") ? "iso8601" : "gregorian",
        _ => calendar,
    };

    /// <summary>Gets a string of the calendar data (e.g. "months/format/wide/1"), with inheritance, or null.</summary>
    public string? GetString(string path)
    {
        if (_strings.TryGetValue(path, out var cached))
            return cached;

        // The narrow no-break spaces of CLDR (e.g. "h:mm\u202Fa") are spaces, as in the browsers (for web compatibility)
        var value = Locale.GetString(_category, _prefix + path)?.Replace('\u202F', ' ');
        _strings.TryAdd(path, value);
        return value;
    }

    /// <summary>Gets a string of the calendar data, else of the Gregorian calendar (the names that a calendar does not define).</summary>
    public string? GetStringOrGregorian(string path) => GetString(path) ?? (CldrCalendar is "gregorian" ? null : Gregorian.GetString(path));

    /// <summary>Gets the keys of a table of the calendar data (e.g. "dateTimeFormats/availableFormats").</summary>
    public IReadOnlyList<string> GetKeys(string path) => Locale.GetKeys(_category, _prefix + path);

    /// <summary>Gets a string of another category of the locale (e.g. Fields "year/displayName").</summary>
    public string? GetLocaleString(CldrCategory category, string path) => Locale.GetString(category, path);

    /// <summary>Gets the date format of a style (dateFormats/full...).</summary>
    public string GetDateFormat(string style) => GetString("dateFormats/" + style) ?? Gregorian.GetString("dateFormats/" + style) ?? "y-MM-dd";

    /// <summary>Gets the time format of a style (timeFormats/full...).</summary>
    public string GetTimeFormat(string style) => GetString("timeFormats/" + style) ?? Gregorian.GetString("timeFormats/" + style) ?? "HH:mm:ss";

    /// <summary>
    /// Gets the pattern that combines a date ({1}) and a time ({0}) for a style: the "atTime" variant as ICU, else the
    /// standard one.
    /// </summary>
    public string GetDateTimeFormat(string style)
    {
        return GetString("dateTimeFormats-atTime/standard/" + style)
            ?? GetString("dateTimeFormats/" + style)
            ?? Gregorian.GetString("dateTimeFormats-atTime/standard/" + style)
            ?? Gregorian.GetString("dateTimeFormats/" + style)
            ?? "{1} {0}";
    }

    /// <summary>Gets the standard pattern that combines a date ({1}) and a time ({0}) for a style (used by the interval formats).</summary>
    public string GetStandardDateTimeFormat(string style)
    {
        return GetString("dateTimeFormats/" + style)
            ?? Gregorian.GetString("dateTimeFormats/" + style)
            ?? "{1} {0}";
    }
}
