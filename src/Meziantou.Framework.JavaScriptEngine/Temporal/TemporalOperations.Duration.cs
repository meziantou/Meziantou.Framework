using System.Numerics;

namespace Meziantou.Framework.JavaScriptEngine;

internal static partial class TemporalOperations
{
    private const double TwoPow32 = 4_294_967_296;
    private const double MaxTimeDurationSeconds = 9_007_199_254_740_992;

    // ------------------------------------------------------------------------------------------------------------------
    // Validity and creation
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>IsValidDuration(years, months, weeks, days, hours, minutes, seconds, milliseconds, microseconds, nanoseconds)</summary>
    public static bool IsValidDuration(double years, double months, double weeks, double days, double hours, double minutes, double seconds, double milliseconds, double microseconds, double nanoseconds)
    {
        var sign = 0;
        if (!CheckSign(years, ref sign) || !CheckSign(months, ref sign) || !CheckSign(weeks, ref sign) || !CheckSign(days, ref sign) || !CheckSign(hours, ref sign)
            || !CheckSign(minutes, ref sign) || !CheckSign(seconds, ref sign) || !CheckSign(milliseconds, ref sign) || !CheckSign(microseconds, ref sign) || !CheckSign(nanoseconds, ref sign))
        {
            return false;
        }

        if (Math.Abs(years) >= TwoPow32 || Math.Abs(months) >= TwoPow32 || Math.Abs(weeks) >= TwoPow32)
            return false;

        // Each term has the same sign: a term larger than the limit makes the duration invalid (and avoids an overflow)
        if (Math.Abs(days) * 86_400 >= MaxTimeDurationSeconds * 2
            || Math.Abs(hours) * 3_600 >= MaxTimeDurationSeconds * 2
            || Math.Abs(minutes) * 60 >= MaxTimeDurationSeconds * 2
            || Math.Abs(seconds) >= MaxTimeDurationSeconds * 2
            || Math.Abs(milliseconds) / 1e3 >= MaxTimeDurationSeconds * 2
            || Math.Abs(microseconds) / 1e6 >= MaxTimeDurationSeconds * 2
            || Math.Abs(nanoseconds) / 1e9 >= MaxTimeDurationSeconds * 2)
        {
            return false;
        }

        var normalizedNanoseconds = TemporalMath.ToInt128(days) * TemporalMath.NsPerDay
            + TemporalMath.ToInt128(hours) * 3_600_000_000_000
            + TemporalMath.ToInt128(minutes) * 60_000_000_000
            + TemporalMath.ToInt128(seconds) * 1_000_000_000
            + TemporalMath.ToInt128(milliseconds) * 1_000_000
            + TemporalMath.ToInt128(microseconds) * 1_000
            + TemporalMath.ToInt128(nanoseconds);
        return Int128.Abs(normalizedNanoseconds) <= TemporalMath.MaxTimeDuration;

        static bool CheckSign(double value, ref int sign)
        {
            if (value < 0)
            {
                if (sign > 0)
                    return false;

                sign = -1;
            }
            else if (value > 0)
            {
                if (sign < 0)
                    return false;

                sign = 1;
            }

            return true;
        }
    }

    /// <summary>CreateTemporalDuration(years, months, weeks, days, hours, minutes, seconds, milliseconds, microseconds, nanoseconds, newTarget)</summary>
    public static JsDuration CreateTemporalDuration(double years, double months, double weeks, double days, double hours, double minutes, double seconds, double milliseconds, double microseconds, double nanoseconds, JsObject? newTarget = null)
    {
        if (!IsValidDuration(years, months, weeks, days, hours, minutes, seconds, milliseconds, microseconds, nanoseconds))
            return ThrowHelper.ThrowRangeError<JsDuration>("The duration is not valid");

        return new JsDuration(GetPrototype(newTarget, i => i.TemporalDurationPrototype), years, months, weeks, days, hours, minutes, seconds, milliseconds, microseconds, nanoseconds);
    }

