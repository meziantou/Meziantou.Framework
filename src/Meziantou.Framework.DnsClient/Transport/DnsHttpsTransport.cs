using System.Net.Http.Headers;

namespace Meziantou.Framework.DnsClient.Transport;

internal sealed class DnsHttpsTransport : IDnsTransport
{
    private static readonly MediaTypeHeaderValue DnsMessageMediaType = new("application/dns-message");

    /// <summary>A DNS message can never exceed 65535 bytes, so anything larger is not a response worth buffering.</summary>
    private const int MaxResponseLength = 65535;

    private readonly HttpClient _httpClient;
    private readonly Uri _endpoint;
    private readonly Version _httpVersion;
    private readonly HttpVersionPolicy _httpVersionPolicy;

    public DnsHttpsTransport(Uri endpoint, HttpMessageHandler? handler, Version httpVersion, HttpVersionPolicy httpVersionPolicy)
    {
        _endpoint = endpoint;
        _httpVersion = httpVersion;
        _httpVersionPolicy = httpVersionPolicy;

        // disposeHandler: false is what protects a caller-supplied handler; the HttpClient wrapper is always ours.
        if (handler is not null)
        {
            _httpClient = new HttpClient(handler, disposeHandler: false);
        }
        else
        {
            // A default handler with a bounded connection lifetime, so a long-lived client notices DNS changes for
            // the resolver's own hostname. Ownership transfers to the HttpClient via disposeHandler: true.
            _httpClient = CreateDefaultHttpClient();
        }

        _httpClient.MaxResponseContentBufferSize = MaxResponseLength;
    }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Ownership of the handler transfers to the HttpClient, which is disposed by this transport.")]
    private static HttpClient CreateDefaultHttpClient()
    {
        return new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) }, disposeHandler: true);
    }

    public async Task<byte[]> SendAsync(byte[] query, CancellationToken cancellationToken)
    {
        // RFC 8484: DNS over HTTPS using POST with application/dns-message
        using var content = new ByteArrayContent(query);
        content.Headers.ContentType = DnsMessageMediaType;

        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = content,
            Version = _httpVersion,
            VersionPolicy = _httpVersionPolicy,
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/dns-message"));

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength > MaxResponseLength)
            throw new DnsProtocolException($"The DNS over HTTPS response declares {response.Content.Headers.ContentLength} bytes, which exceeds the {MaxResponseLength}-byte maximum for a DNS message.");

        var body = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        if (body.Length > MaxResponseLength)
            throw new DnsProtocolException($"The DNS over HTTPS response is {body.Length} bytes, which exceeds the {MaxResponseLength}-byte maximum for a DNS message.");

        return body;
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
