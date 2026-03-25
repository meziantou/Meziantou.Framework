namespace Meziantou.Framework.NtpClient;

/// <summary>
/// Indicates the leap second warning from the NTP server.
/// </summary>
public enum NtpLeapIndicator
{
    /// <summary>No warning.</summary>
    NoWarning = 0,

    /// <summary>Last minute of the day has 61 seconds.</summary>
    LastMinuteHas61Seconds = 1,

    /// <summary>Last minute of the day has 59 seconds.</summary>
    LastMinuteHas59Seconds = 2,

    /// <summary>Clock is not synchronized (alarm condition).</summary>
    AlarmCondition = 3,
}
