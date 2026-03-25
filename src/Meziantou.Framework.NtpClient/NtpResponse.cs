namespace Meziantou.Framework.NtpClient;

/// <summary>
/// Represents the response from an NTP server query.
/// </summary>
public sealed class NtpResponse
{
    internal NtpResponse(NtpPacket packet, DateTimeOffset destinationTimestamp)
    {
        LeapIndicator = packet.LeapIndicator;
        Version = packet.Version;
        Stratum = packet.Stratum;
        PollInterval = packet.PollInterval;
        Precision = packet.Precision;
        ReferenceTimestamp = packet.ReferenceTimestamp;
        OriginateTimestamp = packet.OriginateTimestamp;
        ReceiveTimestamp = packet.ReceiveTimestamp;
        TransmitTimestamp = packet.TransmitTimestamp;
        DestinationTimestamp = destinationTimestamp;
    }

    /// <summary>Gets the leap indicator from the server.</summary>
    public NtpLeapIndicator LeapIndicator { get; }

    /// <summary>Gets the NTP version used by the server.</summary>
    public NtpVersion Version { get; }

    /// <summary>Gets the stratum level of the server (1 = primary reference, 2-15 = secondary).</summary>
    public byte Stratum { get; }

    /// <summary>Gets the maximum interval between successive messages, in log2 seconds.</summary>
    public sbyte PollInterval { get; }

    /// <summary>Gets the precision of the server clock, in log2 seconds.</summary>
    public sbyte Precision { get; }

    /// <summary>Gets the time when the server clock was last set or corrected.</summary>
    public DateTimeOffset ReferenceTimestamp { get; }

    /// <summary>Gets the time at which the request was sent by the client (copied from the client's transmit timestamp).</summary>
    public DateTimeOffset OriginateTimestamp { get; }

    /// <summary>Gets the time at which the request arrived at the server.</summary>
    public DateTimeOffset ReceiveTimestamp { get; }

    /// <summary>Gets the time at which the reply was sent from the server.</summary>
    public DateTimeOffset TransmitTimestamp { get; }

    /// <summary>Gets the local time at which the reply was received by the client.</summary>
    public DateTimeOffset DestinationTimestamp { get; }

    /// <summary>Gets the estimated clock offset between the client and the server.</summary>
    public TimeSpan ClockOffset
    {
        get
        {
            // θ = ((T2 - T1) + (T3 - T4)) / 2
            var t1 = OriginateTimestamp;
            var t2 = ReceiveTimestamp;
            var t3 = TransmitTimestamp;
            var t4 = DestinationTimestamp;

            return TimeSpan.FromTicks(((t2 - t1).Ticks + (t3 - t4).Ticks) / 2);
        }
    }

    /// <summary>Gets the estimated round-trip delay between the client and the server.</summary>
    public TimeSpan RoundTripDelay
    {
        get
        {
            // δ = (T4 - T1) - (T3 - T2)
            var t1 = OriginateTimestamp;
            var t2 = ReceiveTimestamp;
            var t3 = TransmitTimestamp;
            var t4 = DestinationTimestamp;

            return (t4 - t1) - (t3 - t2);
        }
    }
}
