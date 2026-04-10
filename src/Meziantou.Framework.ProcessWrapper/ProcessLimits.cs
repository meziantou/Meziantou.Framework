namespace Meziantou.Framework;

/// <summary>Represents the cross-platform process limits that can be applied by <see cref="ProcessWrapper"/>.</summary>
public sealed class ProcessLimits
{
    /// <summary>Gets or sets the maximum CPU usage in percentage between 1 and 100.</summary>
    public int? CpuPercentage { get; set; }

    /// <summary>Gets or sets the maximum memory usage in bytes.</summary>
    public long? MemoryLimitInBytes { get; set; }

    /// <summary>Gets or sets the maximum number of processes allowed in the process group.</summary>
    public int? ProcessCountLimit { get; set; }

    internal bool HasAnyLimitConfigured => CpuPercentage is not null || MemoryLimitInBytes is not null || ProcessCountLimit is not null;
}
