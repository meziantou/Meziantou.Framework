namespace Meziantou.Framework.OpenTelemetryCollector;

public sealed class OpenTelemetryTailSampler : OpenTelemetrySampler
{
    public TimeSpan MaxTraceDuration { get; set; } = TimeSpan.FromMinutes(2);

    public int MaxBufferedSpansPerTrace { get; set; } = 5000;

    public int MaxBufferedSpans { get; set; } = 100_000;

    public OpenTelemetryTailBufferOverflowPolicy OverflowPolicy { get; set; } = OpenTelemetryTailBufferOverflowPolicy.DropWholeTrace;

    public OpenTelemetryTraceTailSampling? ShouldSample { get; set; }

    /// <summary>Gets or sets how often buffered traces are checked for reaching <see cref="MaxTraceDuration"/>.</summary>
    /// <remarks>
    /// Traces that never receive their root span are evaluated by a background sweep, so they are released even when no
    /// other trace is received. When <see langword="null"/>, the interval is a quarter of <see cref="MaxTraceDuration"/>.
    /// </remarks>
    public TimeSpan? SweepInterval { get; set; }

    internal TimeSpan GetSweepInterval()
    {
        if (SweepInterval is { } interval && interval > TimeSpan.Zero)
            return interval;

        var ticks = Math.Max(MaxTraceDuration.Ticks / 4, TimeSpan.TicksPerMillisecond);
        return TimeSpan.FromTicks(ticks);
    }
}
