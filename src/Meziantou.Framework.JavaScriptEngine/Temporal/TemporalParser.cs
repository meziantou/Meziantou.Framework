using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>The goal symbols of the RFC 9557 / ISO 8601 grammar (13.31) accepted by ParseISODateTime.</summary>
internal enum TemporalParseGoal : byte
{
    /// <summary>TemporalDateTimeString[~Zoned]</summary>
    DateTime,

    /// <summary>TemporalDateTimeString[+Zoned]</summary>
    ZonedDateTime,

    /// <summary>TemporalInstantString</summary>
    Instant,

    /// <summary>TemporalTimeString</summary>
    Time,

    /// <summary>TemporalYearMonthString</summary>
    YearMonth,

    /// <summary>TemporalMonthDayString</summary>
    MonthDay,
}

/// <summary>
/// A hand-written parser of the RFC 9557 / ISO 8601 grammar of Temporal (https://tc39.es/proposal-temporal/#sec-temporal-iso8601grammar).
/// Each goal symbol must match the whole string (ParseText).
/// </summary>
internal static class TemporalParser
{
    private static readonly TemporalParseGoal[] AllGoals = [TemporalParseGoal.ZonedDateTime, TemporalParseGoal.DateTime, TemporalParseGoal.Instant, TemporalParseGoal.Time, TemporalParseGoal.MonthDay, TemporalParseGoal.YearMonth];

    /// <summary>The goals of ParseTemporalCalendarString and ParseTemporalTimeZoneString.</summary>
    public static ReadOnlySpan<TemporalParseGoal> AnyGoal => AllGoals;

    [StructLayout(LayoutKind.Auto)]
    private struct Annotation
    {
        public string Key;
        public string Value;
        public bool Critical;
    }

    /// <summary>The parse nodes found while parsing a string.</summary>
    private sealed class ParseNodes
    {
        public bool HasYear;
        public long Year;
        public int Month;
        public bool HasDay;
        public int Day;
        public bool HasTime;
        public int Hour;
        public int Minute;
        public int Second;
        public int FractionNanoseconds;
        public bool Z;
        public string? OffsetString;
        public bool OffsetHasSeconds;
        public string? TimeZoneAnnotation;
        public List<Annotation>? Annotations;

        public void Reset()
        {
            HasYear = false;
            Year = 0;
            Month = 0;
            HasDay = false;
            Day = 0;
            HasTime = false;
            Hour = 0;
            Minute = 0;
            Second = 0;
            FractionNanoseconds = 0;
            Z = false;
            OffsetString = null;
            OffsetHasSeconds = false;
            TimeZoneAnnotation = null;
            Annotations?.Clear();
        }
    }

    // ------------------------------------------------------------------------------------------------------------------
    // ParseISODateTime
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>ParseISODateTime(isoString, allowedFormats): throws a RangeError when the string is not valid.</summary>
    public static IsoDateTimeParseResult ParseIsoDateTime(string text, ReadOnlySpan<TemporalParseGoal> goals)
    {
        if (!TryParseIsoDateTime(text, goals, out var result, out var error))
            return ThrowHelper.ThrowRangeError<IsoDateTimeParseResult>(error);

        return result;
    }

