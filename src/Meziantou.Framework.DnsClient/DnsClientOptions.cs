using System.Net;

namespace Meziantou.Framework.DnsClient;

/// <summary>
/// Configuration options for <see cref="DnsClient"/>.
/// </summary>
public sealed class DnsClientOptions
{
    /// <summary>Gets or sets the query timeout. Default is 5 seconds.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Gets or sets a value indicating whether to include EDNS(0) OPT records in queries. Default is <see langword="true"/>.</summary>
    public bool EnableEdns { get; set; } = true;

    /// <summary>
    /// Gets or sets the EDNS UDP payload size. Default is 1232, the value recommended by RFC 9715 and DNS Flag Day
    /// 2020 to avoid IP fragmentation; larger UDP answers are routinely dropped by middleboxes.
    /// </summary>
    public ushort EdnsUdpPayloadSize { get; set; } = 1232;

    /// <summary>Gets or sets a value indicating whether to set the DNSSEC OK (DO) flag. Default is <see langword="false"/>.</summary>
    public bool DnssecOk { get; set; }

    /// <summary>Gets or sets how DNSSEC responses are validated. Default is <see cref="DnssecValidationMode.None"/>.</summary>
    public DnssecValidationMode DnssecValidationMode { get; set; }

    /// <summary>Gets or sets the DNSSEC trust anchors used for local validation.</summary>
    public IReadOnlyList<DnssecTrustAnchor> DnssecTrustAnchors { get; set; } = global::Meziantou.Framework.DnsClient.DnssecTrustAnchors.Root;

    /// <summary>Gets or sets the time provider used for DNSSEC signature lifetime checks.</summary>
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;

    /// <summary>
    /// Gets or sets a value indicating whether a truncated (TC) UDP response is automatically re-issued over TCP,
    /// as RFC 1035 section 4.2.1 requires. Default is <see langword="true"/>. Only applies to <see cref="DnsClientProtocol.Udp"/>.
    /// </summary>
    public bool RetryTruncatedOverTcp { get; set; } = true;

    /// <summary>
    /// Gets or sets the HTTP version used for DNS over HTTPS requests. Default is HTTP/2, which every DoH resolver
    /// supports; requesting HTTP/3 costs a failed QUIC attempt per connection on hosts or servers without it.
    /// </summary>
    public Version HttpVersion { get; set; } = System.Net.HttpVersion.Version20;

    /// <summary>Gets or sets the HTTP version policy used for DNS over HTTPS requests. Default is <see cref="HttpVersionPolicy.RequestVersionOrLower"/>.</summary>
    public HttpVersionPolicy HttpVersionPolicy { get; set; } = HttpVersionPolicy.RequestVersionOrLower;

    /// <summary>Gets or sets the HTTP message handler for DNS over HTTPS. When <see langword="null"/>, a default handler is used.</summary>
    public HttpMessageHandler? HttpHandler { get; set; }

    /// <summary>Gets or sets the resolver used to resolve DNS server hostnames to IP addresses.</summary>
    public Func<string, IReadOnlyList<IPAddress>>? ServerAddressResolver { get; set; }
}
