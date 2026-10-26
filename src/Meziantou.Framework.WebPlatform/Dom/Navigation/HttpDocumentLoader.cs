using System.Net;
using System.Net.Http;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// A <see cref="DocumentLoader"/> that gets the resources with HTTP requests (see <see cref="DocumentLoader.Create"/>): the
/// method, the headers and the body of the request, and the cookies of <see cref="DocumentRequest.CookieJar"/> when
/// <see cref="DocumentRequest.IncludeCredentials"/> is true.
/// </summary>
internal sealed class HttpDocumentLoader(HttpClient httpClient) : DocumentLoader
{
    // https://fetch.spec.whatwg.org/#http-redirect-fetch: a redirect chain longer than 20 is a network error
    private const int MaxRedirects = 20;

    private static readonly Lazy<HttpDocumentLoader> SharedLoader = new(() => new HttpDocumentLoader(new HttpClient(new SocketsHttpHandler
    {
        // The redirects are followed by the loader, so that the cookies of each response are stored
        AllowAutoRedirect = false,

        // The cookies are the ones of the browsing context (DocumentRequest.CookieJar), not the ones of the shared client
        UseCookies = false,
        AutomaticDecompression = DecompressionMethods.All,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    })));

    public static HttpDocumentLoader Shared => SharedLoader.Value;

