using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.InteropServices;
using Meziantou.Framework.JavaScriptEngine.Intl.Data;

namespace Meziantou.Framework.JavaScriptEngine.Intl;

/// <summary>A part of a formatted date: its type ("year", "literal"...), its value and its source in a range.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct DateTimePart(string Type, string Value, string Source = "shared");

/// <summary>FormatDateTimePattern: the parts of an instant formatted with a pattern (the fields of the CLDR data).</summary>
internal static class DateTimeFormatter
{
    private static readonly string[] WeekdayKeys = ["mon", "tue", "wed", "thu", "fri", "sat", "sun"];
    private static readonly ConcurrentDictionary<string, DayPeriodRule[]> DayPeriodRules = new(StringComparer.Ordinal);
    private static readonly TemporalTimeZone PlainTimeZone = TemporalTimeZone.FromOffset(0);

    /// <summary>Gets the digits of a numbering system as strings (a digit can be a surrogate pair), or null.</summary>
    public static string[]? GetDigitStrings(string? digits)
    {
        if (string.IsNullOrEmpty(digits))
            return null;

        var result = new string[10];
        var index = 0;
        for (var i = 0; i < digits.Length && index < 10; i++)
        {
            if (char.IsHighSurrogate(digits[i]) && i + 1 < digits.Length)
            {
                result[index++] = digits.Substring(i, 2);
                i++;
            }
            else
            {
                result[index++] = digits[i].ToString();
            }
        }

        return index == 10 ? result : null;
    }

    /// <summary>Appends a non-negative or negative integer with at least <paramref name="minimumDigits"/> digits, in a numbering system.</summary>
    public static void AppendNumber(StringBuilder result, long value, int minimumDigits, string[]? digits)
    {
        if (value < 0)
        {
            result.Append('-');
            value = -value;
        }

        var text = value.ToString(CultureInfo.InvariantCulture);
        for (var i = text.Length; i < minimumDigits; i++)
        {
            result.Append(digits is null ? "0" : digits[0]);
        }

        foreach (var c in text)
        {
            if (digits is null)
            {
                result.Append(c);
            }
            else
            {
                result.Append(digits[c - '0']);
            }
        }
    }

    private static string FormatNumber(long value, int minimumDigits, string[]? digits)
    {
        var builder = new StringBuilder();
        AppendNumber(builder, value, minimumDigits, digits);
        return builder.ToString();
    }

    /// <summary>FormatDateTimePattern(dateTimeFormat, format, pattern, epochNanoseconds, isPlain)</summary>
    public static void FormatPattern(JsDateTimeFormatObject dtf, DateTimePatternPart[] pattern, Int128 epochNanoseconds, bool isPlain, List<DateTimePart> result, string source = "shared")
    {
        var timeZone = isPlain ? PlainTimeZone : dtf.TimeZone;
        var time = DateTimeLocalTimeCalculator.ToLocalTime(epochNanoseconds, dtf.Calendar, timeZone);
        FormatPattern(dtf, pattern, time, result, source);
    }

    /// <summary>Formats the fields of a pattern for a local time.</summary>
    public static void FormatPattern(JsDateTimeFormatObject dtf, DateTimePatternPart[] pattern, in DateTimeLocalTime time, List<DateTimePart> result, string source = "shared", List<char>? symbols = null)
    {
        var hasMinute = false;
        var hasSecond = false;
        foreach (var part in pattern)
        {
            hasMinute |= part.Symbol == 'm';
            hasSecond |= part.Symbol == 's';
        }

        for (var i = 0; i < pattern.Length; i++)
        {
            var part = pattern[i];
            if (part.IsLiteral)
            {
                var count = result.Count;
                var literal = part.Literal!;
                if (i > 0 && i + 1 < pattern.Length && pattern[i - 1].Symbol == 's' && pattern[i + 1].Symbol == 'S' && literal.Length == 1)
                {
                    // The decimal separator of the fractional seconds is the one of the numbering system
                    literal = dtf.DecimalSeparator;
                }
                else
                {
                    literal = literal.Replace(":", dtf.TimeSeparator, StringComparison.Ordinal);
                }

                AddLiteral(result, literal, source);
                if (result.Count != count)
                {
                    symbols?.Add('\0');
                }

                continue;
            }

            var type = DateTimePattern.GetPartType(part.Symbol);
            var value = FormatField(dtf, part.Symbol, part.Count, time, hasMinute, hasSecond);
            if (type is null)
            {
                result.Add(new DateTimePart("unknown", value, source));
                symbols?.Add(part.Symbol);
            }
            else if (value.Length > 0)
            {
                result.Add(new DateTimePart(type, value, source));
                symbols?.Add(part.Symbol);
            }
        }
    }

