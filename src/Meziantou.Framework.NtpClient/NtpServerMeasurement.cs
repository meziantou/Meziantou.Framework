namespace Meziantou.Framework.Ntp;

/// <summary>
/// The result of several queries to one NTP server, filtered as the clock filter of RFC 5905 (section 10) does: the
/// sample with the shortest round-trip delay is the most accurate one.
/// </summary>
public sealed class NtpServerMeasurement
{
    /// <summary>The largest precision taken into account, in log2 seconds, so that a bogus value cannot overflow the root distance.</summary>
    private const int MaxPrecision = 16;

    internal NtpServerMeasurement(NtpClient client, IReadOnlyList<NtpResponse> samples, IReadOnlyList<Exception> errors)
    {
        Client = client;
        Samples = samples;
        Errors = errors;

        var best = samples[0];
        for (var i = 1; i < samples.Count; i++)
        {
            if (GetDelayTicks(samples[i]) < GetDelayTicks(best))
            {
                best = samples[i];
            }
        }

        BestSample = best;

        // RFC 5905 peer jitter: the root mean square of the differences between the offsets of the other samples and the
        // offset of the best one
        var squareSum = 0d;
        foreach (var sample in samples)
        {
            var difference = (double)(sample.ClockOffset.Ticks - best.ClockOffset.Ticks);
            squareSum += difference * difference;
        }

        Jitter = samples.Count > 1 ? TimeSpan.FromTicks((long)Math.Sqrt(squareSum / (samples.Count - 1))) : TimeSpan.Zero;

        // RFC 5905 root distance: half the total delay to the reference clock, plus the dispersions and the jitter. The
        // precision of the server stands for the dispersion of the sample, which only grows with its age.
        var precision = TimeSpan.FromTicks((long)(Math.ScaleB(1, Math.Min((int)best.Precision, MaxPrecision)) * TimeSpan.TicksPerSecond));
        RootDistance = TimeSpan.FromTicks((best.RootDelay.Ticks + GetDelayTicks(best)) / 2) + best.RootDispersion + precision + Jitter;
    }

    /// <summary>Gets the client that queried the server.</summary>
    public NtpClient Client { get; }

    /// <summary>Gets the host name or the IP address of the server.</summary>
    public string Server => Client.Server;

    /// <summary>Gets the valid replies of the server, in the order of the queries.</summary>
    public IReadOnlyList<NtpResponse> Samples { get; }

    /// <summary>Gets the errors of the queries that got no valid reply, such as <see cref="TimeoutException"/>.</summary>
    public IReadOnlyList<Exception> Errors { get; }

    /// <summary>Gets the sample with the shortest round-trip delay, whose offset is the most accurate.</summary>
    public NtpResponse BestSample { get; }

    /// <summary>Gets the clock offset of <see cref="BestSample"/>.</summary>
    public TimeSpan ClockOffset => BestSample.ClockOffset;

    /// <summary>Gets the round-trip delay of <see cref="BestSample"/>.</summary>
    public TimeSpan RoundTripDelay => BestSample.RoundTripDelay;

    /// <summary>
    /// Gets the jitter: the root mean square of the differences between the offsets of the samples and the offset of
    /// <see cref="BestSample"/>. It is zero for a single sample.
    /// </summary>
    public TimeSpan Jitter { get; }

    /// <summary>
    /// Gets the root distance: the maximum error of <see cref="ClockOffset"/> as RFC 5905 estimates it, from the
    /// round-trip delays to the server and to its reference clock, the dispersion and the precision the server reports,
    /// and <see cref="Jitter"/>.
    /// </summary>
    public TimeSpan RootDistance { get; }

    // A negative delay comes from a clock stepped during the query: it carries no information, so count it as zero
    private static long GetDelayTicks(NtpResponse response) => Math.Max(response.RoundTripDelay.Ticks, 0);
}