    public override async ValueTask<DocumentResource?> LoadAsync(DocumentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var url = UrlParser.Parse(request.Url, baseUrl: null);
        var referrer = request.Referrer is null ? null : UrlParser.Parse(request.Referrer, baseUrl: null);
        var referrerPolicy = request.ReferrerPolicy;

        // The URLs of the redirects between the request URL and the response URL (DocumentResource.RedirectUrls)
        var redirectUrls = new List<string>();

        // The URL list of the request (https://fetch.spec.whatwg.org/#concept-request-url-list), for the Fetch Metadata headers
        var urlList = new List<UrlRecord>();
        for (var redirectCount = 0; url is { Scheme: "http" or "https" }; redirectCount++)
        {
            // https://fetch.spec.whatwg.org/#http-redirect-fetch, step 12: a 301, 302 or 303 redirect of a POST request is a GET request without body
            using var message = new HttpRequestMessage(redirectCount == 0 ? new HttpMethod(request.Method) : HttpMethod.Get, url.Serialize());
            var hasBody = redirectCount == 0 && request.Body is not null;
            if (hasBody)
            {
                message.Content = new ByteArrayContent(request.Body!.Value.ToArray());
                if (request.BodyContentType is { } contentType)
                {
                    message.Content.Headers.TryAddWithoutValidation("Content-Type", contentType);
                }
            }

            // The headers of the request of fetch() or XMLHttpRequest (the Content-Length of the content is computed by the client)
            foreach (var (name, value) in request.Headers)
            {
                if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) || (redirectCount > 0 && name.Equals("Referer", StringComparison.OrdinalIgnoreCase)))
                    continue;

                if (name.StartsWith("Content-", StringComparison.OrdinalIgnoreCase))
                {
                    if (hasBody && !name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                    {
                        message.Content!.Headers.TryAddWithoutValidation(name, value);
                    }

                    continue;
                }

                message.Headers.TryAddWithoutValidation(name, value);
            }

            if (!message.Headers.Contains("Accept"))
            {
                message.Headers.TryAddWithoutValidation("Accept", GetAcceptHeader(request.Destination));
            }

            // The headers of the Fetch Metadata (the requests of the fetch algorithm have them: the library follows their redirects)
            urlList.Add(url);
            if (request.FollowRedirects && request.Destination != RequestDestination.Empty)
            {
                var metadataHeaders = new List<KeyValuePair<string, string>>();
                FetchMetadata.AppendHeaders(metadataHeaders, request, urlList);
                foreach (var (name, value) in metadataHeaders)
                {
                    message.Headers.TryAddWithoutValidation(name, value);
                }
            }

            // The referrer is determined again for each URL of the redirect chain (https://fetch.spec.whatwg.org/#main-fetch, step 9)
            if (redirectCount > 0 && referrer is not null)
            {
                referrer = ReferrerPolicies.DetermineReferrer(referrer, url, referrerPolicy) is { } redirectReferrer ? UrlParser.Parse(redirectReferrer, baseUrl: null) : null;
            }

            if (referrer is not null && !message.Headers.Contains("Referer"))
            {
                message.Headers.TryAddWithoutValidation("Referer", referrer.Serialize());
            }

            // The cookies of the URL, after the redirects of the URL list
            var requestUri = message.RequestUri!;
            string[] previousUrls = [.. request.RedirectUrls, .. urlList.Take(urlList.Count - 1).Select(previous => previous.Serialize())];
            if (request.IncludeCredentials && request.GetCookieHeader(url.Serialize(), previousUrls) is { Length: > 0 } cookies)
            {
                message.Headers.TryAddWithoutValidation("Cookie", cookies);
            }

#pragma warning disable CA2000 // The response of an event stream is disposed when its body is read (see ResponseChunks)
            HttpResponseMessage? response = await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
#pragma warning restore CA2000
            try
            {
                if (request.IncludeCredentials && response.Headers.NonValidated.TryGetValues("Set-Cookie", out var setCookies))
                {
                    request.StoreCookies(setCookies, url.Serialize(), previousUrls);
                }

                // Redirects not followed by the client (https://fetch.spec.whatwg.org/#redirect-status)
                if (request.FollowRedirects && (int)response.StatusCode is 301 or 302 or 303 or 307 or 308 && response.Headers.TryGetValues("Location", out var locations))
                {
                    if (redirectCount >= MaxRedirects)
                        return null;

                    // https://fetch.spec.whatwg.org/#http-redirect-fetch, step 16: the Referrer-Policy header of the redirect
                    if (ReferrerPolicies.ParseHeader(GetHeaders(response)) is { Length: > 0 } redirectReferrerPolicy)
                    {
                        referrerPolicy = redirectReferrerPolicy;
                    }

                    if (redirectCount > 0)
                    {
                        redirectUrls.Add(url.Serialize());
                    }

                    url = UrlParser.Parse(locations.First(), url);
                    continue;
                }

                var finalUrl = response.RequestMessage?.RequestUri is { } finalUri && finalUri != requestUri ? finalUri.AbsoluteUri : url.Serialize();

                // An event stream arrives over time and may never end (https://html.spec.whatwg.org/multipage/server-sent-events.html),
                // and the HTML document of a navigation is parsed as its body arrives (https://html.spec.whatwg.org/multipage/parsing.html#the-input-byte-stream):
                // the response is returned with its headers, and its body is read as it arrives. The bodies of the redirects and of
                // the prefetches (Sec-Purpose), which are not parsed as they arrive, are read before the response is returned.
                var mediaType = response.Content.Headers.ContentType?.MediaType;
                var isStreamedDocument = request.Mode == RequestMode.Navigate && (mediaType is null || mediaType.Equals("text/html", StringComparison.OrdinalIgnoreCase))
                    && (int)response.StatusCode is not (301 or 302 or 303 or 307 or 308) && !request.Headers.Any(header => header.Key.Equals("Sec-Purpose", StringComparison.OrdinalIgnoreCase));
                // The requests whose body is read as it arrives (StreamsResponseBody, e.g. fetch()) are streamed too, except the redirects
                if (isStreamedDocument || (request.Mode != RequestMode.Navigate && mediaType is not null && mediaType.Equals("text/event-stream", StringComparison.OrdinalIgnoreCase))
                    || (request.StreamsResponseBody && (int)response.StatusCode is not (301 or 302 or 303 or 307 or 308)))
                {
                    var streamedResponse = response;
                    response = null;
                    return new DocumentResource(ReadOnlyMemory<byte>.Empty) { Url = finalUrl, RedirectUrls = redirectUrls, StatusCode = (int)streamedResponse.StatusCode, StatusText = streamedResponse.ReasonPhrase ?? "", Headers = GetHeaders(streamedResponse), RemainingContentBytes = new ResponseChunks(streamedResponse, cancellationToken) };
                }

                // The body of an HTTP error (e.g. a 404 page) is displayed, as for any response
                var content = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                return new DocumentResource(content) { Url = finalUrl, RedirectUrls = redirectUrls, StatusCode = (int)response.StatusCode, StatusText = response.ReasonPhrase ?? "", Headers = GetHeaders(response) };
            }
            finally
            {
                response?.Dispose();
            }
        }

