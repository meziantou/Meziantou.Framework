namespace Meziantou.Framework.Diagnostics.ContextSnapshot;

/// <summary>Represents a snapshot of a time zone including ID, standard name, UTC offset, and daylight saving time support.</summary>
public sealed class TimeZoneSnapshot
{
    internal TimeZoneSnapshot(TimeZoneInfo timeZoneInfo)
    {
        Id = timeZoneInfo.Id;
        StandardName = timeZoneInfo.StandardName;
        BaseUtcOffset = timeZoneInfo.BaseUtcOffset;
        SupportsDaylightSavingTime = timeZoneInfo.SupportsDaylightSavingTime;
    }

    public string Id { get; }
    public string StandardName { get; }
    public TimeSpan BaseUtcOffset { get; }
    public bool SupportsDaylightSavingTime { get; }

    internal static TimeZoneSnapshot Get()
    {
        var currentTimeZone = TimeZoneInfo.Local;
        return new TimeZoneSnapshot(currentTimeZone);
    }
}
