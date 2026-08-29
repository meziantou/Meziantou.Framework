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

        try
        {
            using var mosProcessor = new ManagementObjectSearcher("SELECT * FROM Win32_Processor");
            using var processors = mosProcessor.Get();
            foreach (var moProcessor in processors.Cast<ManagementObject>())
            {
                using (moProcessor)
                {
                    var name = GetString(moProcessor, Win32ProcessorKeyNames.Name);
                    if (!string.IsNullOrEmpty(name))
                    {
                        processorModelNames.Add(name);
                        processorsCount++;
                        physicalCoreCount += GetUInt32(moProcessor, Win32ProcessorKeyNames.NumberOfCores);
                        logicalCoreCount += GetUInt32(moProcessor, Win32ProcessorKeyNames.NumberOfLogicalProcessors);
                        nominalClockSpeed = Math.Max(nominalClockSpeed, GetUInt32(moProcessor, Win32ProcessorKeyNames.CurrentClockSpeed));
                        maxClockSpeed = Math.Max(maxClockSpeed, GetUInt32(moProcessor, Win32ProcessorKeyNames.MaxClockSpeed));
                    }
                }
            }
        }
        catch (Exception)
        {
            // Best-effort fallback: WMI can fail in many ways (service stopped, missing class, missing property)
            // and none of them are actionable here.
            return null;
        }

        return new CpuInfo(
            processorModelNames.Count > 0 ? string.Join(", ", processorModelNames) : null,
            processorsCount > 0 ? processorsCount : null,
            physicalCoreCount > 0 ? (int?)physicalCoreCount : null,
            logicalCoreCount > 0 ? (int?)logicalCoreCount : null,
            nominalClockSpeed > 0 ? Frequency.FromMHz(nominalClockSpeed) : null,
            minFrequency: null,
            maxClockSpeed > 0 ? Frequency.FromMHz(maxClockSpeed) : null);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static string? GetString(ManagementObject managementObject, string propertyName)
        => managementObject[propertyName] as string;

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static uint GetUInt32(ManagementObject managementObject, string propertyName)
        => managementObject[propertyName] is uint value ? value : 0;
}
