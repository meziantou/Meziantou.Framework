using Meziantou.Framework.Diagnostics.ContextSnapshot.Internals;

namespace Meziantou.Framework.Diagnostics.ContextSnapshot;

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

    public string? ProcessorName { get; }
    public int? PhysicalProcessorCount { get; }
    public int? PhysicalCoreCount { get; }
    public int? LogicalCoreCount { get; }
    public double? NominalFrequency { get; }
    public double? MinFrequency { get; }
    public double? MaxFrequency { get; }
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
