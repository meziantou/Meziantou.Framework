namespace Meziantou.Framework.Http;

public sealed class HstsClientHandler : DelegatingHandler
{
    private readonly HstsDomainPolicyCollection _configuration;

    public HstsClientHandler(HttpMessageHandler innerHandler)
        : this(innerHandler, HstsDomainPolicyCollection.Default)
    {
    }

    public HstsClientHandler(HttpMessageHandler innerHandler, HstsDomainPolicyCollection configuration)
        : base(innerHandler)
    {
        _configuration = configuration;
    }

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
