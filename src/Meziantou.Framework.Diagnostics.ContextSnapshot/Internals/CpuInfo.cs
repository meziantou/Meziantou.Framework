namespace Meziantou.Framework.Diagnostics.ContextSnapshot.Internals;

internal sealed class CpuInfo
{
    public string? ProcessorName { get; }
    public int? PhysicalProcessorCount { get; }
    public int? PhysicalCoreCount { get; }
    public int? LogicalCoreCount { get; }
    public Frequency? NominalFrequency { get; }
    public Frequency? MinFrequency { get; }
    public Frequency? MaxFrequency { get; }

    internal CpuInfo(string? processorName, Frequency? nominalFrequency)
        : this(processorName, physicalProcessorCount: null, physicalCoreCount: null, logicalCoreCount: null, nominalFrequency, minFrequency: null, maxFrequency: null)
    {
    }

    public CpuInfo(string? processorName,
                   int? physicalProcessorCount,
                   int? physicalCoreCount,
                   int? logicalCoreCount,
                   Frequency? nominalFrequency,
                   Frequency? minFrequency,
                   Frequency? maxFrequency)
    {
        ProcessorName = processorName;
        PhysicalProcessorCount = physicalProcessorCount;
        PhysicalCoreCount = physicalCoreCount;
        LogicalCoreCount = logicalCoreCount;
        NominalFrequency = nominalFrequency;
        MinFrequency = minFrequency;
        MaxFrequency = maxFrequency;
    }
}
