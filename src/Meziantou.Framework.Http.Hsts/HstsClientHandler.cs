using System.Net;

namespace Meziantou.Framework.Http;

/// <summary>An HTTP message handler that automatically upgrades HTTP requests to HTTPS based on HSTS (HTTP Strict Transport Security) policies.</summary>
/// <remarks>
/// The handler follows redirects itself so that every hop is checked against the HSTS policies. When the inner
/// handler is a <see cref="SocketsHttpHandler"/> or an <see cref="HttpClientHandler"/> configured to follow
/// redirects, the constructor turns its <c>AllowAutoRedirect</c> off and takes the redirects over; an inner
/// handler already configured not to follow them keeps returning the redirect responses as is.
/// </remarks>
/// <example>
/// <code>
/// var policies = new HstsDomainPolicyCollection(includePreloadDomains: true);
/// using var client = new HttpClient(new HstsClientHandler(new SocketsHttpHandler(), policies), disposeHandler: true);
///
/// // Automatically upgrade to HTTPS as github.com is in the HSTS preload list
/// using var response = await client.GetAsync("http://github.com");
/// </code>
/// </example>
public sealed class HstsClientHandler : DelegatingHandler
{
    private const long MaxMaxAgeInSeconds = 100L * 365 * 24 * 60 * 60;

    private readonly HstsDomainPolicyCollection _configuration;

    // 0 means this handler does not follow redirects, because the inner handler was already configured not to
    // follow them or is not a type the redirects can be taken over from.
    private readonly int _maxAutomaticRedirections;

