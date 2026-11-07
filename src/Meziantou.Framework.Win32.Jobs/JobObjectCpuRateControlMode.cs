namespace Meziantou.Framework.Win32;

/// <summary>Identifies which CPU rate control policy is active for a job object.</summary>
/// <remarks>
/// The policy determines which member of <see cref="JobObjectCpuHardCap"/> carries a meaningful value.
/// Windows stores those values in a union, so reading the wrong one yields an unrelated number rather
/// than an error.
/// </remarks>
public enum JobObjectCpuRateControlMode
{
    /// <summary>CPU rate control is not enabled for the job.</summary>
    Disabled,

    /// <summary>The job is capped at a fixed portion of processor cycles, reported by <see cref="JobObjectCpuHardCap.Rate"/>.</summary>
    HardCap,

    /// <summary>The job's share of processor time is derived from its weight relative to other jobs, reported by <see cref="JobObjectCpuHardCap.Weight"/>.</summary>
    Weight,

    /// <summary>The job is confined to a range of processor cycles, reported by <see cref="JobObjectCpuHardCap.MinRate"/> and <see cref="JobObjectCpuHardCap.MaxRate"/>.</summary>
    MinMaxRate,
}
