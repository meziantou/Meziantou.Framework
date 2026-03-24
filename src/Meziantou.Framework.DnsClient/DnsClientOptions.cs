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

    /// <summary>Gets or sets the EDNS UDP payload size. Default is 4096.</summary>
    public ushort EdnsUdpPayloadSize { get; set; } = 4096;

    /// <summary>Gets or sets a value indicating whether to set the DNSSEC OK (DO) flag. Default is <see langword="false"/>.</summary>
    public bool DnssecOk { get; set; }

    /// <summary>Gets or sets the HTTP message handler for DNS over HTTPS. When <see langword="null"/>, a default handler is used.</summary>
    public HttpMessageHandler? HttpHandler { get; set; }
}
