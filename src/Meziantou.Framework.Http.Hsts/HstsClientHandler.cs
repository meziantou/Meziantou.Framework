using System.Net;

namespace Meziantou.Framework.Http;

/// <summary>An HTTP message handler that automatically upgrades HTTP requests to HTTPS based on HSTS (HTTP Strict Transport Security) policies.</summary>
/// <remarks>
/// <para>
/// The handler follows redirects itself so that every hop is checked against the HSTS policies. Redirects the
/// inner handler follows never reach this handler, so a redirect to an HSTS host would be requested over HTTP.
/// </para>
/// <para>
/// On the first request, when the inner handler is a <see cref="SocketsHttpHandler"/> or an
/// <see cref="HttpClientHandler"/> configured to follow redirects, this handler turns its
/// <c>AllowAutoRedirect</c> off and takes the redirects over, reusing its <c>MaxAutomaticRedirections</c>.
/// <strong>That modifies the inner handler,</strong> so an instance shared with another
/// <see cref="HttpClient"/> stops following redirects for that client too, and an instance that has already
/// sent a request cannot be reconfigured at all and makes the first send throw. Pass
/// <c>maxAutomaticRedirections</c> to the constructor to take the redirects over without touching the inner
/// handler; that is also the only way to follow redirects when the inner handler is neither of those two types.
/// </para>
/// <para>
/// An inner handler already configured not to follow redirects, or of a type this handler does not recognize,
/// keeps returning the redirect responses as is unless <c>maxAutomaticRedirections</c> is given.
/// </para>
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
    private const string HttpPrefix = "http://";

    // The redirect budget is resolved on the first send, so a sentinel is needed for "not resolved yet".
    // 0 means this handler does not follow redirects.
    private const int UnresolvedRedirections = -1;

    private readonly HstsDomainPolicyCollection _configuration;
    private readonly int? _explicitMaxAutomaticRedirections;
    private readonly Lock _redirectionLock = new();

    private volatile int _maxAutomaticRedirections = UnresolvedRedirections;

    /// <summary>Initializes a new instance of the <see cref="HstsClientHandler"/> class with the default HSTS policy collection and no inner handler.</summary>
    /// <remarks>
    /// The inner handler is left unset so the instance can be registered with
    /// <c>IHttpClientFactory.AddHttpMessageHandler</c>, which supplies it.
    /// </remarks>
    public HstsClientHandler()
        : this(innerHandler: null, HstsDomainPolicyCollection.Default)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="HstsClientHandler"/> class with a custom HSTS policy collection and no inner handler.</summary>
    /// <param name="configuration">The HSTS policy collection to use for determining which requests to upgrade.</param>
    /// <remarks>
    /// The inner handler is left unset so the instance can be registered with
    /// <c>IHttpClientFactory.AddHttpMessageHandler</c>, which supplies it.
    /// </remarks>
    public HstsClientHandler(HstsDomainPolicyCollection configuration)
        : this(innerHandler: null, configuration)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="HstsClientHandler"/> class with the default HSTS policy collection.</summary>
    /// <param name="innerHandler">The inner HTTP message handler to delegate requests to.</param>
    public HstsClientHandler(HttpMessageHandler innerHandler)
        : this(innerHandler, HstsDomainPolicyCollection.Default)
    {
        ArgumentNullException.ThrowIfNull(innerHandler);
    }

    /// <summary>Initializes a new instance of the <see cref="HstsClientHandler"/> class with a custom HSTS policy collection.</summary>
    /// <param name="innerHandler">The inner HTTP message handler to delegate requests to, or <see langword="null"/> to let a handler factory supply it.</param>
    /// <param name="configuration">The HSTS policy collection to use for determining which requests to upgrade.</param>
    public HstsClientHandler(HttpMessageHandler? innerHandler, HstsDomainPolicyCollection configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        _configuration = configuration;
        if (innerHandler is not null)
        {
            InnerHandler = innerHandler;
        }
    }

    /// <summary>Initializes a new instance of the <see cref="HstsClientHandler"/> class that follows redirects itself without reconfiguring the inner handler.</summary>
    /// <param name="innerHandler">The inner HTTP message handler to delegate requests to, or <see langword="null"/> to let a handler factory supply it.</param>
    /// <param name="configuration">The HSTS policy collection to use for determining which requests to upgrade.</param>
    /// <param name="maxAutomaticRedirections">The number of redirects to follow, or <c>0</c> not to follow any. The inner handler must be configured not to follow redirects, or every hop it follows bypasses the HSTS upgrade.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxAutomaticRedirections"/> is negative.</exception>
    public HstsClientHandler(HttpMessageHandler? innerHandler, HstsDomainPolicyCollection configuration, int maxAutomaticRedirections)
        : this(innerHandler, configuration)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxAutomaticRedirections);

        _explicitMaxAutomaticRedirections = maxAutomaticRedirections;
    }

    /// <summary>Sends an HTTP request, upgrading to HTTPS if required by HSTS policy, and processes the Strict-Transport-Security response header.</summary>
    /// <param name="request">The HTTP request message to send.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>The HTTP response message.</returns>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var remainingRedirections = GetMaxAutomaticRedirections();
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

    // Resolved on the first send rather than in the constructor: a handler factory sets InnerHandler after
    // construction, and a handler the caller owns must not be reconfigured before the caller has used it.
    private int GetMaxAutomaticRedirections()
    {
        var value = _maxAutomaticRedirections;
        if (value != UnresolvedRedirections)
            return value;

        lock (_redirectionLock)
        {
            value = _maxAutomaticRedirections;
            if (value == UnresolvedRedirections)
            {
                value = _explicitMaxAutomaticRedirections ?? TakeOverAutomaticRedirections(InnerHandler);
                _maxAutomaticRedirections = value;
            }

            return value;
        }
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
                try
                {
                    socketsHttpHandler.AllowAutoRedirect = false;
                }
                catch (InvalidOperationException ex)
                {
                    throw CannotTakeOverRedirections(ex);
                }

                return socketsHttpHandler.MaxAutomaticRedirections;

            case HttpClientHandler httpClientHandler when httpClientHandler.AllowAutoRedirect:
                try
                {
                    httpClientHandler.AllowAutoRedirect = false;
                }
                catch (InvalidOperationException ex)
                {
                    throw CannotTakeOverRedirections(ex);
                }

                return httpClientHandler.MaxAutomaticRedirections;

            default:
                return 0;
        }
    }

    // Reconfiguring the inner handler is what the caller is told about, so the failure has to say so: the
    // alternative is to carry on with redirects resolved below this handler, which is the cleartext hole the
    // take-over exists to close.
    private static InvalidOperationException CannotTakeOverRedirections(Exception innerException)
        => new(
            "The inner handler has already started sending requests, so HstsClientHandler cannot stop it from following redirects. " +
            "A redirect the inner handler follows does not go through the HSTS upgrade, so a redirect to an HSTS host would be requested over HTTP. " +
            "Set AllowAutoRedirect to false on the inner handler before it is used, and pass maxAutomaticRedirections to the HstsClientHandler constructor.",
            innerException);

    private void UpgradeRequest(HttpRequestMessage request)
    {
        // Use IdnHost: the preload list stores internationalized domains in their Punycode form
        var uri = request.RequestUri;
        if (uri?.Scheme != Uri.UriSchemeHttp || !_configuration.MustUpgradeRequest(uri.IdnHost))
            return;

        // https://datatracker.ietf.org/doc/html/rfc6797#section-8.3
        // The default port becomes 443; an explicit port is kept as is. AbsoluteUri leaves out the default
        // HTTP port, so swapping the scheme covers both cases.
        // A textual edit rather than UriBuilder, which cannot rebuild every URI System.Uri accepts: an empty
        // user name in the userinfo component, as in http://:password@host/, makes it throw.
        var absoluteUri = uri.AbsoluteUri;
        request.RequestUri = new Uri(string.Concat("https://", absoluteUri.AsSpan(HttpPrefix.Length)), UriKind.Absolute);
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

        // The fragment of the original request carries over to a Location that has none, matching SocketsHttpHandler
        var fragment = request.RequestUri.Fragment;
        if (fragment.Length > 0 && location.Fragment.Length == 0)
        {
            location = new Uri(location.AbsoluteUri + fragment, UriKind.Absolute);
        }

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

            // The content belongs to the caller, so it is detached and not disposed
            request.Content = null;

            // A request with no content must not keep advertising a chunked body, or sending it fails
            if (request.Headers.TransferEncodingChunked == true)
            {
                request.Headers.TransferEncodingChunked = false;
            }
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

        while (!header.IsEmpty)
        {
            var directive = NextDirective(ref header).Trim();
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

    // https://datatracker.ietf.org/doc/html/rfc6797#section-6.1
    // A directive value may be a quoted-string, which can contain ';', so the separator only separates
    // outside quotes. Splitting on every ';' would let a quoted extension directive either invalidate a
    // valid header or smuggle an includeSubDomains the server never sent.
    private static ReadOnlySpan<char> NextDirective(ref ReadOnlySpan<char> header)
    {
        var inQuotes = false;
        for (var i = 0; i < header.Length; i++)
        {
            var c = header[i];
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == '\\' && inQuotes && i + 1 < header.Length)
            {
                // quoted-pair
                i++;
            }
            else if (c == ';' && !inQuotes)
            {
                var directive = header[..i];
                header = header[(i + 1)..];
                return directive;
            }
        }

        var last = header;
        header = default;
        return last;
    }
}
