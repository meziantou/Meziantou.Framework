namespace Meziantou.Framework.Http;

/// <summary>An HTTP message handler that automatically upgrades HTTP requests to HTTPS based on HSTS (HTTP Strict Transport Security) policies.</summary>
/// <example>
/// <code>
/// var policies = new HstsDomainPolicyCollection(includePreloadDomains: true);
/// using var client = new HttpClient(new HstsClientHandler(new SocketsHttpHandler(), policies), disposeHandler: true);
///
/// // Automatically upgrade to HTTPS as google.com is in the HSTS preload list
/// using var response = await client.GetAsync("http://google.com");
/// </code>
/// </example>
public sealed class HstsClientHandler : DelegatingHandler
{
    private const long MaxMaxAgeInSeconds = 100L * 365 * 24 * 60 * 60;

    private readonly HstsDomainPolicyCollection _configuration;

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
        _configuration = configuration;
    }

    /// <summary>Sends an HTTP request, upgrading to HTTPS if required by HSTS policy, and processes the Strict-Transport-Security response header.</summary>
    /// <param name="request">The HTTP request message to send.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>The HTTP response message.</returns>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri?.Scheme == Uri.UriSchemeHttp && request.RequestUri.Port == 80)
        {
            // Use IdnHost: the preload list stores internationalized domains in their Punycode form
            if (_configuration.MustUpgradeRequest(request.RequestUri.IdnHost))
            {
                var builder = new UriBuilder(request.RequestUri) { Scheme = Uri.UriSchemeHttps };
                builder.Port = 443;
                builder.Scheme = Uri.UriSchemeHttps;
                request.RequestUri = builder.Uri;
            }
        }

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        // https://developer.mozilla.org/en-US/docs/Web/HTTP/Headers/Strict-Transport-Security
        // Note: The Strict-Transport-Security header is ignored by the browser when your site has only been accessed using HTTP.
        // Once your site is accessed over HTTPS with no certificate errors, the browser knows your site is HTTPS-capable and
        // will honor the Strict-Transport-Security header.
        if (response.RequestMessage?.RequestUri?.Scheme == Uri.UriSchemeHttps && response.Headers.TryGetValues("Strict-Transport-Security", out var headers))
        {
            TimeSpan maxAge = default;
            var includeSubdomains = false;
            foreach (var header in headers)
            {
                if (TryParsePolicy(header, out var headerMaxAge, out var headerIncludeSubdomains))
                {
                    maxAge = headerMaxAge;
                    includeSubdomains = headerIncludeSubdomains;
                }
            }

            if (maxAge > TimeSpan.Zero)
            {
                _configuration.Add(response.RequestMessage.RequestUri.IdnHost, maxAge, includeSubdomains);
            }
        }

        return response;
    }

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
            if (directive.StartsWith("max-age=", StringComparison.OrdinalIgnoreCase))
            {
                var value = directive["max-age=".Length..].Trim();

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
            else if (directive.Equals("includeSubDomains", StringComparison.OrdinalIgnoreCase))
            {
                includeSubdomains = true;
            }
        }

        // The max-age directive is required; without it the header is ignored
        return hasMaxAge;
    }
}
