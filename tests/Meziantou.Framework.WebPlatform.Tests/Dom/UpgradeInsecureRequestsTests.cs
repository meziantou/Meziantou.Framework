using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.Tests.Scripting;

namespace Meziantou.Framework.WebPlatform.Tests.Dom;

/// <summary>Upgrade Insecure Requests (https://w3c.github.io/webappsec-upgrade-insecure-requests/): the <c>upgrade-insecure-requests</c> directive.</summary>
public sealed class UpgradeInsecureRequestsTests : IDisposable
{
    private const string Meta = "<meta http-equiv=Content-Security-Policy content=upgrade-insecure-requests>";

    // A 2x3 PNG (only the header is read)
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52, 0, 0, 0, 2, 0, 0, 0, 3, 8, 6, 0, 0, 0];

    private readonly BrowsingContextScope _contexts = new();
    private readonly ScriptEngine _engine = TestScriptEngines.Create();
    private readonly List<DocumentRequest> _requests = [];

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

    private List<string> Requests => [.. _requests.Select(r => r.Destination + " " + r.Url)];

    /// <summary>Serves a script for the .js URLs, a PNG for the .png URLs, a text for the .txt URLs and a document for the other URLs.</summary>
    private sealed class Loader(UpgradeInsecureRequestsTests test) : DocumentLoader
    {
        public override ValueTask<DocumentResource?> LoadAsync(DocumentRequest request, CancellationToken cancellationToken)
        {
            test._requests.Add(request);
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
}
