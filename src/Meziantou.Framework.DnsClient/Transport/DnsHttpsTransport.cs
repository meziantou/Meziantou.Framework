using System.Buffers;
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

    public async Task<DnsTransportResponse> SendAsync(byte[] query, CancellationToken cancellationToken)
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

        var body = await ReadBodyAsync(response.Content, cancellationToken).ConfigureAwait(false);
        return new DnsTransportResponse(body, GetAgeInSeconds(response));
    }

    /// <summary>
    /// Reads the body without trusting the declared length. At most one byte beyond the maximum is read, so a server
    /// sending an unknown-length or endless body cannot make the client buffer more than a DNS message can hold.
    /// </summary>
    private static async Task<byte[]> ReadBodyAsync(HttpContent content, CancellationToken cancellationToken)
    {
        var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            const int MaxReadLength = MaxResponseLength + 1;

            var buffer = ArrayPool<byte>.Shared.Rent(MaxReadLength);
            try
            {
                var length = await stream.ReadAtLeastAsync(buffer.AsMemory(0, MaxReadLength), MaxReadLength, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);
                if (length > MaxResponseLength)
                    throw new DnsProtocolException($"The DNS over HTTPS response exceeds the {MaxResponseLength}-byte maximum for a DNS message.");

                return buffer.AsSpan(0, length).ToArray();
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }

    /// <summary>
    /// RFC 8484 5.1: a response served from an HTTP cache still carries the TTLs the server originally sent, so the
    /// time it spent in that cache has to be taken out of them. The <c>Age</c> header is how far the message is into
    /// its lifetime.
    /// </summary>
    private static uint GetAgeInSeconds(HttpResponseMessage response)
    {
        if (response.Headers.Age is not { } age)
            return 0;

        var seconds = age.TotalSeconds;
        if (seconds <= 0)
            return 0;

        return seconds >= uint.MaxValue ? uint.MaxValue : (uint)seconds;
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