    public static JsDuration CreateZeroDuration() => CreateTemporalDuration(0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    /// <summary>CreateNegatedTemporalDuration(duration)</summary>
    public static JsDuration CreateNegatedTemporalDuration(JsDuration duration)
    {
        return CreateTemporalDuration(-duration.Years, -duration.Months, -duration.Weeks, -duration.Days, -duration.Hours, -duration.Minutes, -duration.Seconds, -duration.Milliseconds, -duration.Microseconds, -duration.Nanoseconds);
    }

    /// <summary>DurationSign(duration)</summary>
    public static int DurationSign(JsDuration duration)
    {
        ReadOnlySpan<double> values = [duration.Years, duration.Months, duration.Weeks, duration.Days, duration.Hours, duration.Minutes, duration.Seconds, duration.Milliseconds, duration.Microseconds, duration.Nanoseconds];
        foreach (var value in values)
        {
            if (value < 0)
                return -1;

            if (value > 0)
                return 1;
        }

        return 0;
    }

    /// <summary>DefaultTemporalLargestUnit(duration)</summary>
    public static TemporalUnit DefaultTemporalLargestUnit(JsDuration duration)
    {
        if (duration.Years != 0)
            return TemporalUnit.Year;

        if (duration.Months != 0)
            return TemporalUnit.Month;

        if (duration.Weeks != 0)
            return TemporalUnit.Week;

        if (duration.Days != 0)
            return TemporalUnit.Day;

        if (duration.Hours != 0)
            return TemporalUnit.Hour;

        if (duration.Minutes != 0)
            return TemporalUnit.Minute;

        if (duration.Seconds != 0)
            return TemporalUnit.Second;

        if (duration.Milliseconds != 0)
            return TemporalUnit.Millisecond;

        if (duration.Microseconds != 0)
            return TemporalUnit.Microsecond;

        return TemporalUnit.Nanosecond;
    }

    /// <summary>ToTemporalPartialDurationRecord(temporalDurationLike)</summary>
    public static PartialDuration ToTemporalPartialDurationRecord(JsValue temporalDurationLike)
    {
        if (temporalDurationLike.Cell is not JsObject obj)
            return ThrowHelper.ThrowTypeError<PartialDuration>("The duration must be an object");

        var result = default(PartialDuration);
        result.Days = Read(obj, "days");
        result.Hours = Read(obj, "hours");
        result.Microseconds = Read(obj, "microseconds");
        result.Milliseconds = Read(obj, "milliseconds");
        result.Minutes = Read(obj, "minutes");
        result.Months = Read(obj, "months");
        result.Nanoseconds = Read(obj, "nanoseconds");
        result.Seconds = Read(obj, "seconds");
        result.Weeks = Read(obj, "weeks");
        result.Years = Read(obj, "years");
        if (result.Years is null && result.Months is null && result.Weeks is null && result.Days is null && result.Hours is null && result.Minutes is null && result.Seconds is null && result.Milliseconds is null && result.Microseconds is null && result.Nanoseconds is null)
            return ThrowHelper.ThrowTypeError<PartialDuration>("The duration object must have at least one duration property");

        return result;

        static double? Read(JsObject obj, string property)
        {
            var value = obj.Get(property);
            if (value.IsUndefined)
                return null;

            return ToIntegerIfIntegral(value);
        }
    }

    /// <summary>ToTemporalDuration(item)</summary>
    public static JsDuration ToTemporalDuration(JsValue item)
    {
        if (item.Cell is JsDuration duration)
            return CreateTemporalDuration(duration.Years, duration.Months, duration.Weeks, duration.Days, duration.Hours, duration.Minutes, duration.Seconds, duration.Milliseconds, duration.Microseconds, duration.Nanoseconds);

        if (item.Cell is not JsObject)
        {
            if (item.Cell is not JsString str)
                return ThrowHelper.ThrowTypeError<JsDuration>("The value cannot be converted to a Temporal.Duration");

            return ParseTemporalDurationString(str.ToString());
        }

        var partial = ToTemporalPartialDurationRecord(item);
        return CreateTemporalDuration(partial.Years ?? 0, partial.Months ?? 0, partial.Weeks ?? 0, partial.Days ?? 0, partial.Hours ?? 0, partial.Minutes ?? 0, partial.Seconds ?? 0, partial.Milliseconds ?? 0, partial.Microseconds ?? 0, partial.Nanoseconds ?? 0);
    }

    /// <summary>ParseTemporalDurationString(isoString)</summary>
    public static JsDuration ParseTemporalDurationString(string isoString)
    {
        if (!TemporalParser.TryParseDuration(isoString, out var parts))
            return ThrowHelper.ThrowRangeError<JsDuration>("The duration '" + isoString + "' is not valid");

        var yearsMV = DigitsToInteger(parts.Years);
        var monthsMV = DigitsToInteger(parts.Months);
        var weeksMV = DigitsToInteger(parts.Weeks);
        var daysMV = DigitsToInteger(parts.Days);
        var hoursMV = DigitsToInteger(parts.Hours);

        // The fractional part of the smallest unit, in nanoseconds (exact: the units are multiples of 10^9 ns)
        double minutesMV;
        double secondsMV;
        long fractionNanoseconds;
        if (parts.FractionalHours.Length > 0)
        {
            fractionNanoseconds = FractionToNanoseconds(parts.FractionalHours, 3_600_000_000_000);
            minutesMV = fractionNanoseconds / 60_000_000_000;
            fractionNanoseconds %= 60_000_000_000;
            secondsMV = fractionNanoseconds / 1_000_000_000;
            fractionNanoseconds %= 1_000_000_000;
        }
        else
        {
            minutesMV = DigitsToInteger(parts.Minutes);
            if (parts.FractionalMinutes.Length > 0)
            {
                fractionNanoseconds = FractionToNanoseconds(parts.FractionalMinutes, 60_000_000_000);
                secondsMV = fractionNanoseconds / 1_000_000_000;
                fractionNanoseconds %= 1_000_000_000;
            }
            else
            {
                secondsMV = DigitsToInteger(parts.Seconds);
                fractionNanoseconds = parts.FractionalSeconds.Length > 0 ? FractionToNanoseconds(parts.FractionalSeconds, 1_000_000_000) : 0;
            }
        }

        var millisecondsMV = fractionNanoseconds / 1_000_000;
        var microsecondsMV = fractionNanoseconds / 1000 % 1000;
        var nanosecondsMV = fractionNanoseconds % 1000;
        var factor = parts.Negative ? -1 : 1;
        return CreateTemporalDuration(yearsMV * factor, monthsMV * factor, weeksMV * factor, daysMV * factor, hoursMV * factor, minutesMV * factor, secondsMV * factor, millisecondsMV * factor, microsecondsMV * factor, nanosecondsMV * factor);

        static double DigitsToInteger(string digits)
        {
            // ToIntegerWithTruncation(the empty String) = 0
            if (digits.Length == 0)
                return 0;

            var number = NumberParsing.StringToNumber(digits);
            if (!double.IsFinite(number))
                return ThrowHelper.ThrowRangeError<double>("The duration value " + digits + " is too large");

            return number;
        }

        static long FractionToNanoseconds(string fraction, long unitNanoseconds)
        {
            // fraction is the separator followed by 1 to 9 digits
            long digits = 0;
            long scale = 1;
            for (var i = 1; i < fraction.Length; i++)
            {
                digits = (digits * 10) + (fraction[i] - '0');
                scale *= 10;
            }

            return (long)((Int128)digits * unitNanoseconds / scale);
        }
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Internal Duration Records
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>TimeDurationFromComponents(hours, minutes, seconds, milliseconds, microseconds, nanoseconds)</summary>
    public static Int128 TimeDurationFromComponents(double hours, double minutes, double seconds, double milliseconds, double microseconds, double nanoseconds)
    {
        return (TemporalMath.ToInt128(hours) * 3_600_000_000_000)
            + (TemporalMath.ToInt128(minutes) * 60_000_000_000)
            + (TemporalMath.ToInt128(seconds) * 1_000_000_000)
            + (TemporalMath.ToInt128(milliseconds) * 1_000_000)
            + (TemporalMath.ToInt128(microseconds) * 1000)
            + TemporalMath.ToInt128(nanoseconds);
    }

    /// <summary>ToInternalDurationRecord(duration)</summary>
    public static InternalDuration ToInternalDurationRecord(JsDuration duration)
    {
        var dateDuration = new DateDuration((long)duration.Years, (long)duration.Months, (long)duration.Weeks, (long)duration.Days);
        var timeDuration = TimeDurationFromComponents(duration.Hours, duration.Minutes, duration.Seconds, duration.Milliseconds, duration.Microseconds, duration.Nanoseconds);
        return new InternalDuration(dateDuration, timeDuration);
    }

    /// <summary>ToInternalDurationRecordWith24HourDays(duration)</summary>
    public static InternalDuration ToInternalDurationRecordWith24HourDays(JsDuration duration)
    {
        var timeDuration = TimeDurationFromComponents(duration.Hours, duration.Minutes, duration.Seconds, duration.Milliseconds, duration.Microseconds, duration.Nanoseconds);
        timeDuration = Add24HourDaysToTimeDuration(timeDuration, (long)duration.Days);
        var dateDuration = new DateDuration((long)duration.Years, (long)duration.Months, (long)duration.Weeks, 0);
        return new InternalDuration(dateDuration, timeDuration);
    }

    /// <summary>ToDateDurationRecordWithoutTime(duration)</summary>
    public static DateDuration ToDateDurationRecordWithoutTime(JsDuration duration)
    {
        var internalDuration = ToInternalDurationRecordWith24HourDays(duration);
        var days = (long)(internalDuration.Time / TemporalMath.NsPerDay);
        return CreateDateDurationRecord(internalDuration.Date.Years, internalDuration.Date.Months, internalDuration.Date.Weeks, days);
    }

    /// <summary>TemporalDurationFromInternal(internalDuration, largestUnit)</summary>
    public static JsDuration TemporalDurationFromInternal(InternalDuration internalDuration, TemporalUnit largestUnit)
    {
        Int128 days = 0, hours = 0, minutes = 0, seconds = 0, milliseconds = 0, microseconds = 0;
        var sign = Int128.Sign(internalDuration.Time);
        var nanoseconds = Int128.Abs(internalDuration.Time);
        if (largestUnit <= TemporalUnit.Second)
        {
            microseconds = nanoseconds / 1000;
            nanoseconds %= 1000;
            milliseconds = microseconds / 1000;
            microseconds %= 1000;
            seconds = milliseconds / 1000;
            milliseconds %= 1000;
            if (largestUnit <= TemporalUnit.Minute)
            {
                minutes = seconds / 60;
                seconds %= 60;
                if (largestUnit <= TemporalUnit.Hour)
                {
                    hours = minutes / 60;
                    minutes %= 60;
                    if (largestUnit <= TemporalUnit.Day)
                    {
                        days = hours / 24;
                        hours %= 24;
                    }
                }
            }
        }
        else if (largestUnit == TemporalUnit.Millisecond)
        {
            microseconds = nanoseconds / 1000;
            nanoseconds %= 1000;
            milliseconds = microseconds / 1000;
            microseconds %= 1000;
        }
        else if (largestUnit == TemporalUnit.Microsecond)
        {
            microseconds = nanoseconds / 1000;
            nanoseconds %= 1000;
        }

        var date = internalDuration.Date;
        return CreateTemporalDuration(
            date.Years,
            date.Months,
            date.Weeks,
            TemporalMath.ToDouble(date.Days + (days * sign)),
            TemporalMath.ToDouble(hours * sign),
            TemporalMath.ToDouble(minutes * sign),
            TemporalMath.ToDouble(seconds * sign),
            TemporalMath.ToDouble(milliseconds * sign),
            TemporalMath.ToDouble(microseconds * sign),
            TemporalMath.ToDouble(nanoseconds * sign));
    }

    /// <summary>CreateDateDurationRecord(years, months, weeks, days)</summary>
    public static DateDuration CreateDateDurationRecord(long years, long months, long weeks, long days)
    {
        if (!IsValidDuration(years, months, weeks, days, 0, 0, 0, 0, 0, 0))
            return ThrowHelper.ThrowRangeError<DateDuration>("The duration is not valid");

        return new DateDuration(years, months, weeks, days);
    }

    /// <summary>AdjustDateDurationRecord(dateDuration, days, weeks, months)</summary>
    public static DateDuration AdjustDateDurationRecord(DateDuration dateDuration, long days, long? weeks = null, long? months = null)
    {
        return CreateDateDurationRecord(dateDuration.Years, months ?? dateDuration.Months, weeks ?? dateDuration.Weeks, days);
    }

    /// <summary>AddTimeDuration(one, two)</summary>
    public static Int128 AddTimeDuration(Int128 one, Int128 two)
    {
        var result = one + two;
        if (Int128.Abs(result) > TemporalMath.MaxTimeDuration)
            return ThrowHelper.ThrowRangeError<Int128>("The duration is too large");

        return result;
    }

    /// <summary>Add24HourDaysToTimeDuration(d, days)</summary>
    public static Int128 Add24HourDaysToTimeDuration(Int128 d, long days)
    {
        var result = d + ((Int128)days * TemporalMath.NsPerDay);
        if (Int128.Abs(result) > TemporalMath.MaxTimeDuration)
            return ThrowHelper.ThrowRangeError<Int128>("The duration is too large");

        return result;
    }

    /// <summary>RoundTimeDurationToIncrement(d, increment, roundingMode)</summary>
    public static Int128 RoundTimeDurationToIncrement(Int128 d, Int128 increment, TemporalRoundingMode roundingMode)
    {
        var rounded = TemporalMath.RoundNumberToIncrement(d, increment, roundingMode);
        if (Int128.Abs(rounded) > TemporalMath.MaxTimeDuration)
            return ThrowHelper.ThrowRangeError<Int128>("The duration is too large");

        return rounded;
    }

    /// <summary>RoundTimeDuration(timeDuration, increment, unit, roundingMode)</summary>
    public static Int128 RoundTimeDuration(Int128 timeDuration, long increment, TemporalUnit unit, TemporalRoundingMode roundingMode)
    {
        return RoundTimeDurationToIncrement(timeDuration, (Int128)TemporalMath.UnitLengthNs(unit) * increment, roundingMode);
    }

    /// <summary>TotalTimeDuration(timeDuration, unit)</summary>
    public static double TotalTimeDuration(Int128 timeDuration, TemporalUnit unit) => TemporalMath.RationalToDouble((BigInteger)timeDuration, TemporalMath.UnitLengthNs(unit));

    /// <summary>DateDurationDays(dateDuration, plainRelativeTo)</summary>
    public static long DateDurationDays(DateDuration dateDuration, JsPlainDate plainRelativeTo)
    {
        var yearsMonthsWeeksDuration = AdjustDateDurationRecord(dateDuration, 0);
        if (yearsMonthsWeeksDuration.Sign == 0)
            return dateDuration.Days;

        var later = CalendarDateAdd(plainRelativeTo.Calendar, plainRelativeTo.IsoDate, yearsMonthsWeeksDuration, TemporalOverflow.Constrain);
        var epochDays1 = IsoDateToEpochDays(plainRelativeTo.IsoDate);
        var epochDays2 = IsoDateToEpochDays(later);
        return dateDuration.Days + (epochDays2 - epochDays1);
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Rounding relative to a date-time
    // ------------------------------------------------------------------------------------------------------------------

    private static Int128 EpochNanosecondsForDateTime(IsoDateTime dateTime, TemporalTimeZone? timeZone)
    {
        if (timeZone is null)
            return GetUtcEpochNanoseconds(dateTime);

        return GetEpochNanosecondsFor(timeZone, dateTime, TemporalDisambiguation.Compatible);
    }

    /// <summary>ComputeNudgeWindow(sign, duration, originEpochNs, isoDateTime, timeZone, calendar, increment, unit, additionalShift)</summary>
    private static (long R1, long R2, Int128 StartEpochNs, Int128 EndEpochNs, InternalDuration StartDuration, InternalDuration EndDuration) ComputeNudgeWindow(int sign, InternalDuration duration, Int128 originEpochNs, IsoDateTime isoDateTime, TemporalTimeZone? timeZone, string calendar, long increment, TemporalUnit unit, bool additionalShift)
    {
        long r1, r2;
        DateDuration startDateDuration, endDateDuration;
        switch (unit)
        {
            case TemporalUnit.Year:
                {
                    var years = TemporalMath.RoundNumberToIncrement(duration.Date.Years, increment, TemporalRoundingMode.Trunc);
                    r1 = additionalShift ? years + (increment * sign) : years;
                    r2 = r1 + (increment * sign);
                    startDateDuration = CreateDateDurationRecord(r1, 0, 0, 0);
                    endDateDuration = CreateDateDurationRecord(r2, 0, 0, 0);
                    break;
                }

            case TemporalUnit.Month:
                {
                    var months = TemporalMath.RoundNumberToIncrement(duration.Date.Months, increment, TemporalRoundingMode.Trunc);
                    r1 = additionalShift ? months + (increment * sign) : months;
                    r2 = r1 + (increment * sign);
                    startDateDuration = AdjustDateDurationRecord(duration.Date, 0, 0, r1);
                    endDateDuration = AdjustDateDurationRecord(duration.Date, 0, 0, r2);
                    break;
                }

            case TemporalUnit.Week:
                {
                    var yearsMonths = AdjustDateDurationRecord(duration.Date, 0, 0);
                    var weeksStart = CalendarDateAdd(calendar, isoDateTime.Date, yearsMonths, TemporalOverflow.Constrain);
                    var weeksEnd = AddDaysToIsoDate(weeksStart, duration.Date.Days);
                    var untilResult = CalendarDateUntil(calendar, weeksStart, weeksEnd, TemporalUnit.Week);
                    var weeks = TemporalMath.RoundNumberToIncrement(duration.Date.Weeks + untilResult.Weeks, increment, TemporalRoundingMode.Trunc);
                    r1 = weeks;
                    r2 = weeks + (increment * sign);
                    startDateDuration = AdjustDateDurationRecord(duration.Date, 0, r1);
                    endDateDuration = AdjustDateDurationRecord(duration.Date, 0, r2);
                    break;
                }

            default:
                {
                    var days = TemporalMath.RoundNumberToIncrement(duration.Date.Days, increment, TemporalRoundingMode.Trunc);
                    r1 = days;
                    r2 = days + (increment * sign);
                    startDateDuration = AdjustDateDurationRecord(duration.Date, r1);
                    endDateDuration = AdjustDateDurationRecord(duration.Date, r2);
                    break;
                }
        }

        // The specification tests r1 = 0, but the start is the origin only when the whole start duration is zero (the
        // larger units of a week or day window are part of it): test262's exact-multiple-of-larger-unit tests
        Int128 startEpochNs;
        if (startDateDuration.Sign == 0)
        {
            startEpochNs = originEpochNs;
        }
        else
        {
            var start = CalendarDateAdd(calendar, isoDateTime.Date, startDateDuration, TemporalOverflow.Constrain);
            startEpochNs = EpochNanosecondsForDateTime(new IsoDateTime(start, isoDateTime.Time), timeZone);
        }

        var end = CalendarDateAdd(calendar, isoDateTime.Date, endDateDuration, TemporalOverflow.Constrain);
        var endEpochNs = EpochNanosecondsForDateTime(new IsoDateTime(end, isoDateTime.Time), timeZone);
        return (r1, r2, startEpochNs, endEpochNs, new InternalDuration(startDateDuration, 0), new InternalDuration(endDateDuration, 0));
    }

    /// <summary>NudgeToCalendarUnit(sign, duration, originEpochNs, destEpochNs, isoDateTime, timeZone, calendar, increment, unit, roundingMode): the total is returned as a fraction.</summary>
    private static (DurationNudgeResult NudgeResult, BigInteger TotalNumerator, BigInteger TotalDenominator) NudgeToCalendarUnit(int sign, InternalDuration duration, Int128 originEpochNs, Int128 destEpochNs, IsoDateTime isoDateTime, TemporalTimeZone? timeZone, string calendar, long increment, TemporalUnit unit, TemporalRoundingMode roundingMode)
    {
        var didExpandCalendarUnit = false;
        var nudgeWindow = ComputeNudgeWindow(sign, duration, originEpochNs, isoDateTime, timeZone, calendar, increment, unit, additionalShift: false);
        var startEpochNs = nudgeWindow.StartEpochNs;
        var endEpochNs = nudgeWindow.EndEpochNs;
        var inWindow = sign == 1 ? startEpochNs <= destEpochNs && destEpochNs <= endEpochNs : endEpochNs <= destEpochNs && destEpochNs <= startEpochNs;
        if (!inWindow)
        {
            nudgeWindow = ComputeNudgeWindow(sign, duration, originEpochNs, isoDateTime, timeZone, calendar, increment, unit, additionalShift: true);
            didExpandCalendarUnit = true;
        }

        var r1 = nudgeWindow.R1;
        var r2 = nudgeWindow.R2;
        startEpochNs = nudgeWindow.StartEpochNs;
        endEpochNs = nudgeWindow.EndEpochNs;
        if (startEpochNs == endEpochNs)
            return ThrowHelper.ThrowRangeError<(DurationNudgeResult, BigInteger, BigInteger)>("The duration cannot be rounded");

        // progress = (destEpochNs - startEpochNs) / (endEpochNs - startEpochNs); total = r1 + progress \u00D7 increment \u00D7 sign
        var progressNumerator = (BigInteger)(destEpochNs - startEpochNs);
        var progressDenominator = (BigInteger)(endEpochNs - startEpochNs);
        var totalNumerator = (r1 * progressDenominator) + (progressNumerator * increment * sign);
        var totalDenominator = progressDenominator;

        var absoluteNumerator = BigInteger.Abs(progressNumerator);
        var absoluteDenominator = BigInteger.Abs(progressDenominator);
        bool roundsToR2;
        if (absoluteNumerator == absoluteDenominator)
        {
            roundsToR2 = true;
        }
        else if (absoluteNumerator.IsZero)
        {
            roundsToR2 = false;
        }
        else
        {
            var unsignedRoundingMode = TemporalMath.GetUnsignedRoundingMode(roundingMode, sign < 0);
            var r1IsEven = (Math.Abs(r1) / increment) % 2 == 0;
            roundsToR2 = TemporalMath.RoundsUp(absoluteNumerator, absoluteDenominator, r1IsEven, unsignedRoundingMode);
        }

        DurationNudgeResult nudgeResult;
        if (roundsToR2)
        {
            didExpandCalendarUnit = true;
            nudgeResult = new DurationNudgeResult(nudgeWindow.EndDuration, endEpochNs, didExpandCalendarUnit);
        }
        else
        {
            nudgeResult = new DurationNudgeResult(nudgeWindow.StartDuration, startEpochNs, didExpandCalendarUnit);
        }

        _ = r2;
        return (nudgeResult, totalNumerator, totalDenominator);
    }

    /// <summary>NudgeToZonedTime(sign, duration, isoDateTime, timeZone, calendar, increment, unit, roundingMode)</summary>
    private static DurationNudgeResult NudgeToZonedTime(int sign, InternalDuration duration, IsoDateTime isoDateTime, TemporalTimeZone timeZone, string calendar, long increment, TemporalUnit unit, TemporalRoundingMode roundingMode)
    {
        var start = CalendarDateAdd(calendar, isoDateTime.Date, duration.Date, TemporalOverflow.Constrain);
        var startDateTime = new IsoDateTime(start, isoDateTime.Time);
        var endDate = AddDaysToIsoDate(start, sign);
        var endDateTime = new IsoDateTime(endDate, isoDateTime.Time);
        var startEpochNs = GetEpochNanosecondsFor(timeZone, startDateTime, TemporalDisambiguation.Compatible);
        var endEpochNs = GetEpochNanosecondsFor(timeZone, endDateTime, TemporalDisambiguation.Compatible);
        var daySpan = endEpochNs - startEpochNs;
        var unitLength = (Int128)TemporalMath.UnitLengthNs(unit);
        var roundedTimeDuration = RoundTimeDurationToIncrement(duration.Time, unitLength * increment, roundingMode);
        var beyondDaySpan = roundedTimeDuration - daySpan;
        bool didRoundBeyondDay;
        long dayDelta;
        Int128 nudgedEpochNs;
        if (Int128.Sign(beyondDaySpan) != -sign)
        {
            didRoundBeyondDay = true;
            dayDelta = sign;
            roundedTimeDuration = RoundTimeDurationToIncrement(beyondDaySpan, unitLength * increment, roundingMode);
            nudgedEpochNs = roundedTimeDuration + endEpochNs;
        }
        else
        {
            didRoundBeyondDay = false;
            dayDelta = 0;
            nudgedEpochNs = roundedTimeDuration + startEpochNs;
        }

        var dateDuration = AdjustDateDurationRecord(duration.Date, duration.Date.Days + dayDelta);
        return new DurationNudgeResult(new InternalDuration(dateDuration, roundedTimeDuration), nudgedEpochNs, didRoundBeyondDay);
    }

    /// <summary>NudgeToDayOrTime(duration, destEpochNs, largestUnit, increment, smallestUnit, roundingMode)</summary>
    private static DurationNudgeResult NudgeToDayOrTime(InternalDuration duration, Int128 destEpochNs, TemporalUnit largestUnit, long increment, TemporalUnit smallestUnit, TemporalRoundingMode roundingMode)
    {
        var timeDuration = Add24HourDaysToTimeDuration(duration.Time, duration.Date.Days);
        var unitLength = (Int128)TemporalMath.UnitLengthNs(smallestUnit);
        var roundedTime = RoundTimeDurationToIncrement(timeDuration, unitLength * increment, roundingMode);
        var diffTime = roundedTime - timeDuration;
        var wholeDays = (long)(timeDuration / TemporalMath.NsPerDay);
        var roundedWholeDays = (long)(roundedTime / TemporalMath.NsPerDay);
        var dayDelta = roundedWholeDays - wholeDays;
        var dayDeltaSign = Math.Sign(dayDelta);
        var didExpandDays = dayDeltaSign == Int128.Sign(timeDuration);
        var nudgedEpochNs = diffTime + destEpochNs;
        long days = 0;
        var remainder = roundedTime;
        if (IsDateUnit(largestUnit))
        {
            days = roundedWholeDays;
            remainder = roundedTime - ((Int128)roundedWholeDays * TemporalMath.NsPerDay);
        }

        var dateDuration = AdjustDateDurationRecord(duration.Date, days);
        return new DurationNudgeResult(new InternalDuration(dateDuration, remainder), nudgedEpochNs, didExpandDays);
    }

    /// <summary>BubbleRelativeDuration(sign, duration, nudgedEpochNs, isoDateTime, timeZone, calendar, largestUnit, smallestUnit)</summary>
    private static InternalDuration BubbleRelativeDuration(int sign, InternalDuration duration, Int128 nudgedEpochNs, IsoDateTime isoDateTime, TemporalTimeZone? timeZone, string calendar, TemporalUnit largestUnit, TemporalUnit smallestUnit)
    {
        if (smallestUnit == largestUnit)
            return duration;

        var unitIndex = (int)smallestUnit - 1;
        var done = false;
        while (unitIndex >= (int)largestUnit && !done)
        {
            var unit = (TemporalUnit)unitIndex;
            if (unit != TemporalUnit.Week || largestUnit == TemporalUnit.Week)
            {
                DateDuration endDuration;
                if (unit == TemporalUnit.Year)
                {
                    endDuration = CreateDateDurationRecord(duration.Date.Years + sign, 0, 0, 0);
                }
                else if (unit == TemporalUnit.Month)
                {
                    endDuration = AdjustDateDurationRecord(duration.Date, 0, 0, duration.Date.Months + sign);
                }
                else
                {
                    endDuration = AdjustDateDurationRecord(duration.Date, 0, duration.Date.Weeks + sign);
                }

                var end = CalendarDateAdd(calendar, isoDateTime.Date, endDuration, TemporalOverflow.Constrain);
                var endEpochNs = EpochNanosecondsForDateTime(new IsoDateTime(end, isoDateTime.Time), timeZone);
                var beyondEndSign = Int128.Sign(nudgedEpochNs - endEpochNs);
                if (beyondEndSign != -sign)
                {
                    duration = new InternalDuration(endDuration, 0);
                }
                else
                {
                    done = true;
                }
            }

            unitIndex--;
        }

        return duration;
    }

    /// <summary>RoundRelativeDuration(duration, originEpochNs, destEpochNs, isoDateTime, timeZone, calendar, largestUnit, increment, smallestUnit, roundingMode)</summary>
    public static InternalDuration RoundRelativeDuration(InternalDuration duration, Int128 originEpochNs, Int128 destEpochNs, IsoDateTime isoDateTime, TemporalTimeZone? timeZone, string calendar, TemporalUnit largestUnit, long increment, TemporalUnit smallestUnit, TemporalRoundingMode roundingMode)
    {
        var irregularLengthUnit = IsCalendarUnit(smallestUnit) || (timeZone is not null && smallestUnit == TemporalUnit.Day);
        var sign = duration.Sign < 0 ? -1 : 1;
        DurationNudgeResult nudgeResult;
        if (irregularLengthUnit)
        {
            nudgeResult = NudgeToCalendarUnit(sign, duration, originEpochNs, destEpochNs, isoDateTime, timeZone, calendar, increment, smallestUnit, roundingMode).NudgeResult;
        }
        else if (timeZone is not null)
        {
            nudgeResult = NudgeToZonedTime(sign, duration, isoDateTime, timeZone, calendar, increment, smallestUnit, roundingMode);
        }
        else
        {
            nudgeResult = NudgeToDayOrTime(duration, destEpochNs, largestUnit, increment, smallestUnit, roundingMode);
        }

        duration = nudgeResult.Duration;
        if (nudgeResult.DidExpandCalendarUnit && smallestUnit != TemporalUnit.Week)
        {
            var startUnit = LargerOfTwoTemporalUnits(smallestUnit, TemporalUnit.Day);
            duration = BubbleRelativeDuration(sign, duration, nudgeResult.NudgedEpochNs, isoDateTime, timeZone, calendar, largestUnit, startUnit);
        }

        return duration;
    }

    /// <summary>TotalRelativeDuration(duration, originEpochNs, destEpochNs, isoDateTime, timeZone, calendar, unit)</summary>
    public static double TotalRelativeDuration(InternalDuration duration, Int128 originEpochNs, Int128 destEpochNs, IsoDateTime isoDateTime, TemporalTimeZone? timeZone, string calendar, TemporalUnit unit)
    {
        if (IsCalendarUnit(unit) || (timeZone is not null && unit == TemporalUnit.Day))
        {
            var sign = duration.Sign < 0 ? -1 : 1;
            var record = NudgeToCalendarUnit(sign, duration, originEpochNs, destEpochNs, isoDateTime, timeZone, calendar, 1, unit, TemporalRoundingMode.Trunc);
            return TemporalMath.RationalToDouble(record.TotalNumerator, record.TotalDenominator);
        }

        var timeDuration = Add24HourDaysToTimeDuration(duration.Time, duration.Date.Days);
        return TotalTimeDuration(timeDuration, unit);
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Differences
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>DifferencePlainDateTimeWithRounding(isoDateTime1, isoDateTime2, calendar, largestUnit, roundingIncrement, smallestUnit, roundingMode)</summary>
    public static InternalDuration DifferencePlainDateTimeWithRounding(IsoDateTime isoDateTime1, IsoDateTime isoDateTime2, string calendar, TemporalUnit largestUnit, long roundingIncrement, TemporalUnit smallestUnit, TemporalRoundingMode roundingMode)
    {
        if (CompareIsoDateTime(isoDateTime1, isoDateTime2) == 0)
            return default;

        if (!IsoDateTimeWithinLimits(isoDateTime1) || !IsoDateTimeWithinLimits(isoDateTime2))
            return ThrowHelper.ThrowRangeError<InternalDuration>("The date and time is outside the range of Temporal");

        var diff = DifferenceIsoDateTime(isoDateTime1, isoDateTime2, calendar, largestUnit);
        if (smallestUnit == TemporalUnit.Nanosecond && roundingIncrement == 1)
            return diff;

        var originEpochNs = GetUtcEpochNanoseconds(isoDateTime1);
        var destEpochNs = GetUtcEpochNanoseconds(isoDateTime2);
        return RoundRelativeDuration(diff, originEpochNs, destEpochNs, isoDateTime1, timeZone: null, calendar, largestUnit, roundingIncrement, smallestUnit, roundingMode);
    }

    /// <summary>DifferencePlainDateTimeWithTotal(isoDateTime1, isoDateTime2, calendar, unit)</summary>
    public static double DifferencePlainDateTimeWithTotal(IsoDateTime isoDateTime1, IsoDateTime isoDateTime2, string calendar, TemporalUnit unit)
    {
        if (CompareIsoDateTime(isoDateTime1, isoDateTime2) == 0)
            return 0;

        if (!IsoDateTimeWithinLimits(isoDateTime1) || !IsoDateTimeWithinLimits(isoDateTime2))
            return ThrowHelper.ThrowRangeError<double>("The date and time is outside the range of Temporal");

        var diff = DifferenceIsoDateTime(isoDateTime1, isoDateTime2, calendar, unit);
        if (unit == TemporalUnit.Nanosecond)
            return TemporalMath.ToDouble(diff.Time);

        var originEpochNs = GetUtcEpochNanoseconds(isoDateTime1);
        var destEpochNs = GetUtcEpochNanoseconds(isoDateTime2);
        return TotalRelativeDuration(diff, originEpochNs, destEpochNs, isoDateTime1, timeZone: null, calendar, unit);
    }

    /// <summary>AddInstant(epochNanoseconds, timeDuration)</summary>
    public static Int128 AddInstant(Int128 epochNanoseconds, Int128 timeDuration)
    {
        var result = epochNanoseconds + timeDuration;
        if (!IsValidEpochNanoseconds(result))
            return ThrowHelper.ThrowRangeError<Int128>("The instant is outside the range of Temporal.Instant");

        return result;
    }

    /// <summary>DifferenceInstant(ns1, ns2, roundingIncrement, smallestUnit, roundingMode)</summary>
    public static InternalDuration DifferenceInstant(Int128 ns1, Int128 ns2, long roundingIncrement, TemporalUnit smallestUnit, TemporalRoundingMode roundingMode)
    {
        var timeDuration = ns2 - ns1;
        timeDuration = RoundTimeDuration(timeDuration, roundingIncrement, smallestUnit, roundingMode);
        return new InternalDuration(default, timeDuration);
    }

    /// <summary>AddZonedDateTime(epochNanoseconds, timeZone, calendar, duration, overflow)</summary>
    public static Int128 AddZonedDateTime(Int128 epochNanoseconds, TemporalTimeZone timeZone, string calendar, InternalDuration duration, TemporalOverflow overflow)
    {
        if (duration.Date.Sign == 0)
            return AddInstant(epochNanoseconds, duration.Time);

        var isoDateTime = GetIsoDateTimeFor(timeZone, epochNanoseconds);
        var addedDate = CalendarDateAdd(calendar, isoDateTime.Date, duration.Date, overflow);
        var intermediateDateTime = new IsoDateTime(addedDate, isoDateTime.Time);
        if (!IsoDateTimeWithinLimits(intermediateDateTime))
            return ThrowHelper.ThrowRangeError<Int128>("The date and time is outside the range of Temporal");

        var intermediateNs = GetEpochNanosecondsFor(timeZone, intermediateDateTime, TemporalDisambiguation.Compatible);
        return AddInstant(intermediateNs, duration.Time);
    }

    /// <summary>DifferenceZonedDateTime(ns1, ns2, timeZone, calendar, largestUnit)</summary>
    public static InternalDuration DifferenceZonedDateTime(Int128 ns1, Int128 ns2, TemporalTimeZone timeZone, string calendar, TemporalUnit largestUnit)
    {
        if (ns1 == ns2)
            return default;

        var startDateTime = GetIsoDateTimeFor(timeZone, ns1);
        var endDateTime = GetIsoDateTimeFor(timeZone, ns2);
        if (CompareIsoDate(startDateTime.Date, endDateTime.Date) == 0)
            return new InternalDuration(default, ns2 - ns1);

        var sign = ns2 - ns1 < 0 ? 1 : -1;
        var maxDayCorrection = sign == -1 ? 2 : 1;
        var dayCorrection = 0;
        var timeDuration = DifferenceTime(startDateTime.Time, endDateTime.Time);
        if (Int128.Sign(timeDuration) == sign)
        {
            dayCorrection++;
        }

        var success = false;
        var intermediateDateTime = default(IsoDateTime);
        while (dayCorrection <= maxDayCorrection && !success)
        {
            var intermediateDate = AddDaysToIsoDate(endDateTime.Date, dayCorrection * sign);
            intermediateDateTime = new IsoDateTime(intermediateDate, startDateTime.Time);
            var intermediateNs = GetEpochNanosecondsFor(timeZone, intermediateDateTime, TemporalDisambiguation.Compatible);
            timeDuration = ns2 - intermediateNs;
            var timeSign = Int128.Sign(timeDuration);
            if (sign != timeSign)
            {
                success = true;
            }

            dayCorrection++;
        }

        var dateLargestUnit = LargerOfTwoTemporalUnits(largestUnit, TemporalUnit.Day);
        var dateDifference = CalendarDateUntil(calendar, startDateTime.Date, intermediateDateTime.Date, dateLargestUnit);
        return new InternalDuration(dateDifference, timeDuration);
    }

    /// <summary>DifferenceZonedDateTimeWithRounding(ns1, ns2, timeZone, calendar, largestUnit, roundingIncrement, smallestUnit, roundingMode)</summary>
    public static InternalDuration DifferenceZonedDateTimeWithRounding(Int128 ns1, Int128 ns2, TemporalTimeZone timeZone, string calendar, TemporalUnit largestUnit, long roundingIncrement, TemporalUnit smallestUnit, TemporalRoundingMode roundingMode)
    {
        if (!IsDateUnit(largestUnit))
            return DifferenceInstant(ns1, ns2, roundingIncrement, smallestUnit, roundingMode);

        var difference = DifferenceZonedDateTime(ns1, ns2, timeZone, calendar, largestUnit);
        if (smallestUnit == TemporalUnit.Nanosecond && roundingIncrement == 1)
            return difference;

        var dateTime = GetIsoDateTimeFor(timeZone, ns1);
        return RoundRelativeDuration(difference, ns1, ns2, dateTime, timeZone, calendar, largestUnit, roundingIncrement, smallestUnit, roundingMode);
    }

    /// <summary>DifferenceZonedDateTimeWithTotal(ns1, ns2, timeZone, calendar, unit)</summary>
    public static double DifferenceZonedDateTimeWithTotal(Int128 ns1, Int128 ns2, TemporalTimeZone timeZone, string calendar, TemporalUnit unit)
    {
        if (!IsDateUnit(unit))
            return TotalTimeDuration(ns2 - ns1, unit);

        var difference = DifferenceZonedDateTime(ns1, ns2, timeZone, calendar, unit);
        var dateTime = GetIsoDateTimeFor(timeZone, ns1);
        return TotalRelativeDuration(difference, ns1, ns2, dateTime, timeZone, calendar, unit);
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Formatting and arithmetic of Temporal.Duration
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>TemporalDurationToString(duration, precision)</summary>
    public static string TemporalDurationToString(JsDuration duration, int precision)
    {
        var sign = DurationSign(duration);
        var sb = new StringBuilder(32);
        if (sign < 0)
        {
            sb.Append('-');
        }

        sb.Append('P');
        AppendComponent(sb, duration.Years, 'Y');
        AppendComponent(sb, duration.Months, 'M');
        AppendComponent(sb, duration.Weeks, 'W');
        AppendComponent(sb, duration.Days, 'D');

        var timePart = new StringBuilder(32);
        AppendComponent(timePart, duration.Hours, 'H');
        AppendComponent(timePart, duration.Minutes, 'M');
        var zeroMinutesAndHigher = DefaultTemporalLargestUnit(duration) >= TemporalUnit.Second;
        var secondsDuration = TimeDurationFromComponents(0, 0, duration.Seconds, duration.Milliseconds, duration.Microseconds, duration.Nanoseconds);
        if (secondsDuration != 0 || zeroMinutesAndHigher || precision != PrecisionAuto)
        {
            var secondsPart = Int128.Abs(secondsDuration / 1_000_000_000);
            var subSeconds = (int)Int128.Abs(secondsDuration % 1_000_000_000);
            timePart.Append(secondsPart.ToString(System.Globalization.CultureInfo.InvariantCulture));
            AppendFractionalSeconds(timePart, subSeconds, precision);
            timePart.Append('S');
        }

        if (timePart.Length > 0)
        {
            sb.Append('T').Append(timePart);
        }

        return sb.ToString();

        static void AppendComponent(StringBuilder sb, double value, char designator)
        {
            if (value != 0)
            {
                sb.Append(TemporalMath.FormatInteger(Math.Abs(value))).Append(designator);
            }
        }
    }

    /// <summary>AddDurations(operation, duration, other)</summary>
    public static JsDuration AddDurations(bool subtract, JsDuration duration, JsValue otherValue)
    {
        var other = ToTemporalDuration(otherValue);
        if (subtract)
        {
            other = CreateNegatedTemporalDuration(other);
        }

        var largestUnit1 = DefaultTemporalLargestUnit(duration);
        var largestUnit2 = DefaultTemporalLargestUnit(other);
        var largestUnit = LargerOfTwoTemporalUnits(largestUnit1, largestUnit2);
        if (IsCalendarUnit(largestUnit))
            return ThrowHelper.ThrowRangeError<JsDuration>("Durations with years, months or weeks cannot be added without a relativeTo");

        var d1 = ToInternalDurationRecordWith24HourDays(duration);
        var d2 = ToInternalDurationRecordWith24HourDays(other);
        var timeResult = AddTimeDuration(d1.Time, d2.Time);
        return TemporalDurationFromInternal(new InternalDuration(default, timeResult), largestUnit);
    }
}
