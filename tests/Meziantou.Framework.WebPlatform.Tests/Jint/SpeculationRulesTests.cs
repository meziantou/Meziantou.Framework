using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Jint;
using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.Tests.Scripting;

namespace Meziantou.Framework.WebPlatform.Tests.Jint;

/// <summary>Speculation rules (https://html.spec.whatwg.org/multipage/speculative-loading.html): navigational prefetches and prerendering.</summary>
public sealed class SpeculationRulesTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly List<ScriptError> _errors = [];
    private readonly RecordingLoader _loader = new();
    private readonly JintScriptEngine _engine = new();

    public void Dispose()
    {
        _contexts.Dispose();
        _engine.Dispose();
    }

    private BrowsingContext Load(string html, SpeculativeLoadingOptions? speculativeLoading = null, WebPlatformFeatures? features = null, TimeProvider? timeProvider = null)
    {
        var context = _contexts.Create(new BrowsingContextOptions
        {
            ScriptEngine = _engine,
            DocumentLoader = _loader,
            UnhandledScriptError = (_, error) => _errors.Add(error),
            SpeculativeLoading = speculativeLoading ?? SpeculativeLoadingOptions.Default,
            Features = features,
            TimeProvider = timeProvider,
        });

        context.Navigate("https://example.com/", _ => html);
        _contexts.EventLoop.RunUntilIdle();
        return context;
    }

    private List<DocumentRequest> RequestsOf(string url)
    {
        lock (_loader.Requests)
        {
            return [.. _loader.Requests.Where(request => request.Url == url)];
        }
    }

    private static string? Header(DocumentRequest request, string name) => request.Headers.FirstOrDefault(header => header.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;

    [Fact]
    public async Task ListRule_PrefetchesTheUrl_AndTheNavigationUsesTheResponse()
    {
        _loader.Resources["https://example.com/next.html"] = new DocumentResource("<title>next</title>");
        var context = Load("""
            <script type="speculationrules">{ "tag": "t", "prefetch": [{ "urls": ["next.html"] }] }</script>
            """);
        await _contexts.EventLoop.RunUntilIdleAsync(TestContext.Current.CancellationToken);

        var request = Assert.Single(RequestsOf("https://example.com/next.html"));
        Assert.Equal("prefetch", Header(request, "Sec-Purpose"));
        Assert.Equal("\"t\"", Header(request, "Sec-Speculation-Tags"));
        Assert.Equal(RequestMode.Navigate, request.Mode);
        Assert.Equal("https://example.com/", request.Referrer);

        context.Navigate("https://example.com/other.html", _ => "<script>location.href = 'next.html';</script>");
        await _contexts.EventLoop.RunUntilIdleAsync(TestContext.Current.CancellationToken);

        // The records are the ones of the document that prefetched: another document fetches the URL
        Assert.Equal("next", context.Document.Title);
        Assert.Equal(2, RequestsOf("https://example.com/next.html").Count);
        Assert.Empty(_errors);
    }

    [Fact]
    public async Task Navigation_UsesThePrefetchedResponse()
    {
        _loader.Resources["https://example.com/next.html"] = new DocumentResource("<title>next</title><script>document.title += ' ' + performance.getEntriesByType('navigation')[0].deliveryType;</script>");
        var context = Load("""
            <script type="speculationrules">{ "prefetch": [{ "source": "list", "urls": ["/next.html"] }] }</script>
            <a id="link" href="next.html#fragment">next</a>
            """);
        await _contexts.EventLoop.RunUntilIdleAsync(TestContext.Current.CancellationToken);
        Assert.Single(RequestsOf("https://example.com/next.html"));

        ((HtmlElement)context.Document.GetElementById("link")!).Click();
        await _contexts.EventLoop.RunUntilIdleAsync(TestContext.Current.CancellationToken);

        Assert.Equal("next navigational-prefetch", context.Document.Title);
        Assert.Equal("https://example.com/next.html#fragment", context.Document.Url);
        Assert.Single(RequestsOf("https://example.com/next.html"));
        Assert.Empty(_errors);
    }

    [Fact]
    public async Task DocumentRule_PrefetchesTheMatchingLinks()
    {
        _loader.Resources["https://example.com/a.html"] = new DocumentResource("a");
        _loader.Resources["https://example.com/b.html"] = new DocumentResource("b");
        _loader.Resources["https://example.com/c.html"] = new DocumentResource("c");
        Load("""
            <script type="speculationrules">
            { "prefetch": [{ "source": "document", "eagerness": "immediate", "where": { "and": [{ "href_matches": "/*.html" }, { "not": { "selector_matches": ".no" } }] } }] }
            </script>
            <a href="a.html">a</a>
            <a href="b.html" class="no">b</a>
            <a href="c.html" style="display: none">c</a>
            """);
        await _contexts.EventLoop.RunUntilIdleAsync(TestContext.Current.CancellationToken);

        Assert.Single(RequestsOf("https://example.com/a.html"));
        Assert.Empty(RequestsOf("https://example.com/b.html"));
        Assert.Empty(RequestsOf("https://example.com/c.html"));
        Assert.Empty(_errors);
    }

    [Fact]
    public async Task InvalidRules_AreIgnored_AndInvalidJsonIsReported()
    {
        _loader.Resources["https://example.com/ok.html"] = new DocumentResource("ok");
        Load("""
            <script type="speculationrules">
            { "prefetch": [
              { "source": "list", "urls": ["unknown-key.html"], "unknown": true },
              { "source": "list", "urls": ["where.html"], "where": {} },
              { "source": "list", "urls": ["eagerness.html"], "eagerness": "sometimes" },
              { "source": "list", "urls": ["policy.html"], "referrer_policy": "invalid" },
              { "source": "list", "urls": ["requires.html"], "requires": ["something"] },
              { "source": "list", "urls": ["ftp://example.com/scheme.html", "ok.html"] }
            ] }
            </script>
            <script type="speculationrules">not json</script>
            """);
        await _contexts.EventLoop.RunUntilIdleAsync(TestContext.Current.CancellationToken);

        lock (_loader.Requests)
        {
            Assert.Equal(["https://example.com/ok.html"], _loader.Requests.Select(request => request.Url));
        }

        Assert.Single(_errors);
    }

    [Fact]
    public async Task RemovingTheScript_CancelsThePrefetch()
    {
        _loader.Resources["https://example.com/next.html"] = new DocumentResource("<title>next</title>");
        var context = Load("""
            <script type="speculationrules">{ "prefetch": [{ "urls": ["next.html"] }] }</script>
            """);
        await _contexts.EventLoop.RunUntilIdleAsync(TestContext.Current.CancellationToken);
        Assert.Single(RequestsOf("https://example.com/next.html"));

        context.Document.QuerySelector("script")!.Remove();
        await _contexts.EventLoop.RunUntilIdleAsync(TestContext.Current.CancellationToken);
        context.Navigate("https://example.com/next.html");
        await _contexts.EventLoop.RunUntilIdleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, RequestsOf("https://example.com/next.html").Count);
        Assert.Null(Header(RequestsOf("https://example.com/next.html")[1], "Sec-Purpose"));
    }

    [Fact]
    public async Task CrossSitePrefetch_HasNoCookies_AndItsCookiesAreCopiedWhenItIsUsed()
    {
        var cookieJar = new CookieJar();
        cookieJar.SetCookie("https://example.com/", "a=1; Secure; SameSite=None");
        var loader = new CookieLoader();
        var context = _contexts.Create(new BrowsingContextOptions { ScriptEngine = _engine, DocumentLoader = loader, CookieJar = cookieJar });
        context.Navigate("https://example.com/", _ => """
            <script type="speculationrules">{ "prefetch": [{ "urls": ["https://other.test/page.html", "https://example.com/same.html"] }] }</script>
            <a href="https://other.test/page.html">other</a>
            """);
        await _contexts.EventLoop.RunUntilIdleAsync(TestContext.Current.CancellationToken);

        Assert.Equal("", loader.Cookies["https://other.test/page.html"]);
        Assert.Equal("a=1", loader.Cookies["https://example.com/same.html"]);
        Assert.Empty(cookieJar.GetCookies("https://other.test/page.html"));

        ((HtmlElement)context.Document.QuerySelector("a")!).Click();
        await _contexts.EventLoop.RunUntilIdleAsync(TestContext.Current.CancellationToken);

        Assert.Equal("https://other.test/page.html", context.Document.Url);
        Assert.Equal(1, loader.Counts["https://other.test/page.html"]);
        Assert.Equal("b", Assert.Single(cookieJar.GetCookies("https://other.test/page.html")).Name);
    }

    [Fact]
    public async Task CrossSitePrefetch_IsNotSent_WhenTheUserAgentHasCookiesForTheUrl()
    {
        var cookieJar = new CookieJar();
        cookieJar.SetCookie("https://other.test/", "a=1");
        var loader = new CookieLoader();
        var context = _contexts.Create(new BrowsingContextOptions { ScriptEngine = _engine, DocumentLoader = loader, CookieJar = cookieJar });
        context.Navigate("https://example.com/", _ => """
            <script type="speculationrules">{ "prefetch": [{ "urls": ["https://other.test/page.html"] }] }</script>
            """);
        await _contexts.EventLoop.RunUntilIdleAsync(TestContext.Current.CancellationToken);

        Assert.Empty(loader.Counts);
    }

    [Fact]
    public async Task Eagerness_ConservativeCandidatesArePrefetchedOnPointerDown_AndModerateOnesOnHover()
    {
        _loader.Resources["https://example.com/moderate.html"] = new DocumentResource("moderate");
        _loader.Resources["https://example.com/conservative.html"] = new DocumentResource("conservative");
        var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        var context = Load("""
            <!doctype html>
            <style>a { display: block; width: 100px; height: 20px; }</style>
            <body style="margin: 0">
            <a href="moderate.html">moderate</a>
            <a href="conservative.html">conservative</a>
            <script type="speculationrules">
            { "prefetch": [{ "urls": ["moderate.html"], "eagerness": "moderate" }, { "urls": ["conservative.html"], "eagerness": "conservative" }] }
            </script>
            """, timeProvider: clock);
        await _contexts.EventLoop.RunUntilIdleAsync(TestContext.Current.CancellationToken);
        Assert.Empty(RequestsOf("https://example.com/moderate.html"));
        Assert.Empty(RequestsOf("https://example.com/conservative.html"));

        // Hovering the links: the moderate candidate is prefetched once the pointer stayed 200 ms over its link
        var mouse = context.Input.Mouse;
        mouse.Move(10, 30);
        mouse.Move(10, 10);
        _contexts.EventLoop.RunUntilIdle();
        Assert.Empty(RequestsOf("https://example.com/moderate.html"));
        clock.Advance(TimeSpan.FromMilliseconds(250));
        _contexts.EventLoop.RunUntilIdle();
        Assert.Single(RequestsOf("https://example.com/moderate.html"));
        Assert.Empty(RequestsOf("https://example.com/conservative.html"));

        // Pressing a button on the other link
        mouse.Move(10, 30);
        mouse.Down();
        _contexts.EventLoop.RunUntilIdle();
        Assert.Single(RequestsOf("https://example.com/conservative.html"));
        mouse.Move(300, 300);
        mouse.Up();
    }

    [Fact]
    public async Task SpeculationRulesHeader_FetchesTheRuleSetWithCors()
    {
        _loader.Resources["https://example.com/rules.json"] = new DocumentResource("""{ "prefetch": [{ "urls": ["next.html"] }] }""") { Headers = [new("Content-Type", "application/speculationrules+json")] };
        _loader.Resources["https://example.com/ignored.json"] = new DocumentResource("""{ "prefetch": [{ "urls": ["ignored.html"] }] }""") { Headers = [new("Content-Type", "application/json")] };
        _loader.Resources["https://example.com/next.html"] = new DocumentResource("next");
        _loader.Resources["https://example.com/ignored.html"] = new DocumentResource("ignored");
        _loader.Resources["https://example.com/page.html"] = new DocumentResource("<title>page</title>") { Headers = [new("Speculation-Rules", "\"/rules.json\", \"/ignored.json\"")] };
        var context = _contexts.Create(new BrowsingContextOptions { DocumentLoader = _loader });
        context.Navigate("https://example.com/page.html");
        await _contexts.EventLoop.RunUntilIdleAsync(TestContext.Current.CancellationToken);

        var rules = Assert.Single(RequestsOf("https://example.com/rules.json"));
        Assert.Equal(RequestDestination.SpeculationRules, rules.Destination);
        Assert.Equal(RequestMode.Cors, rules.Mode);
        Assert.Single(RequestsOf("https://example.com/next.html"));
        Assert.Empty(RequestsOf("https://example.com/ignored.html"));
    }

    [Fact]
    public async Task UserPreference_DisablesThePrefetches()
    {
        _loader.Resources["https://example.com/next.html"] = new DocumentResource("next");
        Load("""
            <script type="speculationrules">{ "prefetch": [{ "urls": ["next.html"] }], "prerender": [{ "urls": ["next.html?prerender"] }] }</script>
            """, speculativeLoading: new SpeculativeLoadingOptions { PrefetchEnabled = false });
        await _contexts.EventLoop.RunUntilIdleAsync(TestContext.Current.CancellationToken);

        lock (_loader.Requests)
        {
            Assert.Empty(_loader.Requests);
        }
    }

    [Fact]
    public async Task DisabledFeature_IgnoresTheRules()
    {
        _loader.Resources["https://example.com/next.html"] = new DocumentResource("next");
        var context = Load("""
            <script type="speculationrules">{ "prefetch": [{ "urls": ["next.html"] }] }</script>
            <script>document.title = [HTMLScriptElement.supports('speculationrules'), 'prerendering' in document].join();</script>
            """, features: WebPlatformFeatures.Default with { SpeculationRulesEnabled = false });
        await _contexts.EventLoop.RunUntilIdleAsync(TestContext.Current.CancellationToken);

        Assert.Equal("false,false", context.Document.Title);
        Assert.Empty(RequestsOf("https://example.com/next.html"));
    }

    [Fact]
    public async Task Prerender_LoadsTheDocumentInAHiddenTraversable_AndTheNavigationActivatesIt()
    {
        _loader.Resources["https://example.com/next.html"] = new DocumentResource("""
            <title>next</title>
            <script>
              window.log = [document.prerendering, document.visibilityState, document.hasFocus(), window.open('about:blank') === null];
              document.addEventListener('visibilitychange', () => log.push('visibilitychange ' + document.visibilityState));
              document.addEventListener('prerenderingchange', e => {
                log.push('prerenderingchange ' + document.prerendering + ' ' + e.bubbles + ' ' + e.cancelable);
                log.push(performance.getEntriesByType('navigation')[0].activationStart > 0);
                document.title = log.join();
              });
            </script>
            """);
        var context = Load("""
            <title>initiator</title>
            <script type="speculationrules">{ "prerender": [{ "urls": ["next.html"] }] }</script>
            <script>addEventListener('pagehide', () => { window.unloaded = true; });</script>
            """);
        await _contexts.EventLoop.RunUntilIdleAsync(TestContext.Current.CancellationToken);

        // The prerendered document is not the document of the navigable, and its requests are speculative
        var request = Assert.Single(RequestsOf("https://example.com/next.html"));
        Assert.Equal("prefetch;prerender", Header(request, "Sec-Purpose"));
        Assert.Equal("null", Header(request, "Sec-Speculation-Tags"));
        Assert.Equal("initiator", context.Document.Title);
        var initiator = context.Document;

        context.Document.DefaultView!.Location.Href = "https://example.com/next.html";
        await _contexts.EventLoop.RunUntilIdleAsync(TestContext.Current.CancellationToken);

        Assert.Equal("true,hidden,false,true,visibilitychange visible,prerenderingchange false false false,true", context.Document.Title);
        Assert.False(context.Document.Prerendering);
        Assert.Same(context, context.Document.DefaultView!.BrowsingContext);
        Assert.NotSame(initiator, context.Document);
        Assert.Single(RequestsOf("https://example.com/next.html"));
        Assert.Equal(2u, context.Document.DefaultView!.History.Length);
        Assert.Empty(_errors);
    }

    [Fact]
    public async Task Prerender_ActivatedInAHiddenBrowsingContext_StaysHidden()
    {
        _loader.Resources["https://example.com/next.html"] = new DocumentResource("""
            <title>next</title>
            <script>
              window.log = [document.visibilityState, document.hasFocus()];
              document.addEventListener('visibilitychange', () => { log.push('visibilitychange ' + document.visibilityState); document.title = log.join(); });
              document.addEventListener('prerenderingchange', () => { log.push('prerenderingchange ' + document.visibilityState + ' ' + document.hasFocus()); document.title = log.join(); });
            </script>
            """);
        var context = Load("""
            <title>initiator</title>
            <script type="speculationrules">{ "prerender": [{ "urls": ["next.html"] }] }</script>
            """);
        await _contexts.EventLoop.RunUntilIdleAsync(TestContext.Current.CancellationToken);

        // The window of the host is minimized and inactive: the prerendered document gets the state of the navigable
        context.IsVisible = false;
        context.HasSystemFocus = false;
        await _contexts.EventLoop.RunUntilIdleAsync(TestContext.Current.CancellationToken);
        context.Document.DefaultView!.Location.Href = "https://example.com/next.html";
        await _contexts.EventLoop.RunUntilIdleAsync(TestContext.Current.CancellationToken);

        Assert.Equal("hidden,false,prerenderingchange hidden false", context.Document.Title);
        Assert.False(context.IsVisible);

        context.IsVisible = true;
        context.HasSystemFocus = true;
        await _contexts.EventLoop.RunUntilIdleAsync(TestContext.Current.CancellationToken);
        Assert.Equal("hidden,false,prerenderingchange hidden false,visibilitychange visible", context.Document.Title);
        Assert.True(context.Document.HasFocus());
        Assert.Empty(_errors);
    }

    [Fact]
    public async Task Prerender_Activation_AppliesTheTextDirectivesOfTheNavigation()
    {
        _loader.Resources["https://example.com/next.html"] = new DocumentResource("<title>next</title><p>Some text</p><p id=target>The target of the directive</p>");
        var context = Load("""
            <title>initiator</title>
            <script type="speculationrules">{ "prerender": [{ "urls": ["next.html"] }] }</script>
            """);
        await _contexts.EventLoop.RunUntilIdleAsync(TestContext.Current.CancellationToken);
        Assert.Single(RequestsOf("https://example.com/next.html"));

        // The fragment directive is removed from the URL of the navigation, which matches the prerendered URL; it is the
        // directive state of the entry of the activated document, whose text is searched
        context.Document.DefaultView!.Location.Href = "https://example.com/next.html#:~:text=target%20of";
        await _contexts.EventLoop.RunUntilIdleAsync(TestContext.Current.CancellationToken);

        Assert.Equal("next", context.Document.Title);
        Assert.Single(RequestsOf("https://example.com/next.html"));
        Assert.Equal("https://example.com/next.html#", context.Document.Url);
        Assert.Equal("target of", Assert.Single(context.Document.TargetTextRanges!).ToString());
        Assert.Empty(_errors);
    }

    [Fact]
    public async Task Prerender_DelaysTheWebLocksUntilTheActivation()
    {
        _loader.Resources["https://example.com/next.html"] = new DocumentResource("""
            <title>next</title>
            <script>
              window.log = [];
              navigator.locks.request('resource', () => { log.push('granted ' + document.prerendering); document.title = log.join(); });
              navigator.locks.query().then(state => { log.push('query ' + document.prerendering + ' ' + state.held.length); document.title = log.join(); });
              document.addEventListener('prerenderingchange', () => log.push('prerenderingchange'));
            </script>
            """);
        var context = Load("""
            <title>initiator</title>
            <script type="speculationrules">{ "prerender": [{ "urls": ["next.html"] }] }</script>
            <script>navigator.locks.request('resource', () => new Promise(() => {}));</script>
            """);
        await _contexts.EventLoop.RunUntilIdleAsync(TestContext.Current.CancellationToken);
        Assert.Single(RequestsOf("https://example.com/next.html"));

        // The request of the prerendering document is not made: the lock manager only has the lock of the initiator
        var query = context.Document.DefaultView!.Navigator.Locks.Query();
        await _contexts.EventLoop.RunUntilIdleAsync(TestContext.Current.CancellationToken);
        var state = await query;
        Assert.Single(state.Held!);
        Assert.Empty(state.Pending!);

        // The initiator is unloaded by the activation, which releases its lock; then the delayed operations run
        context.Document.DefaultView!.Location.Href = "https://example.com/next.html";
        await _contexts.EventLoop.RunUntilIdleAsync(TestContext.Current.CancellationToken);
        Assert.Equal("prerenderingchange,granted false,query false 1", context.Document.Title);
        Assert.Empty(_errors);
    }

    [Fact]
    public async Task Prerender_IsCanceled_WhenTheResponseIsNotSuccessful()
    {
        _loader.Resources["https://example.com/next.html"] = new DocumentResource("<title>error</title>") { StatusCode = 500 };
        var context = Load("""
            <script type="speculationrules">{ "prerender": [{ "urls": ["next.html"] }] }</script>
            """);
        await _contexts.EventLoop.RunUntilIdleAsync(TestContext.Current.CancellationToken);
        Assert.Single(RequestsOf("https://example.com/next.html"));

        context.Document.DefaultView!.Location.Href = "https://example.com/next.html";
        await _contexts.EventLoop.RunUntilIdleAsync(TestContext.Current.CancellationToken);

        // The navigation fetches the URL itself
        Assert.Equal("error", context.Document.Title);
        var requests = RequestsOf("https://example.com/next.html");
        Assert.Equal(2, requests.Count);
        Assert.Null(Header(requests[1], "Sec-Purpose"));
    }

    private sealed class CookieLoader : DocumentLoader
    {
        public Dictionary<string, string> Cookies { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, int> Counts { get; } = new(StringComparer.Ordinal);

        public override ValueTask<DocumentResource?> LoadAsync(DocumentRequest request, CancellationToken cancellationToken)
        {
            Cookies[request.Url] = request.IncludeCredentials ? request.GetCookieHeader() ?? "" : "";
            Counts[request.Url] = Counts.GetValueOrDefault(request.Url) + 1;
            request.StoreCookies(["b=2; Secure; SameSite=None"]);
            return ValueTask.FromResult<DocumentResource?>(new DocumentResource("<title>page</title>"));
        }
    }
}
