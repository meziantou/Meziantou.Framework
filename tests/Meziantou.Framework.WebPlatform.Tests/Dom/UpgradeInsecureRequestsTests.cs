using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.Tests.Scripting;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Dom;

/// <summary>
/// Upgrade Insecure Requests (https://w3c.github.io/webappsec-upgrade-insecure-requests/): the <c>upgrade-insecure-requests</c>
/// directive; and the HTTPS upgrades of the top-level navigations (https://github.com/whatwg/fetch/pull/1655).
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
}