    public static bool TryParseIsoDateTime(string text, ReadOnlySpan<TemporalParseGoal> goals, [NotNullWhen(true)] out IsoDateTimeParseResult? result, out string error)
    {
        var nodes = new ParseNodes();
        foreach (var goal in goals)
        {
            nodes.Reset();
            if (!TryParseGoal(text, goal, nodes, out var yearMonthWithoutDay, out var monthDayWithoutYear))
                continue;

            // Annotations
            string? calendar = null;
            var calendarWasCritical = false;
            if (nodes.Annotations is not null)
            {
                foreach (var annotation in nodes.Annotations)
                {
                    if (annotation.Key == "u-ca")
                    {
                        if (calendar is null)
                        {
                            calendar = annotation.Value;
                            calendarWasCritical = annotation.Critical;
                        }
                        else if (annotation.Critical || calendarWasCritical)
                        {
                            result = null;
                            error = "Invalid ISO string '" + text + "': conflicting calendar annotations";
                            return false;
                        }
                    }
                    else if (annotation.Critical)
                    {
                        result = null;
                        error = "Invalid ISO string '" + text + "': unknown critical annotation '" + annotation.Key + "'";
                        return false;
                    }
                }
            }

            if ((yearMonthWithoutDay || monthDayWithoutYear) && calendar is not null && !string.Equals(calendar, "iso8601", StringComparison.OrdinalIgnoreCase))
            {
                result = null;
                error = "Invalid ISO string '" + text + "': a calendar other than iso8601 requires a full date";
                return false;
            }

            result = new IsoDateTimeParseResult
            {
                Year = monthDayWithoutYear ? null : nodes.Year,
                Month = nodes.Month == 0 ? 1 : nodes.Month,
                Day = nodes.HasDay ? nodes.Day : 1,
                HasTime = nodes.HasTime,
                Time = nodes.HasTime ? new TimeRecord(0, nodes.Hour, nodes.Minute, nodes.Second == 60 ? 59 : nodes.Second, nodes.FractionNanoseconds / 1_000_000, nodes.FractionNanoseconds / 1000 % 1000, nodes.FractionNanoseconds % 1000) : TimeRecord.Midnight,
                TimeZone = new IsoStringTimeZone(nodes.Z, nodes.Z ? null : nodes.OffsetString, nodes.TimeZoneAnnotation),
                Calendar = calendar,
                OffsetHasSubMinutePrecision = nodes.OffsetHasSeconds,
            };
            error = "";
            return true;
        }

        result = null;
        error = "Invalid ISO 8601 string: " + text;
        return false;
    }

    private static bool TryParseGoal(string text, TemporalParseGoal goal, ParseNodes nodes, out bool yearMonthWithoutDay, out bool monthDayWithoutYear)
    {
        yearMonthWithoutDay = false;
        monthDayWithoutYear = false;
        switch (goal)
        {
            case TemporalParseGoal.DateTime:
                return ParseAnnotatedDateTime(text, nodes, zoned: false, timeRequired: false);

            case TemporalParseGoal.ZonedDateTime:
                return ParseAnnotatedDateTime(text, nodes, zoned: true, timeRequired: false);

            case TemporalParseGoal.Instant:
                return ParseInstant(text, nodes);

            case TemporalParseGoal.Time:
                if (ParseAnnotatedTime(text, nodes))
                    return true;

                nodes.Reset();
                return ParseAnnotatedDateTime(text, nodes, zoned: false, timeRequired: true);

            case TemporalParseGoal.YearMonth:
                if (ParseAnnotatedYearMonth(text, nodes))
                {
                    yearMonthWithoutDay = true;
                    return true;
                }

                nodes.Reset();
                return ParseAnnotatedDateTime(text, nodes, zoned: false, timeRequired: false);

            case TemporalParseGoal.MonthDay:
                if (ParseAnnotatedMonthDay(text, nodes))
                {
                    monthDayWithoutYear = !nodes.HasYear;
                    return true;
                }

                nodes.Reset();
                return ParseAnnotatedDateTime(text, nodes, zoned: false, timeRequired: false);

            default:
                return false;
        }
    }

    // AnnotatedDateTime[Zoned, TimeRequired]
    private static bool ParseAnnotatedDateTime(string text, ParseNodes nodes, bool zoned, bool timeRequired)
    {
        var position = 0;
        if (!ParseDate(text, ref position, nodes))
            return false;

        if (position < text.Length && IsDateTimeSeparator(text[position]))
        {
            position++;
            if (!ParseTime(text, ref position, nodes))
                return false;

            ParseDateTimeUtcOffset(text, ref position, nodes, allowZ: zoned);
        }
        else if (timeRequired)
        {
            return false;
        }

        var hasTimeZone = ParseTimeZoneAnnotation(text, ref position, nodes);
        if (zoned && !hasTimeZone)
            return false;

        ParseAnnotations(text, ref position, nodes);
        return position == text.Length;
    }

    // TemporalInstantString ::: Date DateTimeSeparator Time DateTimeUTCOffset[+Z] TimeZoneAnnotation? Annotations?
    private static bool ParseInstant(string text, ParseNodes nodes)
    {
        var position = 0;
        if (!ParseDate(text, ref position, nodes))
            return false;

        if (position >= text.Length || !IsDateTimeSeparator(text[position]))
            return false;

        position++;
        if (!ParseTime(text, ref position, nodes))
            return false;

        if (!ParseDateTimeUtcOffset(text, ref position, nodes, allowZ: true))
            return false;

        ParseTimeZoneAnnotation(text, ref position, nodes);
        ParseAnnotations(text, ref position, nodes);
        return position == text.Length;
    }

