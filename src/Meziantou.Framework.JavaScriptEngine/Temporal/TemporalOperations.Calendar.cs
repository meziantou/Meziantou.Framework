using Meziantou.Framework.JavaScriptEngine.TimeZones;

namespace Meziantou.Framework.JavaScriptEngine;

internal static partial class TemporalOperations
{
    public const string IsoCalendar = "iso8601";

    private static readonly string[] MonthCodes = ["M01", "M02", "M03", "M04", "M05", "M06", "M07", "M08", "M09", "M10", "M11", "M12"];

    // ------------------------------------------------------------------------------------------------------------------
    // ISO 8601 calendar arithmetic
    // ------------------------------------------------------------------------------------------------------------------

    public static bool IsLeapYear(long year) => CalendarMath.IsLeapYear(year);

    /// <summary>MathematicalInLeapYear for any integral Number (the remainders of a float64 are exact).</summary>
    public static bool IsLeapYear(double year) => year % 4 == 0 && (year % 100 != 0 || year % 400 == 0);

    /// <summary>ISODaysInMonth(year, month)</summary>
    public static int IsoDaysInMonth(long year, int month) => CalendarMath.DaysInMonth(year, month);

    private static int IsoDaysInMonth(double year, int month) => month == 2 ? (IsLeapYear(year) ? 29 : 28) : CalendarMath.DaysInMonth(2001, month);

    /// <summary>MathematicalDaysInYear(year)</summary>
    public static int IsoDaysInYear(long year) => IsLeapYear(year) ? 366 : 365;

    /// <summary>IsValidISODate(year, month, day)</summary>
    public static bool IsValidIsoDate(double year, double month, double day)
    {
        if (month is < 1 or > 12)
            return false;

        var daysInMonth = IsoDaysInMonth(year, (int)month);
        return day >= 1 && day <= daysInMonth;
    }

    /// <summary>ISODateToEpochDays(year, month - 1, day) for a valid date.</summary>
    public static long IsoDateToEpochDays(IsoDate date) => CalendarMath.DaysFromCivil(date.Year, date.Month, date.Day);

    public static IsoDate EpochDaysToIsoDate(long epochDays)
    {
        CalendarMath.CivilFromDays(epochDays, out var year, out var month, out var day);
        return new IsoDate(year, month, day);
    }

    /// <summary>AddDaysToISODate(isoDate, days)</summary>
    public static IsoDate AddDaysToIsoDate(IsoDate date, long days) => days == 0 ? date : EpochDaysToIsoDate(IsoDateToEpochDays(date) + days);

    /// <summary>CompareISODate(isoDate1, isoDate2)</summary>
    public static int CompareIsoDate(IsoDate one, IsoDate two)
    {
        if (one.Year != two.Year)
            return one.Year > two.Year ? 1 : -1;

        if (one.Month != two.Month)
            return one.Month > two.Month ? 1 : -1;

        if (one.Day != two.Day)
            return one.Day > two.Day ? 1 : -1;

        return 0;
    }

    /// <summary>ISODateWithinLimits(isoDate)</summary>
    public static bool IsoDateWithinLimits(IsoDate date) => IsoDateTimeWithinLimits(new IsoDateTime(date, TimeRecord.Noon));

    /// <summary>ISOYearMonthWithinLimits(isoDate)</summary>
    public static bool IsoYearMonthWithinLimits(IsoDate date)
    {
        if (date.Year is < -271_821 or > 275_760)
            return false;

        if (date.Year == -271_821 && date.Month < 4)
            return false;

        if (date.Year == 275_760 && date.Month > 9)
            return false;

        return true;
    }

    /// <summary>BalanceISOYearMonth(year, month)</summary>
    public static (long Year, int Month) BalanceIsoYearMonth(long year, long month)
    {
        year += TemporalMath.FloorDivide(month - 1, 12);
        var balancedMonth = (int)TemporalMath.FloorModulo(month - 1, 12) + 1;
        return (year, balancedMonth);
    }

