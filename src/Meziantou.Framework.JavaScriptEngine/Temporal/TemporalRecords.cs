using System.Runtime.InteropServices;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>The Temporal units (Table 21), from the largest to the smallest, then the special values auto and unset.</summary>
internal enum TemporalUnit : byte
{
    Year,
    Month,
    Week,
    Day,
    Hour,
    Minute,
    Second,
    Millisecond,
    Microsecond,
    Nanosecond,
    Auto,
    Unset,
}

/// <summary>The rounding modes (Table 28).</summary>
internal enum TemporalRoundingMode : byte
{
    Ceil,
    Floor,
    Expand,
    Trunc,
    HalfCeil,
    HalfFloor,
    HalfExpand,
    HalfTrunc,
    HalfEven,
}

/// <summary>The unsigned rounding modes (Table 22).</summary>
internal enum UnsignedRoundingMode : byte
{
    Zero,
    Infinity,
    HalfZero,
    HalfInfinity,
    HalfEven,
}

internal enum TemporalOverflow : byte
{
    Constrain,
    Reject,
}

internal enum TemporalDisambiguation : byte
{
    Compatible,
    Earlier,
    Later,
    Reject,
}

internal enum TemporalOffsetOption : byte
{
    Prefer,
    Use,
    Ignore,
    Reject,
}

internal enum ShowCalendarOption : byte
{
    Auto,
    Always,
    Never,
    Critical,
}

internal enum ShowTimeZoneOption : byte
{
    Auto,
    Never,
    Critical,
}

internal enum OffsetBehaviour : byte
{
    Option,
    Exact,
    Wall,
}

internal enum MatchBehaviour : byte
{
    MatchExactly,
    MatchMinutes,
}

internal enum TemporalUnitGroup : byte
{
    Date,
    Time,
    DateTime,
}

internal enum DifferenceOperation : byte
{
    Until,
    Since,
}

internal enum CalendarFieldsType : byte
{
    Date,
    YearMonth,
    MonthDay,
}

/// <summary>The fields of a Calendar Fields Record (Table 19), as enumeration keys.</summary>
[Flags]
internal enum CalendarFieldNames
{
    None = 0,
    Era = 1 << 0,
    EraYear = 1 << 1,
    Year = 1 << 2,
    Month = 1 << 3,
    MonthCode = 1 << 4,
    Day = 1 << 5,
    Hour = 1 << 6,
    Minute = 1 << 7,
    Second = 1 << 8,
    Millisecond = 1 << 9,
    Microsecond = 1 << 10,
    Nanosecond = 1 << 11,
    Offset = 1 << 12,
    TimeZone = 1 << 13,

    YearMonthDay = Year | Month | MonthCode | Day,
    Time = Hour | Minute | Second | Millisecond | Microsecond | Nanosecond,
}

/// <summary>An ISO Date Record: a valid date of the ISO 8601 calendar (the year may be outside the range of Temporal).</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct IsoDate(long Year, int Month, int Day);

/// <summary>A Time Record: a valid clock time and a number of overflow days.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct TimeRecord(long Days, int Hour, int Minute, int Second, int Millisecond, int Microsecond, int Nanosecond)
{
    public static TimeRecord Midnight => default;

    public static TimeRecord Noon => new(0, 12, 0, 0, 0, 0, 0);

    /// <summary>Gets the nanoseconds since midnight (the [[Days]] field is ignored).</summary>
    public long TotalNanoseconds
    {
        get
        {
            long result = Hour;
            result = (result * 60) + Minute;
            result = (result * 60) + Second;
            result = (result * 1000) + Millisecond;
            result = (result * 1000) + Microsecond;
            return (result * 1000) + Nanosecond;
        }
    }

    public TimeRecord WithoutDays() => this with { Days = 0 };
}

/// <summary>An ISO Date-Time Record.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct IsoDateTime(IsoDate Date, TimeRecord Time);

/// <summary>A Date Duration Record (the fields are float64-representable integers, small enough for a long).</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct DateDuration(long Years, long Months, long Weeks, long Days)
{
    public int Sign
    {
        get
        {
            if (Years != 0)
                return Math.Sign(Years);

            if (Months != 0)
                return Math.Sign(Months);

            if (Weeks != 0)
                return Math.Sign(Weeks);

            return Math.Sign(Days);
        }
    }
}

