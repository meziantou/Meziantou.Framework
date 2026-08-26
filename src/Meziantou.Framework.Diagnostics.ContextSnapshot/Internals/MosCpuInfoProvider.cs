using System.Runtime.InteropServices;
using System.Management;

namespace Meziantou.Framework.Diagnostics.ContextSnapshot.Internals;

/// <summary>
/// CPU information from the <c>Win32_Processor</c> WMI class. Used as a fallback when
/// <see cref="WindowsCpuInfoProvider"/> cannot query the operating system.
/// Windows only.
/// </summary>
internal static class MosCpuInfoProvider
{
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    internal static readonly Lazy<CpuInfo?> MosCpuInfo = new(Load);

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static CpuInfo? Load()
    {
        var processorModelNames = new HashSet<string>(StringComparer.Ordinal);
        uint physicalCoreCount = 0;
        uint logicalCoreCount = 0;
        var processorsCount = 0;
        uint nominalClockSpeed = 0;
        uint maxClockSpeed = 0;
        uint minClockSpeed = 0;

        try
        {
            using var mosProcessor = new ManagementObjectSearcher("SELECT * FROM Win32_Processor");
            foreach (var moProcessor in mosProcessor.Get().Cast<ManagementObject>())
            {
                var name = moProcessor[Win32ProcessorKeyNames.Name]?.ToString();
                if (!string.IsNullOrEmpty(name))
                {
                    processorModelNames.Add(name);
                    processorsCount++;
                    physicalCoreCount += (uint)moProcessor[Win32ProcessorKeyNames.NumberOfCores];
                    logicalCoreCount += (uint)moProcessor[Win32ProcessorKeyNames.NumberOfLogicalProcessors];
                    maxClockSpeed = (uint)moProcessor[Win32ProcessorKeyNames.MaxClockSpeed];
                }
            }
        }
        catch (ManagementException)
        {
            return null;
        }
        catch (COMException)
        {
            return null;
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
