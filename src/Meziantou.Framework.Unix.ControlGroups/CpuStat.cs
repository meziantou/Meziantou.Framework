namespace Meziantou.Framework.Unix.ControlGroups;

/// <summary>Represents CPU statistics for a cgroup.</summary>
public sealed class CpuStat
{
    /// <summary>Total CPU time used by all tasks in the cgroup (in microseconds).</summary>
    public long UsageMicroseconds { get; private set; }

    /// <summary>CPU time spent in user mode (in microseconds).</summary>
    public long UserMicroseconds { get; private set; }

    /// <summary>CPU time spent in system mode (in microseconds).</summary>
    public long SystemMicroseconds { get; private set; }

    /// <summary>Number of periods where the cgroup was scheduled.</summary>
    public long? NumberOfPeriods { get; private set; }

    /// <summary>Number of periods where the cgroup was throttled.</summary>
    public long? NumberOfThrottled { get; private set; }

    /// <summary>Total time the cgroup was throttled (in microseconds).</summary>
    public long? ThrottledMicroseconds { get; private set; }

    /// <summary>Number of burst periods.</summary>
    public long? NumberOfBursts { get; private set; }

    /// <summary>Total burst time (in microseconds).</summary>
    public long? BurstMicroseconds { get; private set; }

    internal static CpuStat Parse(string content)
    {
        var stat = new CpuStat();

        foreach (var line in content.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
                continue;

            var key = parts[0];
            var valueStr = parts[1];

            if (!long.TryParse(valueStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                continue;

            switch (key)
            {
                case "usage_usec":
                    stat.UsageMicroseconds = value;
                    break;
                case "user_usec":
                    stat.UserMicroseconds = value;
                    break;
                case "system_usec":
                    stat.SystemMicroseconds = value;
                    break;
                case "nr_periods":
                    stat.NumberOfPeriods = value;
                    break;
                case "nr_throttled":
                    stat.NumberOfThrottled = value;
                    break;
                case "throttled_usec":
                    stat.ThrottledMicroseconds = value;
                    break;
                case "nr_bursts":
                    stat.NumberOfBursts = value;
                    break;
                case "burst_usec":
                    stat.BurstMicroseconds = value;
                    break;
            }
        }

        return stat;
    }
}
