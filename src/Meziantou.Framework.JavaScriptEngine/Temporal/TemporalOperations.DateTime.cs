namespace Meziantou.Framework.JavaScriptEngine;

internal static partial class TemporalOperations
{
    // ------------------------------------------------------------------------------------------------------------------
    // Epoch nanoseconds
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>IsValidEpochNanoseconds(epochNanoseconds)</summary>
    public static bool IsValidEpochNanoseconds(Int128 epochNanoseconds) => epochNanoseconds >= TemporalMath.NsMinInstant && epochNanoseconds <= TemporalMath.NsMaxInstant;

    /// <summary>GetUTCEpochNanoseconds(isoDateTime)</summary>
    public static Int128 GetUtcEpochNanoseconds(IsoDateTime isoDateTime) => ((Int128)IsoDateToEpochDays(isoDateTime.Date) * TemporalMath.NsPerDayLong) + isoDateTime.Time.TotalNanoseconds;

    /// <summary>CheckISODaysRange(isoDate)</summary>
    public static void CheckIsoDaysRange(IsoDate date)
    {
        if (Math.Abs(IsoDateToEpochDays(date)) > 100_000_000)
            ThrowHelper.ThrowRangeError("The date is outside the range of Temporal");
    }

    /// <summary>ISODateTimeWithinLimits(isoDateTime)</summary>
    public static bool IsoDateTimeWithinLimits(IsoDateTime isoDateTime)
    {
        if (Math.Abs(isoDateTime.Date.Year) > 300_000)
            return false;

        if (Math.Abs(IsoDateToEpochDays(isoDateTime.Date)) > 100_000_001)
            return false;

        var ns = GetUtcEpochNanoseconds(isoDateTime);
        if (ns <= TemporalMath.NsMinInstant - TemporalMath.NsPerDay)
            return false;

        if (ns >= TemporalMath.NsMaxInstant + TemporalMath.NsPerDay)
            return false;

        return true;
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Time Records
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>Creates a Time Record from nanoseconds since midnight (0 to nsPerDay - 1).</summary>
    public static TimeRecord NanosecondsToTimeRecord(long nanoseconds, long days)
    {
        var nanosecond = (int)(nanoseconds % 1000);
        nanoseconds /= 1000;
        var microsecond = (int)(nanoseconds % 1000);
        nanoseconds /= 1000;
        var millisecond = (int)(nanoseconds % 1000);
        nanoseconds /= 1000;
        var second = (int)(nanoseconds % 60);
        nanoseconds /= 60;
        var minute = (int)(nanoseconds % 60);
        var hour = (int)(nanoseconds / 60);
        return new TimeRecord(days, hour, minute, second, millisecond, microsecond, nanosecond);
    }

    /// <summary>BalanceTime(hour, minute, second, millisecond, microsecond, nanosecond) for a number of nanoseconds since midnight.</summary>
    public static TimeRecord BalanceTime(Int128 nanoseconds)
    {
        var days = TemporalMath.FloorDivide(nanoseconds, TemporalMath.NsPerDay);
        var remainder = (long)(nanoseconds - (days * TemporalMath.NsPerDay));
        return NanosecondsToTimeRecord(remainder, (long)days);
    }

    /// <summary>AddTime(time, timeDuration)</summary>
    public static TimeRecord AddTime(TimeRecord time, Int128 timeDuration) => BalanceTime(time.TotalNanoseconds + timeDuration);

    /// <summary>DifferenceTime(time1, time2)</summary>
    public static Int128 DifferenceTime(TimeRecord time1, TimeRecord time2) => time2.TotalNanoseconds - time1.TotalNanoseconds;

    /// <summary>IsValidTime(hour, minute, second, millisecond, microsecond, nanosecond)</summary>
    public static bool IsValidTime(double hour, double minute, double second, double millisecond, double microsecond, double nanosecond)
    {
        return hour is >= 0 and <= 23
            && minute is >= 0 and <= 59
            && second is >= 0 and <= 59
            && millisecond is >= 0 and <= 999
            && microsecond is >= 0 and <= 999
            && nanosecond is >= 0 and <= 999;
    }

    /// <summary>RegulateTime(hour, minute, second, millisecond, microsecond, nanosecond, overflow)</summary>
    public static TimeRecord RegulateTime(double hour, double minute, double second, double millisecond, double microsecond, double nanosecond, TemporalOverflow overflow)
    {
        if (overflow == TemporalOverflow.Constrain)
        {
            hour = Math.Clamp(hour, 0, 23);
            minute = Math.Clamp(minute, 0, 59);
            second = Math.Clamp(second, 0, 59);
            millisecond = Math.Clamp(millisecond, 0, 999);
            microsecond = Math.Clamp(microsecond, 0, 999);
            nanosecond = Math.Clamp(nanosecond, 0, 999);
        }
        else if (!IsValidTime(hour, minute, second, millisecond, microsecond, nanosecond))
        {
            ThrowHelper.ThrowRangeError("The time is not valid");
        }

        return new TimeRecord(0, (int)hour, (int)minute, (int)second, (int)millisecond, (int)microsecond, (int)nanosecond);
    }

    /// <summary>CompareTimeRecord(time1, time2)</summary>
    public static int CompareTimeRecord(TimeRecord time1, TimeRecord time2) => time1.TotalNanoseconds.CompareTo(time2.TotalNanoseconds);

    /// <summary>CompareISODateTime(isoDateTime1, isoDateTime2)</summary>
    public static int CompareIsoDateTime(IsoDateTime one, IsoDateTime two)
    {
        var dateResult = CompareIsoDate(one.Date, two.Date);
        if (dateResult != 0)
            return dateResult;

        return CompareTimeRecord(one.Time, two.Time);
    }

    /// <summary>RoundTime(time, increment, unit, roundingMode)</summary>
    public static TimeRecord RoundTime(TimeRecord time, long increment, TemporalUnit unit, TemporalRoundingMode roundingMode)
    {
        long quantity;
        switch (unit)
        {
            case TemporalUnit.Day:
            case TemporalUnit.Hour:
                quantity = time.TotalNanoseconds;
                break;
            case TemporalUnit.Minute:
                quantity = time.TotalNanoseconds - (time.Hour * 3_600_000_000_000L);
                break;
            case TemporalUnit.Second:
                quantity = (((((long)time.Second * 1000) + time.Millisecond) * 1000) + time.Microsecond) * 1000 + time.Nanosecond;
                break;
            case TemporalUnit.Millisecond:
                quantity = (((long)time.Millisecond * 1000) + time.Microsecond) * 1000 + time.Nanosecond;
                break;
            case TemporalUnit.Microsecond:
                quantity = ((long)time.Microsecond * 1000) + time.Nanosecond;
                break;
            default:
                quantity = time.Nanosecond;
                break;
        }

        var unitLength = TemporalMath.UnitLengthNs(unit);
        var result = (long)(TemporalMath.RoundNumberToIncrement(quantity, (Int128)increment * unitLength, roundingMode) / unitLength);
        return unit switch
        {
            TemporalUnit.Day => new TimeRecord(result, 0, 0, 0, 0, 0, 0),
            TemporalUnit.Hour => BalanceTime((Int128)result * 3_600_000_000_000L),
            TemporalUnit.Minute => BalanceTime(((Int128)time.Hour * 3_600_000_000_000L) + ((Int128)result * 60_000_000_000L)),
            TemporalUnit.Second => BalanceTime(((Int128)time.Hour * 3_600_000_000_000L) + (time.Minute * 60_000_000_000L) + ((Int128)result * 1_000_000_000L)),
            TemporalUnit.Millisecond => BalanceTime(((Int128)time.Hour * 3_600_000_000_000L) + (time.Minute * 60_000_000_000L) + (time.Second * 1_000_000_000L) + ((Int128)result * 1_000_000L)),
            TemporalUnit.Microsecond => BalanceTime(((Int128)time.Hour * 3_600_000_000_000L) + (time.Minute * 60_000_000_000L) + (time.Second * 1_000_000_000L) + (time.Millisecond * 1_000_000L) + ((Int128)result * 1000)),
            _ => BalanceTime(((Int128)time.Hour * 3_600_000_000_000L) + (time.Minute * 60_000_000_000L) + (time.Second * 1_000_000_000L) + (time.Millisecond * 1_000_000L) + (time.Microsecond * 1000L) + result),
        };
    }

    /// <summary>RoundISODateTime(isoDateTime, increment, unit, roundingMode)</summary>
    public static IsoDateTime RoundIsoDateTime(IsoDateTime isoDateTime, long increment, TemporalUnit unit, TemporalRoundingMode roundingMode)
    {
        var roundedTime = RoundTime(isoDateTime.Time, increment, unit, roundingMode);
        var balanceResult = AddDaysToIsoDate(isoDateTime.Date, roundedTime.Days);
        return new IsoDateTime(balanceResult, roundedTime.WithoutDays());
    }

    /// <summary>BalanceISODateTime with the time given as nanoseconds since midnight (may be negative or larger than a day).</summary>
    public static IsoDateTime BalanceIsoDateTime(IsoDate date, Int128 timeNanoseconds)
    {
        var balancedTime = BalanceTime(timeNanoseconds);
        var balancedDate = AddDaysToIsoDate(date, balancedTime.Days);
        return new IsoDateTime(balancedDate, balancedTime.WithoutDays());
    }

    /// <summary>DifferenceISODateTime(isoDateTime1, isoDateTime2, calendar, largestUnit)</summary>
    public static InternalDuration DifferenceIsoDateTime(IsoDateTime isoDateTime1, IsoDateTime isoDateTime2, string calendar, TemporalUnit largestUnit)
    {
        var timeDuration = DifferenceTime(isoDateTime1.Time, isoDateTime2.Time);
        var timeSign = Int128.Sign(timeDuration);
        var dateSign = CompareIsoDate(isoDateTime1.Date, isoDateTime2.Date);
        var adjustedDate = isoDateTime2.Date;
        if (timeSign == dateSign)
        {
            adjustedDate = AddDaysToIsoDate(adjustedDate, timeSign);
            timeDuration -= timeSign * TemporalMath.NsPerDay;
        }

        var dateLargestUnit = LargerOfTwoTemporalUnits(TemporalUnit.Day, largestUnit);
        var dateDifference = CalendarDateUntil(calendar, isoDateTime1.Date, adjustedDate, dateLargestUnit);
        if (largestUnit != dateLargestUnit)
        {
            timeDuration += dateDifference.Days * TemporalMath.NsPerDay;
            dateDifference = dateDifference with { Days = 0 };
        }

        return new InternalDuration(dateDifference, timeDuration);
    }

    // ------------------------------------------------------------------------------------------------------------------
    // String formatting
    // ------------------------------------------------------------------------------------------------------------------

    private static void AppendPadded(StringBuilder sb, long value, int length)
    {
        var text = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        sb.Append('0', Math.Max(0, length - text.Length));
        sb.Append(text);
    }

    /// <summary>PadISOYear(y)</summary>
    private static void AppendIsoYear(StringBuilder sb, long year)
    {
        if (year is >= 0 and <= 9999)
        {
            AppendPadded(sb, year, 4);
            return;
        }

        sb.Append(year > 0 ? '+' : '-');
        AppendPadded(sb, Math.Abs(year), 6);
    }

    /// <summary>FormatFractionalSeconds(subSecondNanoseconds, precision)</summary>
    private static void AppendFractionalSeconds(StringBuilder sb, int subSecondNanoseconds, int precision)
    {
        if (precision == PrecisionAuto)
        {
            if (subSecondNanoseconds == 0)
                return;

            var digits = subSecondNanoseconds.ToString("000000000", System.Globalization.CultureInfo.InvariantCulture).TrimEnd('0');
            sb.Append('.').Append(digits);
            return;
        }

        if (precision == 0)
            return;

        sb.Append('.').Append(subSecondNanoseconds.ToString("000000000", System.Globalization.CultureInfo.InvariantCulture), 0, precision);
    }

    /// <summary>FormatTimeString(hour, minute, second, subSecondNanoseconds, precision, style)</summary>
    public static string FormatTimeString(int hour, int minute, int second, int subSecondNanoseconds, int precision, bool separated = true)
    {
        var sb = new StringBuilder(18);
        AppendTimeString(sb, hour, minute, second, subSecondNanoseconds, precision, separated);
        return sb.ToString();
    }

    private static void AppendTimeString(StringBuilder sb, int hour, int minute, int second, int subSecondNanoseconds, int precision, bool separated = true)
    {
        AppendPadded(sb, hour, 2);
        if (separated)
        {
            sb.Append(':');
        }

        AppendPadded(sb, minute, 2);
        if (precision == PrecisionMinute)
            return;

        if (separated)
        {
            sb.Append(':');
        }

        AppendPadded(sb, second, 2);
        AppendFractionalSeconds(sb, subSecondNanoseconds, precision);
    }

    private static int SubSecondNanoseconds(TimeRecord time) => (time.Millisecond * 1_000_000) + (time.Microsecond * 1000) + time.Nanosecond;

    /// <summary>TimeRecordToString(time, precision)</summary>
    public static string TimeRecordToString(TimeRecord time, int precision) => FormatTimeString(time.Hour, time.Minute, time.Second, SubSecondNanoseconds(time), precision);

    private static void AppendIsoDate(StringBuilder sb, IsoDate date)
    {
        AppendIsoYear(sb, date.Year);
        sb.Append('-');
        AppendPadded(sb, date.Month, 2);
        sb.Append('-');
        AppendPadded(sb, date.Day, 2);
    }

    /// <summary>TemporalDateToString(temporalDate, showCalendar)</summary>
    public static string TemporalDateToString(JsPlainDate temporalDate, ShowCalendarOption showCalendar)
    {
        var sb = new StringBuilder(32);
        AppendIsoDate(sb, temporalDate.IsoDate);
        sb.Append(FormatCalendarAnnotation(temporalDate.Calendar, showCalendar));
        return sb.ToString();
    }

    /// <summary>ISODateTimeToString(isoDateTime, calendar, precision, showCalendar)</summary>
    public static string IsoDateTimeToString(IsoDateTime isoDateTime, string calendar, int precision, ShowCalendarOption showCalendar)
    {
        var sb = new StringBuilder(48);
        AppendIsoDate(sb, isoDateTime.Date);
        sb.Append('T');
        AppendTimeString(sb, isoDateTime.Time.Hour, isoDateTime.Time.Minute, isoDateTime.Time.Second, SubSecondNanoseconds(isoDateTime.Time), precision);
        sb.Append(FormatCalendarAnnotation(calendar, showCalendar));
        return sb.ToString();
    }

    /// <summary>TemporalYearMonthToString(yearMonth, showCalendar)</summary>
    public static string TemporalYearMonthToString(JsPlainYearMonth yearMonth, ShowCalendarOption showCalendar)
    {
        var sb = new StringBuilder(32);
        AppendIsoYear(sb, yearMonth.IsoDate.Year);
        sb.Append('-');
        AppendPadded(sb, yearMonth.IsoDate.Month, 2);
        if (showCalendar is ShowCalendarOption.Always or ShowCalendarOption.Critical || yearMonth.Calendar != IsoCalendar)
        {
            sb.Append('-');
            AppendPadded(sb, yearMonth.IsoDate.Day, 2);
        }

        sb.Append(FormatCalendarAnnotation(yearMonth.Calendar, showCalendar));
        return sb.ToString();
    }

    /// <summary>TemporalMonthDayToString(monthDay, showCalendar)</summary>
    public static string TemporalMonthDayToString(JsPlainMonthDay monthDay, ShowCalendarOption showCalendar)
    {
        var sb = new StringBuilder(32);
        if (showCalendar is ShowCalendarOption.Always or ShowCalendarOption.Critical || monthDay.Calendar != IsoCalendar)
        {
            AppendIsoYear(sb, monthDay.IsoDate.Year);
            sb.Append('-');
        }

        AppendPadded(sb, monthDay.IsoDate.Month, 2);
        sb.Append('-');
        AppendPadded(sb, monthDay.IsoDate.Day, 2);
        sb.Append(FormatCalendarAnnotation(monthDay.Calendar, showCalendar));
        return sb.ToString();
    }

    /// <summary>TemporalInstantToString(instant, timeZone, precision)</summary>
    public static string TemporalInstantToString(Int128 epochNs, TemporalTimeZone? timeZone, int precision)
    {
        var outputTimeZone = timeZone ?? TemporalTimeZone.Utc;
        var isoDateTime = GetIsoDateTimeFor(outputTimeZone, epochNs);
        var dateTimeString = IsoDateTimeToString(isoDateTime, IsoCalendar, precision, ShowCalendarOption.Never);
        if (timeZone is null)
            return dateTimeString + "Z";

        var offsetNanoseconds = GetOffsetNanosecondsFor(outputTimeZone, epochNs);
        return dateTimeString + FormatDateTimeUtcOffsetRounded(offsetNanoseconds);
    }

    /// <summary>TemporalZonedDateTimeToString(zonedDateTime, precision, showCalendar, showTimeZone, showOffset, increment, unit, roundingMode)</summary>
    public static string TemporalZonedDateTimeToString(JsZonedDateTime zonedDateTime, int precision, ShowCalendarOption showCalendar, ShowTimeZoneOption showTimeZone, bool showOffset, long increment = 1, TemporalUnit unit = TemporalUnit.Nanosecond, TemporalRoundingMode roundingMode = TemporalRoundingMode.Trunc)
    {
        var epochNs = RoundTemporalInstant(zonedDateTime.EpochNanoseconds, increment, unit, roundingMode);
        var timeZone = zonedDateTime.TimeZone;
        var offsetNanoseconds = GetOffsetNanosecondsFor(timeZone, epochNs);
        var isoDateTime = EpochNanosecondsToIsoDateTime(epochNs + offsetNanoseconds);
        var sb = new StringBuilder(64);
        sb.Append(IsoDateTimeToString(isoDateTime, IsoCalendar, precision, ShowCalendarOption.Never));
        if (showOffset)
        {
            sb.Append(FormatDateTimeUtcOffsetRounded(offsetNanoseconds));
        }

        if (showTimeZone != ShowTimeZoneOption.Never)
        {
            sb.Append('[');
            if (showTimeZone == ShowTimeZoneOption.Critical)
            {
                sb.Append('!');
            }

            sb.Append(timeZone.Identifier).Append(']');
        }

        sb.Append(FormatCalendarAnnotation(zonedDateTime.Calendar, showCalendar));
        return sb.ToString();
    }

    /// <summary>RoundTemporalInstant(ns, increment, unit, roundingMode)</summary>
    public static Int128 RoundTemporalInstant(Int128 ns, long increment, TemporalUnit unit, TemporalRoundingMode roundingMode)
    {
        var incrementNs = (Int128)increment * TemporalMath.UnitLengthNs(unit);
        if (incrementNs == 1)
            return ns;

        return TemporalMath.RoundNumberToIncrementAsIfPositive(ns, incrementNs, roundingMode);
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Creation
    // ------------------------------------------------------------------------------------------------------------------

    private static JsObject GetPrototype(JsObject? newTarget, Func<Intrinsics, JsObject> intrinsicDefaultPrototype)
    {
        if (newTarget is null)
            return intrinsicDefaultPrototype(CurrentIntrinsics);

        return ObjectOperations.GetPrototypeFromConstructor(newTarget, intrinsicDefaultPrototype);
    }

    /// <summary>CreateTemporalDate(isoDate, calendar, newTarget)</summary>
    public static JsPlainDate CreateTemporalDate(IsoDate isoDate, string calendar, JsObject? newTarget = null)
    {
        if (!IsoDateWithinLimits(isoDate))
            return ThrowHelper.ThrowRangeError<JsPlainDate>("The date is outside the range of Temporal.PlainDate");

        return new JsPlainDate(GetPrototype(newTarget, i => i.TemporalPlainDatePrototype), isoDate, calendar);
    }

    /// <summary>CreateTemporalTime(time, newTarget)</summary>
    public static JsPlainTime CreateTemporalTime(TimeRecord time, JsObject? newTarget = null) => new(GetPrototype(newTarget, i => i.TemporalPlainTimePrototype), time.WithoutDays());

    /// <summary>CreateTemporalDateTime(isoDateTime, calendar, newTarget)</summary>
    public static JsPlainDateTime CreateTemporalDateTime(IsoDateTime isoDateTime, string calendar, JsObject? newTarget = null)
    {
        if (!IsoDateTimeWithinLimits(isoDateTime))
            return ThrowHelper.ThrowRangeError<JsPlainDateTime>("The date and time is outside the range of Temporal.PlainDateTime");

        return new JsPlainDateTime(GetPrototype(newTarget, i => i.TemporalPlainDateTimePrototype), isoDateTime with { Time = isoDateTime.Time.WithoutDays() }, calendar);
    }

    /// <summary>CreateTemporalYearMonth(isoDate, calendar, newTarget)</summary>
    public static JsPlainYearMonth CreateTemporalYearMonth(IsoDate isoDate, string calendar, JsObject? newTarget = null)
    {
        if (!IsoYearMonthWithinLimits(isoDate))
            return ThrowHelper.ThrowRangeError<JsPlainYearMonth>("The date is outside the range of Temporal.PlainYearMonth");

        return new JsPlainYearMonth(GetPrototype(newTarget, i => i.TemporalPlainYearMonthPrototype), isoDate, calendar);
    }

    /// <summary>CreateTemporalMonthDay(isoDate, calendar, newTarget)</summary>
    public static JsPlainMonthDay CreateTemporalMonthDay(IsoDate isoDate, string calendar, JsObject? newTarget = null)
    {
        if (!IsoDateWithinLimits(isoDate))
            return ThrowHelper.ThrowRangeError<JsPlainMonthDay>("The date is outside the range of Temporal.PlainMonthDay");

        return new JsPlainMonthDay(GetPrototype(newTarget, i => i.TemporalPlainMonthDayPrototype), isoDate, calendar);
    }

    /// <summary>CreateTemporalInstant(epochNanoseconds, newTarget)</summary>
    public static JsInstant CreateTemporalInstant(Int128 epochNanoseconds, JsObject? newTarget = null) => new(GetPrototype(newTarget, i => i.TemporalInstantPrototype), epochNanoseconds);

    /// <summary>CreateTemporalZonedDateTime(epochNanoseconds, timeZone, calendar, newTarget)</summary>
    public static JsZonedDateTime CreateTemporalZonedDateTime(Int128 epochNanoseconds, TemporalTimeZone timeZone, string calendar, JsObject? newTarget = null) => new(GetPrototype(newTarget, i => i.TemporalZonedDateTimePrototype), epochNanoseconds, timeZone, calendar);

    // ------------------------------------------------------------------------------------------------------------------
    // Conversion of values
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>ToTemporalTimeRecord(temporalTimeLike, completeness)</summary>
    public static TemporalTimeLike ToTemporalTimeRecord(JsObject temporalTimeLike, bool partial)
    {
        var initial = partial ? double.NaN : 0;
        var result = new TemporalTimeLike
        {
            Hour = initial,
            Minute = initial,
            Second = initial,
            Millisecond = initial,
            Microsecond = initial,
            Nanosecond = initial,
        };

        var any = false;
        var value = temporalTimeLike.Get("hour");
        if (!value.IsUndefined)
        {
            result.Hour = ToIntegerWithTruncation(value);
            any = true;
        }

        value = temporalTimeLike.Get("microsecond");
        if (!value.IsUndefined)
        {
            result.Microsecond = ToIntegerWithTruncation(value);
            any = true;
        }

        value = temporalTimeLike.Get("millisecond");
        if (!value.IsUndefined)
        {
            result.Millisecond = ToIntegerWithTruncation(value);
            any = true;
        }

        value = temporalTimeLike.Get("minute");
        if (!value.IsUndefined)
        {
            result.Minute = ToIntegerWithTruncation(value);
            any = true;
        }

        value = temporalTimeLike.Get("nanosecond");
        if (!value.IsUndefined)
        {
            result.Nanosecond = ToIntegerWithTruncation(value);
            any = true;
        }

        value = temporalTimeLike.Get("second");
        if (!value.IsUndefined)
        {
            result.Second = ToIntegerWithTruncation(value);
            any = true;
        }

        if (!any)
            return ThrowHelper.ThrowTypeError<TemporalTimeLike>("The object must have at least one time property");

        return result;
    }

    /// <summary>ToTemporalTime(item, options)</summary>
    public static JsPlainTime ToTemporalTime(JsValue item, JsValue options = default)
    {
        TimeRecord result;
        if (item.Cell is JsObject obj)
        {
            switch (obj)
            {
                case JsPlainTime plainTime:
                    GetTemporalOverflowOption(GetOptionsObject(options));
                    return CreateTemporalTime(plainTime.Time);
                case JsPlainDateTime plainDateTime:
                    GetTemporalOverflowOption(GetOptionsObject(options));
                    return CreateTemporalTime(plainDateTime.IsoDateTime.Time);
                case JsZonedDateTime zonedDateTime:
                    {
                        var isoDateTime = GetIsoDateTimeFor(zonedDateTime.TimeZone, zonedDateTime.EpochNanoseconds);
                        GetTemporalOverflowOption(GetOptionsObject(options));
                        return CreateTemporalTime(isoDateTime.Time);
                    }
            }

            var timeLike = ToTemporalTimeRecord(obj, partial: false);
            var overflow = GetTemporalOverflowOption(GetOptionsObject(options));
            result = RegulateTime(timeLike.Hour, timeLike.Minute, timeLike.Second, timeLike.Millisecond, timeLike.Microsecond, timeLike.Nanosecond, overflow);
        }
        else
        {
            if (item.Cell is not JsString str)
                return ThrowHelper.ThrowTypeError<JsPlainTime>("The value cannot be converted to a Temporal.PlainTime");

            var parseResult = TemporalParser.ParseIsoDateTime(str.ToString(), [TemporalParseGoal.Time]);
            result = parseResult.Time;
            GetTemporalOverflowOption(GetOptionsObject(options));
        }

        return CreateTemporalTime(result);
    }

    /// <summary>ToTimeRecordOrMidnight(item)</summary>
    public static TimeRecord ToTimeRecordOrMidnight(JsValue item)
    {
        if (item.IsUndefined)
            return TimeRecord.Midnight;

        return ToTemporalTime(item).Time;
    }

    /// <summary>ToTemporalDate(item, options)</summary>
    public static JsPlainDate ToTemporalDate(JsValue item, JsValue options = default)
    {
        if (item.Cell is JsObject obj)
        {
            switch (obj)
            {
                case JsPlainDate plainDate:
                    GetTemporalOverflowOption(GetOptionsObject(options));
                    return CreateTemporalDate(plainDate.IsoDate, plainDate.Calendar);
                case JsZonedDateTime zonedDateTime:
                    {
                        var isoDateTime = GetIsoDateTimeFor(zonedDateTime.TimeZone, zonedDateTime.EpochNanoseconds);
                        GetTemporalOverflowOption(GetOptionsObject(options));
                        return CreateTemporalDate(isoDateTime.Date, zonedDateTime.Calendar);
                    }

                case JsPlainDateTime plainDateTime:
                    GetTemporalOverflowOption(GetOptionsObject(options));
                    return CreateTemporalDate(plainDateTime.IsoDateTime.Date, plainDateTime.Calendar);
            }

            var calendar = GetTemporalCalendarIdentifierWithIsoDefault(obj);
            var fields = PrepareCalendarFields(calendar, obj, CalendarFieldNames.YearMonthDay, CalendarFieldNames.None, partial: false);
            var overflow = GetTemporalOverflowOption(GetOptionsObject(options));
            var isoDate = CalendarDateFromFields(calendar, fields, overflow);
            return CreateTemporalDate(isoDate, calendar);
        }

        if (item.Cell is not JsString str)
            return ThrowHelper.ThrowTypeError<JsPlainDate>("The value cannot be converted to a Temporal.PlainDate");

        var result = TemporalParser.ParseIsoDateTime(str.ToString(), [TemporalParseGoal.DateTime]);
        var parsedCalendar = CanonicalizeCalendar(result.Calendar ?? IsoCalendar);
        GetTemporalOverflowOption(GetOptionsObject(options));
        return CreateTemporalDate(new IsoDate(result.Year!.Value, result.Month, result.Day), parsedCalendar);
    }

    /// <summary>InterpretTemporalDateTimeFields(calendar, fields, overflow)</summary>
    public static IsoDateTime InterpretTemporalDateTimeFields(string calendar, CalendarFields fields, TemporalOverflow overflow)
    {
        var isoDate = CalendarDateFromFields(calendar, fields, overflow);
        var time = RegulateTime(fields.Hour ?? 0, fields.Minute ?? 0, fields.Second ?? 0, fields.Millisecond ?? 0, fields.Microsecond ?? 0, fields.Nanosecond ?? 0, overflow);
        return new IsoDateTime(isoDate, time);
    }

    /// <summary>ToTemporalDateTime(item, options)</summary>
    public static JsPlainDateTime ToTemporalDateTime(JsValue item, JsValue options = default)
    {
        if (item.Cell is JsObject obj)
        {
            switch (obj)
            {
                case JsPlainDateTime plainDateTime:
                    GetTemporalOverflowOption(GetOptionsObject(options));
                    return CreateTemporalDateTime(plainDateTime.IsoDateTime, plainDateTime.Calendar);
                case JsZonedDateTime zonedDateTime:
                    {
                        var isoDateTime = GetIsoDateTimeFor(zonedDateTime.TimeZone, zonedDateTime.EpochNanoseconds);
                        GetTemporalOverflowOption(GetOptionsObject(options));
                        return CreateTemporalDateTime(isoDateTime, zonedDateTime.Calendar);
                    }

                case JsPlainDate plainDate:
                    GetTemporalOverflowOption(GetOptionsObject(options));
                    return CreateTemporalDateTime(new IsoDateTime(plainDate.IsoDate, TimeRecord.Midnight), plainDate.Calendar);
            }

            var calendar = GetTemporalCalendarIdentifierWithIsoDefault(obj);
            var fields = PrepareCalendarFields(calendar, obj, CalendarFieldNames.YearMonthDay | CalendarFieldNames.Time, CalendarFieldNames.None, partial: false);
            var overflow = GetTemporalOverflowOption(GetOptionsObject(options));
            var interpreted = InterpretTemporalDateTimeFields(calendar, fields, overflow);
            return CreateTemporalDateTime(interpreted, calendar);
        }

        if (item.Cell is not JsString str)
            return ThrowHelper.ThrowTypeError<JsPlainDateTime>("The value cannot be converted to a Temporal.PlainDateTime");

        var result = TemporalParser.ParseIsoDateTime(str.ToString(), [TemporalParseGoal.DateTime]);
        var time = result.HasTime ? result.Time : TimeRecord.Midnight;
        var parsedCalendar = CanonicalizeCalendar(result.Calendar ?? IsoCalendar);
        GetTemporalOverflowOption(GetOptionsObject(options));
        return CreateTemporalDateTime(new IsoDateTime(new IsoDate(result.Year!.Value, result.Month, result.Day), time), parsedCalendar);
    }

    /// <summary>ToTemporalYearMonth(item, options)</summary>
    public static JsPlainYearMonth ToTemporalYearMonth(JsValue item, JsValue options = default)
    {
        if (item.Cell is JsObject obj)
        {
            if (obj is JsPlainYearMonth plainYearMonth)
            {
                GetTemporalOverflowOption(GetOptionsObject(options));
                return CreateTemporalYearMonth(plainYearMonth.IsoDate, plainYearMonth.Calendar);
            }

            var calendar = GetTemporalCalendarIdentifierWithIsoDefault(obj);
            var fields = PrepareCalendarFields(calendar, obj, CalendarFieldNames.Year | CalendarFieldNames.Month | CalendarFieldNames.MonthCode, CalendarFieldNames.None, partial: false);
            var overflow = GetTemporalOverflowOption(GetOptionsObject(options));
            var isoDate = CalendarYearMonthFromFields(calendar, fields, overflow);
            return CreateTemporalYearMonth(isoDate, calendar);
        }

        if (item.Cell is not JsString str)
            return ThrowHelper.ThrowTypeError<JsPlainYearMonth>("The value cannot be converted to a Temporal.PlainYearMonth");

        var result = TemporalParser.ParseIsoDateTime(str.ToString(), [TemporalParseGoal.YearMonth]);
        var parsedCalendar = CanonicalizeCalendar(result.Calendar ?? IsoCalendar);
        GetTemporalOverflowOption(GetOptionsObject(options));
        var parsedDate = new IsoDate(result.Year!.Value, result.Month, result.Day);
        if (!IsoYearMonthWithinLimits(parsedDate))
            return ThrowHelper.ThrowRangeError<JsPlainYearMonth>("The date is outside the range of Temporal.PlainYearMonth");

        var resultFields = IsoDateToFields(parsedCalendar, parsedDate, CalendarFieldsType.YearMonth);
        var regulated = CalendarYearMonthFromFields(parsedCalendar, resultFields, TemporalOverflow.Constrain);
        return CreateTemporalYearMonth(regulated, parsedCalendar);
    }

    /// <summary>ToTemporalMonthDay(item, options)</summary>
    public static JsPlainMonthDay ToTemporalMonthDay(JsValue item, JsValue options = default)
    {
        if (item.Cell is JsObject obj)
        {
            if (obj is JsPlainMonthDay plainMonthDay)
            {
                GetTemporalOverflowOption(GetOptionsObject(options));
                return CreateTemporalMonthDay(plainMonthDay.IsoDate, plainMonthDay.Calendar);
            }

            var calendar = GetTemporalCalendarIdentifierWithIsoDefault(obj);
            var fields = PrepareCalendarFields(calendar, obj, CalendarFieldNames.YearMonthDay, CalendarFieldNames.None, partial: false);
            var overflow = GetTemporalOverflowOption(GetOptionsObject(options));
            var isoDate = CalendarMonthDayFromFields(calendar, fields, overflow);
            return CreateTemporalMonthDay(isoDate, calendar);
        }

        if (item.Cell is not JsString str)
            return ThrowHelper.ThrowTypeError<JsPlainMonthDay>("The value cannot be converted to a Temporal.PlainMonthDay");

        var result = TemporalParser.ParseIsoDateTime(str.ToString(), [TemporalParseGoal.MonthDay]);
        var parsedCalendar = CanonicalizeCalendar(result.Calendar ?? IsoCalendar);
        GetTemporalOverflowOption(GetOptionsObject(options));
        if (parsedCalendar == IsoCalendar)
            return CreateTemporalMonthDay(new IsoDate(1972, result.Month, result.Day), parsedCalendar);

        var parsedDate = new IsoDate(result.Year!.Value, result.Month, result.Day);
        if (!IsoDateWithinLimits(parsedDate))
            return ThrowHelper.ThrowRangeError<JsPlainMonthDay>("The date is outside the range of Temporal");

        var resultFields = IsoDateToFields(parsedCalendar, parsedDate, CalendarFieldsType.MonthDay);
        var regulated = CalendarMonthDayFromFields(parsedCalendar, resultFields, TemporalOverflow.Constrain);
        return CreateTemporalMonthDay(regulated, parsedCalendar);
    }

    /// <summary>ToTemporalInstant(item)</summary>
    public static JsInstant ToTemporalInstant(JsValue item)
    {
        if (item.Cell is JsObject obj)
        {
            if (obj is JsInstant instant)
                return CreateTemporalInstant(instant.EpochNanoseconds);

            if (obj is JsZonedDateTime zonedDateTime)
                return CreateTemporalInstant(zonedDateTime.EpochNanoseconds);

            item = JsOperations.ToPrimitive(item, JsOperations.PreferredType.String);
        }

        if (item.Cell is not JsString str)
            return ThrowHelper.ThrowTypeError<JsInstant>("The value cannot be converted to a Temporal.Instant");

        var parsed = TemporalParser.ParseIsoDateTime(str.ToString(), [TemporalParseGoal.Instant]);
        var offsetNanoseconds = parsed.TimeZone.Z ? 0 : ParseDateTimeUtcOffset(parsed.TimeZone.OffsetString!);
        var balanced = BalanceIsoDateTime(new IsoDate(parsed.Year!.Value, parsed.Month, parsed.Day), (Int128)parsed.Time.TotalNanoseconds - offsetNanoseconds);
        CheckIsoDaysRange(balanced.Date);
        var epochNanoseconds = GetUtcEpochNanoseconds(balanced);
        if (!IsValidEpochNanoseconds(epochNanoseconds))
            return ThrowHelper.ThrowRangeError<JsInstant>("The instant is outside the range of Temporal.Instant");

        return CreateTemporalInstant(epochNanoseconds);
    }

    /// <summary>ToTemporalZonedDateTime(item, options)</summary>
    public static JsZonedDateTime ToTemporalZonedDateTime(JsValue item, JsValue options = default)
    {
        var hasUtcDesignator = false;
        var matchBehaviour = MatchBehaviour.MatchExactly;
        TemporalTimeZone timeZone;
        string? offsetString;
        string calendar;
        TemporalDisambiguation disambiguation;
        TemporalOffsetOption offsetOption;
        IsoDate isoDate;
        TimeRecord time;
        var hasTime = true;
        if (item.Cell is JsObject obj)
        {
            if (obj is JsZonedDateTime zonedDateTime)
            {
                var options1 = GetOptionsObject(options);
                GetTemporalDisambiguationOption(options1);
                GetTemporalOffsetOption(options1, TemporalOffsetOption.Reject);
                GetTemporalOverflowOption(options1);
                return CreateTemporalZonedDateTime(zonedDateTime.EpochNanoseconds, zonedDateTime.TimeZone, zonedDateTime.Calendar);
            }

            calendar = GetTemporalCalendarIdentifierWithIsoDefault(obj);
            var fields = PrepareCalendarFields(calendar, obj, CalendarFieldNames.YearMonthDay | CalendarFieldNames.Time | CalendarFieldNames.Offset | CalendarFieldNames.TimeZone, CalendarFieldNames.TimeZone, partial: false);
            timeZone = fields.TimeZone!;
            offsetString = fields.OffsetString;
            var resolvedOptions = GetOptionsObject(options);
            disambiguation = GetTemporalDisambiguationOption(resolvedOptions);
            offsetOption = GetTemporalOffsetOption(resolvedOptions, TemporalOffsetOption.Reject);
            var overflow = GetTemporalOverflowOption(resolvedOptions);
            var result = InterpretTemporalDateTimeFields(calendar, fields, overflow);
            isoDate = result.Date;
            time = result.Time;
        }
        else
        {
            if (item.Cell is not JsString str)
                return ThrowHelper.ThrowTypeError<JsZonedDateTime>("The value cannot be converted to a Temporal.ZonedDateTime");

            var result = TemporalParser.ParseIsoDateTime(str.ToString(), [TemporalParseGoal.ZonedDateTime]);
            timeZone = ToTemporalTimeZoneIdentifier(JsValue.FromString(result.TimeZone.TimeZoneAnnotation!));
            offsetString = result.TimeZone.OffsetString;
            hasUtcDesignator = result.TimeZone.Z;
            calendar = CanonicalizeCalendar(result.Calendar ?? IsoCalendar);
            matchBehaviour = MatchBehaviour.MatchMinutes;
            if (offsetString is not null && result.OffsetHasSubMinutePrecision)
            {
                matchBehaviour = MatchBehaviour.MatchExactly;
            }

            var resolvedOptions = GetOptionsObject(options);
            disambiguation = GetTemporalDisambiguationOption(resolvedOptions);
            offsetOption = GetTemporalOffsetOption(resolvedOptions, TemporalOffsetOption.Reject);
            GetTemporalOverflowOption(resolvedOptions);
            isoDate = new IsoDate(result.Year!.Value, result.Month, result.Day);
            time = result.Time;
            hasTime = result.HasTime;
        }

        OffsetBehaviour offsetBehaviour;
        if (hasUtcDesignator)
        {
            offsetBehaviour = OffsetBehaviour.Exact;
        }
        else if (offsetString is null)
        {
            offsetBehaviour = OffsetBehaviour.Wall;
        }
        else
        {
            offsetBehaviour = OffsetBehaviour.Option;
        }

        long offsetNanoseconds = 0;
        if (offsetBehaviour == OffsetBehaviour.Option)
        {
            offsetNanoseconds = ParseDateTimeUtcOffset(offsetString!);
        }

        var epochNanoseconds = InterpretIsoDateTimeOffset(isoDate, hasTime ? time : null, offsetBehaviour, offsetNanoseconds, timeZone, disambiguation, offsetOption, matchBehaviour);
        return CreateTemporalZonedDateTime(epochNanoseconds, timeZone, calendar);
    }

    /// <summary>InterpretISODateTimeOffset(isoDate, time, offsetBehaviour, offsetNanoseconds, timeZone, disambiguation, offsetOption, matchBehaviour): a null time is start-of-day.</summary>
    public static Int128 InterpretIsoDateTimeOffset(IsoDate isoDate, TimeRecord? time, OffsetBehaviour offsetBehaviour, long offsetNanoseconds, TemporalTimeZone timeZone, TemporalDisambiguation disambiguation, TemporalOffsetOption offsetOption, MatchBehaviour matchBehaviour)
    {
        if (time is not { } timeRecord)
            return GetStartOfDay(timeZone, isoDate);

        var isoDateTime = new IsoDateTime(isoDate, timeRecord);
        if (offsetBehaviour == OffsetBehaviour.Wall || (offsetBehaviour == OffsetBehaviour.Option && offsetOption == TemporalOffsetOption.Ignore))
            return GetEpochNanosecondsFor(timeZone, isoDateTime, disambiguation);

        if (offsetBehaviour == OffsetBehaviour.Exact || (offsetBehaviour == OffsetBehaviour.Option && offsetOption == TemporalOffsetOption.Use))
        {
            var balanced = BalanceIsoDateTime(isoDate, (Int128)timeRecord.TotalNanoseconds - offsetNanoseconds);
            CheckIsoDaysRange(balanced.Date);
            var epochNanoseconds = GetUtcEpochNanoseconds(balanced);
            if (!IsValidEpochNanoseconds(epochNanoseconds))
                return ThrowHelper.ThrowRangeError<Int128>("The date and time is outside the range of Temporal");

            return epochNanoseconds;
        }

        CheckIsoDaysRange(isoDate);
        var utcEpochNanoseconds = GetUtcEpochNanoseconds(isoDateTime);
        var possibleEpochNs = GetPossibleEpochNanoseconds(timeZone, isoDateTime);
        for (var i = 0; i < possibleEpochNs.Count; i++)
        {
            var candidate = possibleEpochNs[i];
            var candidateOffset = utcEpochNanoseconds - candidate;
            if (candidateOffset == offsetNanoseconds)
                return candidate;

            if (matchBehaviour == MatchBehaviour.MatchMinutes)
            {
                var roundedCandidateNanoseconds = TemporalMath.RoundNumberToIncrement(candidateOffset, 60_000_000_000, TemporalRoundingMode.HalfExpand);
                if (roundedCandidateNanoseconds == offsetNanoseconds)
                    return candidate;
            }
        }

        if (offsetOption == TemporalOffsetOption.Reject)
            return ThrowHelper.ThrowRangeError<Int128>("The offset is not valid for the date and time in the time zone " + timeZone.Identifier);

        return DisambiguatePossibleEpochNanoseconds(possibleEpochNs, timeZone, isoDateTime, disambiguation);
    }

    /// <summary>GetTemporalRelativeToOption(options)</summary>
    public static RelativeToRecord GetTemporalRelativeToOption(JsObject options)
    {
        var value = options.Get("relativeTo");
        if (value.IsUndefined)
            return default;

        var offsetBehaviour = OffsetBehaviour.Option;
        var matchBehaviour = MatchBehaviour.MatchExactly;
        string calendar;
        TemporalTimeZone? timeZone;
        string? offsetString;
        IsoDate isoDate;
        TimeRecord? time;
        if (value.Cell is JsObject obj)
        {
            switch (obj)
            {
                case JsZonedDateTime zonedDateTime:
                    return new RelativeToRecord(null, zonedDateTime);
                case JsPlainDate plainDate:
                    return new RelativeToRecord(plainDate, null);
                case JsPlainDateTime plainDateTime:
                    return new RelativeToRecord(CreateTemporalDate(plainDateTime.IsoDateTime.Date, plainDateTime.Calendar), null);
            }

            calendar = GetTemporalCalendarIdentifierWithIsoDefault(obj);
            var fields = PrepareCalendarFields(calendar, obj, CalendarFieldNames.YearMonthDay | CalendarFieldNames.Time | CalendarFieldNames.Offset | CalendarFieldNames.TimeZone, CalendarFieldNames.None, partial: false);
            var result = InterpretTemporalDateTimeFields(calendar, fields, TemporalOverflow.Constrain);
            timeZone = fields.TimeZone;
            offsetString = fields.OffsetString;
            if (offsetString is null)
            {
                offsetBehaviour = OffsetBehaviour.Wall;
            }

            isoDate = result.Date;
            time = result.Time;
        }
        else
        {
            if (value.Cell is not JsString str)
                return ThrowHelper.ThrowTypeError<RelativeToRecord>("The relativeTo option must be a string or an object");

            var result = TemporalParser.ParseIsoDateTime(str.ToString(), [TemporalParseGoal.ZonedDateTime, TemporalParseGoal.DateTime]);
            offsetString = result.TimeZone.OffsetString;
            var annotation = result.TimeZone.TimeZoneAnnotation;
            if (annotation is null)
            {
                timeZone = null;
            }
            else
            {
                timeZone = ToTemporalTimeZoneIdentifier(JsValue.FromString(annotation));
                if (result.TimeZone.Z)
                {
                    offsetBehaviour = OffsetBehaviour.Exact;
                }
                else if (offsetString is null)
                {
                    offsetBehaviour = OffsetBehaviour.Wall;
                }

                matchBehaviour = MatchBehaviour.MatchMinutes;
                if (offsetString is not null && result.OffsetHasSubMinutePrecision)
                {
                    matchBehaviour = MatchBehaviour.MatchExactly;
                }
            }

            calendar = CanonicalizeCalendar(result.Calendar ?? IsoCalendar);
            isoDate = new IsoDate(result.Year!.Value, result.Month, result.Day);
            time = result.HasTime ? result.Time : null;
        }

        if (timeZone is null)
            return new RelativeToRecord(CreateTemporalDate(isoDate, calendar), null);

        long offsetNs = 0;
        if (offsetBehaviour == OffsetBehaviour.Option)
        {
            offsetNs = ParseDateTimeUtcOffset(offsetString!);
        }

        var epochNanoseconds = InterpretIsoDateTimeOffset(isoDate, time, offsetBehaviour, offsetNs, timeZone, TemporalDisambiguation.Compatible, TemporalOffsetOption.Reject, matchBehaviour);
        return new RelativeToRecord(null, CreateTemporalZonedDateTime(epochNanoseconds, timeZone, calendar));
    }
}
