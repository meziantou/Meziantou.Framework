namespace Meziantou.Framework.Mail;

/// <summary>The type of a recurrence pattern ([MS-OXOCAL] 2.2.1.44.1, PatternType).</summary>
public enum PstRecurrencePatternType
{
    /// <summary>Every N days (<see cref="PstRecurrencePattern.Period"/> is a number of minutes).</summary>
    Day = 0x0000,

    /// <summary>On some days of the week (<see cref="PstRecurrencePattern.DaysOfWeek"/>).</summary>
    Week = 0x0001,

    /// <summary>On a day of the month (<see cref="PstRecurrencePattern.DayOfMonth"/>).</summary>
    Month = 0x0002,

    /// <summary>On the Nth occurrence of some days of the week in the month (<see cref="PstRecurrencePattern.DaysOfWeek"/> and <see cref="PstRecurrencePattern.WeekOfMonth"/>).</summary>
    MonthNth = 0x0003,

    /// <summary>On the last day of the month.</summary>
    MonthEnd = 0x0004,

    /// <summary>On a day of the month of the Hijri calendar.</summary>
    HijriMonth = 0x000A,

    /// <summary>On the Nth occurrence of some days of the week in the month of the Hijri calendar.</summary>
    HijriMonthNth = 0x000B,

    /// <summary>On the last day of the month of the Hijri calendar.</summary>
    HijriMonthEnd = 0x000C,
}
