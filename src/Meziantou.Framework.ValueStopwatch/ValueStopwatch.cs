using System.Diagnostics;

namespace Meziantou.Framework;

// https://github.com/dotnet/runtime/blob/26b6e4ea97a627ab800362b2c10f32ebecea041d/src/libraries/Common/src/Extensions/ValueStopwatch/ValueStopwatch.cs
public readonly struct ValueStopwatch
{
    private static readonly double TimestampToTicks = TimeSpan.TicksPerSecond / (double)Stopwatch.Frequency;

    private readonly long _startTimestamp;

    public bool IsActive => _startTimestamp != 0;

    private ValueStopwatch(long startTimestamp)
    {
        _startTimestamp = startTimestamp;
    }

    public static ValueStopwatch StartNew() => new(Stopwatch.GetTimestamp());

    public static long GetTimestamp() => Stopwatch.GetTimestamp();

    public static TimeSpan GetElapsedTime(long startTimestamp, long endTimestamp)
    {
        var timestampDelta = endTimestamp - startTimestamp;
        var ticks = (long)(TimestampToTicks * timestampDelta);
        return new TimeSpan(ticks);
    }

    public TimeSpan GetElapsedTime()
    {
        // Start timestamp can't be zero in an initialized ValueStopwatch. It would have to be literally the first thing executed when the machine boots to be 0.
        // So it being 0 is a clear indication of default(ValueStopwatch)
        if (!IsActive)
            throw new InvalidOperationException("An uninitialized, or 'default', ValueStopwatch cannot be used to get elapsed time.");

        var end = Stopwatch.GetTimestamp();
        return GetElapsedTime(_startTimestamp, end);
    }
}
