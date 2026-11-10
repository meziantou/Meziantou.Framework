namespace Meziantou.Framework.Ntp;

/// <summary>
/// Configuration options for <see cref="NtpClient.MeasureAsync(NtpMeasurementOptions?, CancellationToken)"/> and
/// <see cref="NtpClient.MeasureAsync(IEnumerable{NtpClient}, NtpMeasurementOptions?, CancellationToken)"/>.
/// </summary>
public sealed class NtpMeasurementOptions
{
    internal static NtpMeasurementOptions Default { get; } = new();

    /// <summary>Gets or sets the number of queries sent to each server. Default is 4.</summary>
    /// <remarks>
    /// The sample with the shortest round-trip delay is the most accurate: the more samples, the more likely one of them
    /// had no queuing delay. RFC 5905 keeps the last 8.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 1.</exception>
    public int SampleCount
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            field = value;
        }
    } = 4;

    /// <summary>Gets or sets the delay between two queries to the same server. Default is 2 seconds.</summary>
    /// <remarks>
    /// Public NTP servers rate-limit their clients: a burst of queries without delay can get a <c>RATE</c>
    /// Kiss-o'-Death reply. The 2 seconds of the default are what ntpd uses for its initial burst.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public TimeSpan SampleInterval
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, TimeSpan.Zero);
            field = value;
        }
    } = TimeSpan.FromSeconds(2);
}
