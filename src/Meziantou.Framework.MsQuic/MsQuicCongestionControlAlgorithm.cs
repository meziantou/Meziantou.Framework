namespace Meziantou.Framework.MsQuic;

/// <summary>The congestion control algorithm of a connection.</summary>
public enum MsQuicCongestionControlAlgorithm
{
    /// <summary>The algorithm MsQuic uses by default (CUBIC).</summary>
    Default,

    /// <summary>CUBIC (RFC 9438), a loss-based algorithm that favors throughput.</summary>
    Cubic,

    /// <summary>BBR, a model-based algorithm that keeps the latency low.</summary>
    Bbr,
}
