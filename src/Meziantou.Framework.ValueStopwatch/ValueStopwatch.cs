using System.Diagnostics;

namespace Meziantou.Framework;

/// <summary>
/// A lightweight alternative to <see cref="Stopwatch"/> that uses value semantics and avoids allocations.
/// </summary>
// https://github.com/dotnet/runtime/blob/26b6e4ea97a627ab800362b2c10f32ebecea041d/src/libraries/Common/src/Extensions/ValueStopwatch/ValueStopwatch.cs
public readonly struct ValueStopwatch
{
    private static readonly double TimestampToTicks = TimeSpan.TicksPerSecond / (double)Stopwatch.Frequency;

    private readonly long _startTimestamp;

    /// <summary>
    /// Gets a value indicating whether the stopwatch is currently active (has been started).
    /// </summary>
    public bool IsActive => _startTimestamp != 0;

    private ValueStopwatch(long startTimestamp)
    {
        _startTimestamp = startTimestamp;
    }

    /// <summary>
    /// Creates a new <see cref="ValueStopwatch"/> that is started immediately.
    /// </summary>
    /// <returns>A new started stopwatch.</returns>
    public static ValueStopwatch StartNew() => new(Stopwatch.GetTimestamp());

    /// <summary>
    /// Gets the current timestamp from the high-resolution performance counter.
    /// </summary>
    /// <returns>The current timestamp value.</returns>
    public static long GetTimestamp() => Stopwatch.GetTimestamp();

    /// <summary>
    /// Gets the elapsed time between two timestamps.
    /// </summary>
    /// <param name="startTimestamp">The start timestamp.</param>
    /// <param name="endTimestamp">The end timestamp.</param>
    /// <returns>The elapsed time between the two timestamps.</returns>
    public static TimeSpan GetElapsedTime(long startTimestamp, long endTimestamp)
    {
        var timestampDelta = endTimestamp - startTimestamp;
        var ticks = (long)(TimestampToTicks * timestampDelta);
        return new TimeSpan(ticks);
    }

    /// <summary>
    /// Gets the elapsed time since this stopwatch was started.
    /// </summary>
    /// <returns>The elapsed time since the stopwatch was started.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the stopwatch has not been started.</exception>
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
