namespace Meziantou.Extensions.Logging;

/// <summary>Specifies how often a new log file is created based on time.</summary>
public enum RollInterval
{
    /// <summary>The log file is never rolled based on time.</summary>
    None,

    /// <summary>A new log file is created every hour.</summary>
    Hourly,

    /// <summary>A new log file is created every day.</summary>
    Daily,

    /// <summary>A new log file is created every month.</summary>
    Monthly,
}