/// <summary>An Internal Duration Record: a date duration and a time duration (nanoseconds).</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct InternalDuration(DateDuration Date, Int128 Time)
{
    public int Sign
    {
        get
        {
            var dateSign = Date.Sign;
            return dateSign != 0 ? dateSign : Int128.Sign(Time);
        }
    }
}

/// <summary>A Duration Nudge Result Record.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct DurationNudgeResult(InternalDuration Duration, Int128 NudgedEpochNs, bool DidExpandCalendarUnit);

/// <summary>The record returned by GetDifferenceSettings.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct DifferenceSettings(TemporalUnit SmallestUnit, TemporalUnit LargestUnit, TemporalRoundingMode RoundingMode, int RoundingIncrement);

/// <summary>The record returned by ToSecondsStringPrecisionRecord. Precision: 0-9, <see cref="TemporalOperations.PrecisionAuto"/> or <see cref="TemporalOperations.PrecisionMinute"/>.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct SecondsStringPrecision(int Precision, TemporalUnit Unit, int Increment);

/// <summary>A Calendar Fields Record (Table 19): null is unset.</summary>
internal sealed class CalendarFields
{
    public string? Era { get; set; }

    public double? EraYear { get; set; }

    public double? Year { get; set; }

    public double? Month { get; set; }

    public string? MonthCode { get; set; }

    public double? Day { get; set; }

    public double? Hour { get; set; }

    public double? Minute { get; set; }

    public double? Second { get; set; }

    public double? Millisecond { get; set; }

    public double? Microsecond { get; set; }

    public double? Nanosecond { get; set; }

    public string? OffsetString { get; set; }

    public TemporalTimeZone? TimeZone { get; set; }

    public CalendarFields Clone() => (CalendarFields)MemberwiseClone();

    public void SetTime(TimeRecord time)
    {
        Hour = time.Hour;
        Minute = time.Minute;
        Second = time.Second;
        Millisecond = time.Millisecond;
        Microsecond = time.Microsecond;
        Nanosecond = time.Nanosecond;
    }
}

/// <summary>The fields of a time-like object (TemporalTimeLike Record): NaN is unset.</summary>
[StructLayout(LayoutKind.Auto)]
internal struct TemporalTimeLike
{
    public double Hour;
    public double Minute;
    public double Second;
    public double Millisecond;
    public double Microsecond;
    public double Nanosecond;
}

/// <summary>A partial Duration Record: null is undefined.</summary>
[StructLayout(LayoutKind.Auto)]
internal struct PartialDuration
{
    public double? Years;
    public double? Months;
    public double? Weeks;
    public double? Days;
    public double? Hours;
    public double? Minutes;
    public double? Seconds;
    public double? Milliseconds;
    public double? Microseconds;
    public double? Nanoseconds;
}

/// <summary>An ISO String Time Zone Parse Record.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct IsoStringTimeZone(bool Z, string? OffsetString, string? TimeZoneAnnotation);

/// <summary>An ISO Date-Time Parse Record. <see cref="HasTime"/> is false for start-of-day; <see cref="Year"/> is null when it is absent.</summary>
internal sealed class IsoDateTimeParseResult
{
    public long? Year { get; set; }

    public int Month { get; set; }

    public int Day { get; set; }

    public bool HasTime { get; set; }

    public TimeRecord Time { get; set; }

    public IsoStringTimeZone TimeZone { get; set; }

    public string? Calendar { get; set; }

    /// <summary>Gets or sets whether the UTC offset has seconds (more than one MinuteSecond Parse Node).</summary>
    public bool OffsetHasSubMinutePrecision { get; set; }
}

/// <summary>A Time Zone Identifier Parse Record: either a name or an offset in minutes.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct TimeZoneIdentifierParseResult(string? Name, int OffsetMinutes)
{
    public bool IsOffset => Name is null;
}

/// <summary>A Calendar Date Record (Table 18), for the ISO 8601 calendar.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct CalendarDate(long Year, int Month, string MonthCode, int Day, int DayOfWeek, int DayOfYear, int WeekOfYear, long YearOfWeek, int DaysInWeek, int DaysInMonth, int DaysInYear, int MonthsInYear, bool InLeapYear);

/// <summary>The relativeTo option (GetTemporalRelativeToOption).</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct RelativeToRecord(JsPlainDate? PlainRelativeTo, JsZonedDateTime? ZonedRelativeTo);
