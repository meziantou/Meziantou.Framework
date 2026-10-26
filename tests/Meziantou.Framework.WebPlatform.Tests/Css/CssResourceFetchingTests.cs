using System.Security.Cryptography;
using System.Text;
using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Dom;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Css;

/// <summary>
/// The resources fetched by the rendering of a document (https://drafts.csswg.org/css-values-4/#fetch-an-external-resource):
/// the images of the computed styles and the font faces, and their resource timing (https://w3c.github.io/resource-timing/).
/// </summary>
public sealed class CssResourceFetchingTests : IDisposable
{
    private const string BaseUrl = "https://example.com/dir/page.html";
    private static readonly byte[] FontData = TestFonts.Ahem;

    private readonly BrowsingContextScope _contexts = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    private readonly Loader _loader = new();

    public void Dispose() => _contexts.Dispose();

    private Document Load(string html)
    {
        var context = _contexts.Create(new BrowsingContextOptions { DocumentLoader = _loader, TimeProvider = _clock });
        var document = context.Navigate(BaseUrl, _ => html);
        RenderFrame();
        return document;
    }

    /// <summary>Runs the tasks, then the next rendering opportunity.</summary>
    private void RenderFrame()
    {
        _contexts.EventLoop.RunUntilIdle();
        _clock.Advance(TimeSpan.FromMilliseconds(17));
        _contexts.EventLoop.RunUntilIdle();
    }

    private sealed class Loader : DocumentLoader
    {
        public List<DocumentRequest> Requests { get; } = [];

        public override ValueTask<DocumentResource?> LoadAsync(DocumentRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            DocumentResource? resource = request.Url switch
            {
                "https://other.example/policy.css" => new DocumentResource("@import 'b.css'; #target { background-image: url(a.png); font-family: A } @font-face { font-family: A; src: url(a.woff) }")
                {
                    Headers = [new("Content-Type", "text/css"), new("Referrer-Policy", "unsafe-url")],
                },
                _ when request.Url.EndsWith(".css", StringComparison.Ordinal) => DocumentResource.Create("#target { color: green }", "text/css"),
                _ when request.Url.EndsWith(".png", StringComparison.Ordinal) => DocumentResource.Create(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, "image/png"),
                _ when request.Url.EndsWith(".woff", StringComparison.Ordinal) => new DocumentResource(FontData),
                _ => null,
            };

            return ValueTask.FromResult(resource);
        }
    }

    private DocumentRequest GetRequest(string url) => Assert.Single(_loader.Requests, request => request.Url == url);

    [Fact]
    public void BackgroundImage_IsFetchedWithTheNoCorsMode()
    {
        var document = Load("<div id=target style=\"background: url(a.png)\"></div>");

        var request = GetRequest("https://example.com/dir/a.png");
        Assert.Equal(RequestDestination.Image, request.Destination);
        Assert.Equal(RequestMode.NoCors, request.Mode);
        Assert.Equal("https://example.com", request.Origin);
        Assert.Same(document.GetElementById("target"), request.Initiator);
    }

    [Theory]
    [InlineData("shape-outside")]
    [InlineData("mask-image")]
    public void ShapeAndMaskImages_AreFetchedWithTheCorsMode(string property)
    {
        Load($"<style>#target {{ {property}: url(\"a.png\") }}</style><div id=target></div>");

        Assert.Equal(RequestMode.Cors, GetRequest("https://example.com/dir/a.png").Mode);
    }

    [Fact]
    public void BackgroundAttribute_IsFetched()
    {
        Load("<body background=a.png>");

        Assert.Equal(RequestMode.NoCors, GetRequest("https://example.com/dir/a.png").Mode);
    }

    [Fact]
    public void Images_AreFetchedOnce()
    {
        Load("<style>div { background-image: url(a.png), url(b.png) }</style><div></div><div></div><p style=\"list-style-image: url(a.png)\">");

        Assert.Equal(["https://example.com/dir/a.png", "https://example.com/dir/b.png"], _loader.Requests.Select(request => request.Url));
    }

    [Fact]
    public void ImagesOfElementsThatAreNotRendered_AreNotFetched()
    {
        Load("<div hidden><p style=\"background: url(a.png)\"></p></div><template><p style=\"background: url(b.png)\"></p></template><p style=\"background: url(#mask)\">");

        Assert.Empty(_loader.Requests);
    }

