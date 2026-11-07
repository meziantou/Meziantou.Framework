namespace Meziantou.Framework.Win32;

/// <summary>Represents the CPU rate control settings of a job object.</summary>
/// <remarks>
/// Only the members that belong to <see cref="Mode"/> carry a meaningful value; the others are zero.
/// Check <see cref="Mode"/> before reading <see cref="Rate"/>, <see cref="Weight"/>,
/// <see cref="MinRate"/> or <see cref="MaxRate"/>.
/// </remarks>
public readonly record struct JobObjectCpuHardCap
{
    /// <summary>Gets a value indicating whether CPU rate control is enabled for the job.</summary>
    public bool Enabled { get; init; }

    /// <summary>Gets the CPU rate control policy the job is using.</summary>
    public JobObjectCpuRateControlMode Mode { get; init; }

    /// <summary>Gets the portion of processor cycles the job may use, as a percentage times 100. Meaningful when <see cref="Mode"/> is <see cref="JobObjectCpuRateControlMode.HardCap"/>.</summary>
    public int Rate { get; init; }

    /// <summary>Gets the scheduling weight of the job, from 1 to 9. Meaningful when <see cref="Mode"/> is <see cref="JobObjectCpuRateControlMode.Weight"/>.</summary>
    public int Weight { get; init; }

    /// <summary>Gets the minimum portion of processor cycles reserved for the job, as a percentage times 100. Meaningful when <see cref="Mode"/> is <see cref="JobObjectCpuRateControlMode.MinMaxRate"/>.</summary>
    public int MinRate { get; init; }

    /// <summary>Gets the maximum portion of processor cycles the job may use, as a percentage times 100. Meaningful when <see cref="Mode"/> is <see cref="JobObjectCpuRateControlMode.MinMaxRate"/>.</summary>
    public int MaxRate { get; init; }
}
