using System.Diagnostics;

namespace Meziantou.Framework;

/// <summary>
/// A lightweight alternative to <see cref="Stopwatch"/> for measuring elapsed time with minimal allocation overhead.
/// </summary>
/// <remarks>
/// Based on <see href="https://github.com/dotnet/runtime/blob/26b6e4ea97a627ab800362b2c10f32ebecea041d/src/libraries/Common/src/Extensions/ValueStopwatch/ValueStopwatch.cs">the implementation from .NET runtime</see>.
/// </remarks>
public readonly struct ValueStopwatch
{
    private static readonly double TimestampToTicks = TimeSpan.TicksPerSecond / (double)Stopwatch.Frequency;

    private readonly long _startTimestamp;

    /// <summary>
    /// Gets a value indicating whether this <see cref="ValueStopwatch"/> instance has been started.
    /// </summary>
    public bool IsActive => _startTimestamp != 0;

    private ValueStopwatch(long startTimestamp)
    {
        _startTimestamp = startTimestamp;
    }

    /// <summary>
    /// Creates a new <see cref="ValueStopwatch"/> instance and starts measuring elapsed time.
    /// </summary>
    /// <returns>A new <see cref="ValueStopwatch"/> that has already started measuring elapsed time.</returns>
    public static ValueStopwatch StartNew() => new(Stopwatch.GetTimestamp());

    /// <summary>
    /// Gets the current timestamp value from the underlying timer mechanism.
    /// </summary>
    /// <returns>A long integer representing the current timestamp.</returns>
    public static long GetTimestamp() => Stopwatch.GetTimestamp();

    /// <summary>
    /// Calculates the elapsed time between two timestamp values.
    /// </summary>
    /// <param name="startTimestamp">The timestamp marking the beginning of the time period.</param>
    /// <param name="endTimestamp">The timestamp marking the end of the time period.</param>
    /// <returns>A <see cref="TimeSpan"/> representing the elapsed time between the two timestamps.</returns>
    public static TimeSpan GetElapsedTime(long startTimestamp, long endTimestamp)
    {
        var timestampDelta = endTimestamp - startTimestamp;
        var ticks = (long)(TimestampToTicks * timestampDelta);
        return new TimeSpan(ticks);
    }

    /// <summary>
    /// Gets the elapsed time since this <see cref="ValueStopwatch"/> was started.
    /// </summary>
    /// <returns>A <see cref="TimeSpan"/> representing the elapsed time.</returns>
    /// <exception cref="InvalidOperationException">Thrown when this method is called on an uninitialized <see cref="ValueStopwatch"/>.</exception>
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
