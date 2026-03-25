using System.Net;
using System.Security.Cryptography.X509Certificates;

namespace Meziantou.Framework.DnsServer.Hosting;

/// <summary>Configuration for a DNS over QUIC listener.</summary>
public sealed class QuicListenerOptions
{
    /// <summary>Gets or sets the port to listen on.</summary>
    public int Port { get; set; } = 853;

    /// <summary>Gets or sets the address to bind to.</summary>
    public IPAddress BindAddress { get; set; } = IPAddress.Any;

    /// <summary>Gets or sets the TLS certificate.</summary>
    public X509Certificate2 Certificate { get; set; } = null!;
}