    private static void AddLiteral(List<DateTimePart> result, string value, string source)
    {
        if (value.Length == 0)
            return;

        if (result.Count > 0 && result[^1].Type is "literal" && result[^1].Source == source)
        {
            result[^1] = result[^1] with { Value = result[^1].Value + value };
            return;
        }

        result.Add(new DateTimePart("literal", value, source));
    }

    private static string GetWidth(int count) => count switch
    {
        4 => "wide",
        5 => "narrow",
        6 => "short",
        _ => "abbreviated",
    };

    private static string FormatField(JsDateTimeFormatObject dtf, char symbol, int count, in DateTimeLocalTime time, bool hasMinute, bool hasSecond)
    {
        var data = dtf.Data;
        var digits = dtf.Digits;
        switch (symbol)
        {
            case 'G':
                {
                    if (time.Era < 0)
                        return "";

                    var path = count switch
                    {
                        4 => "eras/eraNames/",
                        5 => "eras/eraNarrow/",
                        _ => "eras/eraAbbr/",
                    };

                    var era = time.Era.ToString(CultureInfo.InvariantCulture);
                    return data.GetString(path + era) ?? data.GetString("eras/eraAbbr/" + era) ?? era;
                }

            case 'y':
                if (count == 2)
                    return FormatNumber(Math.Abs(time.Year) % 100, 2, digits);

                return FormatNumber(time.Year, count, digits);

            case 'Y' or 'u':
                return FormatNumber(time.ExtendedYear, count == 2 ? 2 : count, digits);

            case 'r':
                return FormatNumber(time.RelatedYear ?? time.ExtendedYear, count, digits);

            case 'U':
                {
                    if (time.CyclicYear is not { } cyclicYear)
                        return FormatNumber(time.Year, 1, digits);

                    var width = count switch
                    {
                        4 => "wide",
                        5 => "narrow",
                        _ => "abbreviated",
                    };

                    var number = cyclicYear.ToString(CultureInfo.InvariantCulture);
                    return data.GetString("cyclicNameSets/years/format/" + width + "/" + number)
                        ?? data.GetString("cyclicNameSets/years/format/abbreviated/" + number)
                        ?? FormatNumber(cyclicYear, 1, digits);
                }

            case 'M' or 'L':
                return FormatMonth(data, symbol == 'L' ? "stand-alone" : "format", count, time, digits);

            case 'd':
                return FormatNumber(time.Day, count, digits);

            case 'E' or 'c' or 'e':
                if (symbol != 'E' && count < 3)
                    return FormatNumber(time.Weekday, count, digits);

                {
                    var context = symbol == 'c' ? "stand-alone" : "format";
                    var key = WeekdayKeys[time.Weekday - 1];
                    var width = count < 3 ? "abbreviated" : GetWidth(count);
                    return data.GetStringOrGregorian("days/" + context + "/" + width + "/" + key)
                        ?? data.GetStringOrGregorian("days/format/" + width + "/" + key)
                        ?? key;
                }

            case 'a':
                return FormatAmPm(data, count, time.Hour);

            case 'b':
                {
                    var width = GetWidth(Math.Min(count, 5));
                    var minute = hasMinute ? time.Minute : 0;
                    var second = hasSecond ? time.Second : 0;
                    if (minute == 0 && second == 0)
                    {
                        var name = time.Hour switch
                        {
                            12 => data.GetStringOrGregorian("dayPeriods/format/" + width + "/noon"),
                            0 => data.GetStringOrGregorian("dayPeriods/format/" + width + "/midnight"),
                            _ => null,
                        };

                        if (name is not null)
                            return name;
                    }

                    return FormatAmPm(data, count, time.Hour);
                }

            case 'B':
                return FormatFlexibleDayPeriod(data, count, time, hasMinute, hasSecond);

            case 'h':
                {
                    var hour = time.Hour % 12;
                    return FormatNumber(hour == 0 ? 12 : hour, count, digits);
                }

            case 'K':
                return FormatNumber(time.Hour % 12, count, digits);

            case 'H':
                return FormatNumber(time.Hour, count, digits);

            case 'k':
                return FormatNumber(time.Hour == 0 ? 24 : time.Hour, count, digits);

            case 'm':
                return FormatNumber(time.Minute, count, digits);

            case 's':
                return FormatNumber(time.Second, count, digits);

            case 'S':
                {
                    var value = (long)time.Millisecond;
                    if (count <= 3)
                        return FormatNumber(value / (count == 1 ? 100 : count == 2 ? 10 : 1), count, digits);

                    var result = new StringBuilder();
                    AppendNumber(result, value, 3, digits);
                    for (var i = 3; i < count; i++)
                    {
                        result.Append(digits is null ? "0" : digits[0]);
                    }

                    return result.ToString();
                }

            case 'z' or 'Z' or 'O' or 'v' or 'V' or 'X' or 'x':
                return TimeZoneNameFormatter.Format(data.Locale, dtf.TimeZoneIdentifier, dtf.TimeZone, time, symbol, count, digits);

            default:
                return new string(symbol, count);
        }
    }

