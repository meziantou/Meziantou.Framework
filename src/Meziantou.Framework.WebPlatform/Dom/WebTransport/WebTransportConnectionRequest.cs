namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// A request of a WebTransport session, given to <see cref="DocumentLoader.ConnectWebTransportAsync"/>
/// (https://w3c.github.io/webtransport/#initialize-webtransport-over-http): an extended CONNECT request to an
/// <c>https:</c> URL over HTTP/3.
/// </summary>
public sealed class WebTransportConnectionRequest
{
    /// <summary>Gets the URL of the session, serialized: an <c>https:</c> URL without fragment.</summary>
    public required string Url { get; init; }

    /// <summary>Gets the headers of the CONNECT request: <c>origin</c>, then the headers of the <c>headers</c> option. The names are lowercase.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Headers { get; init; } = [];

    /// <summary>Gets the application protocols the page offers (the <c>protocols</c> option), sent in <c>wt-available-protocols</c>.</summary>
    public IReadOnlyList<string> Protocols { get; init; } = [];

    /// <summary>
    /// Gets the SHA-256 hashes of the certificates the page accepts instead of the certificates trusted by the user agent
    /// (the <c>serverCertificateHashes</c> option). The certificate must also be valid now, for at most 14 days.
    /// </summary>
    public IReadOnlyList<ReadOnlyMemory<byte>> ServerCertificateHashes { get; init; } = [];

    /// <summary>Gets the congestion control the page prefers: <c>default</c>, <c>throughput</c> or <c>low-latency</c>.</summary>
    public string CongestionControl { get; init; } = "default";

    /// <summary>Gets a value indicating whether the session may share its connection with other sessions (the <c>allowPooling</c> option).</summary>
    public bool AllowPooling { get; init; }

    /// <summary>Gets the number of unidirectional streams the page expects the server to open at the same time (the <c>anticipatedConcurrentIncomingUnidirectionalStreams</c> option), or null.</summary>
    public int? AnticipatedConcurrentIncomingUnidirectionalStreams { get; init; }

    /// <summary>Gets the number of bidirectional streams the page expects the server to open at the same time (the <c>anticipatedConcurrentIncomingBidirectionalStreams</c> option), or null.</summary>
    public int? AnticipatedConcurrentIncomingBidirectionalStreams { get; init; }

    /// <summary>Gets a value indicating whether the session requires datagrams, so it cannot fall back to HTTP/2 (the <c>requireUnreliable</c> option).</summary>
    public bool RequireUnreliable { get; init; }
}
