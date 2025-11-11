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
            if (_configuration.MustUpgradeRequest(request.RequestUri.Host))
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
                var headerSpan = header.AsSpan();
                foreach (var part in headerSpan.Split(';'))
                {
                    var trimmed = headerSpan[part].Trim();
                    if (trimmed.StartsWith("max-age=", StringComparison.OrdinalIgnoreCase))
                    {
                        var maxAgeValue = int.Parse(trimmed[8..], NumberStyles.None, CultureInfo.InvariantCulture);
                        maxAge = TimeSpan.FromSeconds(maxAgeValue);
                    }
                    else if (trimmed.Equals("includeSubDomains", StringComparison.OrdinalIgnoreCase))
                    {
                        includeSubdomains = true;
                    }
                }
            }

            if (maxAge > TimeSpan.Zero)
            {
                _configuration.Add(response.RequestMessage.RequestUri.Host, maxAge, includeSubdomains);
            }
        }

        return response;
    }
}
