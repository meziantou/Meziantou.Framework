using System.Net.Http.Headers;

namespace Meziantou.Framework.DnsClient.Transport;

internal sealed class DnsHttpsTransport : IDnsTransport
{
    private static readonly MediaTypeHeaderValue DnsMessageMediaType = new("application/dns-message");

    private readonly HttpClient _httpClient;
    private readonly Uri _endpoint;
    private readonly bool _disposeHttpClient;

    public DnsHttpsTransport(Uri endpoint, HttpMessageHandler? handler)
    {
        _endpoint = endpoint;
        if (handler is not null)
        {
            _httpClient = new HttpClient(handler, disposeHandler: false);
            _disposeHttpClient = true;
        }
        else
        {
            _httpClient = new HttpClient();
            _disposeHttpClient = true;
        }
    }

    public async Task<byte[]> SendAsync(byte[] query, CancellationToken cancellationToken)
    {
        // RFC 8484: DNS over HTTPS using POST with application/dns-message
        using var content = new ByteArrayContent(query);
        content.Headers.ContentType = DnsMessageMediaType;

        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = content,
            Version = System.Net.HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionOrHigher,
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/dns-message"));

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposeHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}