    private static string FormatMonth(DateTimeLocaleData data, string context, int count, in DateTimeLocalTime time, string[]? digits)
    {
        string value;
        string? leapPatternPath;
        if (count < 3)
        {
            value = FormatNumber(time.NumericMonth, count, digits);
            leapPatternPath = "monthPatterns/numeric/all/leap";
        }
        else
        {
            var width = GetWidth(Math.Min(count, 5));
            var month = time.Month.ToString(CultureInfo.InvariantCulture);
            string? name = null;
            if (time.IsLeapYear && time.Month == 7 && data.Calendar is "hebrew")
            {
                name = data.GetString("months/" + context + "/" + width + "/7-yeartype-leap");
            }

            name ??= data.GetString("months/" + context + "/" + width + "/" + month)
                ?? data.GetString("months/format/" + width + "/" + month)
                ?? data.GetStringOrGregorian("months/" + context + "/" + width + "/" + month);
            value = name ?? month;
            leapPatternPath = "monthPatterns/" + context + "/" + width + "/leap";
        }

        if (time.IsLeapMonth)
        {
            var leapPattern = data.GetString(leapPatternPath) ?? data.GetString("monthPatterns/format/abbreviated/leap") ?? "{0}bis";
            value = leapPattern.Replace("{0}", value, StringComparison.Ordinal);
        }

        return value;
    }

    private static string FormatAmPm(DateTimeLocaleData data, int count, int hour)
    {
        var width = GetWidth(Math.Min(count, 5));
        var key = hour < 12 ? "am" : "pm";
        return data.GetStringOrGregorian("dayPeriods/format/" + width + "/" + key)
            ?? data.GetStringOrGregorian("dayPeriods/format/abbreviated/" + key)
            ?? key.ToUpperInvariant();
    }

    // The flexible day periods (B): noon at 12:00, else the period of the rules of the language that contains the hour
    private static string FormatFlexibleDayPeriod(DateTimeLocaleData data, int count, in DateTimeLocalTime time, bool hasMinute, bool hasSecond)
    {
        var rules = GetDayPeriodRules(data.Locale);
        if (rules.Length == 0)
            return FormatAmPm(data, count, time.Hour);

        var width = GetWidth(Math.Min(count, 5));
        var minute = hasMinute ? time.Minute : 0;
        var second = hasSecond ? time.Second : 0;
        if (time.Hour == 12 && minute == 0 && second == 0 && HasRule(rules, "noon"))
        {
            if (data.GetStringOrGregorian("dayPeriods/format/" + width + "/noon") is { } noon)
                return noon;
        }

        var period = GetDayPeriod(rules, time.Hour);
        if (period is null or "am" or "pm")
            return FormatAmPm(data, count, time.Hour);

        return data.GetStringOrGregorian("dayPeriods/format/" + width + "/" + period)
            ?? data.GetStringOrGregorian("dayPeriods/format/abbreviated/" + period)
            ?? FormatAmPm(data, count, time.Hour);
    }

    /// <summary>Gets the flexible day period of an hour (e.g. "morning1"), or null.</summary>
    public static string? GetDayPeriod(DayPeriodRule[] rules, int hour)
    {
        var minutes = hour * 60;
        foreach (var rule in rules)
        {
            if (rule.At is not null)
                continue;

            if (rule.From <= rule.Before)
            {
                if (minutes >= rule.From && minutes < rule.Before)
                    return rule.Period;
            }
            else if (minutes >= rule.From || minutes < rule.Before)
            {
                return rule.Period;
            }
        }

        return null;
    }

    private static bool HasRule(DayPeriodRule[] rules, string period)
    {
        foreach (var rule in rules)
        {
            if (rule.Period == period)
                return true;
        }

        return false;
    }

    /// <summary>Gets the day period rules of the language of a locale (supplemental dayPeriods.json), or an empty array.</summary>
    public static DayPeriodRule[] GetDayPeriodRules(CldrLocale locale)
    {
        return DayPeriodRules.GetOrAdd(locale.Name, static (_, locale) =>
        {
            var supplemental = CldrData.Default.Supplemental;
            for (var current = locale; current is not null && !current.IsRoot; current = current.Parent)
            {
                var rules = supplemental.GetDayPeriodRules(current.Name);
                if (rules.Count > 0)
                    return [.. rules];
            }

            var languageRules = supplemental.GetDayPeriodRules(locale.Language);
            return [.. languageRules];
        }, locale);
    }
}
