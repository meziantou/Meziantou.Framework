using System.Net;
using System.Security.Cryptography.X509Certificates;

namespace Meziantou.Framework.DnsServer.Hosting;

/// <summary>Configures the DNS server listeners.</summary>
public sealed class DnsServerOptions
{
    internal List<UdpListenerOptions> UdpListeners { get; } = [];
    internal List<TcpListenerOptions> TcpListeners { get; } = [];
    internal List<TlsListenerOptions> TlsListeners { get; } = [];
    internal List<QuicListenerOptions> QuicListeners { get; } = [];

    /// <summary>Adds a UDP listener on the specified port.</summary>
    public DnsServerOptions AddUdpListener(int port = 53, IPAddress? bindAddress = null)
    {
        UdpListeners.Add(new UdpListenerOptions
        {
            Port = port,
            BindAddress = bindAddress ?? IPAddress.Any,
        });

        return this;
    }

    /// <summary>Adds a TCP listener on the specified port.</summary>
    public DnsServerOptions AddTcpListener(int port = 53, IPAddress? bindAddress = null)
    {
        TcpListeners.Add(new TcpListenerOptions
        {
            Port = port,
            BindAddress = bindAddress ?? IPAddress.Any,
        });

        return this;
    }

    /// <summary>Adds a DNS over TLS listener on the specified port.</summary>
    public DnsServerOptions AddTlsListener(int port, X509Certificate2 certificate, IPAddress? bindAddress = null)
    {
        ArgumentNullException.ThrowIfNull(certificate);

        TlsListeners.Add(new TlsListenerOptions
        {
            Port = port,
            BindAddress = bindAddress ?? IPAddress.Any,
            Certificate = certificate,
        });

        return this;
    }

    /// <summary>Adds a DNS over QUIC listener on the specified port.</summary>
    public DnsServerOptions AddQuicListener(int port, X509Certificate2 certificate, IPAddress? bindAddress = null)
    {
        ArgumentNullException.ThrowIfNull(certificate);

        QuicListeners.Add(new QuicListenerOptions
        {
            Port = port,
            BindAddress = bindAddress ?? IPAddress.Any,
            Certificate = certificate,
        });

        return this;
    }
}
