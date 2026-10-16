using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters.Json;

namespace Meziantou.Framework.Imaging.Benchmarks.Infrastructure;

/// <summary>
/// Configuration of the workload benchmarks: managed allocations (memory diagnoser), the workload columns, and the
/// full JSON exporter in addition to the default ones (the JSON reports hold every measurement, used by
/// <c>eng/compare-benchmarks.cs</c> for statistical regression checks). Jobs come from the command line.
/// </summary>
internal sealed class WorkloadConfig : ManualConfig
{
    public WorkloadConfig()
    {
        AddDiagnoser(MemoryDiagnoser.Default);
        AddColumn(WorkloadColumns.All);
        AddExporter(JsonExporter.Full);
    }
}
