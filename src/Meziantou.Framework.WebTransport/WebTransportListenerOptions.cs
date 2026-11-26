using System.Net;
using System.Security.Cryptography.X509Certificates;
using Meziantou.Framework.MsQuic;

namespace Meziantou.Framework.WebTransport;

/// <summary>The options of a <see cref="WebTransportListener"/>.</summary>
public sealed class WebTransportListenerOptions
{
    /// <summary>Gets or sets the address to listen on. Port 0 picks a free port.</summary>
    public IPEndPoint ListenEndPoint { get; set; } = new(IPAddress.IPv6Any, 0);

    /// <summary>Gets or sets the certificate of the server, with its private key. On Linux and macOS, the private key must be exportable.</summary>
    public X509Certificate2? ServerCertificate { get; set; }

    /// <summary>Gets or sets the congestion control algorithm of the connections.</summary>
    public MsQuicCongestionControlAlgorithm CongestionControlAlgorithm { get; set; }

    /// <summary>Gets or sets the time without activity after which a connection is closed. <see cref="TimeSpan.Zero"/> keeps the default of MsQuic.</summary>
    public TimeSpan IdleTimeout { get; set; }

    /// <summary>Gets or sets the number of received datagrams kept per session until <see cref="WebTransportSession.ReceiveDatagramAsync"/> reads them. The default is 1024.</summary>
    public int ReceivedDatagramQueueCapacity { get; set; } = 1024;

    /// <summary>Gets or sets the number of streams a client can open at the same time, of each type. The default is 100.</summary>
    public int MaxIncomingStreams { get; set; } = 100;
}
