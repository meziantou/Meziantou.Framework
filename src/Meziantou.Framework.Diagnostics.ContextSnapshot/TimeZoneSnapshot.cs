namespace Meziantou.Framework.Diagnostics.ContextSnapshot;

/// <summary>Represents a snapshot of time zone information at a specific point in time.</summary>
public sealed class TimeZoneSnapshot
{
    internal TimeZoneSnapshot(TimeZoneInfo timeZoneInfo)
    {
        Id = timeZoneInfo.Id;
        StandardName = timeZoneInfo.StandardName;
        BaseUtcOffset = timeZoneInfo.BaseUtcOffset;
        SupportsDaylightSavingTime = timeZoneInfo.SupportsDaylightSavingTime;
    }

    /// <summary>Gets the time zone identifier.</summary>
    public string Id { get; }
    /// <summary>Gets the standard time name for this time zone.</summary>
    public string StandardName { get; }
    /// <summary>Gets the time difference between the current time zone's standard time and Coordinated Universal Time (UTC).</summary>
    public TimeSpan BaseUtcOffset { get; }
    /// <summary>Gets a value indicating whether this time zone has any daylight saving time rules.</summary>
    public bool SupportsDaylightSavingTime { get; }

    internal static TimeZoneSnapshot Get()
    {
        var currentTimeZone = TimeZoneInfo.Local;
        return new TimeZoneSnapshot(currentTimeZone);
    }
}
