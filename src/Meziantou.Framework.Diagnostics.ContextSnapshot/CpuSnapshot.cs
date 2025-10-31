using Meziantou.Framework.Diagnostics.ContextSnapshot.Internals;

namespace Meziantou.Framework.Diagnostics.ContextSnapshot;

/// <summary>Represents a snapshot of CPU information at a specific point in time.</summary>
public sealed class CpuSnapshot
{
    private CpuSnapshot(CpuInfo info)
    {
        if (info is not null)
        {
            ProcessorName = info.ProcessorName;
            PhysicalProcessorCount = info.PhysicalProcessorCount;
            PhysicalCoreCount = info.PhysicalCoreCount;
            LogicalCoreCount = info.LogicalCoreCount;
            NominalFrequency = info.NominalFrequency;
            MinFrequency = info.MinFrequency;
            MaxFrequency = info.MaxFrequency;
        }
    }

    /// <summary>Gets the processor name.</summary>
    public string? ProcessorName { get; }
    /// <summary>Gets the number of physical processors.</summary>
    public int? PhysicalProcessorCount { get; }
    /// <summary>Gets the number of physical CPU cores.</summary>
    public int? PhysicalCoreCount { get; }
    /// <summary>Gets the number of logical CPU cores.</summary>
    public int? LogicalCoreCount { get; }
    /// <summary>Gets the nominal frequency of the CPU in MHz.</summary>
    public double? NominalFrequency { get; }
    /// <summary>Gets the minimum frequency of the CPU in MHz.</summary>
    public double? MinFrequency { get; }
    /// <summary>Gets the maximum frequency of the CPU in MHz.</summary>
    public double? MaxFrequency { get; }
    /// <summary>Gets hardware intrinsics support information for the CPU.</summary>
    public HardwareIntrinsicsSnapshot? HardwareIntrinsics { get; } = new HardwareIntrinsicsSnapshot();

    internal static CpuSnapshot Get()
    {
        var cpuInfo = GetCpuInfo();
        return new(cpuInfo);
    }

    private static CpuInfo? GetCpuInfo()
    {
        if (OperatingSystem.IsWindows() && DotnetRuntimeSnapshot.IsFullFramework && !DotnetRuntimeSnapshot.IsMonoStatic)
            return MosCpuInfoProvider.MosCpuInfo.Value;
        if (OperatingSystem.IsWindows())
            return WmicCpuInfoProvider.WmicCpuInfo.Value;
        if (OperatingSystem.IsLinux())
            return ProcCpuInfoProvider.ProcCpuInfo.Value;
        if (OperatingSystem.IsMacOS())
            return SysctlCpuInfoProvider.SysctlCpuInfo.Value;

        return null;
    }
}
