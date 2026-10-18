using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Jint;

namespace Meziantou.Framework.WebPlatform.Tests.Html;

/// <summary>
/// The resources fetched by the link elements (https://html.spec.whatwg.org/multipage/links.html#link-type-stylesheet and
/// https://html.spec.whatwg.org/multipage/links.html#link-type-preload), the SVG image elements and the poster of the videos.
/// </summary>
public sealed class LinkedResourceTests : IDisposable
{
    // A 2x3 PNG (only the header is read)
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52, 0, 0, 0, 2, 0, 0, 0, 3, 8, 6, 0, 0, 0];

    private readonly BrowsingContextScope _contexts = new();
    private readonly JintScriptEngine _engine = new();
    private readonly List<DocumentRequest> _requests = [];

    public void Dispose()
    {
        _contexts.Dispose();
        _engine.Dispose();
    }

    private Document Load(string html)
    {
        var context = _contexts.Create(new BrowsingContextOptions { ScriptEngine = _engine, DocumentLoader = new Loader(this) });
        var document = context.Navigate("https://example.com/", _ => html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    private List<string> Requests => [.. _requests.Select(r => r.Destination + " " + r.Url)];

    /// <summary>Serves a style sheet for the .css URLs, a PNG for the .png URLs, and a text file for the other ones.</summary>
    private sealed class Loader(LinkedResourceTests test) : DocumentLoader
    {
        public override ValueTask<DocumentResource?> LoadAsync(DocumentRequest request, CancellationToken cancellationToken)
        {
            test._requests.Add(request);
            DocumentResource? resource = request.Url switch
            {
                _ when request.Url.Contains("missing", StringComparison.Ordinal) => null,
                _ when request.Url.Contains("redirect-to-same-origin", StringComparison.Ordinal) => new DocumentResource("p { color: rgb(0, 128, 0) }") { Headers = [new("Content-Type", "text/plain"), new("Access-Control-Allow-Origin", "*")], Url = "https://example.com/a.txt" },
                _ when request.Url.EndsWith("import.css", StringComparison.Ordinal) => DocumentResource.Create("@import 'b.css'; p { color: red }", "text/css"),
                _ when request.Url.EndsWith(".css", StringComparison.Ordinal) => DocumentResource.Create("p { color: rgb(0, 128, 0) }", "text/css"),
                _ when request.Url.EndsWith(".png", StringComparison.Ordinal) => DocumentResource.Create(Png, "image/png"),
                _ => DocumentResource.Create("p { color: red }", "text/plain"),
            };

            return ValueTask.FromResult(resource);
        }
    }

    [Fact]
    public void StyleSheet_IsFetchedAndApplied()
    {
        var document = Load("<link rel=stylesheet href=a.css onload=\"document.title = 'load ' + this.sheet.href\"><p>text</p>");
        var link = (HtmlLinkElement)document.QuerySelector("link")!;
        Assert.Equal("load https://example.com/a.css", document.Title);
        Assert.Same(link.Sheet, Assert.Single(document.StyleSheets));
        Assert.Same(link, link.Sheet!.OwnerNode);
        Assert.Equal("rgb(0, 128, 0)", document.DefaultView!.GetComputedStyle(document.QuerySelector("p")!).Color);

        var request = Assert.Single(_requests);
        Assert.Equal(RequestDestination.Style, request.Destination);
        Assert.Equal(RequestMode.NoCors, request.Mode);
        Assert.Same(link, request.Initiator);
        Assert.Equal("https://example.com/", request.Referrer);
    }

    [Theory]
    [InlineData("a.txt")]
    [InlineData("missing.css")]
    public void StyleSheet_ThatIsNotACssResponse_FiresError(string href)
    {
        var document = Load($"<!doctype html><link rel=stylesheet href={href} onerror=\"document.title = 'error'\"><p>text</p>");
        Assert.Equal("error", document.Title);

        // As in browsers, the element has an empty style sheet
        var sheet = ((HtmlLinkElement)document.QuerySelector("link")!).Sheet;
        Assert.Same(sheet, Assert.Single(document.StyleSheets));
        Assert.Equal("https://example.com/" + href, sheet!.Href);
        if (href.Contains("missing", StringComparison.Ordinal))
        {
            // The rules of the style sheet of a network error cannot be accessed
            Assert.Equal("SecurityError", Assert.Throws<DomException>(() => sheet.CssRules).Name);
        }
        else
        {
            Assert.Empty(sheet.CssRules);
        }
    }

    [Fact]
    public void StyleSheet_BlocksTheScriptsOfTheParser()
    {
        var document = Load("<!doctype html><link rel=stylesheet href=import.css><style>@import 'a.css';</style><script>document.title = document.styleSheets.length + ' ' + document.styleSheets[1].cssRules[0].styleSheet.cssRules.length</script>");
        Assert.Equal("2 1", document.Title);
    }

    [Fact]
    public void StyleSheet_WhoseMediaDoesNotMatch_IsFetchedButDoesNotBlockTheScriptsNorApply()
    {
        var document = Load("<!doctype html><link rel=stylesheet media=\"not all\" href=a.css><script>document.title = document.styleSheets.length</script><p>text</p>");
        Assert.Equal("0", document.Title);
        Assert.Equal(["Style https://example.com/a.css"], Requests);
        Assert.Single(document.StyleSheets);
        Assert.Equal("rgb(0, 0, 0)", document.DefaultView!.GetComputedStyle(document.QuerySelector("p")!).Color);

        // The style sheet applies when its media matches, without being fetched again
        document.QuerySelector("link")!.SetAttribute("media", "all");
        _contexts.EventLoop.RunUntilIdle();
        Assert.Single(_requests);
        Assert.Equal("rgb(0, 128, 0)", document.DefaultView!.GetComputedStyle(document.QuerySelector("p")!).Color);
    }

    [Fact]
    public void StyleSheet_TitleIsTheAttribute()
    {
        var document = Load("<!doctype html><link rel=stylesheet href=a.css title=a>");
        var link = (HtmlLinkElement)document.QuerySelector("link")!;
        var sheet = link.Sheet!;
        Assert.Equal("a", sheet.Title);

        link.SetAttribute("title", "");
        Assert.Null(sheet.Title);
        Assert.Same(sheet, link.Sheet);
    }

    [Fact]
    public void AlternateStyleSheet_IsFetchedButDoesNotApply_UnlessExplicitlyEnabled()
    {
        var document = Load("<!doctype html><link rel=\"alternate stylesheet\" title=alt href=a.css disabled><p>text</p>");
        var link = (HtmlLinkElement)document.QuerySelector("link")!;
        Assert.Null(link.Sheet);

        // Removing the disabled attribute enables the alternative style sheet explicitly
        link.Disabled = false;
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal("rgb(0, 128, 0)", document.DefaultView!.GetComputedStyle(document.QuerySelector("p")!).Color);

        var clone = (HtmlLinkElement)link.CloneNode();
        link.Remove();
        document.Head!.AppendChild(clone);
        _contexts.EventLoop.RunUntilIdle();
        Assert.NotNull(clone.Sheet);
        Assert.Equal("rgb(0, 0, 0)", document.DefaultView!.GetComputedStyle(document.QuerySelector("p")!).Color);
    }

    [Fact]
    public void StyleSheet_InQuirksMode_SameOriginResponsesAreStyleSheets()
    {
        var document = Load("<link rel=stylesheet href=a.txt onload=\"document.title = 'load'\">");
        Assert.Equal("BackCompat", document.CompatMode);
        Assert.Equal("load", document.Title);
    }

    [Fact]
    public void StyleSheet_InQuirksMode_TheQuirkUsesTheResponseUrl()
    {
        // The document has the same origin as the URL of the response (after a redirect from another origin), and the CORS
        // response is CORS-same-origin
        var document = Load("<link rel=stylesheet crossorigin href='https://other.example/redirect-to-same-origin' onload=\"document.title = 'load'\"><p>text</p>");
        Assert.Equal("load", document.Title);
        Assert.Equal("rgb(0, 128, 0)", document.DefaultView!.GetComputedStyle(document.QuerySelector("p")!).Color);

        // A no-CORS response redirected through another origin is not CORS-same-origin
        document = Load("<link rel=stylesheet href='https://other.example/redirect-to-same-origin' onerror=\"document.title = 'error'\"><p>text</p>");
        Assert.Equal("error", document.Title);
    }

    [Fact]
    public void StyleSheet_InQuirksMode_DataUrlsWithAnotherMimeTypeAreNotStyleSheets()
    {
        // The origin of a data: URL is opaque, so it is not same origin with the document
        var document = Load("<link rel=stylesheet href=\"data:text/plain,p { color: red }\" onerror=\"document.title = 'error'\"><link rel=stylesheet href=\"data:text/css,@import url('data:text/plain,p { color: red }');\"><p>text</p>");
        Assert.Equal("BackCompat", document.CompatMode);
        Assert.Equal("error", document.Title);
        Assert.Equal("rgb(0, 0, 0)", document.DefaultView!.GetComputedStyle(document.QuerySelector("p")!).Color);
    }

    [Fact]
    public void RelList_SupportsTheImplementedLinkTypes()
    {
        var document = Load("<!doctype html><link><a></a><area><form></form>");
        var link = (HtmlLinkElement)document.QuerySelector("link")!;
        Assert.True(link.RelList.Supports("MANIFEST"));
        Assert.True(link.RelList.Supports("stylesheet"));
        Assert.False(link.RelList.Supports("noopener"));
        Assert.True(link.RelList.Supports("modulepreload"));
        Assert.True(link.RelList.Supports("prefetch"));
        Assert.False(link.RelList.Supports("preconnect"));
        Assert.True(((HtmlAnchorElement)document.QuerySelector("a")!).RelList.Supports("noopener"));
        Assert.True(((HtmlAreaElement)document.QuerySelector("area")!).RelList.Supports("noreferrer"));
        Assert.True(((HtmlFormElement)document.QuerySelector("form")!).RelList.Supports("opener"));
        Assert.False(((HtmlFormElement)document.QuerySelector("form")!).RelList.Supports("stylesheet"));
    }

    [Theory]
    [InlineData("rel=\"alternate stylesheet\"")]
    [InlineData("rel=\"alternate stylesheet\" title=\"\"")]
    [InlineData("rel=stylesheet disabled")]
    [InlineData("rel=stylesheet type=text/plain")]
    [InlineData("rel=preload as=style media=\"not all\"")]
    [InlineData("rel=icon")]
    [InlineData("rel=preload as=video")]
    public void NotFetched(string attributes)
    {
        Load($"<!doctype html><link {attributes} href=a.css>");
        Assert.Empty(_requests);
    }

    [Fact]
    public void StyleSheet_WaitsForItsImports()
    {
        var document = Load("<!doctype html><link rel=stylesheet href=import.css onload=\"document.title = 'load'\"><p>text</p>");
        Assert.Equal("load", document.Title);
        Assert.Equal(["Style https://example.com/import.css", "Style https://example.com/b.css"], Requests);
        Assert.Equal("rgb(255, 0, 0)", document.DefaultView!.GetComputedStyle(document.QuerySelector("p")!).Color);
    }

    [Fact]
    public void StyleSheet_IsRemovedWithTheElement_AndFetchedAgainWhenTheHrefChanges()
    {
        var document = Load("<!doctype html><link rel=stylesheet href=a.css>");
        var link = (HtmlLinkElement)document.QuerySelector("link")!;
        var sheet = link.Sheet;
        Assert.NotNull(sheet);

        // The same URL is not fetched again
        link.SetAttribute("href", "a.css");
        _contexts.EventLoop.RunUntilIdle();
        Assert.Same(sheet, link.Sheet);
        Assert.Single(_requests);

        link.SetAttribute("href", "b.css");
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal("https://example.com/b.css", link.Sheet!.Href);
        Assert.Null(sheet!.OwnerNode);

        link.Remove();
        Assert.Null(link.Sheet);
        Assert.Empty(document.StyleSheets);
    }

    [Fact]
    public void StyleSheet_DelaysTheLoadEvent()
    {
        var document = Load("<!doctype html><link rel=stylesheet href=a.css onload=\"window.order = 'link'\"><body onload=\"document.title = window.order + ' window'\">");
        Assert.Equal("link window", document.Title);
    }

    [Fact]
    public void Preload_FetchesTheDestinationOfAs()
    {
        var document = Load("""
            <!doctype html>
            <link rel=preload as=font href=a.woff2 onload="document.title += 'font,'">
            <link rel=preload as=image href=b.png crossorigin onload="document.title += 'image,'">
            <link rel=preload as=script href=missing.js onerror="document.title += 'error'">
            """);

        // The mode of the request is the one of the crossorigin attribute, for a font too (the request of the font itself
        // is always a CORS request, which only consumes a preload that has the attribute)
        Assert.Equal("font,image,error", document.Title);
        Assert.Equal([(RequestDestination.Font, RequestMode.NoCors), (RequestDestination.Image, RequestMode.Cors), (RequestDestination.Script, RequestMode.NoCors)], _requests.Select(r => (r.Destination, r.Mode)));
    }

    [Fact]
    public void StyleSheet_AlreadyFetchedAppliesSynchronously()
    {
        var document = Load("<!doctype html><link rel=stylesheet href=a.css><p>text</p>");
        var link = document.CreateElement("link");
        link.SetAttribute("rel", "stylesheet");
        link.SetAttribute("href", "a.css");
        link.AddEventListener("load", _ => document.Title = "load");
        document.Head!.AppendChild(link);

        // As in the browsers (memory cache), the style sheet is not fetched again
        Assert.NotNull(((HtmlLinkElement)link).Sheet);
        Assert.Equal(["Style https://example.com/a.css"], Requests);
        Assert.Equal("", document.Title);

        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal("load", document.Title);
    }

    [Fact]
    public void Preload_StyleSheetConsumesThePreloadedResource()
    {
        var document = Load("<!doctype html><link rel=preload as=style href=a.css><p>text</p>");
        var link = (HtmlLinkElement)document.QuerySelector("link")!;
        link.SetAttribute("rel", "stylesheet");
        _contexts.EventLoop.RunUntilIdle();

        Assert.NotNull(link.Sheet);
        Assert.Equal("rgb(0, 128, 0)", document.DefaultView!.GetComputedStyle(document.QuerySelector("p")!).Color);
        Assert.Equal(["Style https://example.com/a.css"], Requests);
    }

    [Fact]
    public void SvgImage_IsFetched()
    {
        var document = Load("""
            <!doctype html>
            <svg>
              <image href=a.png onload="document.title += 'load,'" />
              <image xlink:href=a.txt onerror="document.title += 'error'" />
            </svg>
            """);

        Assert.Equal("load,error", document.Title);
        Assert.Equal(["Image https://example.com/a.png", "Image https://example.com/a.txt"], Requests);
    }

    [Fact]
    public void VideoPoster_IsFetched()
    {
        Load("<!doctype html><video poster=a.png></video>");
        Assert.Equal(["Image https://example.com/a.png"], Requests);
    }
}
