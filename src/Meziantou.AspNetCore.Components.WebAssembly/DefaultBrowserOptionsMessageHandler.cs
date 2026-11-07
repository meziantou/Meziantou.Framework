using Microsoft.AspNetCore.Components.WebAssembly.Http;

namespace Meziantou.AspNetCore.Components.WebAssembly;

/// <summary>A message handler that sets default browser fetch options (cache, credentials, mode) for HTTP requests in Blazor WebAssembly.</summary>
/// <remarks>
/// Only the options explicitly set on this handler are applied. An option that is left unset is not sent to the browser,
/// so the <see href="https://fetch.spec.whatwg.org/#requestinit">Fetch defaults</see> keep applying. Options already set
/// on the request are never overridden.
/// </remarks>
/// <seealso href="https://www.meziantou.net/bypass-browser-cache-using-httpclient-in-blazor-webassembly.htm"/>
public sealed class DefaultBrowserOptionsMessageHandler : DelegatingHandler
{
    private static readonly HttpRequestOptionsKey<IDictionary<string, object>> FetchRequestOptionsKey = new("WebAssemblyFetchOptions");

    private BrowserRequestCache? _browserRequestCache;
    private BrowserRequestCredentials? _browserRequestCredentials;
    private BrowserRequestMode? _browserRequestMode;

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

    /// <summary>Gets or sets the default cache mode for browser requests.</summary>
    /// <remarks>
    /// When this property is not set, the <c>cache</c> option is not sent to the browser and the Fetch default
    /// (<see cref="BrowserRequestCache.Default"/>) applies.
    /// </remarks>
    public BrowserRequestCache DefaultBrowserRequestCache
    {
        get => _browserRequestCache ?? BrowserRequestCache.Default;
        set => _browserRequestCache = value;
    }

    /// <summary>Gets or sets the default credentials mode for browser requests.</summary>
    /// <remarks>
    /// <para>
    /// When this property is not set, the <c>credentials</c> option is not sent to the browser and the Fetch default
    /// (<see cref="BrowserRequestCredentials.SameOrigin"/>) applies.
    /// </para>
    /// <para>
    /// <see cref="BrowserRequestCredentials.Include"/> applies to every request sent through this handler, including
    /// cross-origin ones. Only use it on an <see cref="HttpClient"/> dedicated to a single trusted origin.
    /// </para>
    /// </remarks>
    public BrowserRequestCredentials DefaultBrowserRequestCredentials
    {
        get => _browserRequestCredentials ?? BrowserRequestCredentials.SameOrigin;
        set => _browserRequestCredentials = value;
    }

    /// <summary>Gets or sets the default request mode for browser requests.</summary>
    /// <remarks>
    /// When this property is not set, the <c>mode</c> option is not sent to the browser and the Fetch default
    /// (<see cref="BrowserRequestMode.Cors"/>) applies.
    /// </remarks>
    public BrowserRequestMode DefaultBrowserRequestMode
    {
        get => _browserRequestMode ?? BrowserRequestMode.Cors;
        set => _browserRequestMode = value;
    }

    /// <summary>Sends an HTTP request with default browser options applied if not explicitly set.</summary>
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

        if (_browserRequestCache is { } cache && fetchOptions?.ContainsKey("cache") != true)
        {
            request.SetBrowserRequestCache(cache);
        }

        if (_browserRequestCredentials is { } credentials && fetchOptions?.ContainsKey("credentials") != true)
        {
            request.SetBrowserRequestCredentials(credentials);
        }

        if (_browserRequestMode is { } mode && fetchOptions?.ContainsKey("mode") != true)
        {
            request.SetBrowserRequestMode(mode);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