    /// <summary>RegulateISODate(year, month, day, overflow)</summary>
    public static IsoDate RegulateIsoDate(long year, long month, long day, TemporalOverflow overflow)
    {
        if (overflow == TemporalOverflow.Constrain)
        {
            month = Math.Clamp(month, 1, 12);
            day = Math.Clamp(day, 1, IsoDaysInMonth(year, (int)month));
        }
        else if (!IsValidIsoDate(year, month, day))
        {
            ThrowHelper.ThrowRangeError("The date " + year.ToString(System.Globalization.CultureInfo.InvariantCulture) + "-" + month.ToString(System.Globalization.CultureInfo.InvariantCulture) + "-" + day.ToString(System.Globalization.CultureInfo.InvariantCulture) + " is not valid");
        }

        return new IsoDate(year, (int)month, (int)day);
    }

    /// <summary>RegulateISODate for the values of a Calendar Fields Record (any integral Number): the year is returned as is.</summary>
    private static (double Year, int Month, int Day) RegulateIsoDateFields(double year, double month, double day, TemporalOverflow overflow)
    {
        if (overflow == TemporalOverflow.Constrain)
        {
            month = Math.Clamp(month, 1, 12);
            day = Math.Clamp(day, 1, IsoDaysInMonth(year, (int)month));
        }
        else if (!IsValidIsoDate(year, month, day))
        {
            ThrowHelper.ThrowRangeError("The date " + NumberFormatting.ToString(year) + "-" + NumberFormatting.ToString(month) + "-" + NumberFormatting.ToString(day) + " is not valid");
        }

        return (year, (int)month, (int)day);
    }

    /// <summary>Creates an ISO Date Record from a regulated date whose year may be too large for the ISO Date Records of Temporal: a RangeError.</summary>
    private static IsoDate CreateIsoDateFromRegulated(double year, int month, int day)
    {
        // Such a date is outside the limits of every Temporal type: the caller would throw a RangeError anyway
        if (Math.Abs(year) > 1_000_000_000)
            return ThrowHelper.ThrowRangeError<IsoDate>("The date is outside the range of Temporal");

        return new IsoDate((long)year, month, day);
    }

    /// <summary>ISODayOfWeek(isoDate): 1 (Monday) to 7 (Sunday).</summary>
    public static int IsoDayOfWeek(IsoDate date)
    {
        var dayOfWeek = CalendarMath.DayOfWeek(IsoDateToEpochDays(date));
        return dayOfWeek == 0 ? 7 : dayOfWeek;
    }

    /// <summary>ISODayOfYear(isoDate)</summary>
    public static int IsoDayOfYear(IsoDate date) => (int)(IsoDateToEpochDays(date) - CalendarMath.DaysFromCivil(date.Year, 1, 1)) + 1;