        return null;
    }

    /// <summary>
    /// The body of a response, read as it arrives (<see cref="DocumentResource.RemainingContentBytes"/>). The response is
    /// disposed at the end of the body, when an enumerator is disposed (even if it was never moved), or when the request is
    /// canceled (even if the sequence is never enumerated).
    /// </summary>
    private sealed class ResponseChunks : IAsyncEnumerable<ReadOnlyMemory<byte>>
    {
        private readonly HttpResponseMessage _response;
        private readonly CancellationTokenRegistration _registration;

        public ResponseChunks(HttpResponseMessage response, CancellationToken requestCancellationToken)
        {
            _response = response;
            _registration = requestCancellationToken.Register(static state => ((HttpResponseMessage)state!).Dispose(), response);
        }

        public IAsyncEnumerator<ReadOnlyMemory<byte>> GetAsyncEnumerator(CancellationToken cancellationToken = default) => new Enumerator(this, Read(cancellationToken).GetAsyncEnumerator(cancellationToken));

        private async IAsyncEnumerable<ReadOnlyMemory<byte>> Read([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var stream = await _response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var buffer = new byte[8192];
            while (true)
            {
                var count = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (count == 0)
                    yield break;

                yield return buffer.AsMemory(0, count).ToArray();
            }
        }

        private void Dispose()
        {
            _registration.Dispose();
            _response.Dispose();
        }

        private sealed class Enumerator(ResponseChunks owner, IAsyncEnumerator<ReadOnlyMemory<byte>> inner) : IAsyncEnumerator<ReadOnlyMemory<byte>>
        {
            public ReadOnlyMemory<byte> Current => inner.Current;

            public async ValueTask<bool> MoveNextAsync()
            {
                if (await inner.MoveNextAsync().ConfigureAwait(false))
                    return true;

                owner.Dispose();
                return false;
            }

            public async ValueTask DisposeAsync()
            {
                await inner.DisposeAsync().ConfigureAwait(false);
                owner.Dispose();
            }
        }
    }

    /// <summary>
    /// Sends the opening handshake of a WebSocket as an HTTP/1.1 upgrade request (https://www.rfc-editor.org/rfc/rfc6455#section-4.1),
    /// with the cookies of the request. The connection of a <c>101</c> response is the stream of the response, on which the
    /// frames are read and written. The redirects are not followed (unless the handler of the client follows them).
    /// </summary>
    public override async ValueTask<DocumentResource?> ConnectWebSocketAsync(DocumentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (UrlParser.Parse(request.Url, baseUrl: null) is not { Scheme: "http" or "https" } url)
            return null;

        // The WebSocket protocol is an upgrade of an HTTP/1.1 connection (WebSockets over HTTP/2, RFC 8441, are not used)
        using var message = new HttpRequestMessage(HttpMethod.Get, url.Serialize())
        {
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
        };

        foreach (var (name, value) in request.Headers)
        {
            message.Headers.TryAddWithoutValidation(name, value);
        }

        if (request.GetCookieHeader() is { Length: > 0 } cookies)
        {
            message.Headers.TryAddWithoutValidation("Cookie", cookies);
        }

#pragma warning disable CA2000 // The response of a 101 status owns the connection, which is disposed by the library
        var response = await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
#pragma warning restore CA2000
        try
        {
            if (response.Headers.NonValidated.TryGetValues("Set-Cookie", out var setCookies))
            {
                request.StoreCookies(setCookies);
            }

            var headers = GetHeaders(response);
            if (response.StatusCode != HttpStatusCode.SwitchingProtocols)
                return new DocumentResource(ReadOnlyMemory<byte>.Empty) { StatusCode = (int)response.StatusCode, StatusText = response.ReasonPhrase ?? "", Headers = headers };

            // The content of a 101 response is the connection: it can be read and written
            var connection = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var resource = new DocumentResource(ReadOnlyMemory<byte>.Empty) { StatusCode = 101, StatusText = response.ReasonPhrase ?? "", Headers = headers, Connection = connection };
            response = null;
            return resource;
        }
        finally
        {
            response?.Dispose();
        }
    }

    /// <summary>Returns the headers of a response, including the headers of its content (e.g. <c>Content-Type</c>), as received.</summary>
    private static List<KeyValuePair<string, string>> GetHeaders(HttpResponseMessage response)
    {
        var headers = new List<KeyValuePair<string, string>>();
        foreach (var header in response.Headers.NonValidated.Concat(response.Content.Headers.NonValidated))
        {
            foreach (var value in header.Value)
            {
                headers.Add(new(header.Key, value));
            }
        }

        return headers;
    }

    /// <summary>The Accept header of a request (https://fetch.spec.whatwg.org/#fetching, step 12).</summary>
    private static string GetAcceptHeader(RequestDestination destination) => destination switch
    {
        RequestDestination.Style => "text/css,*/*;q=0.1",
        RequestDestination.Json => "application/json,*/*;q=0.5",
        RequestDestination.Text => "text/plain,*/*;q=0.5",
        RequestDestination.Script or RequestDestination.PaintWorklet or RequestDestination.AudioWorklet or RequestDestination.Font or RequestDestination.Ping or RequestDestination.Report or RequestDestination.Audio or RequestDestination.Video or RequestDestination.Track => "*/*",
        RequestDestination.Image => "image/avif,image/webp,image/apng,image/svg+xml,image/*,*/*;q=0.8",
        _ => "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8",
    };
}
