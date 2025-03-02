using System.Globalization;

namespace Meziantou.Framework.Diagnostics.ContextSnapshot.Internals;

internal static class WmicCpuInfoParser
{
    internal static CpuInfo ParseOutput(string? content)
    {
        var processors = SectionsHelper.ParseSections(content, '=');

        var processorModelNames = new HashSet<string>(StringComparer.Ordinal);
        var physicalCoreCount = 0;
        var logicalCoreCount = 0;
        var processorsCount = 0;

        var currentClockSpeed = Frequency.Zero;
        var maxClockSpeed = Frequency.Zero;
        var minClockSpeed = Frequency.Zero;

        foreach (var processor in processors)
        {
            if (processor.TryGetValue(WmicCpuInfoKeyNames.NumberOfCores, out var numberOfCoresValue) &&
                int.TryParse(numberOfCoresValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var numberOfCores) &&
                numberOfCores > 0)
            {
                physicalCoreCount += numberOfCores;
            }

            if (processor.TryGetValue(WmicCpuInfoKeyNames.NumberOfLogicalProcessors, out var numberOfLogicalValue) &&
                int.TryParse(numberOfLogicalValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var numberOfLogical) &&
                numberOfLogical > 0)
            {
                logicalCoreCount += numberOfLogical;
            }

            if (processor.TryGetValue(WmicCpuInfoKeyNames.Name, out var name))
            {
                processorModelNames.Add(name);
                processorsCount++;
            }

            if (processor.TryGetValue(WmicCpuInfoKeyNames.MaxClockSpeed, out var frequencyValue)
                && int.TryParse(frequencyValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var frequency)
                && frequency > 0)
            {
                maxClockSpeed += frequency;
            }
        }

        return new CpuInfo(
            processorModelNames.Count > 0 ? string.Join(", ", processorModelNames) : null,
            processorsCount > 0 ? processorsCount : null,
            physicalCoreCount > 0 ? physicalCoreCount : null,
            logicalCoreCount > 0 ? logicalCoreCount : null,
            currentClockSpeed > 0 && processorsCount > 0 ? Frequency.FromMHz(currentClockSpeed / processorsCount) : null,
            minClockSpeed > 0 && processorsCount > 0 ? Frequency.FromMHz(minClockSpeed / processorsCount) : null,
            maxClockSpeed > 0 && processorsCount > 0 ? Frequency.FromMHz(maxClockSpeed / processorsCount) : null);
    }
}