    // AnnotatedTime ::: TimeDesignator Time DateTimeUTCOffset[~Z]? TimeZoneAnnotation? Annotations?
    //                 | Time DateTimeUTCOffset[~Z]? TimeZoneAnnotation? Annotations?
    private static bool ParseAnnotatedTime(string text, ParseNodes nodes)
    {
        var position = 0;
        var hasDesignator = position < text.Length && text[position] is 'T' or 't';
        if (hasDesignator)
        {
            position++;
        }

        if (!ParseTime(text, ref position, nodes))
            return false;

        ParseDateTimeUtcOffset(text, ref position, nodes, allowZ: false);
        var timeEnd = position;
        ParseTimeZoneAnnotation(text, ref position, nodes);
        ParseAnnotations(text, ref position, nodes);
        if (position != text.Length)
            return false;

        if (!hasDesignator)
        {
            // Early errors: the time must not be ambiguous with a month-day or a year-month
            var timeText = text[..timeEnd];
            var scratch = new ParseNodes();
            var p = 0;
            if (ParseDateSpecMonthDay(timeText, ref p, scratch) && p == timeText.Length)
                return false;

            scratch.Reset();
            p = 0;
            if (ParseDateSpecYearMonth(timeText, ref p, scratch) && p == timeText.Length)
                return false;
        }

        return true;
    }

    // AnnotatedYearMonth ::: DateSpecYearMonth TimeZoneAnnotation? Annotations?
    private static bool ParseAnnotatedYearMonth(string text, ParseNodes nodes)
    {
        var position = 0;
        if (!ParseDateSpecYearMonth(text, ref position, nodes))
            return false;

        ParseTimeZoneAnnotation(text, ref position, nodes);
        ParseAnnotations(text, ref position, nodes);
        return position == text.Length;
    }

    // AnnotatedMonthDay ::: DateSpecMonthDay TimeZoneAnnotation? Annotations?
    private static bool ParseAnnotatedMonthDay(string text, ParseNodes nodes)
    {
        var position = 0;
        if (!ParseDateSpecMonthDay(text, ref position, nodes))
            return false;

        ParseTimeZoneAnnotation(text, ref position, nodes);
        ParseAnnotations(text, ref position, nodes);
        return position == text.Length;
    }

    private static bool IsDateTimeSeparator(char c) => c is ' ' or 'T' or 't';

    private static bool IsDigit(char c) => c is >= '0' and <= '9';

    private static bool IsAlpha(char c) => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z');

    private static bool TryReadDigits(string text, int position, int count, out int value)
    {
        value = 0;
        if (position + count > text.Length)
            return false;

        for (var i = 0; i < count; i++)
        {
            var c = text[position + i];
            if (!IsDigit(c))
                return false;

            value = (value * 10) + (c - '0');
        }

        return true;
    }

    // DateYear ::: DecimalDigit{4} | ASCIISign DecimalDigit{6}
    private static bool ParseDateYear(string text, ref int position, ParseNodes nodes)
    {
        if (position < text.Length && text[position] is '+' or '-')
        {
            var negative = text[position] == '-';
            if (!TryReadDigits(text, position + 1, 6, out var value))
                return false;

            if (negative && value == 0)
                return false;

            nodes.Year = negative ? -value : value;
            nodes.HasYear = true;
            position += 7;
            return true;
        }

        if (!TryReadDigits(text, position, 4, out var year))
            return false;

        nodes.Year = year;
        nodes.HasYear = true;
        position += 4;
        return true;
    }

    private static bool ParseDateMonth(string text, ref int position, out int month)
    {
        if (!TryReadDigits(text, position, 2, out month) || month is < 1 or > 12)
            return false;

        position += 2;
        return true;
    }

    private static bool ParseDateDay(string text, ref int position, out int day)
    {
        if (!TryReadDigits(text, position, 2, out day) || day is < 1 or > 31)
            return false;

        position += 2;
        return true;
    }

    private static bool IsValidMonthDay(int month, int day)
    {
        if (day == 31 && month is 2 or 4 or 6 or 9 or 11)
            return false;

        return !(month == 2 && day == 30);
    }

