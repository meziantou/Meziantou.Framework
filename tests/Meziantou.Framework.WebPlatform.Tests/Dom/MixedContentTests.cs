using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.Tests.Scripting;

namespace Meziantou.Framework.WebPlatform.Tests.Dom;

/// <summary>Mixed Content (https://w3c.github.io/webappsec-mixed-content/): the http: subresources of an https: document.</summary>
public sealed class MixedContentTests : IDisposable
{
    // A 2x3 PNG (only the header is read)
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52, 0, 0, 0, 2, 0, 0, 0, 3, 8, 6, 0, 0, 0];

    private static readonly byte[] FontData = TestFonts.Ahem;

    private readonly BrowsingContextScope _contexts = new();
    private readonly ScriptEngine _engine = TestScriptEngines.Create();
    private readonly List<DocumentRequest> _requests = [];

    public void Dispose()
    {
        _contexts.Dispose();
        TestScriptEngines.Dispose(_engine);
    }

    private Document Load(string html, string url = "https://example.com/")
    {
        var context = _contexts.Create(new BrowsingContextOptions { ScriptEngine = _engine, DocumentLoader = new Loader(this, html) });
        var document = context.Navigate(url, _ => html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    private List<string> Requests => [.. _requests.Select(r => r.Destination + " " + r.Url)];

    /// <summary>
    /// Serves a style sheet for the .css URLs, a script for the .js URLs, a PNG for the .png URLs, a font for the .woff URLs
    /// and a document for the .html URLs. A URL containing "redirect" is answered from http://redirected.example/.
    /// </summary>
    private sealed class Loader(MixedContentTests test, string html) : DocumentLoader
    {
        public override ValueTask<DocumentResource?> LoadAsync(DocumentRequest request, CancellationToken cancellationToken)
        {
            test._requests.Add(request);
            var path = request.Url.Split('?')[0];
            var resource = path switch
            {
                _ when path.EndsWith(".css", StringComparison.Ordinal) => DocumentResource.Create("p { color: rgb(0, 128, 0) }", "text/css"),
                _ when path.EndsWith(".js", StringComparison.Ordinal) => DocumentResource.Create("document.title += ' executed'", "text/javascript"),
                _ when path.EndsWith(".png", StringComparison.Ordinal) => DocumentResource.Create(Png, "image/png"),
                // The fonts are CORS requests (https://drafts.csswg.org/css-fonts/#font-fetching-requirements)
                _ when path.EndsWith(".woff", StringComparison.Ordinal) => new DocumentResource(FontData) { Headers = [new("Access-Control-Allow-Origin", "*")] },
                _ => DocumentResource.Create(html, "text/html"),
            };

            return ValueTask.FromResult<DocumentResource?>(request.Url.Contains("redirect", StringComparison.Ordinal) ? new DocumentResource(resource.ContentBytes ?? System.Text.Encoding.UTF8.GetBytes(resource.Content ?? "")) { Url = "http://redirected.example/" + path[(path.LastIndexOf('/', StringComparison.Ordinal) + 1)..], Headers = resource.Headers } : resource);
        }
    }

    [Theory]
    [InlineData("http://other.example/a.css")]
    [InlineData("http://192.0.2.1/a.css")]
    public void StyleSheet_HttpFromHttps_IsBlocked(string href)
    {
        var document = Load($"<link rel=stylesheet href={href} onload=\"document.title = 'load'\" onerror=\"document.title = 'error'\">");
        Assert.Equal("error", document.Title);

        // The blocked request is a network error: as for any failed load, the element has an empty style sheet
        Assert.Single(document.StyleSheets);
        Assert.Empty(_requests);
    }

    [Theory]
    [InlineData("https://example.com/", "https://other.example/a.css")]
    [InlineData("https://example.com/", "http://localhost/a.css")]
    [InlineData("https://example.com/", "http://127.0.0.1/a.css")]
    [InlineData("http://example.com/", "http://other.example/a.css")]
    public void StyleSheet_PotentiallyTrustworthyOrNotSecureDocument_IsLoaded(string pageUrl, string href)
    {
        var document = Load($"<link rel=stylesheet href={href} onload=\"document.title = 'load'\" onerror=\"document.title = 'error'\">", pageUrl);
        Assert.Equal("load", document.Title);
        Assert.Equal(["Style " + href], Requests);
    }

    [Fact]
    public void StyleSheet_ResponseRedirectedToHttp_IsBlocked()
    {
        var document = Load("<link rel=stylesheet href=https://other.example/redirect.css onload=\"document.title = 'load'\" onerror=\"document.title = 'error'\">");
        Assert.Equal("error", document.Title);
        Assert.Single(document.StyleSheets);
    }

    [Theory]
    [InlineData("data:text/css,@import url('http://other.example/a.css');")]
    [InlineData("data:text/css,@import url(\"data:text/css,@import url('http://other.example/a.css');\");")]
    public void Import_HttpFromHttps_IsBlocked(string href)
    {
        var document = Load($"<link rel=stylesheet href=\"{href.Replace("\"", "&quot;", StringComparison.Ordinal)}\" onload=\"document.title = 'load'\" onerror=\"document.title = 'error'\">");
        Assert.Equal("error", document.Title);
        Assert.Empty(_requests);
    }

    [Fact]
    public void Import_HttpsFromHttps_IsLoaded()
    {
        var document = Load("<style>@import url(https://other.example/a.css);</style><p>text</p>");
        Assert.Equal("rgb(0, 128, 0)", document.DefaultView!.GetComputedStyle(document.QuerySelector("p")!).Color);
        Assert.Equal(["Style https://other.example/a.css"], Requests);
    }

    [Theory]
    [InlineData("")]
    [InlineData("type=module")]
    public void Script_HttpFromHttps_IsBlocked(string attributes)
    {
        var document = Load($"<title>start</title><script {attributes} src=http://other.example/a.js onerror=\"document.title += ' error'\"></script>");
        Assert.Equal("start error", document.Title);
        Assert.Empty(_requests);
    }

    [Fact]
    public void Script_HttpsFromHttps_IsExecuted()
    {
        var document = Load("<title>start</title><script src=https://other.example/a.js></script>");
        Assert.Equal("start executed", document.Title);
    }

    [Fact]
    public void Image_HttpFromHttps_IsUpgraded()
    {
        var document = Load("<img src=http://other.example/a.png onload=\"document.title = 'load'\">");
        var image = (HtmlImageElement)document.QuerySelector("img")!;
        Assert.Equal("load", document.Title);
        Assert.Equal((2u, 3u), (image.NaturalWidth, image.NaturalHeight));

        // currentSrc is the URL of the element, the request is the upgraded URL (and its referrer is the full URL of the page)
        Assert.Equal("http://other.example/a.png", image.CurrentSrc);
        var request = Assert.Single(_requests);
        Assert.Equal("https://other.example/a.png", request.Url);
        Assert.Equal("https://example.com/", request.Referrer);
    }

    [Theory]
    [InlineData("http://other.example:80/a.png", "https://other.example/a.png")]
    [InlineData("http://other.example:8080/a.png", "https://other.example:8080/a.png")]
    [InlineData("http://other.example:443/a.png", "https://other.example/a.png")]
    public void Image_Upgrade_KeepsANonDefaultPort(string src, string expected)
    {
        Load($"<img src={src}>");
        Assert.Equal(["Image " + expected], Requests);
    }

    [Theory]
    [InlineData("<img src=http://192.0.2.1/a.png onerror=\"document.title = 'error'\">")]
    [InlineData("<img srcset=http://other.example/a.png onerror=\"document.title = 'error'\">")]
    [InlineData("<picture><img src=http://other.example/a.png onerror=\"document.title = 'error'\"></picture>")]
    public void Image_NotUpgradeable_IsBlocked(string html)
    {
        var document = Load(html);
        Assert.Equal("error", document.Title);
        Assert.Empty(_requests);
    }

    [Fact]
    public void Image_HttpFromHttp_IsNotUpgraded()
    {
        Load("<img src=http://other.example/a.png>", "http://example.com/");
        Assert.Equal(["Image http://other.example/a.png"], Requests);
    }

    [Fact]
    public void SvgImageAndPoster_AreUpgraded()
    {
        Load("<svg><image href=http://other.example/a.png /></svg><video poster=http://other.example/b.png></video>");
        Assert.Equal(["Image https://other.example/a.png", "Image https://other.example/b.png"], Requests.Order());
    }

    [Fact]
    public void FontFace_HttpFromHttps_IsBlocked()
    {
        var document = Load("");
        var face = new FontFace("A", "url(http://other.example/a.woff), url(https://other.example/b.woff)");
        document.Fonts.Add(face);
        _ = face.Load();
        _contexts.EventLoop.RunUntilIdle();

        // The http: source is a network error: the next source is loaded
        Assert.Equal(FontFaceLoadStatus.Loaded, face.Status);
        Assert.Equal(["Font https://other.example/b.woff"], Requests);
    }

    [Fact]
    public void IframeNavigation_HttpFromHttps_IsBlocked()
    {
        // https://w3c.github.io/webappsec-mixed-content/#should-block-fetch: the navigation of a child navigable is blockable
        // mixed content (the iframe has an error document)
        var document = Load("<iframe src=http://other.example/frame.html></iframe>");
        Assert.Empty(Requests);
        var frame = ((HtmlIFrameElement)document.QuerySelector("iframe")!).ContentNavigable!.Document;
        Assert.Equal(0u, frame.GetElementsByTagName("iframe").Length);
    }

    [Fact]
    public void IframeNavigation_RedirectedToHttp_IsBlocked()
    {
        // Each URL of the redirect chain is checked (the loader follows the redirect to http://redirected.example/)
        var document = Load("<iframe src=https://other.example/redirect.html></iframe>");
        var frame = ((HtmlIFrameElement)document.QuerySelector("iframe")!).ContentNavigable!.Document;
        Assert.NotEqual("http://redirected.example/redirect.html", frame.Url);
        Assert.Equal(0u, frame.GetElementsByTagName("iframe").Length);
    }

    [Fact]
    public void TopLevelNavigation_HttpFromHttps_IsNotBlocked()
    {
        // The navigations of the top-level navigables are not mixed content
        var document = Load("<a href=http://other.example/page.html>link</a>");
        ((HtmlElement)document.QuerySelector("a")!).Click();
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal(["Document http://other.example/page.html"], Requests);
    }

    [Fact]
    public void NestedHttpDocument_OfAnHttpsDocument_BlocksMixedContent()
    {
        // The document of the iframe is not secure, but the origin of its parent is potentially trustworthy. The navigation is
        // not initiated by a document (it has no client), so it is not checked.
        var context = _contexts.Create(new BrowsingContextOptions { ScriptEngine = _engine, DocumentLoader = new Loader(this, "<link rel=stylesheet href=http://other.example/a.css onerror=\"document.title = 'error'\">") });
        var document = context.Navigate("https://example.com/", _ => "<iframe></iframe>");
        _contexts.EventLoop.RunUntilIdle();

        var navigable = ((HtmlIFrameElement)document.QuerySelector("iframe")!).ContentNavigable!;
        navigable.Navigate("http://other.example/frame.html");
        _contexts.EventLoop.RunUntilIdle();

        var frameDocument = navigable.Document;
        Assert.Equal("http://other.example/frame.html", frameDocument.Url);
        Assert.Equal("error", frameDocument.Title);
        Assert.Equal(["Iframe http://other.example/frame.html"], Requests);
    }
}