    /// <summary>Initializes a new instance of the <see cref="HstsClientHandler"/> class with the default HSTS policy collection.</summary>
    /// <param name="innerHandler">The inner HTTP message handler to delegate requests to.</param>
    public HstsClientHandler(HttpMessageHandler innerHandler)
        : this(innerHandler, HstsDomainPolicyCollection.Default)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="HstsClientHandler"/> class with a custom HSTS policy collection.</summary>
    /// <param name="innerHandler">The inner HTTP message handler to delegate requests to.</param>
    /// <param name="configuration">The HSTS policy collection to use for determining which requests to upgrade.</param>
    public HstsClientHandler(HttpMessageHandler innerHandler, HstsDomainPolicyCollection configuration)
        : base(innerHandler)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        _configuration = configuration;
        _maxAutomaticRedirections = TakeOverAutomaticRedirections(innerHandler);
    }

    // The inner handler follows redirects below this handler, so the requests it derives from a redirect
    // response never go through the HSTS upgrade and reach an HSTS host in cleartext. Take the redirects over
    // when the inner handler was going to follow them anyway, so that every hop is checked; a handler
    // explicitly configured not to follow them keeps that behavior.
    private static int TakeOverAutomaticRedirections(HttpMessageHandler? handler)
    {
        while (handler is DelegatingHandler delegatingHandler)
        {
            handler = delegatingHandler.InnerHandler;
        }

        switch (handler)
        {
            case SocketsHttpHandler socketsHttpHandler when socketsHttpHandler.AllowAutoRedirect:
                socketsHttpHandler.AllowAutoRedirect = false;
                return socketsHttpHandler.MaxAutomaticRedirections;

            case HttpClientHandler httpClientHandler when httpClientHandler.AllowAutoRedirect:
                httpClientHandler.AllowAutoRedirect = false;
                return httpClientHandler.MaxAutomaticRedirections;

            default:
                return 0;
        }
    }

    // internal for tests: the redirect loop cannot be reached through a mock inner handler, as the public
    // constructors only take the redirects over from the handler types that would have followed them
    internal HstsClientHandler(HttpMessageHandler innerHandler, HstsDomainPolicyCollection configuration, int maxAutomaticRedirections)
        : base(innerHandler)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        _configuration = configuration;
        _maxAutomaticRedirections = maxAutomaticRedirections;
    }

    /// <summary>Sends an HTTP request, upgrading to HTTPS if required by HSTS policy, and processes the Strict-Transport-Security response header.</summary>
    /// <param name="request">The HTTP request message to send.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>The HTTP response message.</returns>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var remainingRedirections = _maxAutomaticRedirections;
        while (true)
        {
            UpgradeRequest(request);

            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            ProcessStrictTransportSecurityHeader(request, response);

            if (remainingRedirections <= 0)
                return response;

            var location = GetUriForRedirect(request, response);
            if (location is null)
                return response;

            remainingRedirections--;
            PrepareRedirect(request, response, location);

            // Release the connection before issuing the next request
            response.Dispose();
        }
    }

    private void UpgradeRequest(HttpRequestMessage request)
    {
        // Use IdnHost: the preload list stores internationalized domains in their Punycode form
        if (request.RequestUri?.Scheme == Uri.UriSchemeHttp && _configuration.MustUpgradeRequest(request.RequestUri.IdnHost))
        {
            // https://datatracker.ietf.org/doc/html/rfc6797#section-8.3
            // The default port becomes 443; an explicit port is kept as is.
            var builder = new UriBuilder(request.RequestUri)
            {
                Scheme = Uri.UriSchemeHttps,
                Port = request.RequestUri.IsDefaultPort ? 443 : request.RequestUri.Port,
            };

            request.RequestUri = builder.Uri;
        }
    }

    private void ProcessStrictTransportSecurityHeader(HttpRequestMessage request, HttpResponseMessage response)
    {
        // https://developer.mozilla.org/en-US/docs/Web/HTTP/Headers/Strict-Transport-Security
        // Note: The Strict-Transport-Security header is ignored by the browser when your site has only been accessed using HTTP.
        // Once your site is accessed over HTTPS with no certificate errors, the browser knows your site is HTTPS-capable and
        // will honor the Strict-Transport-Security header.
        // The response URI is the one the request ended on; an inner handler that does not set RequestMessage
        // leaves the request itself as the best available source
        var responseUri = response.RequestMessage?.RequestUri ?? request.RequestUri;
        if (responseUri?.Scheme == Uri.UriSchemeHttps && !IsIPAddress(responseUri) && response.Headers.TryGetValues("Strict-Transport-Security", out var headers))
        {
            // https://datatracker.ietf.org/doc/html/rfc6797#section-8.1
            // Only the first header field is processed when the response contains more than one
            var header = headers.FirstOrDefault();
            if (header is not null && TryParsePolicy(header, out var maxAge, out var includeSubdomains))
            {
                if (maxAge > TimeSpan.Zero)
                {
                    _configuration.Add(responseUri.IdnHost, maxAge, includeSubdomains);
                }
                else
                {
                    // max-age=0 signals the host is no longer a Known HSTS Host
                    _configuration.RemoveLearnedPolicy(responseUri.IdnHost);
                }
            }
        }
    }

    private static Uri? GetUriForRedirect(HttpRequestMessage request, HttpResponseMessage response)
    {
        if (request.RequestUri is null || !IsRedirect(response.StatusCode))
            return null;

        var location = response.Headers.Location;
        if (location is null)
            return null;

        // A relative Location is resolved against the URI of the request that produced the response
        if (!location.IsAbsoluteUri)
        {
            location = new Uri(request.RequestUri, location);
        }

        if (location.Scheme != Uri.UriSchemeHttp && location.Scheme != Uri.UriSchemeHttps)
            return null;

        // An established secure connection is never downgraded, matching SocketsHttpHandler
        if (request.RequestUri.Scheme == Uri.UriSchemeHttps && location.Scheme != Uri.UriSchemeHttps)
            return null;

        return location;
    }

    private static bool IsRedirect(HttpStatusCode statusCode) => statusCode is
        HttpStatusCode.MultipleChoices or
        HttpStatusCode.MovedPermanently or
        HttpStatusCode.Found or
        HttpStatusCode.SeeOther or
        HttpStatusCode.TemporaryRedirect or
        HttpStatusCode.PermanentRedirect;

    private static void PrepareRedirect(HttpRequestMessage request, HttpResponseMessage response, Uri location)
    {
        // 300, 301 and 302 turn a POST into a GET; 303 turns any method other than GET and HEAD into a GET.
        // The body is dropped with the method. 307 and 308 keep both.
        var dropBody = response.StatusCode switch
        {
            HttpStatusCode.MultipleChoices or HttpStatusCode.MovedPermanently or HttpStatusCode.Found => request.Method == HttpMethod.Post,
            HttpStatusCode.SeeOther => request.Method != HttpMethod.Get && request.Method != HttpMethod.Head,
            _ => false,
        };

        if (dropBody)
        {
            request.Method = HttpMethod.Get;
            request.Content?.Dispose();
            request.Content = null;
        }

        // The credentials were granted to the origin that answered, not to the one it points at
        request.Headers.Authorization = null;

        request.RequestUri = location;
    }

    // https://datatracker.ietf.org/doc/html/rfc6797#section-8.1
    // An IP address must not be noted as a Known HSTS Host
    private static bool IsIPAddress(Uri uri) => uri.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6;

    // https://datatracker.ietf.org/doc/html/rfc6797#section-6.1
    // A malformed header must be ignored instead of failing the request, as the response is otherwise valid.
    private static bool TryParsePolicy(ReadOnlySpan<char> header, out TimeSpan maxAge, out bool includeSubdomains)
    {
        maxAge = default;
        includeSubdomains = false;
        var hasMaxAge = false;

        foreach (var part in header.Split(';'))
        {
            var directive = header[part].Trim();
            if (directive.IsEmpty)
                continue;

            // The name and the value may be separated by optional whitespace around the '='
            var separator = directive.IndexOf('=');
            var name = separator < 0 ? directive : directive[..separator].TrimEnd();
            var value = separator < 0 ? [] : directive[(separator + 1)..].TrimStart();

            if (name.Equals("max-age", StringComparison.OrdinalIgnoreCase))
            {
                // A repeated directive makes the whole header field invalid
                if (hasMaxAge)
                    return false;

                // The directive value may be a quoted-string
                if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
                {
                    value = value[1..^1].Trim();
                }

                if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
                    return false;

                // Clamp so computing the expiration date cannot overflow
                maxAge = TimeSpan.FromSeconds(Math.Min(seconds, MaxMaxAgeInSeconds));
                hasMaxAge = true;
            }
            else if (separator < 0 && name.Equals("includeSubDomains", StringComparison.OrdinalIgnoreCase))
            {
                if (includeSubdomains)
                    return false;

                includeSubdomains = true;
            }
        }

        // The max-age directive is required; without it the header is ignored
        return hasMaxAge;
    }
}