    // Date ::: DateSpec[+Extended] | DateSpec[~Extended]
    private static bool ParseDate(string text, ref int position, ParseNodes nodes)
    {
        var p = position;
        if (!ParseDateYear(text, ref p, nodes))
            return false;

        var extended = p < text.Length && text[p] == '-';
        if (extended)
        {
            p++;
        }

        if (!ParseDateMonth(text, ref p, out var month))
            return false;

        if (extended)
        {
            if (p >= text.Length || text[p] != '-')
                return false;

            p++;
        }

        if (!ParseDateDay(text, ref p, out var day))
            return false;

        // IsValidDate
        if (!IsValidMonthDay(month, day))
            return false;

        if (month == 2 && day == 29 && !TemporalOperations.IsLeapYear(nodes.Year))
            return false;

        nodes.Month = month;
        nodes.Day = day;
        nodes.HasDay = true;
        position = p;
        return true;
    }

    // DateSpecYearMonth ::: DateYear -? DateMonth
    private static bool ParseDateSpecYearMonth(string text, ref int position, ParseNodes nodes)
    {
        var p = position;
        if (!ParseDateYear(text, ref p, nodes))
            return false;

        if (p < text.Length && text[p] == '-')
        {
            p++;
        }

        if (!ParseDateMonth(text, ref p, out var month))
            return false;

        nodes.Month = month;
        position = p;
        return true;
    }

    // DateSpecMonthDay ::: --? DateMonth -? DateDay
    private static bool ParseDateSpecMonthDay(string text, ref int position, ParseNodes nodes)
    {
        var p = position;
        if (p + 1 < text.Length && text[p] == '-' && text[p + 1] == '-')
        {
            p += 2;
        }

        if (!ParseDateMonth(text, ref p, out var month))
            return false;

        if (p < text.Length && text[p] == '-')
        {
            p++;
        }

        if (!ParseDateDay(text, ref p, out var day))
            return false;

        if (!IsValidMonthDay(month, day))
            return false;

        nodes.Month = month;
        nodes.Day = day;
        nodes.HasDay = true;
        position = p;
        return true;
    }

    private static bool ParseHour(string text, ref int position, out int hour)
    {
        if (!TryReadDigits(text, position, 2, out hour) || hour > 23)
            return false;

        position += 2;
        return true;
    }

    private static bool ParseMinuteSecond(string text, ref int position, out int value)
    {
        if (!TryReadDigits(text, position, 2, out value) || value > 59)
            return false;

        position += 2;
        return true;
    }

    // TemporalDecimalFraction ::: TemporalDecimalSeparator DecimalDigit{1,9}
    private static bool ParseFraction(string text, ref int position, out int nanoseconds, out int digitCount)
    {
        nanoseconds = 0;
        digitCount = 0;
        if (position >= text.Length || text[position] is not ('.' or ','))
            return false;

        var p = position + 1;
        while (p < text.Length && IsDigit(text[p]) && digitCount < 9)
        {
            nanoseconds = (nanoseconds * 10) + (text[p] - '0');
            digitCount++;
            p++;
        }

        if (digitCount == 0)
            return false;

        for (var i = digitCount; i < 9; i++)
        {
            nanoseconds *= 10;
        }

        position = p;
        return true;
    }

    // Time ::: TimeSpec[+Extended] | TimeSpec[~Extended]
    private static bool ParseTime(string text, ref int position, ParseNodes nodes)
    {
        var p = position;
        if (!ParseHour(text, ref p, out var hour))
            return false;

        int minute = 0, second = 0, fraction = 0;
        if (p < text.Length && text[p] == ':')
        {
            var q = p + 1;
            if (!ParseMinuteSecond(text, ref q, out minute))
                return false;

            p = q;
            if (p < text.Length && text[p] == ':')
            {
                q = p + 1;
                if (!ParseTimeSecond(text, ref q, out second))
                    return false;

                p = q;
                ParseFraction(text, ref p, out fraction, out _);
            }
        }
        else if (p + 1 < text.Length && IsDigit(text[p]) && IsDigit(text[p + 1]))
        {
            if (!ParseMinuteSecond(text, ref p, out minute))
                return false;

            if (p + 1 < text.Length && IsDigit(text[p]) && IsDigit(text[p + 1]))
            {
                if (!ParseTimeSecond(text, ref p, out second))
                    return false;

                ParseFraction(text, ref p, out fraction, out _);
            }
        }

        nodes.HasTime = true;
        nodes.Hour = hour;
        nodes.Minute = minute;
        nodes.Second = second;
        nodes.FractionNanoseconds = fraction;
        position = p;
        return true;
    }

