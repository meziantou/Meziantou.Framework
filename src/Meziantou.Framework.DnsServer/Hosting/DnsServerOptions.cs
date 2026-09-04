using System.Net;
using System.Security.Cryptography.X509Certificates;

namespace Meziantou.Framework.DnsServer.Hosting;

/// <summary>Configures the DNS server listeners.</summary>
public sealed class DnsServerOptions
{
    private int _maxUdpResponseSize = 1232;

    internal List<UdpListenerOptions> UdpListeners { get; } = [];
    internal List<TcpListenerOptions> TcpListeners { get; } = [];
    internal List<TlsListenerOptions> TlsListeners { get; } = [];
    internal List<QuicListenerOptions> QuicListeners { get; } = [];

    /// <summary>
    /// Gets or sets the largest UDP response the server will send, in bytes. A client's advertised EDNS
    /// payload size is clamped to this value; larger answers are truncated so the client retries over TCP.
    /// Defaults to 1232 bytes, the size recommended by DNS Flag Day 2020, which also limits how much a
    /// spoofed query can amplify.
    /// </summary>
    public int MaxUdpResponseSize
    {
        get => _maxUdpResponseSize;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 512);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, ushort.MaxValue);
            _maxUdpResponseSize = value;
        }
    }

    /// <summary>
    /// Gets or sets how long a TCP or DNS over TLS connection may sit without a complete query before the
    /// server closes it (RFC 7766 6.2.3). Defaults to 30 seconds. Use <see cref="Timeout.InfiniteTimeSpan"/>
    /// to disable, at the cost of letting idle connections accumulate.
    /// </summary>
    public TimeSpan TcpIdleTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets how long a DNS over QUIC connection may stay idle before it is closed. Defaults to
    /// 30 seconds.
    /// </summary>
    public TimeSpan QuicIdleTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets how many queries a single connection may have in flight at once. Defaults to 16.
    /// </summary>
    public int MaxConcurrentQueriesPerConnection { get; set; } = 16;

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
