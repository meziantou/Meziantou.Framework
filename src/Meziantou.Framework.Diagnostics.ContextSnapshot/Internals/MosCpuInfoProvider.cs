using System.Management;

namespace Meziantou.Framework.Diagnostics.ContextSnapshot.Internals;

internal static class MosCpuInfoProvider
{
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    internal static readonly Lazy<CpuInfo> MosCpuInfo = new(Load);

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static CpuInfo Load()
    {
        var processorModelNames = new HashSet<string>(StringComparer.Ordinal);
        uint physicalCoreCount = 0;
        uint logicalCoreCount = 0;
        var processorsCount = 0;
        uint nominalClockSpeed = 0;
        uint maxClockSpeed = 0;
        uint minClockSpeed = 0;

        using (var mosProcessor = new ManagementObjectSearcher("SELECT * FROM Win32_Processor"))
        {
            foreach (var moProcessor in mosProcessor.Get().Cast<ManagementObject>())
            {
                var name = moProcessor[WmicCpuInfoKeyNames.Name]?.ToString();
                if (!string.IsNullOrEmpty(name))
                {
                    processorModelNames.Add(name);
                    processorsCount++;
                    physicalCoreCount += (uint)moProcessor[WmicCpuInfoKeyNames.NumberOfCores];
                    logicalCoreCount += (uint)moProcessor[WmicCpuInfoKeyNames.NumberOfLogicalProcessors];
                    maxClockSpeed = (uint)moProcessor[WmicCpuInfoKeyNames.MaxClockSpeed];
                }
            }
        }

        return new CpuInfo(
            processorModelNames.Count > 0 ? string.Join(", ", processorModelNames) : null,
            processorsCount > 0 ? processorsCount : null,
            physicalCoreCount > 0 ? (int?)physicalCoreCount : null,
            logicalCoreCount > 0 ? (int?)logicalCoreCount : null,
            nominalClockSpeed > 0 && logicalCoreCount > 0 ? Frequency.FromMHz(nominalClockSpeed) : null,
            minClockSpeed > 0 && logicalCoreCount > 0 ? Frequency.FromMHz(minClockSpeed) : null,
            maxClockSpeed > 0 && logicalCoreCount > 0 ? Frequency.FromMHz(maxClockSpeed) : null);
    }
}
