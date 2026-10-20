using Meziantou.Framework.Diagnostics.ContextSnapshot.Internals;

namespace Meziantou.Framework.Diagnostics.ContextSnapshot;

/// <summary>Represents a snapshot of CPU information including processor name, core counts, frequencies, and hardware intrinsics support.</summary>
/// <example>
/// Get CPU information:
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.Diagnostics.ContextSnapshot.Tests/SnapshotTests.cs" ref:region="CpuSnapshot_AddCpu" ref:dedent="true">
/// var snapshot = new ContextSnapshotBuilder().AddCpu().BuildSnapshot();
/// var cpuInfo = Assert.IsType&lt;CpuSnapshot&gt;(snapshot["CPU"]);
///
/// // A value is null when it is not available on the current platform
/// Assert.NotEqual("", cpuInfo.ProcessorName);
/// Assert.True(cpuInfo.PhysicalCoreCount is null or &gt; 0);
/// Assert.True(cpuInfo.LogicalCoreCount is null or &gt; 0);
/// Assert.True(cpuInfo.MaxFrequency is null or &gt; 0); // in hertz
/// </code>
/// </example>
public sealed class CpuSnapshot
{
    private CpuSnapshot(CpuInfo? info)
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
        if (OperatingSystem.IsWindows())
            return WindowsCpuInfoProvider.WindowsCpuInfo.Value;
        if (OperatingSystem.IsLinux())
            return ProcCpuInfoProvider.ProcCpuInfo.Value;
        if (OperatingSystem.IsMacOS())
            return SysctlCpuInfoProvider.SysctlCpuInfo.Value;

        return null;
    }
}
