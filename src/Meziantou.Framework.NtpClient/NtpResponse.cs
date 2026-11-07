using System.Diagnostics;

namespace Meziantou.Framework.Ntp;

/// <summary>
/// Represents the response from an NTP server query.
/// </summary>
public sealed class NtpResponse
{
    internal NtpResponse(NtpPacket packet, DateTimeOffset destinationTimestamp)
    {
        // The client rejects a response missing any of these before constructing the result, so the
        // offset and delay below can never be computed from an unset timestamp.
        Debug.Assert(packet.OriginateTimestamp is not null, "The originate timestamp must be validated before building a response");
        Debug.Assert(packet.ReceiveTimestamp is not null, "The receive timestamp must be validated before building a response");
        Debug.Assert(packet.TransmitTimestamp is not null, "The transmit timestamp must be validated before building a response");

        LeapIndicator = packet.LeapIndicator;
        Version = packet.Version;
        Stratum = packet.Stratum;
        PollInterval = packet.PollInterval;
        Precision = packet.Precision;
        RootDelay = packet.RootDelay;
        RootDispersion = packet.RootDispersion;
        ReferenceIdentifier = packet.ReferenceIdentifier;
        ReferenceTimestamp = packet.ReferenceTimestamp;
        OriginateTimestamp = packet.OriginateTimestamp.GetValueOrDefault();
        ReceiveTimestamp = packet.ReceiveTimestamp.GetValueOrDefault();
        TransmitTimestamp = packet.TransmitTimestamp.GetValueOrDefault();
        DestinationTimestamp = destinationTimestamp;
    }

    /// <summary>Gets the leap indicator from the server.</summary>
    public NtpLeapIndicator LeapIndicator { get; }

    /// <summary>Gets the NTP version used by the server.</summary>
    public NtpVersion Version { get; }

    /// <summary>Gets the stratum level of the server (0 = Kiss-o'-Death, 1 = primary reference, 2-15 = secondary).</summary>
    public byte Stratum { get; }

    /// <summary>Gets the maximum interval between successive messages, in log2 seconds.</summary>
    public sbyte PollInterval { get; }

    /// <summary>Gets the precision of the server clock, in log2 seconds.</summary>
    public sbyte Precision { get; }

    /// <summary>Gets the total round-trip delay from the server to the reference clock.</summary>
    public TimeSpan RootDelay { get; }

    /// <summary>
    /// Gets the server's own estimate of the maximum error of its clock. Use it as the error bound on
    /// <see cref="ClockOffset"/>: a large value means the server does not vouch for its own accuracy.
    /// </summary>
    public TimeSpan RootDispersion { get; }

    /// <summary>
    /// Gets the raw 32-bit reference identifier. For stratum 1 it names the reference clock and for
    /// stratum 0 it carries the Kiss-o'-Death code; see <see cref="ReferenceIdentifierText"/>.
    /// </summary>
    public uint ReferenceIdentifier { get; }

    /// <summary>
    /// Gets the reference identifier decoded as four ASCII characters (for example <c>GPS</c> or
    /// <c>PPS</c>), or <see langword="null"/> when it does not hold printable ASCII. For stratum 2 and
    /// above the field holds an address rather than text, so this is normally <see langword="null"/>.
    /// </summary>
    public string? ReferenceIdentifierText => NtpPacket.FormatReferenceIdentifier(ReferenceIdentifier);

    /// <summary>
    /// Gets a value indicating whether this is a Kiss-o'-Death packet: the server is refusing service
    /// and its timestamps carry no time. <see cref="KissCode"/> says why.
    /// </summary>
    /// <remarks>
    /// This is only ever <see langword="true"/> when <see cref="NtpClientOptions.ValidateResponse"/>
    /// is disabled; otherwise the client rejects such a response instead of returning it.
    /// </remarks>
    public bool IsKissOfDeath => Stratum is NtpPacket.KissOfDeathStratum;

    /// <summary>
    /// Gets the four-character Kiss-o'-Death code (for example <c>RATE</c> when the client is being
    /// rate limited, or <c>DENY</c> when it is not allowed to query the server), or
    /// <see langword="null"/> when this is not a Kiss-o'-Death packet.
    /// </summary>
    public string? KissCode => IsKissOfDeath ? ReferenceIdentifierText : null;

    /// <summary>
    /// Gets the time when the server clock was last set or corrected, or <see langword="null"/> when
    /// the server did not supply one because its clock has never been synchronized.
    /// </summary>
    public DateTimeOffset? ReferenceTimestamp { get; }

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
