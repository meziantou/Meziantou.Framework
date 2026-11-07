namespace Meziantou.Framework.OpenTelemetryCollector;

public sealed class OpenTelemetryTailSampler : OpenTelemetrySampler
{
    private TimeSpan _maxTraceDuration = TimeSpan.FromMinutes(2);
    private int _maxBufferedSpansPerTrace = 5000;
    private int _maxBufferedSpans = 100_000;
    private TimeSpan? _sweepInterval;

    /// <summary>Gets or sets how long a trace is buffered while waiting for its root span.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or zero.</exception>
    public TimeSpan MaxTraceDuration
    {
        get => _maxTraceDuration;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
            _maxTraceDuration = value;
        }
    }

    /// <summary>Gets or sets how many spans a single trace can buffer before <see cref="OverflowPolicy"/> is applied to it.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or zero.</exception>
    public int MaxBufferedSpansPerTrace
    {
        get => _maxBufferedSpansPerTrace;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            _maxBufferedSpansPerTrace = value;
        }
    }

    /// <summary>Gets or sets how many spans can be buffered across all traces.</summary>
    /// <remarks>
    /// Reaching this limit evicts whole buffered traces, largest first, so that new traces keep being accepted and a
    /// trace within <see cref="MaxBufferedSpansPerTrace"/> is never truncated because of unrelated traffic. Evicted
    /// traces are remembered for <see cref="MaxTraceDuration"/>, so their later spans are dropped instead of being
    /// emitted as a fragment of the original trace.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or zero.</exception>
    public int MaxBufferedSpans
    {
        get => _maxBufferedSpans;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            _maxBufferedSpans = value;
        }
    }

    /// <summary>Gets or sets what happens to a trace that exceeds <see cref="MaxBufferedSpansPerTrace"/>.</summary>
    public OpenTelemetryTailBufferOverflowPolicy OverflowPolicy { get; set; } = OpenTelemetryTailBufferOverflowPolicy.DropWholeTrace;

    public OpenTelemetryTraceTailSampling? ShouldSample { get; set; }

    /// <summary>Gets or sets how often buffered traces are checked for reaching <see cref="MaxTraceDuration"/>.</summary>
    /// <remarks>
    /// Traces that never receive their root span are evaluated by a background sweep, so they are released even when no
    /// other trace is received. When <see langword="null"/>, the interval is a quarter of <see cref="MaxTraceDuration"/>.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or zero.</exception>
    public TimeSpan? SweepInterval
    {
        get => _sweepInterval;
        set
        {
            if (value is { } interval)
            {
                ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero, nameof(value));
            }

            _sweepInterval = value;
        }
    }

    internal TimeSpan GetSweepInterval()
    {
        if (SweepInterval is { } interval)
            return interval;

        var ticks = Math.Max(MaxTraceDuration.Ticks / 4, TimeSpan.TicksPerMillisecond);
        return TimeSpan.FromTicks(ticks);
    }
}