    // TimeSecond ::: MinuteSecond | 60
    private static bool ParseTimeSecond(string text, ref int position, out int second)
    {
        if (TryReadDigits(text, position, 2, out second) && second == 60)
        {
            position += 2;
            return true;
        }

        return ParseMinuteSecond(text, ref position, out second);
    }

    // DateTimeUTCOffset[Z] ::: [+Z] UTCDesignator | UTCOffset[+SubMinutePrecision]
    private static bool ParseDateTimeUtcOffset(string text, ref int position, ParseNodes nodes, bool allowZ)
    {
        if (position >= text.Length)
            return false;

        if (allowZ && text[position] is 'Z' or 'z')
        {
            position++;
            nodes.Z = true;
            return true;
        }

        var start = position;
        if (!ParseUtcOffset(text, ref position, subMinutePrecision: true, out _, out var hasSeconds))
            return false;

        nodes.OffsetString = text[start..position];
        nodes.OffsetHasSeconds = hasSeconds;
        return true;
    }

    /// <summary>UTCOffset[SubMinutePrecision]: the offset in nanoseconds.</summary>
    private static bool ParseUtcOffset(string text, ref int position, bool subMinutePrecision, out long offsetNanoseconds, out bool hasSeconds)
    {
        offsetNanoseconds = 0;
        hasSeconds = false;
        var p = position;
        if (p >= text.Length || text[p] is not ('+' or '-'))
            return false;

        var negative = text[p] == '-';
        p++;
        if (!ParseHour(text, ref p, out var hour))
            return false;

        int minute = 0, second = 0, fraction = 0;
        if (p < text.Length && text[p] == ':')
        {
            var q = p + 1;
            if (ParseMinuteSecond(text, ref q, out minute))
            {
                p = q;
                if (subMinutePrecision && p < text.Length && text[p] == ':')
                {
                    q = p + 1;
                    if (ParseMinuteSecond(text, ref q, out second))
                    {
                        p = q;
                        hasSeconds = true;
                        ParseFraction(text, ref p, out fraction, out _);
                    }
                }
            }
        }
        else if (p + 1 < text.Length && IsDigit(text[p]) && IsDigit(text[p + 1]))
        {
            if (ParseMinuteSecond(text, ref p, out minute))
            {
                if (subMinutePrecision && p + 1 < text.Length && IsDigit(text[p]) && IsDigit(text[p + 1]))
                {
                    if (ParseMinuteSecond(text, ref p, out second))
                    {
                        hasSeconds = true;
                        ParseFraction(text, ref p, out fraction, out _);
                    }
                }
            }
        }

        var total = ((((hour * 60L) + minute) * 60) + second) * 1_000_000_000L + fraction;
        offsetNanoseconds = negative ? -total : total;
        position = p;
        return true;
    }

    // TimeZoneAnnotation ::: [ AnnotationCriticalFlag? TimeZoneIdentifier ]
    private static bool ParseTimeZoneAnnotation(string text, ref int position, ParseNodes nodes)
    {
        var p = position;
        if (p >= text.Length || text[p] != '[')
            return false;

        p++;
        if (p < text.Length && text[p] == '!')
        {
            p++;
        }

        var start = p;
        if (!ParseTimeZoneIdentifier(text, ref p))
            return false;

        if (p >= text.Length || text[p] != ']')
            return false;

        nodes.TimeZoneAnnotation = text[start..p];
        position = p + 1;
        return true;
    }

    // TimeZoneIdentifier ::: UTCOffset[~SubMinutePrecision] | TimeZoneIANAName
    private static bool ParseTimeZoneIdentifier(string text, ref int position)
    {
        if (position < text.Length && text[position] is '+' or '-')
            return ParseUtcOffset(text, ref position, subMinutePrecision: false, out _, out _);

        return ParseTimeZoneIanaName(text, ref position);
    }

    // TimeZoneIANAName ::: TimeZoneIANANameComponent (/ TimeZoneIANANameComponent)*
    private static bool ParseTimeZoneIanaName(string text, ref int position)
    {
        var p = position;
        while (true)
        {
            // TZLeadingChar ::: Alpha . _
            if (p >= text.Length || !(IsAlpha(text[p]) || text[p] is '.' or '_'))
                return false;

            p++;

            // TZChar ::: TZLeadingChar DecimalDigit - +
            while (p < text.Length && (IsAlpha(text[p]) || IsDigit(text[p]) || text[p] is '.' or '_' or '-' or '+'))
            {
                p++;
            }

            if (p < text.Length && text[p] == '/')
            {
                p++;
                continue;
            }

            position = p;
            return true;
        }
    }