    [Fact]
    public void ImagesOfPseudoElements_AreFetched()
    {
        Load("<style>p::before { content: url(a.png) } p::after { background-image: url(b.png) } p::marker { content: url(c.png) }</style><p>");

        Assert.Equal(["https://example.com/dir/a.png"], _loader.Requests.Select(request => request.Url));
    }

    [Fact]
    public void ImageSet_FetchesTheFirstImage()
    {
        Load("<p style=\"background-image: image-set('a.png' 1x, 'b.png' 2x)\">");

        Assert.Equal(["https://example.com/dir/a.png"], _loader.Requests.Select(request => request.Url));
    }

    [Fact]
    public void ImageOfAnInsertedElement_IsFetchedAtTheNextRenderingOpportunity()
    {
        var document = Load("<body>");
        var element = (HtmlElement)document.CreateElement("div");
        element.Style.SetProperty("background-image", "url(a.png)");
        document.Body!.AppendChild(element);
        Assert.Empty(_loader.Requests);

        RenderFrame();
        GetRequest("https://example.com/dir/a.png");
    }

    [Fact]
    public void FontFaceUsedByAnInsertedElement_IsFetchedWithTheCorsMode()
    {
        var document = Load("<body>");
        var style = document.CreateElement("style");
        style.TextContent = "@font-face { font-family: A; src: url(a.woff) }";
        document.Head!.AppendChild(style);
        var element = (HtmlElement)document.CreateElement("p");
        element.TextContent = "text";
        element.Style.SetProperty("font-family", "A");
        document.Body!.AppendChild(element);

        RenderFrame();
        var request = GetRequest("https://example.com/dir/a.woff");
        Assert.Equal(RequestDestination.Font, request.Destination);
        Assert.Equal(RequestMode.Cors, request.Mode);
        Assert.Equal(FontFaceLoadStatus.Loaded, document.Fonts.Single().Status);
    }

    [Fact]
    public void Import_IsFetchedWithTheNoCorsMode()
    {
        Load("<style>@import 'a.css';</style>");

        var request = GetRequest("https://example.com/dir/a.css");
        Assert.Equal(RequestMode.NoCors, request.Mode);
        Assert.Equal("https://example.com", request.Origin);
    }

    [Fact]
    public void ResourcesOfAFetchedStyleSheet_AreRequestedWithTheReferrerAndThePolicyOfTheStyleSheet()
    {
        Load("<meta name=referrer content=no-referrer><link rel=stylesheet href='https://other.example/policy.css'><div id=target>text</div><img src=page.png>");
        RenderFrame();

        foreach (var url in new[] { "https://other.example/b.css", "https://other.example/a.png", "https://other.example/a.woff" })
        {
            var request = GetRequest(url);
            Assert.Equal("https://other.example/policy.css", request.Referrer);
            Assert.Equal("unsafe-url", request.ReferrerPolicy);
        }

        var documentRequest = GetRequest("https://example.com/dir/page.png");
        Assert.Null(documentRequest.Referrer);
        Assert.Equal("no-referrer", documentRequest.ReferrerPolicy);
    }

    [Fact]
    public void RequestUrlModifiers_ApplyToTheRequestOfAnImage()
    {
        Load("<div style=\"background-image: url('https://other.example/a.png' cross-origin(use-credentials) integrity('sha256-abc') referrer-policy(no-referrer))\"></div>");

        var request = GetRequest("https://other.example/a.png");
        Assert.Equal(RequestMode.Cors, request.Mode);
        Assert.True(request.IncludeCredentials);
        Assert.Equal("sha256-abc", request.Integrity);
        Assert.Equal("no-referrer", request.ReferrerPolicy);
        Assert.Null(request.Referrer);
    }

    [Fact]
    public void CrossOriginAnonymous_FetchesAnImageWithoutCredentials()
    {
        Load("<div style=\"background-image: url('https://other.example/a.png' cross-origin(anonymous))\"></div>");

        var request = GetRequest("https://other.example/a.png");
        Assert.Equal(RequestMode.Cors, request.Mode);
        Assert.False(request.IncludeCredentials);
        Assert.Equal("", request.Integrity);
        Assert.Equal("strict-origin-when-cross-origin", request.ReferrerPolicy);
    }

    [Fact]
    public void ImagesWithDifferentModifiers_AreFetchedSeparately()
    {
        Load("<div style=\"background-image: url(a.png), url('a.png' referrer-policy(origin))\"></div>");

        Assert.Equal(["strict-origin-when-cross-origin", "origin"], _loader.Requests.Select(request => request.ReferrerPolicy));
    }

