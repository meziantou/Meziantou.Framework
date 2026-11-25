namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The statistics of the connection of a WebTransport session (https://w3c.github.io/webtransport/#web-transport-connection-stats).</summary>
public sealed record WebTransportConnectionStatistics
{
    /// <summary>Gets the number of bytes sent, without the overhead of the protocol.</summary>
    public long BytesSent { get; init; }

    /// <summary>Gets the number of bytes of the protocol overhead sent.</summary>
    public long BytesSentOverhead { get; init; }

    /// <summary>Gets the number of bytes sent and acknowledged by the server.</summary>
    public long BytesAcknowledged { get; init; }

    /// <summary>Gets the number of packets sent.</summary>
    public long PacketsSent { get; init; }

    /// <summary>Gets the number of bytes lost.</summary>
    public long BytesLost { get; init; }

    /// <summary>Gets the number of packets lost.</summary>
    public long PacketsLost { get; init; }

    /// <summary>Gets the number of bytes received.</summary>
    public long BytesReceived { get; init; }

    /// <summary>Gets the number of packets received.</summary>
    public long PacketsReceived { get; init; }

    /// <summary>Gets the smoothed round-trip time.</summary>
    public TimeSpan SmoothedRtt { get; init; }

    /// <summary>Gets the variation of the round-trip time.</summary>
    public TimeSpan RttVariation { get; init; }

    /// <summary>Gets the minimum round-trip time.</summary>
    public TimeSpan MinRtt { get; init; }

    /// <summary>Gets the estimated send rate in bits per second, or null.</summary>
    public long? EstimatedSendRate { get; init; }
}