    // Annotations ::: Annotation+
    private static void ParseAnnotations(string text, ref int position, ParseNodes nodes)
    {
        while (ParseAnnotation(text, ref position, nodes))
        {
        }
    }

    // Annotation ::: [ AnnotationCriticalFlag? AnnotationKey = AnnotationValue ]
    private static bool ParseAnnotation(string text, ref int position, ParseNodes nodes)
    {
        var p = position;
        if (p >= text.Length || text[p] != '[')
            return false;

        p++;
        var critical = false;
        if (p < text.Length && text[p] == '!')
        {
            critical = true;
            p++;
        }

        // AnnotationKey: AKeyLeadingChar AKeyChar*
        var keyStart = p;
        if (p >= text.Length || !(text[p] is (>= 'a' and <= 'z') or '_'))
            return false;

        p++;
        while (p < text.Length && text[p] is (>= 'a' and <= 'z') or '_' or '-' or (>= '0' and <= '9'))
        {
            p++;
        }

        var key = text[keyStart..p];
        if (p >= text.Length || text[p] != '=')
            return false;

        p++;
        var valueStart = p;
        if (!ParseAnnotationValue(text, ref p))
            return false;

        var value = text[valueStart..p];
        if (p >= text.Length || text[p] != ']')
            return false;

        nodes.Annotations ??= [];
        nodes.Annotations.Add(new Annotation { Key = key, Value = value, Critical = critical });
        position = p + 1;
        return true;
    }

