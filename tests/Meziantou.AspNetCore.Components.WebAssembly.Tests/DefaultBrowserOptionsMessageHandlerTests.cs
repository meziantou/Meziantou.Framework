using Microsoft.AspNetCore.Components.WebAssembly.Http;

namespace Meziantou.AspNetCore.Components.WebAssembly.Tests;

public sealed class DefaultBrowserOptionsMessageHandlerTests
{
    private static readonly HttpRequestOptionsKey<IDictionary<string, object>> FetchRequestOptionsKey = new("WebAssemblyFetchOptions");

    [Fact]
    public async Task UnsetOptionsAreNotSentToTheBrowser()
    {
        var request = await SendAsync(_ => { });

        Assert.False(request.Options.TryGetValue(FetchRequestOptionsKey, out _));
    }

    [Fact]
    public async Task SettingOneOptionDoesNotSetTheOthers()
    {
        var request = await SendAsync(handler => handler.DefaultBrowserRequestCache = BrowserRequestCache.NoCache);

        var options = GetFetchOptions(request);
        Assert.Equal("no-cache", options["cache"]);
        Assert.DoesNotContain("credentials", options);
        Assert.DoesNotContain("mode", options);
    }

    [Fact]
    public async Task SetOptionsAreApplied()
    {
        var request = await SendAsync(handler =>
        {
            handler.DefaultBrowserRequestCache = BrowserRequestCache.NoStore;
            handler.DefaultBrowserRequestCredentials = BrowserRequestCredentials.Include;
            handler.DefaultBrowserRequestMode = BrowserRequestMode.NoCors;
        });

        var options = GetFetchOptions(request);
        Assert.Equal("no-store", options["cache"]);
        Assert.Equal("include", options["credentials"]);
        Assert.Equal("no-cors", options["mode"]);
    }

    [Fact]
    public async Task ExplicitRequestOptionsAreNotOverridden()
    {
        var request = await SendAsync(
            handler =>
            {
                handler.DefaultBrowserRequestCache = BrowserRequestCache.NoStore;
                handler.DefaultBrowserRequestCredentials = BrowserRequestCredentials.Include;
                handler.DefaultBrowserRequestMode = BrowserRequestMode.NoCors;
            },
            request =>
            {
                request.SetBrowserRequestCache(BrowserRequestCache.ForceCache);
                request.SetBrowserRequestCredentials(BrowserRequestCredentials.Omit);
                request.SetBrowserRequestMode(BrowserRequestMode.SameOrigin);
            });

        var options = GetFetchOptions(request);
        Assert.Equal("force-cache", options["cache"]);
        Assert.Equal("omit", options["credentials"]);
        Assert.Equal("same-origin", options["mode"]);
    }

    [Fact]
    public async Task DefaultsAreAppliedNextToExplicitOptions()
    {
        var request = await SendAsync(
            handler =>
            {
                handler.DefaultBrowserRequestCache = BrowserRequestCache.NoStore;
                handler.DefaultBrowserRequestMode = BrowserRequestMode.NoCors;
            },
            request => request.SetBrowserRequestMode(BrowserRequestMode.Cors));

        var options = GetFetchOptions(request);
        Assert.Equal("no-store", options["cache"]);
        Assert.Equal("cors", options["mode"]);
    }

    [Fact]
    public void UnsetPropertiesReportTheFetchDefaults()
    {
        using var handler = new DefaultBrowserOptionsMessageHandler();

        Assert.Equal(BrowserRequestCache.Default, handler.DefaultBrowserRequestCache);
        Assert.Equal(BrowserRequestCredentials.SameOrigin, handler.DefaultBrowserRequestCredentials);
        Assert.Equal(BrowserRequestMode.Cors, handler.DefaultBrowserRequestMode);
    }

    private static IDictionary<string, object> GetFetchOptions(HttpRequestMessage request)
    {
        Assert.True(request.Options.TryGetValue(FetchRequestOptionsKey, out var options));

        return options;
    }

    private static async Task<HttpRequestMessage> SendAsync(Action<DefaultBrowserOptionsMessageHandler> configureHandler, Action<HttpRequestMessage>? configureRequest = null)
    {
        using var innerHandler = new StubHandler();
        using var handler = new DefaultBrowserOptionsMessageHandler(innerHandler);
        configureHandler(handler);

        using var invoker = new HttpMessageInvoker(handler, disposeHandler: false);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
        configureRequest?.Invoke(request);

        using var response = await invoker.SendAsync(request, TestContext.Current.CancellationToken);

        return request;
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { RequestMessage = request });
    }
}
