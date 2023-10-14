namespace Meziantou.Framework.Diagnostics.ContextSnapshot;

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