    /// <summary>ISOWeekOfYear(isoDate)</summary>
    public static (int Week, long Year) IsoWeekOfYear(IsoDate date)
    {
        const int Wednesday = 3;
        const int Thursday = 4;
        const int Friday = 5;
        const int Saturday = 6;
        const int DaysInWeek = 7;
        const int MaxWeekNumber = 53;
        var year = date.Year;
        var dayOfYear = IsoDayOfYear(date);
        var dayOfWeek = IsoDayOfWeek(date);
        var week = (int)Math.Floor((dayOfYear + DaysInWeek - dayOfWeek + Wednesday) / (double)DaysInWeek);
        if (week < 1)
        {
            var dayOfJan1st = IsoDayOfWeek(new IsoDate(year, 1, 1));
            if (dayOfJan1st == Friday)
                return (MaxWeekNumber, year - 1);

            if (dayOfJan1st == Saturday && IsLeapYear(year - 1))
                return (MaxWeekNumber, year - 1);

            return (MaxWeekNumber - 1, year - 1);
        }

        if (week == MaxWeekNumber)
        {
            var daysInYear = IsoDaysInYear(year);
            var daysLaterInYear = daysInYear - dayOfYear;
            var daysAfterThursday = Thursday - dayOfWeek;
            if (daysLaterInYear < daysAfterThursday)
                return (1, year + 1);
        }

        return (week, year);
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Calendar types
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>CanonicalizeCalendar(id): only the ISO 8601 calendar is available.</summary>
    public static string CanonicalizeCalendar(string id)
    {
        if (!string.Equals(id, IsoCalendar, StringComparison.OrdinalIgnoreCase))
            return ThrowHelper.ThrowRangeError<string>("The calendar '" + id + "' is not supported");

        return IsoCalendar;
    }

    /// <summary>ToTemporalCalendarIdentifier(temporalCalendarLike)</summary>
    public static string ToTemporalCalendarIdentifier(JsValue temporalCalendarLike)
    {
        switch (temporalCalendarLike.Cell)
        {
            case JsPlainDate date:
                return date.Calendar;
            case JsPlainDateTime dateTime:
                return dateTime.Calendar;
            case JsPlainMonthDay monthDay:
                return monthDay.Calendar;
            case JsPlainYearMonth yearMonth:
                return yearMonth.Calendar;
            case JsZonedDateTime zonedDateTime:
                return zonedDateTime.Calendar;
            case JsString str:
                return CanonicalizeCalendar(ParseTemporalCalendarString(str.ToString()));
            default:
                return ThrowHelper.ThrowTypeError<string>("The calendar must be a string or a Temporal object");
        }
    }

    /// <summary>ParseTemporalCalendarString(string)</summary>
    public static string ParseTemporalCalendarString(string text)
    {
        if (TemporalParser.TryParseIsoDateTime(text, TemporalParser.AnyGoal, out var result, out _))
            return result.Calendar ?? IsoCalendar;

        if (!TemporalParser.IsAnnotationValue(text))
            return ThrowHelper.ThrowRangeError<string>("The calendar '" + text + "' is not valid");

        return text;
    }

    /// <summary>GetTemporalCalendarIdentifierWithISODefault(item)</summary>
    public static string GetTemporalCalendarIdentifierWithIsoDefault(JsObject item)
    {
        switch (item)
        {
            case JsPlainDate date:
                return date.Calendar;
            case JsPlainDateTime dateTime:
                return dateTime.Calendar;
            case JsPlainMonthDay monthDay:
                return monthDay.Calendar;
            case JsPlainYearMonth yearMonth:
                return yearMonth.Calendar;
            case JsZonedDateTime zonedDateTime:
                return zonedDateTime.Calendar;
        }

        var calendarLike = item.Get("calendar");
        if (calendarLike.IsUndefined)
            return IsoCalendar;

        return ToTemporalCalendarIdentifier(calendarLike);
    }

    /// <summary>CalendarEquals(one, two)</summary>
    public static bool CalendarEquals(string one, string two) => string.Equals(one, two, StringComparison.Ordinal);

    /// <summary>FormatCalendarAnnotation(id, showCalendar)</summary>
    public static string FormatCalendarAnnotation(string id, ShowCalendarOption showCalendar)
    {
        if (showCalendar == ShowCalendarOption.Never)
            return "";

        if (showCalendar == ShowCalendarOption.Auto && id == IsoCalendar)
            return "";

        var flag = showCalendar == ShowCalendarOption.Critical ? "!" : "";
        return "[" + flag + "u-ca=" + id + "]";
    }

    /// <summary>CreateMonthCode(monthNumber, false)</summary>
    public static string CreateMonthCode(int month) => MonthCodes[month - 1];

    /// <summary>ParseMonthCode(argument)</summary>
    public static (int MonthNumber, bool IsLeapMonth) ParseMonthCode(JsValue argument)
    {
        var monthCode = JsOperations.ToPrimitive(argument, JsOperations.PreferredType.String);
        if (monthCode.Cell is not JsString str)
            return ThrowHelper.ThrowTypeError<(int, bool)>("The monthCode must be a string");

        var text = str.ToString();
        if (!TemporalParser.TryParseMonthCode(text, out var monthNumber, out var isLeapMonth))
            return ThrowHelper.ThrowRangeError<(int, bool)>("The month code '" + text + "' is not valid");

        return (monthNumber, isLeapMonth);
    }

    /// <summary>CalendarISOToDate(calendar, isoDate)</summary>
    public static CalendarDate CalendarIsoToDate(string calendar, IsoDate date)
    {
        _ = calendar;
        var (week, yearOfWeek) = IsoWeekOfYear(date);
        return new CalendarDate(
            date.Year,
            date.Month,
            CreateMonthCode(date.Month),
            date.Day,
            IsoDayOfWeek(date),
            IsoDayOfYear(date),
            week,
            yearOfWeek,
            DaysInWeek: 7,
            IsoDaysInMonth(date.Year, date.Month),
            IsoDaysInYear(date.Year),
            MonthsInYear: 12,
            IsLeapYear(date.Year));
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Calendar Fields Records
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>ISODateToFields(calendar, isoDate, type)</summary>
    public static CalendarFields IsoDateToFields(string calendar, IsoDate date, CalendarFieldsType type)
    {
        _ = calendar;
        var fields = new CalendarFields { MonthCode = CreateMonthCode(date.Month) };
        if (type is CalendarFieldsType.MonthDay or CalendarFieldsType.Date)
        {
            fields.Day = date.Day;
        }

        if (type is CalendarFieldsType.YearMonth or CalendarFieldsType.Date)
        {
            fields.Year = date.Year;
        }

        return fields;
    }

    /// <summary>
    /// PrepareCalendarFields(calendar, fields, calendarFieldNames, nonCalendarFieldNames, requiredFieldNames): when
    /// <paramref name="partial"/> is true, requiredFieldNames is partial.
    /// </summary>
    public static CalendarFields PrepareCalendarFields(string calendar, JsObject fields, CalendarFieldNames fieldNames, CalendarFieldNames requiredFieldNames, bool partial)
    {
        // CalendarExtraFields is empty for the ISO 8601 calendar
        _ = calendar;
        var result = new CalendarFields();
        var any = false;

        // The property names in lexicographic code unit order
        if ((fieldNames & CalendarFieldNames.Day) != 0)
        {
            var value = fields.Get("day");
            if (!value.IsUndefined)
            {
                any = true;
                result.Day = ToPositiveIntegerWithTruncation(value);
            }
            else
            {
                CheckRequired(CalendarFieldNames.Day, "day");
            }
        }

        if ((fieldNames & CalendarFieldNames.Era) != 0)
        {
            var value = fields.Get("era");
            if (!value.IsUndefined)
            {
                any = true;
                result.Era = JsOperations.ToString(value);
            }
            else
            {
                CheckRequired(CalendarFieldNames.Era, "era");
            }
        }

        if ((fieldNames & CalendarFieldNames.EraYear) != 0)
        {
            var value = fields.Get("eraYear");
            if (!value.IsUndefined)
            {
                any = true;
                result.EraYear = ToIntegerWithTruncation(value);
            }
            else
            {
                CheckRequired(CalendarFieldNames.EraYear, "eraYear");
            }
        }

        if ((fieldNames & CalendarFieldNames.Hour) != 0)
        {
            result.Hour = ReadTimeField("hour", CalendarFieldNames.Hour);
        }

        if ((fieldNames & CalendarFieldNames.Microsecond) != 0)
        {
            result.Microsecond = ReadTimeField("microsecond", CalendarFieldNames.Microsecond);
        }

        if ((fieldNames & CalendarFieldNames.Millisecond) != 0)
        {
            result.Millisecond = ReadTimeField("millisecond", CalendarFieldNames.Millisecond);
        }

        if ((fieldNames & CalendarFieldNames.Minute) != 0)
        {
            result.Minute = ReadTimeField("minute", CalendarFieldNames.Minute);
        }

        if ((fieldNames & CalendarFieldNames.Month) != 0)
        {
            var value = fields.Get("month");
            if (!value.IsUndefined)
            {
                any = true;
                result.Month = ToPositiveIntegerWithTruncation(value);
            }
            else
            {
                CheckRequired(CalendarFieldNames.Month, "month");
            }
        }

        if ((fieldNames & CalendarFieldNames.MonthCode) != 0)
        {
            var value = fields.Get("monthCode");
            if (!value.IsUndefined)
            {
                any = true;
                var (monthNumber, isLeapMonth) = ParseMonthCode(value);
                result.MonthCode = CreateMonthCode(monthNumber, isLeapMonth);
            }
            else
            {
                CheckRequired(CalendarFieldNames.MonthCode, "monthCode");
            }
        }

        if ((fieldNames & CalendarFieldNames.Nanosecond) != 0)
        {
            result.Nanosecond = ReadTimeField("nanosecond", CalendarFieldNames.Nanosecond);
        }

        if ((fieldNames & CalendarFieldNames.Offset) != 0)
        {
            var value = fields.Get("offset");
            if (!value.IsUndefined)
            {
                any = true;
                result.OffsetString = ToOffsetString(value);
            }
            else
            {
                CheckRequired(CalendarFieldNames.Offset, "offset");
            }
        }

        if ((fieldNames & CalendarFieldNames.Second) != 0)
        {
            result.Second = ReadTimeField("second", CalendarFieldNames.Second);
        }

        if ((fieldNames & CalendarFieldNames.TimeZone) != 0)
        {
            var value = fields.Get("timeZone");
            if (!value.IsUndefined)
            {
                any = true;
                result.TimeZone = ToTemporalTimeZoneIdentifier(value);
            }
            else
            {
                CheckRequired(CalendarFieldNames.TimeZone, "timeZone");
            }
        }

        if ((fieldNames & CalendarFieldNames.Year) != 0)
        {
            var value = fields.Get("year");
            if (!value.IsUndefined)
            {
                any = true;
                result.Year = ToIntegerWithTruncation(value);
            }
            else
            {
                CheckRequired(CalendarFieldNames.Year, "year");
            }
        }

        if (partial && !any)
            return ThrowHelper.ThrowTypeError<CalendarFields>("The object must have at least one recognized property");

        return result;

        double? ReadTimeField(string property, CalendarFieldNames key)
        {
            var value = fields.Get(property);
            if (!value.IsUndefined)
            {
                any = true;
                return ToIntegerWithTruncation(value);
            }

            if (partial)
                return null;

            CheckRequired(key, property);
            return 0;
        }

        void CheckRequired(CalendarFieldNames key, string property)
        {
            if (!partial && (requiredFieldNames & key) != 0)
                ThrowHelper.ThrowTypeError("The property '" + property + "' is required");
        }
    }

    private static string CreateMonthCode(int monthNumber, bool isLeapMonth)
    {
        var numberPart = monthNumber.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
        return isLeapMonth ? "M" + numberPart + "L" : "M" + numberPart;
    }

    /// <summary>CalendarMergeFields(calendar, fields, additionalFields)</summary>
    public static CalendarFields CalendarMergeFields(string calendar, CalendarFields fields, CalendarFields additionalFields)
    {
        _ = calendar;
        var merged = fields.Clone();

        // CalendarFieldKeysToIgnore: a key present in additionalFields replaces the field (month and monthCode exclude each other)
        var monthOverridden = additionalFields.Month is not null || additionalFields.MonthCode is not null;
        if (monthOverridden)
        {
            merged.Month = additionalFields.Month;
            merged.MonthCode = additionalFields.MonthCode;
        }

        if (additionalFields.Era is not null)
        {
            merged.Era = additionalFields.Era;
        }

        if (additionalFields.EraYear is not null)
        {
            merged.EraYear = additionalFields.EraYear;
        }

        if (additionalFields.Year is not null)
        {
            merged.Year = additionalFields.Year;
        }

        if (additionalFields.Day is not null)
        {
            merged.Day = additionalFields.Day;
        }

        if (additionalFields.Hour is not null)
        {
            merged.Hour = additionalFields.Hour;
        }

        if (additionalFields.Minute is not null)
        {
            merged.Minute = additionalFields.Minute;
        }

        if (additionalFields.Second is not null)
        {
            merged.Second = additionalFields.Second;
        }

        if (additionalFields.Millisecond is not null)
        {
            merged.Millisecond = additionalFields.Millisecond;
        }

        if (additionalFields.Microsecond is not null)
        {
            merged.Microsecond = additionalFields.Microsecond;
        }

        if (additionalFields.Nanosecond is not null)
        {
            merged.Nanosecond = additionalFields.Nanosecond;
        }

        if (additionalFields.OffsetString is not null)
        {
            merged.OffsetString = additionalFields.OffsetString;
        }

        if (additionalFields.TimeZone is not null)
        {
            merged.TimeZone = additionalFields.TimeZone;
        }

        return merged;
    }

    /// <summary>CalendarResolveFields(calendar, fields, type)</summary>
    public static void CalendarResolveFields(string calendar, CalendarFields fields, CalendarFieldsType type)
    {
        _ = calendar;
        var needsYear = type is CalendarFieldsType.Date or CalendarFieldsType.YearMonth;
        var needsDay = type is CalendarFieldsType.Date or CalendarFieldsType.MonthDay;
        if (needsYear && fields.Year is null)
            ThrowHelper.ThrowTypeError("The property 'year' is required");

        if (needsDay && fields.Day is null)
            ThrowHelper.ThrowTypeError("The property 'day' is required");

        if (fields.Month is null && fields.MonthCode is null)
            ThrowHelper.ThrowTypeError("The property 'month' or 'monthCode' is required");

        if (fields.MonthCode is { } monthCode)
        {
            if (!TemporalParser.TryParseMonthCode(monthCode, out var month, out var isLeapMonth) || isLeapMonth)
                ThrowHelper.ThrowRangeError("The month code '" + monthCode + "' is not valid in the ISO 8601 calendar");

            if (month > 12)
                ThrowHelper.ThrowRangeError("The month code '" + monthCode + "' is not valid in the ISO 8601 calendar");

            if (fields.Month is { } fieldMonth && fieldMonth != month)
                ThrowHelper.ThrowRangeError("The month " + NumberFormatting.ToString(fieldMonth) + " does not match the month code '" + monthCode + "'");

            fields.Month = month;
        }
    }

    /// <summary>CalendarDateFromFields(calendar, fields, overflow)</summary>
    public static IsoDate CalendarDateFromFields(string calendar, CalendarFields fields, TemporalOverflow overflow)
    {
        CalendarResolveFields(calendar, fields, CalendarFieldsType.Date);
        var (year, month, day) = RegulateIsoDateFields(fields.Year!.Value, fields.Month!.Value, fields.Day!.Value, overflow);
        var result = CreateIsoDateFromRegulated(year, month, day);
        if (!IsoDateWithinLimits(result))
            return ThrowHelper.ThrowRangeError<IsoDate>("The date is outside the range of Temporal.PlainDate");

        return result;
    }

    /// <summary>CalendarYearMonthFromFields(calendar, fields, overflow)</summary>
    public static IsoDate CalendarYearMonthFromFields(string calendar, CalendarFields fields, TemporalOverflow overflow)
    {
        fields.Day = 1;
        CalendarResolveFields(calendar, fields, CalendarFieldsType.YearMonth);
        var (year, month, day) = RegulateIsoDateFields(fields.Year!.Value, fields.Month!.Value, fields.Day!.Value, overflow);
        var result = CreateIsoDateFromRegulated(year, month, day);
        if (!IsoYearMonthWithinLimits(result))
            return ThrowHelper.ThrowRangeError<IsoDate>("The date is outside the range of Temporal.PlainYearMonth");

        return result;
    }

    /// <summary>CalendarMonthDayFromFields(calendar, fields, overflow)</summary>
    public static IsoDate CalendarMonthDayFromFields(string calendar, CalendarFields fields, TemporalOverflow overflow)
    {
        CalendarResolveFields(calendar, fields, CalendarFieldsType.MonthDay);

        // CalendarMonthDayToISOReferenceDate
        const int ReferenceIsoYear = 1972;
        var year = fields.Year ?? ReferenceIsoYear;
        var (_, month, day) = RegulateIsoDateFields(year, fields.Month!.Value, fields.Day!.Value, overflow);
        return new IsoDate(ReferenceIsoYear, month, day);
    }

    /// <summary>CalendarDateAdd(calendar, isoDate, duration, overflow)</summary>
    public static IsoDate CalendarDateAdd(string calendar, IsoDate date, DateDuration duration, TemporalOverflow overflow)
    {
        _ = calendar;
        var (year, month) = BalanceIsoYearMonth(date.Year + duration.Years, date.Month + duration.Months);
        var intermediate = RegulateIsoDate(year, month, date.Day, overflow);
        var days = duration.Days + (7 * duration.Weeks);
        var result = AddDaysToIsoDate(intermediate, days);
        if (!IsoDateWithinLimits(result))
            return ThrowHelper.ThrowRangeError<IsoDate>("The date is outside the range of Temporal");

        return result;
    }

    /// <summary>CompareSurpasses(sign, year, monthOrCode, day, target) with a month number (the month codes of the ISO calendar have the order of the months).</summary>
    private static bool CompareSurpasses(int sign, long year, long month, long day, IsoDate target)
    {
        if (year != target.Year)
            return sign * (year - target.Year) > 0;

        if (month != target.Month)
            return sign * (month - target.Month) > 0;

        if (day != target.Day)
            return sign * (day - target.Day) > 0;

        return false;
    }

    /// <summary>ISODateSurpasses(sign, baseDate, isoDate2, years, months, weeks, days)</summary>
    private static bool IsoDateSurpasses(int sign, IsoDate baseDate, IsoDate isoDate2, long years, long months, long weeks, long days)
    {
        var y0 = baseDate.Year + years;
        if (CompareSurpasses(sign, y0, baseDate.Month, baseDate.Day, isoDate2))
            return true;

        // The specification returns false here when months is 0, which would never end the loops on weeks and days of
        // CalendarDateUntil: the month test is only skipped (it is the same as the year test)
        var (balancedYear, balancedMonth) = BalanceIsoYearMonth(y0, baseDate.Month + months);
        if (months != 0 && CompareSurpasses(sign, balancedYear, balancedMonth, baseDate.Day, isoDate2))
            return true;

        if (weeks == 0 && days == 0)
            return false;

        var regulatedDate = RegulateIsoDate(balancedYear, balancedMonth, baseDate.Day, TemporalOverflow.Constrain);
        var balancedDate = AddDaysToIsoDate(regulatedDate, (7 * weeks) + days);
        return CompareSurpasses(sign, balancedDate.Year, balancedDate.Month, balancedDate.Day, isoDate2);
    }

    /// <summary>
    /// CalendarDateUntil(calendar, one, two, largestUnit). Each loop of the specification finds the largest count that does
    /// not surpass the second date; the count is estimated first, then adjusted with the same test.
    /// </summary>
    public static DateDuration CalendarDateUntil(string calendar, IsoDate one, IsoDate two, TemporalUnit largestUnit)
    {
        _ = calendar;
        var sign = CompareIsoDate(one, two);
        if (sign == 0)
            return default;

        sign = -sign;
        long years = 0;
        if (largestUnit == TemporalUnit.Year)
        {
            var estimate = two.Year - one.Year;
            years = sign * Math.Max(0, sign * estimate);
            while (years != 0 && IsoDateSurpasses(sign, one, two, years, 0, 0, 0))
            {
                years -= sign;
            }

            while (!IsoDateSurpasses(sign, one, two, years + sign, 0, 0, 0))
            {
                years += sign;
            }
        }

        long months = 0;
        if (largestUnit is TemporalUnit.Year or TemporalUnit.Month)
        {
            var estimate = ((two.Year - (one.Year + years)) * 12) + (two.Month - one.Month);
            months = sign * Math.Max(0, sign * estimate);
            while (months != 0 && IsoDateSurpasses(sign, one, two, years, months, 0, 0))
            {
                months -= sign;
            }

            while (!IsoDateSurpasses(sign, one, two, years, months + sign, 0, 0))
            {
                months += sign;
            }
        }

        long weeks = 0;
        if (largestUnit == TemporalUnit.Week)
        {
            var estimate = (IsoDateToEpochDays(two) - IsoDateToEpochDays(one)) / 7;
            weeks = sign * Math.Max(0, sign * estimate);
            while (weeks != 0 && IsoDateSurpasses(sign, one, two, years, months, weeks, 0))
            {
                weeks -= sign;
            }

            while (!IsoDateSurpasses(sign, one, two, years, months, weeks + sign, 0))
            {
                weeks += sign;
            }
        }

        // The date reached by the years, months and weeks (ISODateSurpasses regulates it with constrain)
        var (intermediateYear, intermediateMonth) = BalanceIsoYearMonth(one.Year + years, one.Month + months);
        var intermediate = AddDaysToIsoDate(RegulateIsoDate(intermediateYear, intermediateMonth, one.Day, TemporalOverflow.Constrain), 7 * weeks);
        var daysEstimate = IsoDateToEpochDays(two) - IsoDateToEpochDays(intermediate);
        var days = sign * Math.Max(0, sign * daysEstimate);
        while (days != 0 && IsoDateSurpasses(sign, one, two, years, months, weeks, days))
        {
            days -= sign;
        }

        while (!IsoDateSurpasses(sign, one, two, years, months, weeks, days + sign))
        {
            days += sign;
        }

        return new DateDuration(years, months, weeks, days);
    }
}