    // AnnotationValue ::: AnnotationValueComponent (- AnnotationValueComponent)*
    private static bool ParseAnnotationValue(string text, ref int position)
    {
        var p = position;
        while (true)
        {
            var start = p;
            while (p < text.Length && (IsAlpha(text[p]) || IsDigit(text[p])))
            {
                p++;
            }

            if (p == start)
                return false;

            if (p + 1 < text.Length && text[p] == '-' && (IsAlpha(text[p + 1]) || IsDigit(text[p + 1])))
            {
                p++;
                continue;
            }

            position = p;
            return true;
        }
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Other goal symbols
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>ParseText(string, AnnotationValue) is a Parse Node.</summary>
    public static bool IsAnnotationValue(string text)
    {
        var position = 0;
        return ParseAnnotationValue(text, ref position) && position == text.Length;
    }

    /// <summary>ParseText(string, TimeZoneIdentifier) is a Parse Node.</summary>
    public static bool IsTimeZoneIdentifier(string text)
    {
        var position = 0;
        return ParseTimeZoneIdentifier(text, ref position) && position == text.Length;
    }

    /// <summary>ParseTimeZoneIdentifier(identifier) without the throw: false when the identifier is not syntactically valid.</summary>
    public static bool TryParseTimeZoneIdentifier(string text, out TimeZoneIdentifierParseResult result)
    {
        var position = 0;
        if (text.Length > 0 && text[0] is '+' or '-')
        {
            if (ParseUtcOffset(text, ref position, subMinutePrecision: false, out var offset, out _) && position == text.Length)
            {
                result = new TimeZoneIdentifierParseResult(null, (int)(offset / 60_000_000_000));
                return true;
            }
        }
        else if (ParseTimeZoneIanaName(text, ref position) && position == text.Length)
        {
            result = new TimeZoneIdentifierParseResult(text, 0);
            return true;
        }

        result = default;
        return false;
    }

    /// <summary>ParseText(string, UTCOffset[~SubMinutePrecision]) is a Parse Node (IsOffsetTimeZoneIdentifier).</summary>
    public static bool IsOffsetTimeZoneIdentifier(string text)
    {
        var position = 0;
        return ParseUtcOffset(text, ref position, subMinutePrecision: false, out _, out _) && position == text.Length;
    }

    /// <summary>ParseDateTimeUTCOffset(offsetString) without the throw.</summary>
    public static bool TryParseDateTimeUtcOffset(string text, out long offsetNanoseconds, out bool hasSeconds)
    {
        var position = 0;
        return ParseUtcOffset(text, ref position, subMinutePrecision: true, out offsetNanoseconds, out hasSeconds) && position == text.Length;
    }

    /// <summary>ParseText(monthCode, MonthCode) ::: M00L | M0 NonZeroDigit L? | M NonZeroDigit DecimalDigit L?</summary>
    public static bool TryParseMonthCode(string text, out int monthNumber, out bool isLeapMonth)
    {
        monthNumber = 0;
        isLeapMonth = false;
        if (text.Length is not (3 or 4) || text[0] != 'M' || !IsDigit(text[1]) || !IsDigit(text[2]))
            return false;

        if (text.Length == 4)
        {
            if (text[3] != 'L')
                return false;

            isLeapMonth = true;
        }

        monthNumber = ((text[1] - '0') * 10) + (text[2] - '0');
        if (monthNumber == 0 && !isLeapMonth)
            return false;

        return true;
    }

    /// <summary>The components of a duration string (strings of digits, empty when absent).</summary>
    [StructLayout(LayoutKind.Auto)]
    public struct DurationParts
    {
        public bool Negative;
        public string Years;
        public string Months;
        public string Weeks;
        public string Days;
        public string Hours;
        public string FractionalHours;
        public string Minutes;
        public string FractionalMinutes;
        public string Seconds;
        public string FractionalSeconds;
    }

    /// <summary>ParseText(isoString, TemporalDurationString)</summary>
    public static bool TryParseDuration(string text, out DurationParts parts)
    {
        parts = new DurationParts
        {
            Years = "",
            Months = "",
            Weeks = "",
            Days = "",
            Hours = "",
            FractionalHours = "",
            Minutes = "",
            FractionalMinutes = "",
            Seconds = "",
            FractionalSeconds = "",
        };

        var p = 0;
        if (p < text.Length && text[p] is '+' or '-')
        {
            parts.Negative = text[p] == '-';
            p++;
        }

        if (p >= text.Length || text[p] is not ('P' or 'p'))
            return false;

        p++;

        // DurationDate: Y, M, W, D in order, each optional
        var hasDate = false;
        var stage = 0;
        while (p < text.Length && IsDigit(text[p]))
        {
            var start = p;
            while (p < text.Length && IsDigit(text[p]))
            {
                p++;
            }

            if (p >= text.Length)
                return false;

            var digits = text[start..p];
            var designator = char.ToUpperInvariant(text[p]);
            var designatorStage = designator switch
            {
                'Y' => 1,
                'M' => 2,
                'W' => 3,
                'D' => 4,
                _ => 0,
            };

            if (designatorStage == 0 || designatorStage <= stage)
                return false;

            stage = designatorStage;
            switch (designatorStage)
            {
                case 1:
                    parts.Years = digits;
                    break;
                case 2:
                    parts.Months = digits;
                    break;
                case 3:
                    parts.Weeks = digits;
                    break;
                default:
                    parts.Days = digits;
                    break;
            }

            hasDate = true;
            p++;
        }

        var hasTime = false;
        if (p < text.Length && text[p] is 'T' or 't')
        {
            p++;
            stage = 0;
            var hadFraction = false;
            while (p < text.Length && IsDigit(text[p]))
            {
                if (hadFraction)
                    return false;

                var start = p;
                while (p < text.Length && IsDigit(text[p]))
                {
                    p++;
                }

                var digits = text[start..p];
                var fraction = "";
                if (p < text.Length && text[p] is '.' or ',')
                {
                    var fractionStart = p;
                    if (!ParseFraction(text, ref p, out _, out _))
                        return false;

                    fraction = text[fractionStart..p];
                    hadFraction = true;
                }

                if (p >= text.Length)
                    return false;

                var designatorStage = char.ToUpperInvariant(text[p]) switch
                {
                    'H' => 1,
                    'M' => 2,
                    'S' => 3,
                    _ => 0,
                };

                if (designatorStage == 0 || designatorStage <= stage)
                    return false;

                stage = designatorStage;
                switch (designatorStage)
                {
                    case 1:
                        parts.Hours = digits;
                        parts.FractionalHours = fraction;
                        break;
                    case 2:
                        parts.Minutes = digits;
                        parts.FractionalMinutes = fraction;
                        break;
                    default:
                        parts.Seconds = digits;
                        parts.FractionalSeconds = fraction;
                        break;
                }

                hasTime = true;
                p++;
            }

            if (!hasTime)
                return false;
        }

        return p == text.Length && (hasDate || hasTime);
    }
}
