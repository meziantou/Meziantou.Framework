using System.Reflection;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

namespace Meziantou.Framework.Imaging.Benchmarks.Infrastructure;

/// <summary>Report columns describing each workload: input, frames, output size, peak live bytes and allocations per frame.</summary>
internal static class WorkloadColumns
{
    public static IColumn[] All { get; } =
    [
        new WorkloadColumn("Input", "Input and settings: dimensions, frames, pixel type, codec settings", isNumeric: false, (_, c) => GetAttribute(c)?.Description ?? "-"),
        new WorkloadColumn("Frames", "Frames processed per operation", isNumeric: true, (_, c) => (GetAttribute(c)?.Frames ?? 1).ToString(CultureInfo.InvariantCulture)),
        new WorkloadColumn("Output", "Encoded output size (one operation)", isNumeric: true, (_, c) => WorkloadMeasurement.Get(c).OutputBytes is { } bytes ? FormatBytes(bytes) : "-"),
        new WorkloadColumn("PeakLive", "Peak library-accounted live bytes (sum of allocation-scope peaks, untimed run)", isNumeric: true, (_, c) => GetAttribute(c)?.MeasurePeakLive == false ? "-" : FormatBytes(WorkloadMeasurement.Get(c).PeakLiveBytes)),
        new WorkloadColumn("Alloc/frame", "Managed bytes allocated per frame (Allocated / Frames)", isNumeric: true, GetAllocatedPerFrame),
    ];

    private static WorkloadAttribute? GetAttribute(BenchmarkCase benchmarkCase) => benchmarkCase.Descriptor.WorkloadMethod.GetCustomAttribute<WorkloadAttribute>();

    private static string GetAllocatedPerFrame(Summary summary, BenchmarkCase benchmarkCase)
    {
        var report = summary[benchmarkCase];
        if (report is null || !report.Success)
            return "-";

        var bytes = report.GcStats.GetBytesAllocatedPerOperation(benchmarkCase);
        if (bytes is null)
            return "-";

        return FormatBytes(bytes.Value / (GetAttribute(benchmarkCase)?.Frames ?? 1));
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        < 1024 => bytes.ToString(CultureInfo.InvariantCulture) + " B",
        < 1024 * 1024 => (bytes / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " KB",
        _ => (bytes / (1024.0 * 1024)).ToString("0.00", CultureInfo.InvariantCulture) + " MB",
    };

    private sealed class WorkloadColumn(string name, string legend, bool isNumeric, Func<Summary, BenchmarkCase, string> getValue) : IColumn
    {
        public string Id => nameof(WorkloadColumn) + "." + name;

        public string ColumnName => name;

        public bool AlwaysShow => true;

        public ColumnCategory Category => ColumnCategory.Custom;

        public int PriorityInCategory => 0;

        public bool IsNumeric => isNumeric;

        public UnitType UnitType => UnitType.Dimensionless;

        public string Legend => legend;

        public string GetValue(Summary summary, BenchmarkCase benchmarkCase) => getValue(summary, benchmarkCase);

        public string GetValue(Summary summary, BenchmarkCase benchmarkCase, SummaryStyle style) => GetValue(summary, benchmarkCase);

        public bool IsAvailable(Summary summary) => true;

        public bool IsDefault(Summary summary, BenchmarkCase benchmarkCase) => false;
    }
}
