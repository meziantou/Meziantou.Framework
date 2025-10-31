using System.Diagnostics;

namespace Meziantou.Framework;

/// <summary>A lightweight value-type stopwatch for measuring elapsed time without heap allocations.</summary>
/// <example>
/// Measure execution time of an operation:
/// <code>
/// var stopwatch = ValueStopwatch.StartNew();
/// 
/// // Perform some operation
/// DoWork();
/// 
/// TimeSpan elapsed = stopwatch.GetElapsedTime();
/// Console.WriteLine($"Operation took {elapsed.TotalMilliseconds}ms");
/// </code>
/// </example>
// https://github.com/dotnet/runtime/blob/26b6e4ea97a627ab800362b2c10f32ebecea041d/src/libraries/Common/src/Extensions/ValueStopwatch/ValueStopwatch.cs
public readonly struct ValueStopwatch
{
    private static readonly double TimestampToTicks = TimeSpan.TicksPerSecond / (double)Stopwatch.Frequency;

    private readonly long _startTimestamp;

    /// <summary>Gets a value indicating whether this stopwatch instance has been initialized via <see cref="StartNew"/>.</summary>
    public bool IsActive => _startTimestamp != 0;

    private ValueStopwatch(long startTimestamp)
    {
        _startTimestamp = startTimestamp;
    }

    /// <summary>Creates and starts a new <see cref="ValueStopwatch"/> instance.</summary>
    /// <returns>A new <see cref="ValueStopwatch"/> that has been started.</returns>
    public static ValueStopwatch StartNew() => new(Stopwatch.GetTimestamp());

    /// <summary>Gets the current timestamp value from the high-resolution performance counter.</summary>
    /// <returns>A long integer representing the current timestamp.</returns>
    public static long GetTimestamp() => Stopwatch.GetTimestamp();

    /// <summary>Calculates the elapsed time between two timestamp values.</summary>
    /// <param name="startTimestamp">The starting timestamp obtained from <see cref="GetTimestamp"/>.</param>
    /// <param name="endTimestamp">The ending timestamp obtained from <see cref="GetTimestamp"/>.</param>
    /// <returns>A <see cref="TimeSpan"/> representing the elapsed time between the two timestamps.</returns>
    public static TimeSpan GetElapsedTime(long startTimestamp, long endTimestamp)
    {
        var timestampDelta = endTimestamp - startTimestamp;
        var ticks = (long)(TimestampToTicks * timestampDelta);
        return new TimeSpan(ticks);
    }

    /// <summary>Gets the elapsed time since this stopwatch was started.</summary>
    /// <returns>A <see cref="TimeSpan"/> representing the elapsed time since the stopwatch was started.</returns>
    /// <exception cref="InvalidOperationException">Thrown when called on an uninitialized or default <see cref="ValueStopwatch"/> instance.</exception>
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
