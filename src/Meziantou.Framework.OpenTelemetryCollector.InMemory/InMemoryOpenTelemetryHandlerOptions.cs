namespace Meziantou.Framework.OpenTelemetryCollector.InMemory;

public sealed class InMemoryOpenTelemetryHandlerOptions
{
    public int MaximumLogCount { get; set; } = int.MaxValue;
    public int MaximumTraceCount { get; set; } = int.MaxValue;
    public int MaximumMetricCount { get; set; } = int.MaxValue;
}
