using Microsoft.AspNetCore.Components.WebAssembly.Http;

namespace Meziantou.AspNetCore.Components.WebAssembly;

/// <summary>
/// A message handler that sets default browser fetch options (cache, credentials, mode) for HTTP requests in Blazor WebAssembly.
/// </summary>
/// <seealso href="https://www.meziantou.net/bypass-browser-cache-using-httpclient-in-blazor-webassembly.htm"/>
public sealed class DefaultBrowserOptionsMessageHandler : DelegatingHandler
{
    private static readonly HttpRequestOptionsKey<IDictionary<string, object>> FetchRequestOptionsKey = new("WebAssemblyFetchOptions");

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultBrowserOptionsMessageHandler"/> class.
    /// </summary>
    public DefaultBrowserOptionsMessageHandler()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultBrowserOptionsMessageHandler"/> class with a specific inner handler.
    /// </summary>
    /// <param name="innerHandler">The inner handler which is responsible for processing the HTTP response messages.</param>
    public DefaultBrowserOptionsMessageHandler(HttpMessageHandler innerHandler)
    {
        InnerHandler = innerHandler;
    }

    /// <summary>
    /// Gets or sets the default cache mode for browser requests.
    /// </summary>
    public BrowserRequestCache DefaultBrowserRequestCache { get; set; }

    /// <summary>
    /// Gets or sets the default credentials mode for browser requests.
    /// </summary>
    public BrowserRequestCredentials DefaultBrowserRequestCredentials { get; set; }

    /// <summary>
    /// Gets or sets the default request mode for browser requests.
    /// </summary>
    public BrowserRequestMode DefaultBrowserRequestMode { get; set; }

    /// <summary>
    /// Sends an HTTP request with default browser options applied if not explicitly set.
    /// </summary>
    /// <param name="request">The HTTP request message to send.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>The task object representing the asynchronous operation containing the HTTP response message.</returns>
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Get the existing options to not override them if set explicitly
        if (!request.Options.TryGetValue(FetchRequestOptionsKey, out var fetchOptions))
        {
            fetchOptions = null;
        }

        if (fetchOptions?.ContainsKey("cache") != true)
        {
            request.SetBrowserRequestCache(DefaultBrowserRequestCache);
        }

        if (fetchOptions?.ContainsKey("credentials") != true)
        {
            request.SetBrowserRequestCredentials(DefaultBrowserRequestCredentials);
        }

        if (fetchOptions?.ContainsKey("mode") != true)
        {
            request.SetBrowserRequestMode(DefaultBrowserRequestMode);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
