namespace Meziantou.Framework.Ntp;

/// <summary>
/// The result of
/// <see cref="NtpClient.MeasureAsync(IEnumerable{NtpClient}, NtpMeasurementOptions?, CancellationToken)"/>:
/// the clock offset agreed on by the majority of the servers that answered.
/// </summary>
public sealed class NtpClockMeasurement
{
    internal NtpClockMeasurement(
        TimeSpan clockOffset,
        TimeSpan jitter,
        TimeSpan minimumClockOffset,
        TimeSpan maximumClockOffset,
        IReadOnlyList<NtpServerMeasurement> selectedServers,
        IReadOnlyList<NtpServerMeasurement> rejectedServers,
        IReadOnlyDictionary<NtpClient, Exception> failedServers)
    {
        ClockOffset = clockOffset;
        Jitter = jitter;
        MinimumClockOffset = minimumClockOffset;
        MaximumClockOffset = maximumClockOffset;
        SelectedServers = selectedServers;
        RejectedServers = rejectedServers;
        FailedServers = failedServers;
    }

    /// <summary>
    /// Gets the estimated clock offset between the local clock and the servers: the average of the offsets of
    /// <see cref="SelectedServers"/>, weighted by the inverse of their root distance. Add it to the local clock to get the
    /// time of the servers.
    /// </summary>
    public TimeSpan ClockOffset { get; }

    /// <summary>
    /// Gets the root mean square of the differences between the offsets of <see cref="SelectedServers"/> and
    /// <see cref="ClockOffset"/>.
    /// </summary>
    public TimeSpan Jitter { get; }

    /// <summary>
    /// Gets the lower bound of the clock offset: the true offset lies between <see cref="MinimumClockOffset"/> and
    /// <see cref="MaximumClockOffset"/> as long as the majority of the servers that answered tell the time correctly.
    /// </summary>
    public TimeSpan MinimumClockOffset { get; }

    /// <summary>Gets the upper bound of the clock offset. See <see cref="MinimumClockOffset"/>.</summary>
    public TimeSpan MaximumClockOffset { get; }

    /// <summary>Gets the servers that agree with the majority (the truechimers of RFC 5905), whose offsets make <see cref="ClockOffset"/>.</summary>
    public IReadOnlyList<NtpServerMeasurement> SelectedServers { get; }

    /// <summary>Gets the servers whose time disagrees with the majority (the falsetickers of RFC 5905). Their offsets are ignored.</summary>
    public IReadOnlyList<NtpServerMeasurement> RejectedServers { get; }

    /// <summary>Gets the clients whose server gave no valid reply, with the reason.</summary>
    public IReadOnlyDictionary<NtpClient, Exception> FailedServers { get; }
}
