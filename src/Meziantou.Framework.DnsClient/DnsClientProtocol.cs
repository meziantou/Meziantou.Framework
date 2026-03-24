namespace Meziantou.Framework.DnsClient;

/// <summary>Specifies the DNS transport protocol to use for queries.</summary>
public enum DnsClientProtocol
{
    /// <summary>DNS over UDP (RFC 1035), port 53.</summary>
    Udp,

    /// <summary>DNS over TCP (RFC 1035, RFC 7766), port 53.</summary>
    Tcp,

    /// <summary>DNS over TLS (RFC 7858), port 853.</summary>
    Tls,

    /// <summary>DNS over HTTPS (RFC 8484), port 443.</summary>
    Https,

    /// <summary>DNS over QUIC (RFC 9250), port 853.</summary>
    Quic,
}
