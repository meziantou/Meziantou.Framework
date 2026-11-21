using System.Globalization;

namespace Meziantou.Framework.Mail;

/// <summary>A date or a date and time of an iCalendar object: a date (<c>VALUE=DATE</c>), a time in UTC (<c>Z</c>), a local time in a time zone (<c>TZID</c>), or a floating time.</summary>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
public readonly record struct CalendarDateTime
{
    /// <summary>Creates a date and time.</summary>
    /// <param name="value">The value: in UTC when its kind is <see cref="DateTimeKind.Utc"/>, a local time otherwise.</param>
    /// <param name="timeZoneId">The identifier of the time zone of a local time (<c>TZID</c>), or <see langword="null"/> for a time in UTC or a floating time.</param>
    /// <param name="isDate">Whether the value is a date, without a time.</param>
    public CalendarDateTime(DateTime value, string? timeZoneId = null, bool isDate = false)
    {
        Value = isDate ? DateTime.SpecifyKind(value.Date, DateTimeKind.Unspecified) : value;
        TimeZoneId = value.Kind == DateTimeKind.Utc || isDate ? null : timeZoneId;
        IsDate = isDate;
    }

    /// <summary>Gets the value.</summary>
    public DateTime Value { get; }

    /// <summary>Gets the identifier of the time zone of a local time, or <see langword="null"/>.</summary>
    public string? TimeZoneId { get; }

    /// <summary>Gets whether the value is a date, without a time (an all-day event).</summary>
    public bool IsDate { get; }

    /// <summary>Gets whether the value is in UTC.</summary>
    public bool IsUtc => Value.Kind == DateTimeKind.Utc;

    /// <summary>Creates a date.</summary>
    /// <param name="date">The date.</param>
    /// <returns>The date.</returns>
    public static CalendarDateTime FromDate(DateOnly date) => new(date.ToDateTime(TimeOnly.MinValue), isDate: true);

    /// <summary>Creates a time in UTC.</summary>
    /// <param name="value">The time; a time that is not in UTC is converted.</param>
    /// <returns>The time.</returns>
    public static CalendarDateTime FromUtc(DateTime value) => new(value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : value, DateTimeKind.Utc));

    /// <summary>Returns the value in UTC: a time in UTC as it is, a local time with the definition of its time zone in the calendar (<c>VTIMEZONE</c>). A date is midnight in UTC.</summary>
    /// <param name="calendar">The calendar that defines the time zones, or <see langword="null"/>.</param>
    /// <returns>The time in UTC, or <see langword="null"/> for a floating time or a time zone the calendar does not define.</returns>
    public DateTime? ToUtc(VCalendar? calendar)
    {
        if (IsUtc)
        {
            return Value;
        }

        if (IsDate)
        {
            return DateTime.SpecifyKind(Value, DateTimeKind.Utc);
        }

        if (TimeZoneId is null || calendar?.FindTimeZone(TimeZoneId) is not { } timeZone || timeZone.GetUtcOffset(Value) is not { } offset)
        {
            return null;
        }

        return DateTime.SpecifyKind(Value - offset, DateTimeKind.Utc);
    }

    /// <summary>Returns the value as it is written in a property.</summary>
    /// <returns>The value: <c>20260115</c>, <c>20260115T090000Z</c> or <c>20260115T090000</c>.</returns>
    public override string ToString()
    {
        if (IsDate)
        {
            return Value.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        }

        return Value.ToString("yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture) + (IsUtc ? "Z" : "");
    }

    /// <summary>Reads the value of a property: its value, and its parameters <c>VALUE</c> and <c>TZID</c>.</summary>
    /// <param name="line">The property.</param>
    /// <returns>The value, or <see langword="null"/> when it is not a date or a date and time.</returns>
    public static CalendarDateTime? Parse(ContentLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        return Parse(line.Value, line.GetParameter("TZID"));
    }

    /// <summary>Reads a date or a date and time.</summary>
    /// <param name="value">The value, such as <c>20260115</c>, <c>20260115T090000Z</c> or <c>20260115T090000</c>.</param>
    /// <param name="timeZoneId">The identifier of the time zone of a local time.</param>
    /// <returns>The value, or <see langword="null"/> when it is not a date or a date and time.</returns>
    public static CalendarDateTime? Parse(string value, string? timeZoneId = null)
    {
        ArgumentNullException.ThrowIfNull(value);
        value = value.Trim();
        if (value.Length == 8 && DateTime.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return new CalendarDateTime(date, isDate: true);
        }

        var utc = value is [.., 'Z' or 'z'];
        if (utc)
        {
            value = value[..^1];
        }

        if (!DateTime.TryParseExact(value, "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateTime))
        {
            return null;
        }

        return utc ? new CalendarDateTime(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)) : new CalendarDateTime(dateTime, timeZoneId);
    }

    internal void Apply(ContentLine line)
    {
        line.Value = ToString();
        line.SetParameter("TZID", TimeZoneId);
        line.SetParameter("VALUE", IsDate ? "DATE" : null);
    }
}
