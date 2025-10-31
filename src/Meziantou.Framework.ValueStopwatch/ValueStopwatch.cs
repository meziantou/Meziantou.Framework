using System.Diagnostics;

namespace Meziantou.Framework;

// https://github.com/dotnet/runtime/blob/26b6e4ea97a627ab800362b2c10f32ebecea041d/src/libraries/Common/src/Extensions/ValueStopwatch/ValueStopwatch.cs
/// <summary>
/// A lightweight stopwatch struct that provides high-resolution time measurements.
/// </summary>
public readonly struct ValueStopwatch
{
    private static readonly double TimestampToTicks = TimeSpan.TicksPerSecond / (double)Stopwatch.Frequency;

    private readonly long _startTimestamp;

    /// <summary>
    /// Gets a value indicating whether the stopwatch is active (has been started).
    /// </summary>
    public bool IsActive => _startTimestamp != 0;

    private ValueStopwatch(long startTimestamp)
    {
        _startTimestamp = startTimestamp;
    }

    /// <summary>
    /// Creates and starts a new <see cref="ValueStopwatch"/>.
    /// </summary>
    /// <returns>A new started <see cref="ValueStopwatch"/>.</returns>
    public static ValueStopwatch StartNew() => new(Stopwatch.GetTimestamp());

    /// <summary>
    /// Gets the current timestamp value.
    /// </summary>
    /// <returns>The current timestamp.</returns>
    public static long GetTimestamp() => Stopwatch.GetTimestamp();

    /// <summary>
    /// Gets the elapsed time between two timestamps.
    /// </summary>
    /// <param name="startTimestamp">The start timestamp.</param>
    /// <param name="endTimestamp">The end timestamp.</param>
    /// <returns>The elapsed time as a <see cref="TimeSpan"/>.</returns>
    public static TimeSpan GetElapsedTime(long startTimestamp, long endTimestamp)
    {
        var timestampDelta = endTimestamp - startTimestamp;
        var ticks = (long)(TimestampToTicks * timestampDelta);
        return new TimeSpan(ticks);
    }

    /// <summary>
    /// Gets the elapsed time since the stopwatch was started.
    /// </summary>
    /// <returns>The elapsed time as a <see cref="TimeSpan"/>.</returns>
    /// <exception cref="InvalidOperationException">The stopwatch has not been started.</exception>
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
