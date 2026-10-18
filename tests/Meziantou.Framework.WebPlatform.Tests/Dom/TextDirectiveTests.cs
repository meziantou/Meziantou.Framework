using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Tests.Scripting;

namespace Meziantou.Framework.WebPlatform.Tests.Dom;

/// <summary>https://wicg.github.io/scroll-to-text-fragment/</summary>
public sealed class TextDirectiveTests : IDisposable
{
    private const string Page = """
        <!DOCTYPE html>
        <style>body { margin: 0 } p { margin: 2000px 0 }</style>
        <p id="first">First paragraph</p>
        <p id="second">The second paragraph</p>
        <p id="third">The third one</p>
        """;

    private readonly BrowsingContextScope _contexts = new();
    private readonly RecordingLoader _loader = new();

    public void Dispose() => _contexts.Dispose();

    private BrowsingContext CreateContext(WebPlatformFeatures? features = null)
        => _contexts.Create(new BrowsingContextOptions { DocumentLoader = _loader, Features = features });

    private Document Load(string url, string html = Page, WebPlatformFeatures? features = null)
    {
        var document = CreateContext(features).Navigate(url, _ => html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    private static string? TargetId(Document document) => document.QuerySelector(":target")?.Id;

    [Fact]
    public void FragmentDirective_IsRemovedFromTheUrlAndScrolledTo()
    {
        var document = Load("https://example.com/page#first:~:text=second%20paragraph");

        Assert.Equal("https://example.com/page#first", document.Url);
        Assert.Equal("#first", document.DefaultView!.Location.Hash);
        Assert.NotNull(document.FragmentDirective);
        Assert.Same(document.FragmentDirective, document.FragmentDirective);

        // The text is the indicated part: its element is the target element, and it is centered in the viewport
        var range = Assert.Single(document.TargetTextRanges!);
        Assert.Equal("second paragraph", range.ToString());
        Assert.Equal("second", TargetId(document));
        var top = document.GetElementById("second")!.GetBoundingClientRect().Top;
        Assert.InRange(top, 200, 400);
    }

    [Fact]
    public void TextThatIsNotFound_FallsBackToTheElementOfTheFragment()
    {
        var document = Load("https://example.com/page#third:~:text=missing");

        Assert.Null(document.TargetTextRanges);
        Assert.Equal("third", TargetId(document));
        Assert.Equal(0, document.GetElementById("third")!.GetBoundingClientRect().Top);
    }

    [Theory]
    [InlineData("text=,,,,,", null)] // too many terms
    [InlineData("text=", null)]
    [InlineData("text=The-,second", "second")]
    [InlineData("text=The-,third", "third")]
    [InlineData("text=The,-third", "The")]
    [InlineData("text=second,third", "second paragraph\nThe third")]
    [InlineData("text=SECOND", "second")]
    [InlineData("text=secon", null)] // not at a word boundary
    [InlineData("text=econd", null)]
    [InlineData("unknown&text=third%20one&text=missing", "third one")]
    public void Directives_AreParsedAndSearched(string directive, string? expected)
    {
        var document = Load("https://example.com/page#:~:" + directive);

        Assert.Equal("https://example.com/page#", document.Url);
        Assert.Equal(expected, document.TargetTextRanges?[0].ToString());
    }

    [Fact]
    public void Term_CanContainADash()
    {
        var document = Load("https://example.com/#:~:text=well-known", "<!DOCTYPE html><p>A well-known text</p>");

        Assert.Equal("well-known", document.TargetTextRanges![0].ToString());
    }

    [Fact]
    public void BlockBoundaries_EndTheTextOfABlock()
    {
        const string Html = """
            <!DOCTYPE html>
            <div>Text <div style="visibility: hidden">hidden</div> with a block</div>
            <div>Text <span style="visibility: hidden">hidden</span> with an inline</div>
            <div>Text <span style="display: none">hidden</span> without a box</div>
            <div>Cell<span style="display: inline-block">ular</span></div>
            """;

        // An element that is not an inline box is a boundary, even when its text is not visible
        Assert.Null(Load("https://example.com/#:~:text=Text%20with%20a%20block", Html).TargetTextRanges);
        Assert.NotNull(Load("https://example.com/#:~:text=Text%20with%20an%20inline", Html).TargetTextRanges);
        Assert.NotNull(Load("https://example.com/#:~:text=Text%20without%20a%20box", Html).TargetTextRanges);
        Assert.Null(Load("https://example.com/#:~:text=hidden", Html).TargetTextRanges);
        Assert.NotNull(Load("https://example.com/#:~:text=Cell", Html).TargetTextRanges);
    }

    [Fact]
    public void ShadowTrees_AreSearched()
    {
        var context = CreateContext();
        var document = context.Navigate("https://example.com/", _ => "<!DOCTYPE html><div id=host><span>not slotted</span></div>");
        _contexts.EventLoop.RunUntilIdle();
        var shadowRoot = document.GetElementById("host")!.AttachShadow(new ShadowRootInit { Mode = ShadowRootMode.Open });
        shadowRoot.InnerHtml = "<p id=inner>shadow text</p>";

        // The children of the host are not rendered: they are not assigned to a slot
        document.DefaultView!.Location.Hash = ":~:text=not%20slotted";
        Assert.Null(document.TargetTextRanges);

        document.DefaultView.Location.Hash = ":~:text=shadow%20text";
        var range = Assert.Single(document.TargetTextRanges!);
        Assert.Same(shadowRoot, range.StartContainer.GetRootNode());
        Assert.True(shadowRoot.GetElementById("inner")!.Matches(":target"));
        Assert.Null(document.QuerySelector(":target"));
    }

    [Fact]
    public void FragmentNavigation_KeepsTheHighlightWithoutDirective()
    {
        var document = Load("https://example.com/page");
        var window = document.DefaultView!;

        // A same-origin fragment navigation scrolls without user activation
        window.Location.Hash = ":~:text=second";
        Assert.Equal("https://example.com/page#", document.Url);
        Assert.Equal("second", TargetId(document));
        Assert.True(window.ScrollY > 0);

        // A fragment navigation without directive shares the directive state: the text stays highlighted
        window.Location.Hash = "third";
        Assert.Equal("second", document.TargetTextRanges![0].ToString());
        Assert.Equal("third", TargetId(document));

        // A new directive replaces it
        window.Location.Hash = "first:~:text=missing";
        Assert.Null(document.TargetTextRanges);
        Assert.Equal("first", TargetId(document));
    }

    [Fact]
    public void ScriptNavigationWithoutUserActivation_DoesNotScrollToTheText()
    {
        // The URL of the request has the fragment, without the fragment directive
        _loader.Resources["https://example.com/other#"] = new DocumentResource(Page) { Headers = [new("Content-Type", "text/html")] };
        var context = CreateContext();
        var first = context.Navigate("https://example.com/page", _ => "<!DOCTYPE html><p>start</p>");
        _contexts.EventLoop.RunUntilIdle();

        // The navigation that created the document has a user involvement, which the first script navigation uses (a
        // client-side redirect)
        first.DefaultView!.Location.Href = "https://example.com/other#:~:text=second";
        _contexts.EventLoop.RunUntilIdle();
        var second = context.Document;
        Assert.Equal("https://example.com/other#", second.Url);
        Assert.Equal("second", TargetId(second));
        Assert.True(second.DefaultView!.ScrollY > 0);

        // The next one has none: the text is highlighted, and the element of the fragment is the target
        _loader.Resources["https://example.com/other?again#third"] = _loader.Resources["https://example.com/other#"];
        second.DefaultView.Location.Href = "https://example.com/other?again#third:~:text=second";
        _contexts.EventLoop.RunUntilIdle();
        var third = context.Document;
        Assert.Equal("https://example.com/other?again#third", third.Url);
        Assert.Equal("second", third.TargetTextRanges![0].ToString());
        Assert.Equal("third", TargetId(third));
    }

    [Fact]
    public void ForceLoadAtTop_BlocksTheScrollOnLoad()
    {
        var context = CreateContext();
        var document = context.Navigate("https://example.com/page#:~:text=second", _ => new DocumentResource(Page)
        {
            Headers = [new("Content-Type", "text/html"), new("Document-Policy", "force-load-at-top")],
        });
        _contexts.EventLoop.RunUntilIdle();

        Assert.Equal(0, document.DefaultView!.ScrollY);
        Assert.Null(TargetId(document));

        // The fragment navigations of the document still scroll
        document.DefaultView.Location.Hash = ":~:text=third";
        Assert.Equal("third", TargetId(document));
        Assert.True(document.DefaultView.ScrollY > 0);
    }

    [Theory]
    [InlineData("text/plain", true)]
    [InlineData("text/css", false)]
    [InlineData("application/json", false)]
    public void TextDocuments_ScrollOnlyForPlainText(string contentType, bool scrolls)
    {
        var text = string.Concat(Enumerable.Repeat("line\n", 200)) + "target\n";
        var context = CreateContext();
        var document = context.Navigate("https://example.com/file#:~:text=target", _ => new DocumentResource(text) { Headers = [new("Content-Type", contentType)] });
        _contexts.EventLoop.RunUntilIdle();

        Assert.Equal(contentType, document.ContentType);
        Assert.Equal("PRE", document.Body!.FirstElementChild!.TagName);
        Assert.Equal(scrolls, document.DefaultView!.ScrollY > 0);
    }

    [Fact]
    public void DisabledFeature_KeepsTheDirectiveInTheFragment()
    {
        var document = Load("https://example.com/page#:~:text=second", features: WebPlatformFeatures.Default with { TextDirectivesEnabled = false });

        Assert.Equal("https://example.com/page#:~:text=second", document.Url);
        Assert.Null(document.TargetTextRanges);
    }
}
