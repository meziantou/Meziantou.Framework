namespace Meziantou.Framework.MediaTags.Internals;

internal static class DurationHelpers
{
    /// <summary>The largest whole number of seconds a <see cref="TimeSpan"/> can hold.</summary>
    private const double MaxSeconds = long.MaxValue / TimeSpan.TicksPerSecond;

    /// <summary>
    /// Converts a duration computed from values declared by the file, or returns <see langword="null"/> when it is
    /// too long for a <see cref="TimeSpan"/>: <see cref="TimeSpan.FromSeconds(double)"/> would throw, and one bad
    /// duration must not fail the whole read.
    /// </summary>
    public static TimeSpan? FromSeconds(double seconds) => seconds <= MaxSeconds ? TimeSpan.FromSeconds(seconds) : null;
}
