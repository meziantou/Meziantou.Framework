using System.Runtime.InteropServices;

namespace Meziantou.Framework.MsQuic;

/// <summary>The statistics of a connection, see <see cref="MsQuicConnection.GetStatistics"/>.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct MsQuicConnectionStatistics
{
    /// <summary>Gets the smoothed round-trip time.</summary>
    public TimeSpan SmoothedRoundTripTime { get; init; }

    /// <summary>Gets the minimum round-trip time observed.</summary>
    public TimeSpan MinRoundTripTime { get; init; }

    /// <summary>Gets the maximum round-trip time observed.</summary>
    public TimeSpan MaxRoundTripTime { get; init; }

    /// <summary>Gets the variation of the round-trip time.</summary>
    public TimeSpan RoundTripTimeVariation { get; init; }

    /// <summary>Gets the current maximum transmission unit of the path, in bytes.</summary>
    public int PathMtu { get; init; }

    /// <summary>Gets the number of bytes sent in UDP payloads, including the QUIC overhead.</summary>
    public long BytesSent { get; init; }

    /// <summary>Gets the number of QUIC packets sent.</summary>
    public long PacketsSent { get; init; }

    /// <summary>Gets the number of stream bytes sent.</summary>
    public long StreamBytesSent { get; init; }

    /// <summary>Gets the number of bytes received in UDP payloads, including the QUIC overhead.</summary>
    public long BytesReceived { get; init; }

    /// <summary>Gets the number of QUIC packets received.</summary>
    public long PacketsReceived { get; init; }

    /// <summary>Gets the number of stream bytes received.</summary>
    public long StreamBytesReceived { get; init; }

    /// <summary>Gets the number of packets considered lost, excluding the ones acknowledged later.</summary>
    public long PacketsLost { get; init; }

    /// <summary>Gets the number of received packets dropped, duplicates included.</summary>
    public long PacketsDropped { get; init; }

    /// <summary>Gets the number of congestion events.</summary>
    public long CongestionEvents { get; init; }

    /// <summary>Gets the size of the congestion window, in bytes.</summary>
    public long CongestionWindow { get; init; }
}