    [Fact]
    public void RequestUrlModifiers_ApplyToTheRequestOfAFontFace()
    {
        var document = Load("<style>@font-face { font-family: A; src: url('a.woff' cross-origin(use-credentials) referrer-policy(origin)) } p { font-family: A }</style><p>text");
        RenderFrame();

        var request = GetRequest("https://example.com/dir/a.woff");
        Assert.Equal(RequestMode.Cors, request.Mode);
        Assert.True(request.IncludeCredentials);
        Assert.Equal("origin", request.ReferrerPolicy);
        Assert.Equal("https://example.com/", request.Referrer);
        Assert.Equal(FontFaceLoadStatus.Loaded, document.Fonts.Single().Status);
    }

    [Theory]
    [InlineData(true, FontFaceLoadStatus.Loaded)]
    [InlineData(false, FontFaceLoadStatus.Error)]
    public void Integrity_IsCheckedForAFontFace(bool matching, FontFaceLoadStatus expectedStatus)
    {
        var integrity = matching ? "sha256-" + Convert.ToBase64String(SHA256.HashData(FontData)) : "sha256-AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";
        var document = Load($"<style>@font-face {{ font-family: A; src: url('a.woff' integrity('{integrity}')) }} p {{ font-family: A }}</style><p>text");
        RenderFrame();

        Assert.Equal(integrity, GetRequest("https://example.com/dir/a.woff").Integrity);
        Assert.Equal(expectedStatus, document.Fonts.Single().Status);
    }

    [Fact]
    public void RequestUrlModifiers_ApplyToTheRequestOfAnImport()
    {
        var document = Load("<style>@import url('https://other.example/a.css' cross-origin(anonymous) referrer-policy(origin));</style>");

        var request = GetRequest("https://other.example/a.css");
        Assert.Equal(RequestMode.Cors, request.Mode);
        Assert.False(request.IncludeCredentials);
        Assert.Equal("origin", request.ReferrerPolicy);
        Assert.Equal("https://example.com/", request.Referrer);

        // The response has no Access-Control-Allow-Origin header: the CORS check fails
        Assert.Null(((CssImportRule)document.StyleSheets[0].CssRules[0]!).StyleSheet);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Integrity_IsCheckedForAnImport(bool matching)
    {
        var integrity = matching ? "sha384-" + Convert.ToBase64String(SHA384.HashData(Encoding.UTF8.GetBytes("#target { color: green }"))) : "sha384-AAAA";
        var document = Load($"<style>@import url('a.css' integrity('{integrity}'));</style>");

        Assert.Equal(integrity, GetRequest("https://example.com/dir/a.css").Integrity);
        var styleSheet = ((CssImportRule)document.StyleSheets[0].CssRules[0]!).StyleSheet;
        Assert.Equal(matching, styleSheet is not null);
    }

    [Fact]
    public void Integrity_OfACrossOriginNoCorsImport_NeverMatches()
    {
        var integrity = "sha256-" + Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes("#target { color: green }")));
        var document = Load($"<style>@import url('https://other.example/a.css' integrity('{integrity}'));</style>");

