using System.Net.Security;
using Meziantou.Framework.MsQuic;

namespace Meziantou.Framework.WebTransport;

/// <summary>The options of <see cref="WebTransportSession.ConnectAsync"/>.</summary>
public sealed class WebTransportClientOptions
{
    /// <summary>Gets the headers added to the CONNECT request, such as <c>origin</c>. Names are lowercase.</summary>
    public IList<KeyValuePair<string, string>> Headers { get; } = [];

    /// <summary>Gets the application protocols the client offers, sent in the <c>wt-available-protocols</c> header. The server picks one in <see cref="WebTransportSession.Protocol"/>.</summary>
    public IList<string> Protocols { get; } = [];

    /// <summary>
    /// Gets the SHA-256 hashes of the certificates the client accepts. When the list is not empty, the certificate of the
    /// server is accepted when its hash is in the list, instead of being validated against the trusted roots, as the
    /// <c>serverCertificateHashes</c> option of the browsers does.
    /// </summary>
    public IList<byte[]> ServerCertificateHashes { get; } = [];

    /// <summary>Gets or sets the callback that validates the certificate of the server, when <see cref="ServerCertificateHashes"/> is empty.</summary>
    public RemoteCertificateValidationCallback? RemoteCertificateValidationCallback { get; set; }

    /// <summary>Gets or sets the congestion control algorithm of the connection.</summary>
    public MsQuicCongestionControlAlgorithm CongestionControlAlgorithm { get; set; }

    /// <summary>Gets or sets the time without activity after which the connection is closed. <see cref="TimeSpan.Zero"/> keeps the default of MsQuic.</summary>
    public TimeSpan IdleTimeout { get; set; }

    /// <summary>Gets or sets the number of received datagrams kept until <see cref="WebTransportSession.ReceiveDatagramAsync"/> reads them; the oldest are dropped. The default is 1024.</summary>
    public int ReceivedDatagramQueueCapacity { get; set; } = 1024;

    /// <summary>Gets or sets the number of streams the server can open at the same time, of each type. The default is 100.</summary>
    public int MaxIncomingStreams { get; set; } = 100;
}
