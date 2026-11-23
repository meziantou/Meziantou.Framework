using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.Tests.Scripting;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Dom;

/// <summary>
/// Upgrade Insecure Requests (https://w3c.github.io/webappsec-upgrade-insecure-requests/): the <c>upgrade-insecure-requests</c>
/// directive; the HTTPS upgrades of the top-level navigations (https://github.com/whatwg/fetch/pull/1655); and HTTP Strict
/// Transport Security (https://www.rfc-editor.org/rfc/rfc6797).
/// </summary>
public sealed class UpgradeInsecureRequestsTests : IDisposable
{
    private const string Meta = "<meta http-equiv=Content-Security-Policy content=upgrade-insecure-requests>";

    // A 2x3 PNG (only the header is read)
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52, 0, 0, 0, 2, 0, 0, 0, 3, 8, 6, 0, 0, 0];

    private readonly BrowsingContextScope _contexts = new();
    private readonly ScriptEngine _engine = TestScriptEngines.Create();
    private readonly List<DocumentRequest> _requests = [];

    /// <summary>The responses of the loader for some URLs (a null response is a network error).</summary>
    private readonly Dictionary<string, Func<CancellationToken, Task<DocumentResource?>>> _responses = new(StringComparer.Ordinal);

    public void Dispose()
    {
        _contexts.Dispose();
        TestScriptEngines.Dispose(_engine);
    }

    private Document Load(string html, string url = "https://example.com/", string? headerName = null)
    {
        var context = _contexts.Create(new BrowsingContextOptions { ScriptEngine = _engine, DocumentLoader = new Loader(this) });
        var document = context.Navigate(url, _ => headerName is null ? DocumentResource.Create(html, "text/html") : new DocumentResource(html) { Headers = [new("Content-Type", "text/html"), new(headerName, "upgrade-insecure-requests")] });
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    /// <summary>Navigates a new browsing context to <paramref name="url"/> with the loader, whose responses are synchronous.</summary>
    private BrowsingContext Navigate(string url, HttpsUpgradeOptions? httpsUpgrades, TimeProvider? timeProvider = null)
    {
        var context = _contexts.Create(new BrowsingContextOptions { ScriptEngine = _engine, DocumentLoader = new Loader(this), HttpsUpgrades = httpsUpgrades, TimeProvider = timeProvider });
        context.Navigate(url);
        _contexts.EventLoop.RunUntilIdle();
        return context;
    }

    /// <summary>A browsing context with the loader and the HSTS settings, whose responses are synchronous.</summary>
    private BrowsingContext CreateContext(HstsOptions? hsts, HttpsUpgradeOptions? httpsUpgrades = null, TimeProvider? timeProvider = null)
        => _contexts.Create(new BrowsingContextOptions { ScriptEngine = _engine, DocumentLoader = new Loader(this), Hsts = hsts, HttpsUpgrades = httpsUpgrades, TimeProvider = timeProvider });

    private void Navigate(BrowsingContext context, string url)
    {
        context.Navigate(url);
        _contexts.EventLoop.RunUntilIdle();
    }

    /// <summary>The loader returns a document with a <c>Strict-Transport-Security</c> header for <paramref name="url"/>.</summary>
    private void ServeStrictTransportSecurity(string url, string value)
        => _responses[url] = _ => Task.FromResult<DocumentResource?>(new DocumentResource("<title>page</title>") { Headers = [new("Content-Type", "text/html"), new("Strict-Transport-Security", value)] });

    private static DocumentResource Redirect(string location) => new("") { StatusCode = 302, Headers = [new("Location", location)] };

    private List<string> Requests => [.. _requests.Select(r => r.Destination + " " + r.Url)];

    /// <summary>Serves a script for the .js URLs, a PNG for the .png URLs, a text for the .txt URLs and a document for the other URLs.</summary>
    private sealed class Loader(UpgradeInsecureRequestsTests test) : DocumentLoader
    {
        public override ValueTask<DocumentResource?> LoadAsync(DocumentRequest request, CancellationToken cancellationToken)
        {
            test._requests.Add(request);
            if (test._responses.TryGetValue(request.Url, out var response))
                return new ValueTask<DocumentResource?>(response(cancellationToken));

            var path = request.Url.Split('?')[0];
            var resource = path switch
            {
                _ when path.EndsWith(".js", StringComparison.Ordinal) => DocumentResource.Create("document.title += ' executed'", "text/javascript"),
                _ when path.EndsWith(".png", StringComparison.Ordinal) => DocumentResource.Create(Png, "image/png"),
                _ when path.EndsWith(".txt", StringComparison.Ordinal) => new DocumentResource("text") { Headers = [new("Content-Type", "text/plain"), new("Access-Control-Allow-Origin", "*")] },
                _ => DocumentResource.Create("<title>page</title>", "text/html"),
            };

            return ValueTask.FromResult<DocumentResource?>(resource);
        }
    }

    [Fact]
    public void Meta_UpgradesTheSubresources()
    {
        // Without the directive, the script would be blocked as mixed content. The upgrade keeps a non-default port.
        var document = Load(Meta + "<title>start</title><script src=http://other.example/a.js></script><img src=http://other.example:8080/a.png>");
        Assert.Equal("start executed", document.Title);
        Assert.Equal(["Image https://other.example:8080/a.png", "Script https://other.example/a.js"], Requests.Order());
    }

    [Fact]
    public void Header_UpgradesTheFetches()
    {
        Load("<script>fetch('http://other.example/a.txt'); var xhr = new XMLHttpRequest(); xhr.open('GET', 'http://other.example:80/b.txt'); xhr.send();</script>", headerName: "Content-Security-Policy");
        Assert.Equal(["Empty https://other.example/a.txt", "Empty https://other.example/b.txt"], Requests.Order());
    }

    [Fact]
    public void Beacon_IsUpgraded()
    {
        // Without the directive, sendBeacon() returns false (mixed content)
        var document = Load(Meta + "<script>document.title = navigator.sendBeacon('http://other.example/a.txt', 'data');</script>");
        Assert.Equal("true", document.Title);
        Assert.Equal(["Empty https://other.example/a.txt"], Requests);
    }

    [Fact]
    public void ReportOnly_DoesNotUpgrade()
    {
        var document = Load("<title>start</title><script src=http://other.example/a.js onerror=\"document.title += ' error'\"></script>", headerName: "Content-Security-Policy-Report-Only");
        Assert.Equal("start error", document.Title);
        Assert.Empty(Requests);
    }

    [Fact]
    public void InsecureDocument_UpgradesTheSubresources()
    {
        Load(Meta + "<img src=http://other.example/a.png><img src=http://other.example/b.png>", "http://example.com/");
        Assert.Equal(["Image https://other.example/a.png", "Image https://other.example/b.png"], Requests.Order());
    }

    [Theory]
    [InlineData("http://localhost:5000/")]
    [InlineData("http://a.localhost/")]
    [InlineData("http://127.0.0.1:5000/")]
    [InlineData("http://[::1]:5000/")]
    public void PotentiallyTrustworthyUrls_AreNotUpgraded(string url)
    {
        // As in Chromium, the URLs of localhost and of the loopback addresses are not upgraded: the same-origin requests
        // (e.g. the request of the external resource of a use element, whose mode is same-origin) stay same-origin
        Load(Meta + "<img src=a.png><svg><use href=sprite.svg#a /></svg><img src=http://other.example/b.png>", url);
        Assert.Equal([$"Image {url}a.png", $"Image {url}sprite.svg#a", "Image https://other.example/b.png"], Requests.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void WithoutDirective_InsecureDocument_DoesNotUpgrade()
    {
        Load("<img src=http://other.example/a.png>", "http://example.com/");
        Assert.Equal(["Image http://other.example/a.png"], Requests);
    }

    [Fact]
    public void Navigation_HasTheUpgradeInsecureRequestsHeader()
    {
        var document = Load("<a href=https://other.example/page.html>link</a>");
        ((HtmlElement)document.QuerySelector("a")!).Click();
        _contexts.EventLoop.RunUntilIdle();
        var request = Assert.Single(_requests);
        Assert.Contains(request.Headers!, header => header.Key == "Upgrade-Insecure-Requests" && header.Value == "1");
    }

    [Theory]
    [InlineData("http://example.com/page.html", "https://example.com/page.html")]
    [InlineData("http://example.com:8080/page.html", "http://example.com:8080/page.html")]
    [InlineData("http://other.example/page.html", "http://other.example/page.html")]
    public void TopLevelNavigation_IsUpgradedForTheHostAndPortOfTheDocument(string href, string expected)
    {
        var document = Load(Meta + $"<a href={href}>link</a>", "http://example.com/");
        ((HtmlElement)document.QuerySelector("a")!).Click();
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal(["Document " + expected], Requests);
    }

    [Theory]
    [InlineData("get")]
    [InlineData("post")]
    public void FormSubmission_IsUpgraded(string method)
    {
        var document = Load(Meta + $"<form method={method} action=http://other.example/page.html><input name=a value=b></form>");
        ((HtmlFormElement)document.QuerySelector("form")!).Submit();
        _contexts.EventLoop.RunUntilIdle();
        var request = Assert.Single(_requests);
        Assert.StartsWith("https://other.example/page.html", request.Url, StringComparison.Ordinal);
    }

    [Fact]
    public void IframeNavigation_IsUpgraded()
    {
        var document = Load(Meta + "<iframe src=http://other.example/frame.html></iframe>");
        Assert.Equal(["Iframe https://other.example/frame.html"], Requests);
        var frame = ((HtmlIFrameElement)document.QuerySelector("iframe")!).ContentNavigable!.Document;
        Assert.Equal("https://other.example/frame.html", frame.Url);
    }

    [Fact]
    public void NestedDocuments_InheritThePolicy()
    {
        Load(Meta + "<iframe srcdoc=\"<img src=http://other.example/a.png>\"></iframe><iframe id=blank></iframe><script>var img = blank.contentDocument.createElement('img'); img.src = 'http://other.example/b.png'; blank.contentDocument.body.appendChild(img);</script>");
        Assert.Equal(["Image https://other.example/a.png", "Image https://other.example/b.png"], Requests.Order());
    }

    [Fact]
    public void MetaAddedAfterTheIframe_AppliesToItsNextDocuments()
    {
        var document = Load("<iframe srcdoc=a></iframe>", "http://example.com/");
        var meta = document.CreateElement("meta");
        meta.SetAttribute("http-equiv", "Content-Security-Policy");
        meta.SetAttribute("content", "upgrade-insecure-requests");
        document.Head!.AppendChild(meta);

        var iframe = (HtmlIFrameElement)document.QuerySelector("iframe")!;
        iframe.SetAttribute("srcdoc", "<img src=http://other.example/a.png>");
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal(["Image https://other.example/a.png"], Requests);
    }

    [Fact]
    public void ReportOnlyPolicy_ChecksTheUrlBeforeTheUpgrade()
    {
        // https://fetch.spec.whatwg.org/#main-fetch: the violations of the report-only policies are reported before the request
        // is upgraded (step 4), the enforced policies check the upgraded URL (step 7)
        var context = _contexts.Create(new BrowsingContextOptions { ScriptEngine = _engine, DocumentLoader = new Loader(this) });
        var document = context.Navigate("https://example.com/", _ => new DocumentResource(Meta + "<title>start</title><script>addEventListener('securitypolicyviolation', e => document.title += ' ' + e.disposition + ' ' + e.blockedURI);</script><img src=http://other.example/a.png>")
        {
            Headers = [new("Content-Type", "text/html"), new("Content-Security-Policy-Report-Only", "img-src https:")],
        });
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal("start report http://other.example/a.png", document.Title);
        Assert.Equal(["Image https://other.example/a.png"], Requests);
    }

    [Fact]
    public void Popup_InheritsThePolicyOfItsCreator()
    {
        // The initial about:blank document of a popup inherits the policy container of its creator, whose
        // upgrade-insecure-requests directive is initialized for the popup (without it, the script is blocked as mixed content)
        Load(Meta + "<script>var w = window.open(); var script = w.document.createElement('script'); script.src = 'http://other.example/a.js'; w.document.body.appendChild(script);</script>");
        Assert.Equal(["Script https://other.example/a.js"], Requests);
    }

    [Fact]
    public void HttpsUpgrades_AreDisabledByDefault()
    {
        var context = Navigate("http://example.com/page.html", httpsUpgrades: null);
        Assert.Equal(["Document http://example.com/page.html"], Requests);
        Assert.Equal("http://example.com/page.html", context.Document.Url);
    }

    [Fact]
    public void HttpsUpgrades_UpgradeTheTopLevelNavigation()
    {
        var context = Navigate("http://example.com/page.html#top", HttpsUpgradeOptions.Default);
        Assert.Equal(["Document https://example.com/page.html#top"], Requests);
        Assert.Equal("https://example.com/page.html#top", context.Document.Url);
    }

    [Fact]
    public void HttpsUpgrades_FallBackToHttp_WhenTheUpgradedRequestFails()
    {
        _responses["https://http-only.example.com/"] = _ => Task.FromResult<DocumentResource?>(null);
        var context = Navigate("http://http-only.example.com/", HttpsUpgradeOptions.Default);
        Assert.Equal(["Document https://http-only.example.com/", "Document http://http-only.example.com/"], Requests);
        Assert.Equal("http://http-only.example.com/", context.Document.Url);
        Assert.Equal("page", context.Document.Title);

        // The host is in the allowlist of the failed upgrades: its next navigations are not upgraded
        _requests.Clear();
        context.Navigate("http://http-only.example.com/other.html");
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal(["Document http://http-only.example.com/other.html"], Requests);
    }

    [Fact]
    public void HttpsUpgrades_FallBackToHttp_WhenTheHttpsUrlRedirectsToTheHttpUrl()
    {
        _responses["https://loop.example.com/"] = _ => Task.FromResult<DocumentResource?>(Redirect("http://loop.example.com/"));
        var context = Navigate("http://loop.example.com/", HttpsUpgradeOptions.Default);
        Assert.Equal(["Document https://loop.example.com/", "Document http://loop.example.com/"], Requests);
        Assert.Equal("http://loop.example.com/", context.Document.Url);
    }

    [Fact]
    public void HttpsUpgrades_UpgradeEachUrlOfTheRedirectChain()
    {
        _responses["https://a.example.com/"] = _ => Task.FromResult<DocumentResource?>(Redirect("http://b.example.com/"));
        _responses["https://b.example.com/"] = _ => Task.FromResult<DocumentResource?>(Redirect("https://c.example.com/"));
        var context = Navigate("http://a.example.com/", HttpsUpgradeOptions.Default);
        Assert.Equal(["Document https://a.example.com/", "Document https://b.example.com/", "Document https://c.example.com/"], Requests);
        Assert.Equal("https://c.example.com/", context.Document.Url);
    }

    [Fact]
    public async Task HttpsUpgrades_FallBackToHttp_AfterTheFallbackTimeout()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        _responses["https://slow.example.com/"] = async cancellationToken =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return null;
        };

        var context = Navigate("http://slow.example.com/", new HttpsUpgradeOptions { FallbackTimeout = TimeSpan.FromSeconds(3) }, clock);
        Assert.Equal(["Document https://slow.example.com/"], Requests);

        clock.Advance(TimeSpan.FromSeconds(3));
        await context.EventLoop.RunUntilIdleAsync();
        Assert.Equal(["Document https://slow.example.com/", "Document http://slow.example.com/"], Requests);
        Assert.Equal("http://slow.example.com/", context.Document.Url);
    }

    [Theory]
    [InlineData("http://192.168.1.10/")]
    [InlineData("http://[2001:db8::1]/")]
    [InlineData("http://localhost/")]
    [InlineData("http://intranet/")]
    [InlineData("http://printer.local/")]
    [InlineData("http://web-platform.test/")]
    [InlineData("http://example.com:8080/")]
    [InlineData("https://example.com/")]
    public void HttpsUpgrades_DoNotUpgradeTheExemptedUrls(string url)
    {
        Navigate(url, HttpsUpgradeOptions.Default);
        Assert.Equal(["Document " + url], Requests);
    }

    [Theory]
    [InlineData("http://intranet/", "https://intranet/")]
    [InlineData("http://web-platform.test:8443/", "https://web-platform.test:8443/")]
    [InlineData("http://example.com:443/", "https://example.com/")]
    public void HttpsUpgrades_CanUpgradeTheNonUniqueHostsAndTheNonDefaultPorts(string url, string expected)
    {
        Navigate(url, new HttpsUpgradeOptions { UpgradeNonDefaultPorts = true, UpgradeNonUniqueHosts = true });
        Assert.Equal(["Document " + expected], Requests);
    }

    [Fact]
    public void HttpsUpgrades_DoNotUpgradeTheNavigationsOfNestedNavigables()
    {
        var context = _contexts.Create(new BrowsingContextOptions { ScriptEngine = _engine, DocumentLoader = new Loader(this), HttpsUpgrades = HttpsUpgradeOptions.Default });
        context.Navigate("http://example.com/", _ => DocumentResource.Create("<iframe src=http://other.example.com/frame.html></iframe><form method=post action=http://other.example.com/post.html></form>", "text/html"));
        _contexts.EventLoop.RunUntilIdle();
        ((HtmlFormElement)context.Document.QuerySelector("form")!).Submit();
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal(["Iframe http://other.example.com/frame.html", "Document http://other.example.com/post.html"], Requests);
    }

    [Fact]
    public void HttpsUpgrades_TheReferrerIsDeterminedForTheUpgradedUrl()
    {
        var context = _contexts.Create(new BrowsingContextOptions { ScriptEngine = _engine, DocumentLoader = new Loader(this), HttpsUpgrades = HttpsUpgradeOptions.Default });
        context.Navigate("https://example.com/page.html", _ => DocumentResource.Create("<meta name=referrer content=no-referrer-when-downgrade><a href=http://other.example.com/>link</a>", "text/html"));
        _contexts.EventLoop.RunUntilIdle();
        ((HtmlElement)context.Document.QuerySelector("a")!).Click();
        _contexts.EventLoop.RunUntilIdle();
        var request = Assert.Single(_requests);
        Assert.Equal("https://other.example.com/", request.Url);
        Assert.Equal("https://example.com/page.html", request.Referrer);
        Assert.Equal("https://example.com/page.html", context.Document.Referrer);
    }

    [Fact]
    public void HttpsUpgradeAllowlist_IsBoundedAndExpires()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var allowlist = new HttpsUpgradeAllowlist();
        for (var i = 0; i <= HttpsUpgradeAllowlist.MaxCount; i++)
        {
            allowlist.Add("host" + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".example", clock);
            clock.Advance(TimeSpan.FromSeconds(1));
        }

        Assert.Equal(HttpsUpgradeAllowlist.MaxCount, allowlist.Count);
        Assert.False(allowlist.Contains("host0.example", clock));
        Assert.True(allowlist.Contains("host1.example", clock));

        clock.Advance(TimeSpan.FromDays(7) - TimeSpan.FromSeconds(1));
        Assert.True(allowlist.Contains("host1.example", clock)); // extended by the previous lookup
        Assert.False(allowlist.Contains("host2.example", clock));
    }

    [Fact]
    public void Hsts_IsEnabledByDefault()
    {
        Assert.Same(HstsOptions.Default, new BrowsingContextOptions().Hsts);
        Assert.True(HstsOptions.Default.LearnFromResponses);
        Assert.True(HstsOptions.Default.UsePreloadList);
        Assert.Null(HstsOptions.Default.IsKnownHost);
    }

    [Fact]
    public void Hsts_UpgradesThePreloadedHostsByDefault()
    {
        var context = CreateContext(HstsOptions.Default);
        Navigate(context, "http://github.com/");
        Navigate(context, "http://api.github.com/");
        Navigate(context, "http://mail.gmail.com/");
        Assert.Equal(["Document https://github.com/", "Document https://api.github.com/", "Document http://mail.gmail.com/"], Requests);

        // The preload list upgrades the third-party requests too
        _requests.Clear();
        context.Navigate("http://site.example/", _ => DocumentResource.Create("<title>start</title><script src=http://github.com/a.js></script>", "text/html"));
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal(["Script https://github.com/a.js"], Requests);
        Assert.Equal("start executed", context.Document.Title);
    }

    [Fact]
    public void Hsts_UsePreloadListFalse_DoesNotUpgradeThePreloadedHosts()
    {
        var context = CreateContext(new HstsOptions { UsePreloadList = false });
        Navigate(context, "http://github.com/");
        Assert.Equal(["Document http://github.com/"], Requests);
    }

    [Theory]
    [InlineData("github.com", true)]
    [InlineData("api.github.com", true)]
    [InlineData("gmail.com", true)]
    [InlineData("www.gmail.com", true)]
    [InlineData("mail.gmail.com", false)]
    [InlineData("mail.google.com", true)]
    [InlineData("a.b.mail.google.com", true)]
    [InlineData("google.com", false)]
    [InlineData("dev", true)]
    [InlineData("my-project.dev", true)]
    [InlineData("example.com", false)]
    [InlineData("com", false)]
    [InlineData("a..github.com", true)]
    [InlineData("a.b.c.d.e.f.g.h.i.j.k.l.m.n.o.p.q.r.s.t.u.v.w.x.y.z.github.com", true)]
    public void Hsts_IsPreloaded(string host, bool expected)
    {
        Assert.Equal(expected, StrictTransportSecurity.IsPreloaded(host));
    }

    [Fact]
    public void Hsts_IsPreloaded_AgreesWithEveryEntryOfTheList()
    {
        var entries = global::Meziantou.Framework.Http.HstsPreloadList.Shared.GetEntries().ToList();
        Assert.True(entries.Count > 90_000);
        foreach (var (host, includeSubdomains) in entries)
        {
            Assert.True(StrictTransportSecurity.IsPreloaded(host), host);

            // A subdomain is preloaded by its own entry, by the entry of the host, or by the entry of a superdomain
            if (includeSubdomains)
            {
                Assert.True(StrictTransportSecurity.IsPreloaded("subdomain-of." + host), host);
            }
        }
    }

    [Fact]
    public void Hsts_LearnsTheHostFromAnHttpsResponse()
    {
        ServeStrictTransportSecurity("https://secure.example.com/", "max-age=31536000");
        var context = CreateContext(HstsOptions.Default);
        Navigate(context, "https://secure.example.com/");

        // The navigations, their redirects and the subresources to the host are upgraded; a non-default port is kept
        _requests.Clear();
        _responses["https://secure.example.com/redirect"] = _ => Task.FromResult<DocumentResource?>(Redirect("http://secure.example.com/page.html"));
        Navigate(context, "http://secure.example.com/redirect");
        Assert.Equal(["Document https://secure.example.com/redirect", "Document https://secure.example.com/page.html"], Requests);
        Assert.Equal("https://secure.example.com/page.html", context.Document.Url);

        _requests.Clear();
        context.Navigate("http://other.example.com/", _ => DocumentResource.Create("<title>start</title><script src=http://secure.example.com:8080/a.js></script>", "text/html"));
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal(["Script https://secure.example.com:8080/a.js"], Requests);
        Assert.Equal("start executed", context.Document.Title);

        // The policy does not include the subdomains
        _requests.Clear();
        Navigate(context, "http://www.secure.example.com/");
        Assert.Equal(["Document http://www.secure.example.com/"], Requests);
    }

    [Fact]
    public void Hsts_IncludeSubDomains_UpgradesTheSubdomains()
    {
        ServeStrictTransportSecurity("https://example.org/", "max-age=60; includeSubDomains");
        var context = CreateContext(HstsOptions.Default);
        Navigate(context, "https://example.org/");

        _requests.Clear();
        Navigate(context, "http://a.b.example.org/");
        Navigate(context, "http://other.org/");
        Assert.Equal(["Document https://a.b.example.org/", "Document http://other.org/"], Requests);
    }

    [Fact]
    public void Hsts_IgnoresTheHeaderOfAnHttpResponse()
    {
        ServeStrictTransportSecurity("http://insecure.example.com/", "max-age=31536000");
        var context = CreateContext(HstsOptions.Default);
        Navigate(context, "http://insecure.example.com/");
        Navigate(context, "http://insecure.example.com/page.html");
        Assert.Equal(["Document http://insecure.example.com/", "Document http://insecure.example.com/page.html"], Requests);
    }

    [Fact]
    public void Hsts_TheFirstHeaderIsUsed_AndAnInvalidHeaderIsIgnored()
    {
        _responses["https://first.example.com/"] = _ => Task.FromResult<DocumentResource?>(new DocumentResource("") { Headers = [new("Strict-Transport-Security", "max-age=60"), new("Strict-Transport-Security", "max-age=0")] });
        ServeStrictTransportSecurity("https://invalid.example.com/", "max-age=60; max-age=60");
        var context = CreateContext(HstsOptions.Default);
        Navigate(context, "https://first.example.com/");
        Navigate(context, "https://invalid.example.com/");

        _requests.Clear();
        Navigate(context, "http://first.example.com/");
        Navigate(context, "http://invalid.example.com/");
        Assert.Equal(["Document https://first.example.com/", "Document http://invalid.example.com/"], Requests);
    }

    [Fact]
    public void Hsts_MaxAgeZero_RemovesTheHost()
    {
        ServeStrictTransportSecurity("https://secure.example.com/", "max-age=60");
        ServeStrictTransportSecurity("https://secure.example.com/clear", "max-age=0");
        var context = CreateContext(HstsOptions.Default);
        Navigate(context, "https://secure.example.com/");
        Navigate(context, "https://secure.example.com/clear");

        _requests.Clear();
        Navigate(context, "http://secure.example.com/");
        Assert.Equal(["Document http://secure.example.com/"], Requests);
    }

    [Fact]
    public void Hsts_ThePolicyExpires()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        ServeStrictTransportSecurity("https://secure.example.com/", "max-age=\"60\"");
        var context = CreateContext(HstsOptions.Default, timeProvider: clock);
        Navigate(context, "https://secure.example.com/");

        _requests.Clear();
        clock.Advance(TimeSpan.FromSeconds(59));
        Navigate(context, "http://secure.example.com/page.html");
        clock.Advance(TimeSpan.FromSeconds(1));
        Navigate(context, "http://secure.example.com/page.html");
        Assert.Equal(["Document https://secure.example.com/page.html", "Document http://secure.example.com/page.html"], Requests);
    }

    [Fact]
    public void Hsts_Null_DisablesHsts()
    {
        ServeStrictTransportSecurity("https://secure.example.com/", "max-age=60");
        var context = CreateContext(hsts: null);
        Navigate(context, "https://secure.example.com/");
        Navigate(context, "http://secure.example.com/");
        Assert.Equal(["Document https://secure.example.com/", "Document http://secure.example.com/"], Requests);
        Assert.Equal(0, context.Options.HstsHosts.Count);
    }

    [Fact]
    public void Hsts_LearnFromResponsesFalse_DoesNotLearnTheHosts()
    {
        ServeStrictTransportSecurity("https://secure.example.com/", "max-age=60");
        var context = CreateContext(new HstsOptions { LearnFromResponses = false });
        Navigate(context, "https://secure.example.com/");
        Navigate(context, "http://secure.example.com/");
        Assert.Equal(["Document https://secure.example.com/", "Document http://secure.example.com/"], Requests);
    }

    [Fact]
    public void Hsts_IsKnownHost_UpgradesItsHosts()
    {
        var hosts = new List<string>();
        var context = CreateContext(new HstsOptions
        {
            IsKnownHost = host =>
            {
                lock (hosts)
                {
                    hosts.Add(host);
                }

                return host is "preloaded.example.com";
            },
        });

        Navigate(context, "http://PreLoaded.Example.COM./page.html");
        Navigate(context, "http://other.example.com/");
        Navigate(context, "http://localhost/");
        Navigate(context, "http://app.localhost/");
        Navigate(context, "http://192.168.1.10/");
        Assert.Equal(["Document https://preloaded.example.com./page.html", "Document http://other.example.com/", "Document http://localhost/", "Document http://app.localhost/", "Document http://192.168.1.10/"], Requests);
        Assert.Equal(["preloaded.example.com", "other.example.com"], hosts);
    }

    [Fact]
    public void Hsts_TheReferrerIsDeterminedForTheHttpUrl()
    {
        var context = CreateContext(new HstsOptions { IsKnownHost = host => host is "preloaded.example.com" });
        context.Navigate("https://example.com/page.html", _ => DocumentResource.Create("<meta name=referrer content=no-referrer-when-downgrade><a href=http://preloaded.example.com/>link</a>", "text/html"));
        _contexts.EventLoop.RunUntilIdle();
        ((HtmlElement)context.Document.QuerySelector("a")!).Click();
        _contexts.EventLoop.RunUntilIdle();
        var request = Assert.Single(_requests);
        Assert.Equal("https://preloaded.example.com/", request.Url);
        Assert.Null(request.Referrer);
    }

    [Fact]
    public void Hsts_DoesNotFallBackToHttp_WithHttpsUpgrades()
    {
        _responses["https://preloaded.example.com/"] = _ => Task.FromResult<DocumentResource?>(null);
        var context = CreateContext(new HstsOptions { IsKnownHost = host => host is "preloaded.example.com" }, HttpsUpgradeOptions.Default);
        Navigate(context, "http://preloaded.example.com/");
        Assert.Equal(["Document https://preloaded.example.com/"], Requests);
        Assert.Equal(0, context.Options.HttpsUpgradeAllowlist.Count);
    }

    [Fact]
    public void Hsts_TheLearnedHostsDoNotUpgradeTheThirdPartyRequests()
    {
        ServeStrictTransportSecurity("https://tracker.example/", "max-age=60");
        var context = CreateContext(new HstsOptions { IsKnownHost = host => host is "preloaded.example" });
        Navigate(context, "https://tracker.example/");

        // The iframe and the image of another site are not upgraded, the preloaded hosts are
        _requests.Clear();
        context.Navigate("http://site.example/", _ => DocumentResource.Create("<title>start</title><iframe src=http://tracker.example/frame.html></iframe><img src=http://tracker.example/pixel.png><script src=http://preloaded.example/a.js></script>", "text/html"));
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal(["Iframe http://tracker.example/frame.html", "Image http://tracker.example/pixel.png", "Script https://preloaded.example/a.js"], Requests.Order(StringComparer.Ordinal));

        // A request to the same site as the top-level document is upgraded
        _requests.Clear();
        context.Navigate("http://www.tracker.example/", _ => DocumentResource.Create("<img src=http://tracker.example/pixel.png>", "text/html"));
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal(["Image https://tracker.example/pixel.png"], Requests);
    }

    // The Hsts_WebKit_* tests are ported from the HSTS tests of WebKit (Tools/TestWebKitAPI/Tests/WebKit/WKWebView/HSTS.mm,
    // see THIRD-PARTY-NOTICES.md)
    [Fact]
    public void Hsts_WebKit_Basic()
    {
        ServeStrictTransportSecurity("http://example.com/", "max-age=31536000");
        ServeStrictTransportSecurity("https://example.com/", "max-age=31536000");
        var context = CreateContext(HstsOptions.Default);
        Navigate(context, "http://example.com/");
        Assert.Equal("http://example.com/", context.Document.Url);
        Navigate(context, "http://example.com/");
        Assert.Equal("http://example.com/", context.Document.Url);
        Navigate(context, "https://example.com/");
        Assert.Equal("https://example.com/", context.Document.Url);
        Navigate(context, "http://example.com/");
        Assert.Equal("https://example.com/", context.Document.Url);
    }

    [Fact]
    public void Hsts_WebKit_ThirdPartyFetch()
    {
        ServeStrictTransportSecurity("https://example.com/", "max-age=31536000");
        _responses["http://example.com/"] = _ => Task.FromResult<DocumentResource?>(new DocumentResource("hi") { Headers = [new("Content-Type", "text/plain"), new("Access-Control-Allow-Origin", "http://example.org")] });
        var context = CreateContext(HstsOptions.Default);
        Navigate(context, "https://example.com/");

        _requests.Clear();
        context.Navigate("http://example.org/", _ => DocumentResource.Create("<script>fetch('http://example.com/').then(async response => document.title = await response.text() + (response.redirected ? ', redirected' : ', not redirected'))</script>", "text/html"));
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal("hi, not redirected", context.Document.Title);
        Assert.Equal(["http://example.com/"], _requests.Select(request => request.Url));
    }

    [Fact]
    public void Hsts_WebKit_ThirdParty()
    {
        ServeStrictTransportSecurity("https://example.com/", "max-age=31536000");
        _responses["http://example.com/"] = _ => Task.FromResult<DocumentResource?>(new DocumentResource("hi") { Headers = [new("Content-Type", "text/plain"), new("Access-Control-Allow-Origin", "http://example.org")] });
        var context = CreateContext(HstsOptions.Default);
        Navigate(context, "https://example.com/");

        _requests.Clear();
        context.Navigate("http://example.org/", _ => DocumentResource.Create("<script>var xhr = new XMLHttpRequest(); xhr.open('GET', 'http://example.com/'); xhr.onreadystatechange = () => { if (xhr.readyState == 4) document.title = xhr.responseURL + ' ' + xhr.responseText; }; xhr.send();</script>", "text/html"));
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal("http://example.com/ hi", context.Document.Title);
        Assert.Equal(["http://example.com/"], _requests.Select(request => request.Url));
    }

    [Fact]
    public void Hsts_WebKit_CrossOriginRedirect()
    {
        ServeStrictTransportSecurity("https://example.com/", "max-age=31536000");
        _responses["http://example.org/"] = _ => Task.FromResult<DocumentResource?>(new DocumentResource("") { StatusCode = 301, Headers = [new("Location", "http://example.com/")] });
        var context = CreateContext(HstsOptions.Default);
        Navigate(context, "https://example.com/");

        _requests.Clear();
        Navigate(context, "http://example.org/");
        Assert.Equal("https://example.com/", context.Document.Url);
        Assert.Equal(["Document http://example.org/", "Document https://example.com/"], Requests);
    }

    [Theory]
    [InlineData("max-age=60", 60L, false)]
    [InlineData("MAX-AGE=60;INCLUDESUBDOMAINS", 60L, true)]
    [InlineData(" max-age = \"60\" ; includeSubDomains ; preload ", 60L, true)]
    [InlineData(";;max-age=0;;", 0L, false)]
    [InlineData("includeSubDomains; max-age=1; ext=\"a;includeSubDomains\"", 1L, true)]
    [InlineData("max-age=60; ext=\"a;includeSubDomains\"", 60L, false)]
    [InlineData("max-age=99999999999999999999999", StrictTransportSecurityHeader.MaxMaxAgeSeconds, false)]
    [InlineData("max-age=31536001", StrictTransportSecurityHeader.MaxMaxAgeSeconds, false)]
    [InlineData("max-age=\"\\60\"", 60L, false)]
    [InlineData("max-age=0000000000000000000000060", 60L, false)]
    [InlineData("max-age=0000000000000000000000000", 0L, false)]
    public void Hsts_ParsesTheHeader(string value, long maxAge, bool includeSubdomains)
    {
        var header = StrictTransportSecurityHeader.Parse(value);
        Assert.Equal(new StrictTransportSecurityHeader(maxAge, includeSubdomains), header);
        Assert.Equal(header, StrictTransportSecurityHeader.Parse(header!.Value.Serialize()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("includeSubDomains")]
    [InlineData("max-age")]
    [InlineData("max-age=")]
    [InlineData("max-age=-1")]
    [InlineData("max-age=1.5")]
    [InlineData("max-age=\"60")]
    [InlineData("max-age=60 60")]
    [InlineData("max-age=60, max-age=60")]
    [InlineData("max-age=60; max-age=60")]
    [InlineData("max-age=60; includeSubDomains; includeSubDomains")]
    [InlineData("max-age=60; includeSubDomains=1")]
    [InlineData("max-age=60; =1")]
    [InlineData("max-age=60; ext=")]
    public void Hsts_RejectsAnInvalidHeader(string value)
    {
        Assert.Null(StrictTransportSecurityHeader.Parse(value));
    }

    // The cases of the tests of the parser of Chromium (net/http/http_security_headers_unittest.cc, see THIRD-PARTY-NOTICES.md)
    public static TheoryData<string, long, bool> ChromiumValidHeaders => new()
    {
        { "max-age=123", 123L, false },
        { ";max-age=123", 123L, false },
        { ";;max-age=123", 123L, false },
        { ";;;;max-age=123", 123L, false },
        { "; ;max-age=123", 123L, false },
        { "; ; max-age=123", 123L, false },
        { ";max-age=123;", 123L, false },
        { ";;max-age=123;;", 123L, false },
        { ";;;;max-age=123;;;;", 123L, false },
        { "; ;max-age=123; ;", 123L, false },
        { "; ; max-age=123; ; ", 123L, false },
        { "max-age=123;", 123L, false },
        { "max-age=123;;", 123L, false },
        { "max-age=123;;;;", 123L, false },
        { "max-age=123; ;", 123L, false },
        { "max-age=123; ; ", 123L, false },
        { "max-age=243", 243L, false },
        { "max-age=3488923;", 3488923L, false },
        { "  Max-agE    = 567", 567L, false },
        { "  mAx-aGe    = 890      ", 890L, false },
        { "max-age=123;incLudesUbdOmains", 123L, true },
        { "incLudesUbdOmains; max-age=123", 123L, true },
        { "   incLudesUbdOmains; max-age=123", 123L, true },
        { "   incLudesUbdOmains; max-age=123; pumpkin=kitten", 123L, true },
        { "   pumpkin=894; incLudesUbdOmains; max-age=123  ", 123L, true },
        { "   pumpkin; incLudesUbdOmains; max-age=123  ", 123L, true },
        { "   pumpkin; incLudesUbdOmains; max-age=\"123\"  ", 123L, true },
        { "animal=\"squirrel; distinguished\"; incLudesUbdOmains; max-age=123", 123L, true },
        { "max-age=394082;  incLudesUbdOmains", 394082L, true },
        { "max-age=39408299  ;incLudesUbdOmains", StrictTransportSecurityHeader.MaxMaxAgeSeconds, true },
        { "max-age=394082038  ; incLudesUbdOmains", StrictTransportSecurityHeader.MaxMaxAgeSeconds, true },
        { "max-age=394082038  ; incLudesUbdOmains;", StrictTransportSecurityHeader.MaxMaxAgeSeconds, true },
        { ";; max-age=394082038  ; incLudesUbdOmains; ;", StrictTransportSecurityHeader.MaxMaxAgeSeconds, true },
        { ";; max-age=394082038  ;", StrictTransportSecurityHeader.MaxMaxAgeSeconds, false },
        { ";;    ; ; max-age=394082038;;; includeSubdomains     ;;  ;", StrictTransportSecurityHeader.MaxMaxAgeSeconds, true },
        { "incLudesUbdOmains   ; max-age=394082038 ;;", StrictTransportSecurityHeader.MaxMaxAgeSeconds, true },
        { "  max-age=0  ;  incLudesUbdOmains   ", 0L, true },
        { "  max-age=999999999999999999999999999999999999999999999  ;  incLudesUbdOmains   ", StrictTransportSecurityHeader.MaxMaxAgeSeconds, true },
    };

    public static TheoryData<string> ChromiumInvalidHeaders => new()
    {
        "'max-age'=1",
        "\"max-age\"=1",
        "max-age=1; max-age=2",
        "max-age=1; MaX-AgE=2",
        "max-age=1; includeSubDomains; iNcLUdEsUbDoMaInS",
        "max-age=1; \"",
        "max-age=1; \"includeSubdomains",
        "max-age=1; in\"cludeSubdomains",
        "max-age=1; includeSubdomains\"",
        "max-age=1; \"includeSubdomains\"",
        "max-age=1; includeSubdomains; non\"token",
        "max-age=1; includeSubdomains; non@token",
        "max-age=1; includeSubdomains; non,token",
        "max-age=1; =2",
        "max-age=1; =2; unknownDirective",
        "max-age=",
        "max-age=@",
        "max-age=1a;",
        "max-age=1a2;",
        "max-age=1##;",
        "max-age=12\";",
        "max-age=-1;",
        "max-age=+1;",
        "max-age='1';",
        "max-age=1abc;",
        "max-age=1 abc;",
        "max-age=1.5;",
        "max-age=1; includeSubDomains=true",
        "max-age=1; includeSubDomains=false",
        "max-age=1; includeSubDomains=\"\"",
        "max-age=1; includeSubDomains=''",
        "max-age=1; includeSubDomains=\"true\"",
        "max-age=1; includeSubDomains=\"false\"",
        "max-age=1; unknownDirective=non\"token",
        "max-age=1; unknownDirective=non@token",
        "max-age=1; unknownDirective=non,token",
        "max-age=1; unknownDirective=",
        "",
        "    ",
        "abc",
        "  abc",
        "  abc   ",
        "max-age",
        "  max-age",
        "  max-age  ",
        "   max-age=",
        "   max-age  =",
        "   max-age=   ",
        "   max-age  =     ",
        "   max-age  =     xy",
        "   max-age  =     3488a923",
        "max-age=3488a923  ",
        "max-ag=3488923",
        "max-aged=3488923",
        "max-age==3488923",
        "amax-age=3488923",
        "max-age=-3488923",
        "max-age=+3488923",
        "max-age=13####",
        "max-age=9223372036854775807#####",
        "max-age=18446744073709551615####",
        "max-age=999999999999999999999999$.&#!",
        "max-age=3488923     e",
        "max-age=3488923     includesubdomain",
        "max-age=3488923includesubdomains",
        "max-age=3488923=includesubdomains",
        "max-age=3488923 includesubdomainx",
        "max-age=3488923 includesubdomain=",
        "max-age=3488923 includesubdomain=true",
        "max-age=3488923 includesubdomainsx",
        "max-age=3488923 includesubdomains x",
        "max-age=34889.23 includesubdomains",
        "max-age=34889 includesubdomains",
        ";;;; ;;;",
        ";;;; includeSubDomains;;;",
        "   includeSubDomains;  ",
        ";",
        "max-age; ;",
    };

    [Theory]
    [MemberData(nameof(ChromiumValidHeaders))]
    public void Hsts_ParsesTheHeader_Chromium(string value, long maxAge, bool includeSubdomains)
    {
        Assert.Equal(new StrictTransportSecurityHeader(maxAge, includeSubdomains), StrictTransportSecurityHeader.Parse(value));
    }

    [Theory]
    [MemberData(nameof(ChromiumInvalidHeaders))]
    public void Hsts_RejectsAnInvalidHeader_Chromium(string value)
    {
        Assert.Null(StrictTransportSecurityHeader.Parse(value));
    }

    [Fact]
    public void HstsHostStore_IsBounded()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var store = new HstsHostStore();
        for (var i = 0; i <= HstsHostStore.MaxCount; i++)
        {
            store.Add("host" + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".example", new StrictTransportSecurityHeader(60 + i, IncludeSubdomains: false), clock);
        }

        Assert.Equal(HstsHostStore.MaxCount, store.Count);
        Assert.False(store.Contains("host0.example", clock));
        Assert.True(store.Contains("host1.example", clock));
    }
}
