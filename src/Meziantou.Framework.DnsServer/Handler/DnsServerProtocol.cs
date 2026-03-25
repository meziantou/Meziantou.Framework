namespace Meziantou.Framework.DnsServer.Handler;

/// <summary>Specifies the transport protocol used for a DNS request.</summary>
public enum DnsServerProtocol
{
    /// <summary>DNS over UDP (RFC 1035), typically port 53.</summary>
    Udp,

    /// <summary>DNS over TCP (RFC 1035, RFC 7766), typically port 53.</summary>
    Tcp,

    /// <summary>DNS over TLS (RFC 7858), typically port 853.</summary>
    Tls,

    /// <summary>DNS over HTTPS (RFC 8484), typically port 443.</summary>
    Https,

    /// <summary>DNS over QUIC (RFC 9250), typically port 853.</summary>
    Quic,
}
