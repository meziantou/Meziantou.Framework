using System.Globalization;

namespace Meziantou.Framework.Diagnostics.ContextSnapshot.Internals;

internal static class SysctlCpuInfoParser
{
    internal static CpuInfo ParseOutput(string? content)
    {
        var sysctl = SectionsHelper.ParseSection(content, ':');
        var processorName = sysctl.GetValueOrDefault("machdep.cpu.brand_string");
        var physicalProcessorCount = GetPositiveIntValue(sysctl, "hw.packages");
        var physicalCoreCount = GetPositiveIntValue(sysctl, "hw.physicalcpu");
        var logicalCoreCount = GetPositiveIntValue(sysctl, "hw.logicalcpu");
        var nominalFrequency = GetPositiveLongValue(sysctl, "hw.cpufrequency");
        var minFrequency = GetPositiveLongValue(sysctl, "hw.cpufrequency_min");
        var maxFrequency = GetPositiveLongValue(sysctl, "hw.cpufrequency_max");
        return new CpuInfo(processorName, physicalProcessorCount, physicalCoreCount, logicalCoreCount, nominalFrequency, minFrequency, maxFrequency);
    }

    private static int? GetPositiveIntValue(Dictionary<string, string> sysctl, string keyName)
    {
        if (sysctl.TryGetValue(keyName, out var value) &&
            int.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result) &&
            result > 0)
        {
            return result;
        }

        return null;
    }

    private static long? GetPositiveLongValue(Dictionary<string, string> sysctl, string keyName)
    {
        if (sysctl.TryGetValue(keyName, out var value) &&
            long.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result) &&
            result > 0)
        {
            return result;
        }

        return null;
    }
}