        Assert.Equal(RequestMode.NoCors, GetRequest("https://other.example/a.css").Mode);
        Assert.Null(((CssImportRule)document.StyleSheets[0].CssRules[0]!).StyleSheet);
    }

    [Fact]
    public void ResourceTiming_IsAddedToThePerformanceTimeline()
    {
        var document = Load("<style>@import 'a.css';</style><div style=\"background: url(a.png)\"></div><img src=b.png><div style=\"background: url(missing.gif)\"></div>");
        var performance = document.DefaultView!.Performance;

        var entries = performance.GetEntriesByType("resource").Cast<PerformanceResourceTiming>().ToList();
        Assert.Equal(["https://example.com/dir/a.css", "https://example.com/dir/b.png", "https://example.com/dir/a.png", "https://example.com/dir/missing.gif"], entries.Select(entry => entry.Name));
        Assert.Equal(["css", "img", "css", "css"], entries.Select(entry => entry.InitiatorType));

        var css = entries[0];
        Assert.Equal("text/css", css.ContentType);
        Assert.Equal(200, css.ResponseStatus);
        Assert.Equal(24ul, css.DecodedBodySize);
        Assert.Equal(324ul, css.TransferSize);
        Assert.Equal("http/1.1", css.NextHopProtocol);
        Assert.Equal(css.StartTime, css.FetchStart);

        // A network error: the timing info is opaque
        var missing = entries[3];
        Assert.Equal(0, missing.ResponseStatus);
        Assert.Equal(0ul, missing.TransferSize);
        Assert.Equal("", missing.NextHopProtocol);

        Assert.Same(css, Assert.Single(performance.GetEntriesByName("https://example.com/dir/a.css")));
        Assert.Empty(performance.GetEntriesByName("https://example.com/dir/a.css", "mark"));
        Assert.Equal(entries, performance.GetEntries().Where(entry => entry is not (PerformanceNavigationTiming or VisibilityStateEntry)));

        performance.ClearResourceTimings();
        Assert.IsType<PerformanceNavigationTiming>(Assert.Single(performance.GetEntries(), entry => entry is not VisibilityStateEntry));
    }

    [Fact]
    public void ResourceTiming_OfACrossOriginResource_IsOpaque()
    {
        var document = Load("<div style=\"background: url(https://other.example/a.png)\"></div>");

        var entry = (PerformanceResourceTiming)Assert.Single(document.DefaultView!.Performance.GetEntriesByType("resource"));
        Assert.Equal(0, entry.ResponseStatus);
        Assert.Equal(0ul, entry.DecodedBodySize);
        Assert.Equal(0ul, entry.TransferSize);
        Assert.Equal("", entry.ContentType);
        Assert.Equal(0, entry.RequestStart);
        Assert.Equal(entry.StartTime, entry.FetchStart);
    }

    [Fact]
    public void PerformanceObserver_ReceivesTheEntries()
    {
        var document = Load("<style>@import 'a.css';</style>");
        var window = document.DefaultView!;
        var calls = new List<(string[] Names, ulong? DroppedEntriesCount)>();
        var observer = new PerformanceObserver(window, (entries, _, options) => calls.Add(([.. entries.GetEntries().Select(entry => entry.Name)], options.DroppedEntriesCount)));
        observer.Observe(new PerformanceObserverInit { Type = "resource", Buffered = true });
        _contexts.EventLoop.RunUntilIdle();
        var call = Assert.Single(calls);
        Assert.Equal(["https://example.com/dir/a.css"], call.Names);
        Assert.Equal(0ul, call.DroppedEntriesCount);

        var element = (HtmlElement)document.CreateElement("p");
        element.Style.SetProperty("background", "url(a.png)");
        document.Body!.AppendChild(element);
        RenderFrame();
        Assert.Equal(2, calls.Count);
        Assert.Equal(["https://example.com/dir/a.png"], calls[1].Names);
        Assert.Null(calls[1].DroppedEntriesCount);

        observer.Disconnect();
        element.Style.SetProperty("background", "url(b.png)");
        RenderFrame();
        Assert.Equal(2, calls.Count);
    }

    [Fact]
    public void PerformanceObserver_Observe_ValidatesTheOptions()
    {
        var document = Load("<body>");
        var observer = new PerformanceObserver(document.DefaultView!, (_, _, _) => { });

        Assert.Throws<TypeErrorException>(() => observer.Observe());
        Assert.Throws<TypeErrorException>(() => observer.Observe(new PerformanceObserverInit { EntryTypes = ["resource"], Buffered = false }));
        observer.Observe(new PerformanceObserverInit { EntryTypes = ["resource", "unknown"] });
        var exception = Assert.Throws<DomException>(() => observer.Observe(new PerformanceObserverInit { Type = "resource" }));
        Assert.Equal("InvalidModificationError", exception.Name);
        Assert.Equal(["resource"], PerformanceObserver.SupportedEntryTypes);
    }

    [Fact]
    public void ResourceTimingBuffer_FiresTheBufferFullEvent()
    {
        var document = Load("<body>");
        var performance = document.DefaultView!.Performance;
        performance.SetResourceTimingBufferSize(1);
        var events = 0;
        performance.OnResourceTimingBufferFull = _ => events++;
        foreach (var name in new[] { "a", "b", "c" })
        {
            var element = (HtmlElement)document.CreateElement("p");
            element.Style.SetProperty("background", $"url({name}.png)");
            document.Body!.AppendChild(element);
        }

        RenderFrame();
        Assert.Equal(1, events);
        Assert.Equal(["https://example.com/dir/a.png"], performance.GetEntriesByType("resource").Select(entry => entry.Name));
    }
}
