namespace Meziantou.Framework.Http;

/// <summary>An HTTP message handler that automatically upgrades HTTP requests to HTTPS based on HSTS (HTTP Strict Transport Security) policies.</summary>
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
    }

    /// <summary>Sends an HTTP request, upgrading to HTTPS if required by HSTS policy, and processes the Strict-Transport-Security response header.</summary>
    /// <param name="request">The HTTP request message to send.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>The HTTP response message.</returns>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
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

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        // https://developer.mozilla.org/en-US/docs/Web/HTTP/Headers/Strict-Transport-Security
        // Note: The Strict-Transport-Security header is ignored by the browser when your site has only been accessed using HTTP.
        // Once your site is accessed over HTTPS with no certificate errors, the browser knows your site is HTTPS-capable and
        // will honor the Strict-Transport-Security header.
        var responseUri = response.RequestMessage?.RequestUri;
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

        return response;
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
