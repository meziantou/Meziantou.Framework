using Meziantou.Framework.JavaScriptEngine.TimeZones;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// An available time zone identifier: an offset time zone (e.g. "+05:30") or an available named time zone of the IANA
/// time zone database (the identifier keeps the case of the database, links are not canonicalized).
/// </summary>
internal sealed class TemporalTimeZone
{
    private static TemporalTimeZone? s_utc;

    private TemporalTimeZone(string identifier, int offsetMinutes, NamedTimeZone? zone, string primaryIdentifier)
    {
        Identifier = identifier;
        OffsetMinutes = offsetMinutes;
        Zone = zone;
        PrimaryIdentifier = primaryIdentifier;
    }

    /// <summary>Gets the identifier, in normalized format.</summary>
    public string Identifier { get; }

    /// <summary>Gets the offset of an offset time zone, in minutes.</summary>
    public int OffsetMinutes { get; }

    /// <summary>Gets the zone of a named time zone, or null for an offset time zone.</summary>
    public NamedTimeZone? Zone { get; }

    /// <summary>Gets the primary identifier of a named time zone (the identifier for an offset time zone).</summary>
    public string PrimaryIdentifier { get; }

    public bool IsOffset => Zone is null;

    public static TemporalTimeZone Utc => s_utc ??= FromNamed(TimeZoneDatabase.Default.TryGetTimeZone("UTC", out var record) ? record : throw new InvalidOperationException("The time zone database does not contain UTC"));

    public static TemporalTimeZone FromOffset(int offsetMinutes) => new(TemporalOperations.FormatOffsetTimeZoneIdentifier(offsetMinutes), offsetMinutes, zone: null, TemporalOperations.FormatOffsetTimeZoneIdentifier(offsetMinutes));

    public static TemporalTimeZone FromNamed(TimeZoneIdentifier record) => new(record.Identifier, 0, record.Zone, record.PrimaryIdentifier);

    /// <inheritdoc/>
    public override string ToString() => Identifier;
}

/// <summary>A Temporal.PlainDate ([[InitializedTemporalDate]]).</summary>
internal sealed class JsPlainDate(JsObject? prototype, IsoDate isoDate, string calendar) : JsObject(prototype, ObjectClass.Temporal)
{
    public IsoDate IsoDate { get; } = isoDate;

    public string Calendar { get; } = calendar;
}

/// <summary>A Temporal.PlainTime ([[InitializedTemporalTime]]).</summary>
internal sealed class JsPlainTime(JsObject? prototype, TimeRecord time) : JsObject(prototype, ObjectClass.Temporal)
{
    public TimeRecord Time { get; } = time;
}

/// <summary>A Temporal.PlainDateTime ([[InitializedTemporalDateTime]]).</summary>
internal sealed class JsPlainDateTime(JsObject? prototype, IsoDateTime isoDateTime, string calendar) : JsObject(prototype, ObjectClass.Temporal)
{
    public IsoDateTime IsoDateTime { get; } = isoDateTime;

    public string Calendar { get; } = calendar;
}

/// <summary>A Temporal.PlainYearMonth ([[InitializedTemporalYearMonth]]).</summary>
internal sealed class JsPlainYearMonth(JsObject? prototype, IsoDate isoDate, string calendar) : JsObject(prototype, ObjectClass.Temporal)
{
    public IsoDate IsoDate { get; } = isoDate;

    public string Calendar { get; } = calendar;
}

/// <summary>A Temporal.PlainMonthDay ([[InitializedTemporalMonthDay]]).</summary>
internal sealed class JsPlainMonthDay(JsObject? prototype, IsoDate isoDate, string calendar) : JsObject(prototype, ObjectClass.Temporal)
{
    public IsoDate IsoDate { get; } = isoDate;

    public string Calendar { get; } = calendar;
}

/// <summary>A Temporal.Instant ([[InitializedTemporalInstant]]).</summary>
internal sealed class JsInstant(JsObject? prototype, Int128 epochNanoseconds) : JsObject(prototype, ObjectClass.Temporal)
{
    public Int128 EpochNanoseconds { get; } = epochNanoseconds;
}

/// <summary>A Temporal.ZonedDateTime ([[InitializedTemporalZonedDateTime]]).</summary>
internal sealed class JsZonedDateTime(JsObject? prototype, Int128 epochNanoseconds, TemporalTimeZone timeZone, string calendar) : JsObject(prototype, ObjectClass.Temporal)
{
    public Int128 EpochNanoseconds { get; } = epochNanoseconds;

    public TemporalTimeZone TimeZone { get; } = timeZone;

    public string Calendar { get; } = calendar;
}

/// <summary>A Temporal.Duration ([[InitializedTemporalDuration]]): each field is a float64-representable integer.</summary>
internal sealed class JsDuration : JsObject
{
    public JsDuration(JsObject? prototype, double years, double months, double weeks, double days, double hours, double minutes, double seconds, double milliseconds, double microseconds, double nanoseconds)
        : base(prototype, ObjectClass.Temporal)
    {
        // +0 instead of -0
        Years = years + 0.0;
        Months = months + 0.0;
        Weeks = weeks + 0.0;
        Days = days + 0.0;
        Hours = hours + 0.0;
        Minutes = minutes + 0.0;
        Seconds = seconds + 0.0;
        Milliseconds = milliseconds + 0.0;
        Microseconds = microseconds + 0.0;
        Nanoseconds = nanoseconds + 0.0;
    }

    public double Years { get; }

    public double Months { get; }

    public double Weeks { get; }

    public double Days { get; }

    public double Hours { get; }

    public double Minutes { get; }

    public double Seconds { get; }

    public double Milliseconds { get; }

    public double Microseconds { get; }

    public double Nanoseconds { get; }
}
