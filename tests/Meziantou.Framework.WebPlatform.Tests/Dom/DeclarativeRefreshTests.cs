using Meziantou.Framework.WebPlatform.Dom;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Dom;

/// <summary>https://html.spec.whatwg.org/multipage/semantics.html#shared-declarative-refresh-steps, with a fake clock.</summary>
public sealed class DeclarativeRefreshTests : IDisposable
{
    private const string PageUrl = "https://example.com/dir/page.html";

    private readonly BrowsingContextScope _contexts = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    private readonly List<string> _requests = [];

    public void Dispose() => _contexts.Dispose();

    private BrowsingContext Load(string html, KeyValuePair<string, string>[]? headers = null, bool? enabled = null)
    {
        var context = _contexts.Create(new BrowsingContextOptions
        {
            TimeProvider = _clock,
            DocumentLoader = new Loader(_requests),
            DeclarativeRefreshEnabled = enabled ?? true,
        });

        context.Navigate(PageUrl, _ => new DocumentResource(html) { Headers = headers ?? [] });
        RunUntilIdle();
        return context;
    }

    private void RunUntilIdle() => _contexts.EventLoop.RunUntilIdle();

    private void Advance(double seconds)
    {
        _clock.Advance(TimeSpan.FromSeconds(seconds));
        RunUntilIdle();
    }

    [Fact]
    public void RefreshHeader_NavigatesOnceTheRefreshComesDue()
    {
        var context = Load("<p>", [new("Refresh", "5; url=next.html")]);
        var document = context.Document;
        Assert.True(document.WillDeclarativelyRefresh);

        Advance(4.9);
        Assert.Empty(_requests);
        Assert.Same(document, context.Document);

        Advance(0.1);
        Assert.Equal(["https://example.com/dir/next.html"], _requests);
        Assert.Equal("https://example.com/dir/next.html", context.Document.Url);

        // The navigation replaces the entry of the document
        Assert.Equal(1u, context.Window.History.Length);
        Assert.Equal(PageUrl, context.Document.Referrer);
    }

    [Fact]
    public void MetaRefresh_NavigatesAfterTheLoadEvent()
    {
        var context = Load("<meta http-equiv=refresh content=\"0;URL='https://other.example/'\">");
        Assert.Equal(["https://other.example/"], _requests);
        Assert.Equal("https://other.example/", context.Document.Url);
    }

    [Fact]
    public void MetaRefresh_InsertedAfterTheLoad_ComesDueAfterItsInsertion()
    {
        var context = Load("<p>");
        Advance(10);
        var meta = context.Document.CreateElement("meta");
        meta.SetAttribute("http-equiv", "Refresh");
        meta.SetAttribute("content", "2");
        context.Document.Head!.AppendChild(meta);

        Advance(1);
        Assert.Empty(_requests);
        Advance(1);
        Assert.Equal([PageUrl], _requests);
    }

    [Fact]
    public void MetaRefresh_InsertedInTheInitialAboutBlankDocumentOfAnIframe_Navigates()
    {
        // The initial about:blank document is completely loaded when it is created
        // (https://html.spec.whatwg.org/multipage/document-sequences.html#creating-a-new-browsing-context)
        var context = Load("<iframe></iframe>");
        var iframe = (HtmlIFrameElement)context.Document.QuerySelector("iframe")!;
        var initialDocument = iframe.ContentDocument!;
        var meta = initialDocument.CreateElement("meta");
        meta.SetAttribute("http-equiv", "Refresh");
        meta.SetAttribute("content", "1; url=next.html");
        initialDocument.Head!.AppendChild(meta);

        Advance(0.9);
        Assert.Empty(_requests);
        Advance(0.1);
        Assert.Equal(["https://example.com/dir/next.html"], _requests);
        Assert.Equal("https://example.com/dir/next.html", iframe.ContentDocument!.Url);
    }

    [Fact]
    public void OnlyTheFirstRefreshIsProcessed()
    {
        Load("<meta http-equiv=refresh content='1; url=a.html'><meta http-equiv=refresh content='0; url=b.html'>", [new("Refresh", "2; url=c.html")]);
        Advance(1);
        Assert.Empty(_requests);
        Advance(1);
        Assert.Equal(["https://example.com/dir/c.html"], _requests);
    }

    [Theory]
    [InlineData("0", PageUrl)]
    [InlineData(" 0 ", PageUrl)]
    [InlineData(".5; url=a.html", "https://example.com/dir/a.html")]
    [InlineData("0.5.5,a.html", "https://example.com/dir/a.html")]
    [InlineData("0 a.html", "https://example.com/dir/a.html")]
    [InlineData("0; URL = 'a.html'b", "https://example.com/dir/a.html")]
    [InlineData("0;url='a.html", "https://example.com/dir/a.html")]
    [InlineData("0; 'a.html'", "https://example.com/dir/a.html")]
    [InlineData("0; u", "https://example.com/dir/u")]
    [InlineData("0; urlx", "https://example.com/dir/urlx")]
    [InlineData("0; url a.html", "https://example.com/dir/url%20a.html")]
    [InlineData("x", null)]
    [InlineData("", null)]
    [InlineData("0x; url=a.html", null)]
    [InlineData("0; url=javascript:alert(1)", null)]
    [InlineData("0; url=https://[", null)]
    public void Parsing(string content, string? expectedUrl)
    {
        Load($"<meta http-equiv=refresh content=\"{content}\">");
        Assert.Equal(expectedUrl is null ? [] : [expectedUrl], _requests);
    }

    [Fact]
    public void Disabled_DoesNotNavigate()
    {
        var context = Load("<meta http-equiv=refresh content='0; url=next.html'>", enabled: false);
        Advance(10);
        Assert.Empty(_requests);
        Assert.True(context.Document.WillDeclarativelyRefresh);
        Assert.Equal(PageUrl, context.Document.Url);
    }

    [Fact]
    public void Enabled_DefaultsToWhetherADocumentLoaderIsSet()
    {
        Assert.False(new BrowsingContextOptions().DeclarativeRefreshEnabled);
        Assert.True(new BrowsingContextOptions { DocumentLoader = new Loader([]) }.DeclarativeRefreshEnabled);
        Assert.False(new BrowsingContextOptions { DocumentLoader = new Loader([]), DeclarativeRefreshEnabled = false }.DeclarativeRefreshEnabled);
    }

    [Fact]
    public void Refresh_IsCanceledWhenTheDocumentIsReplaced()
    {
        var context = Load("<meta http-equiv=refresh content='5; url=a.html'>");
        context.Navigate("https://example.com/other.html", _ => "<p>");
        Advance(10);
        Assert.Empty(_requests);
        Assert.Equal("https://example.com/other.html", context.Document.Url);
    }

    private sealed class Loader(List<string> requests) : DocumentLoader
    {
        public override ValueTask<DocumentResource?> LoadAsync(DocumentRequest request, CancellationToken cancellationToken)
        {
            requests.Add(request.Url);
            return ValueTask.FromResult<DocumentResource?>(new DocumentResource("<p>loaded"));
        }
    }
}
