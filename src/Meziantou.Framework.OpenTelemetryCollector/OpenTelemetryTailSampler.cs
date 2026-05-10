namespace Meziantou.Framework.OpenTelemetryCollector;

public sealed class OpenTelemetryTailSampler : OpenTelemetrySampler
{
    public TimeSpan MaxTraceDuration { get; set; } = TimeSpan.FromMinutes(2);

    public int MaxBufferedSpansPerTrace { get; set; } = 5000;

    public int MaxBufferedSpans { get; set; } = 100_000;

    public OpenTelemetryTailBufferOverflowPolicy OverflowPolicy { get; set; } = OpenTelemetryTailBufferOverflowPolicy.DropWholeTrace;

    public OpenTelemetryTraceTailSampling? ShouldSample { get; set; }
}
