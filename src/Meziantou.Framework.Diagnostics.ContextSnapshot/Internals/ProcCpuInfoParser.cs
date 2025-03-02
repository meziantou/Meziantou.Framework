using System.Globalization;
using System.Text.RegularExpressions;

namespace Meziantou.Framework.Diagnostics.ContextSnapshot.Internals;

internal static partial class ProcCpuInfoParser
{
    internal static CpuInfo ParseOutput(string? content)
    {
        var logicalCores = SectionsHelper.ParseSections(content, ':');
        var processorModelNames = new HashSet<string>(StringComparer.Ordinal);
        var processorsToPhysicalCoreCount = new Dictionary<string, int>(StringComparer.Ordinal);

        var logicalCoreCount = 0;
        var nominalFrequency = Frequency.Zero;
        var minFrequency = Frequency.Zero;
        var maxFrequency = Frequency.Zero;

        foreach (var logicalCore in logicalCores)
        {
            if (logicalCore.TryGetValue(ProcCpuInfoKeyNames.PhysicalId, out var physicalId) &&
                logicalCore.TryGetValue(ProcCpuInfoKeyNames.CpuCores, out var cpuCoresValue) &&
                int.TryParse(cpuCoresValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var cpuCoreCount) &&
                cpuCoreCount > 0)
            {
                processorsToPhysicalCoreCount[physicalId] = cpuCoreCount;
            }

            if (logicalCore.TryGetValue(ProcCpuInfoKeyNames.ModelName, out var modelName))
            {
                processorModelNames.Add(modelName);
                nominalFrequency = ParseFrequencyFromBrandString(modelName);
                logicalCoreCount++;
            }

            if (logicalCore.TryGetValue(ProcCpuInfoKeyNames.MinFrequency, out var minCpuFreqValue)
                && Frequency.TryParseMHz(minCpuFreqValue, out var minCpuFreq))
            {
                minFrequency = minCpuFreq;
            }

            if (logicalCore.TryGetValue(ProcCpuInfoKeyNames.MaxFrequency, out var maxCpuFreqValue)
                 && Frequency.TryParseMHz(maxCpuFreqValue, out var maxCpuFreq))
            {
                maxFrequency = maxCpuFreq;
            }
        }

        return new CpuInfo(
            processorModelNames.Count > 0 ? string.Join(", ", processorModelNames) : null,
            processorsToPhysicalCoreCount.Count > 0 ? processorsToPhysicalCoreCount.Count : null,
            processorsToPhysicalCoreCount.Count > 0 ? processorsToPhysicalCoreCount.Values.Sum() : null,
            logicalCoreCount > 0 ? logicalCoreCount : null,
            nominalFrequency > 0 ? nominalFrequency : null,
            minFrequency > 0 ? minFrequency : null,
            maxFrequency > 0 ? maxFrequency : null);
    }

    internal static Frequency ParseFrequencyFromBrandString(string brandString)
    {
        var matches = FrequencyRegex().Matches(brandString);
        if (matches.Count > 0 && matches[0].Groups.Count > 1)
        {
            var match = matches[0].Groups[1].ToString();
            return Frequency.TryParseGHz(match, out var result) ? result : Frequency.Zero;
        }

        return 0d;
    }

    [GeneratedRegex("(?<Value>\\d.\\d+)GHz", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: -1)]
    private static partial Regex FrequencyRegex();
}
