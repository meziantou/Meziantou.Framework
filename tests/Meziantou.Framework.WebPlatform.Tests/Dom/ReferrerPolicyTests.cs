using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Tests.Dom;

/// <summary>https://w3c.github.io/webappsec-referrer-policy/</summary>
public sealed class ReferrerPolicyTests
{
    private const string PageUrl = "https://user:password@example.com/dir/page.html?query#fragment";

    /// <summary>Loads a page with an iframe, and returns the request of the iframe and its document.</summary>
    private static (DocumentRequest Request, Document Document) LoadIframe(string iframeUrl, string html = "", KeyValuePair<string, string>[]? headers = null, string iframeAttributes = "", string pageUrl = PageUrl)
    {
        DocumentRequest? request = null;
        var context = new BrowsingContext(new BrowsingContextOptions
        {
            DocumentLoader = new RecordingLoader(r => request = r),
        });

        try
        {
            var document = context.Navigate(pageUrl, _ => new DocumentResource(html + $"<iframe src='{iframeUrl}' {iframeAttributes}></iframe>") { Headers = headers ?? [] });
            context.EventLoop.RunUntilIdle();
            var iframe = (HtmlIFrameElement)document.QuerySelector("iframe")!;
            // The content document of a cross-origin iframe is not accessible from the parent
            return (request!, iframe.ContentNavigable!.Document);
        }
        finally
        {
            context.Dispose();
        }
    }

    /// <summary>
    /// Loads a page with a hyperlink and follows it (a top-level navigation, which is not mixed content, so that the page can
    /// navigate to an http: URL), and returns the request and the new document.
    /// </summary>
    private static (DocumentRequest Request, Document Document) FollowLink(string url, KeyValuePair<string, string>[]? headers = null)
    {
        DocumentRequest? request = null;
        var context = new BrowsingContext(new BrowsingContextOptions
        {
            DocumentLoader = new RecordingLoader(r => request = r),
        });

        try
        {
            var document = context.Navigate(PageUrl, _ => new DocumentResource($"<a href='{url}'>link</a>") { Headers = headers ?? [] });
            context.EventLoop.RunUntilIdle();
            ((HtmlElement)document.QuerySelector("a")!).Click();
            context.EventLoop.RunUntilIdle();
            return (request!, context.Document);
        }
        finally
        {
            context.Dispose();
        }
    }

    [Theory]
    [InlineData("https://example.com/other.html", "https://example.com/dir/page.html?query")]
    [InlineData("https://other.example/", "https://example.com/")]
    [InlineData("http://other.example/", null)]
    [InlineData("http://localhost/", "https://example.com/")]
    public void DefaultPolicy_StrictOriginWhenCrossOrigin(string url, string? expected)
    {
        var (request, document) = FollowLink(url);
        Assert.Equal(expected, request.Referrer);
        Assert.Equal("strict-origin-when-cross-origin", request.ReferrerPolicy);
        Assert.Equal(expected ?? "", document.Referrer);
    }

    [Theory]
    [InlineData("no-referrer", "https://example.com/other.html", null)]
    [InlineData("origin", "https://example.com/other.html", "https://example.com/")]
    [InlineData("unsafe-url", "http://other.example/", "https://example.com/dir/page.html?query")]
    [InlineData("same-origin", "https://other.example/", null)]
    [InlineData("same-origin", "https://example.com/other.html", "https://example.com/dir/page.html?query")]
    [InlineData("strict-origin", "http://other.example/", null)]
    [InlineData("strict-origin", "https://example.com/other.html", "https://example.com/")]
    [InlineData("origin-when-cross-origin", "http://other.example/", "https://example.com/")]
    [InlineData("no-referrer-when-downgrade", "https://other.example/", "https://example.com/dir/page.html?query")]
    [InlineData("no-referrer-when-downgrade", "http://other.example/", null)]
    [InlineData("unsafe-url, invalid", "https://other.example/", "https://example.com/dir/page.html?query")]
    [InlineData("unsafe-url, no-referrer", "https://other.example/", null)]
    [InlineData("invalid", "https://other.example/", "https://example.com/")]
    public void ReferrerPolicyHeader(string header, string url, string? expected)
    {
        var (request, document) = FollowLink(url, headers: [new("Referrer-Policy", header)]);
        Assert.Equal(expected, request.Referrer);
        Assert.Equal(expected ?? "", document.Referrer);
    }

    [Theory]
    [InlineData("<meta name=referrer content=never>", null)]
    [InlineData("<meta name=Referrer content=ALWAYS>", "https://example.com/dir/page.html?query")]
    [InlineData("<meta name=referrer content=origin>", "https://example.com/")]
    [InlineData("<meta name=referrer content=invalid>", "https://example.com/")]
    [InlineData("<meta name=referrer content=no-referrer><meta name=referrer content=unsafe-url>", "https://example.com/dir/page.html?query")]
    public void MetaReferrer(string html, string? expected)
    {
        var (request, _) = LoadIframe("https://other.example/", html);
        Assert.Equal(expected, request.Referrer);
    }

    [Fact]
    public void MetaReferrer_ContentChanged()
    {
        using var context = new BrowsingContext(new BrowsingContextOptions { DocumentLoader = new RecordingLoader(_ => { }) });
        var document = context.Navigate(PageUrl, _ => "<meta name=referrer content=no-referrer>");
        var meta = document.QuerySelector("meta")!;
        meta.SetAttribute("content", "unsafe-url");
        Assert.Equal("unsafe-url", document.ReferrerPolicy);

        // An element that is not in the document has no effect
        var other = document.CreateElement("meta");
        other.SetAttribute("name", "referrer");
        other.SetAttribute("content", "origin");
        Assert.Equal("unsafe-url", document.ReferrerPolicy);
    }

    [Fact]
    public void IframeReferrerPolicyAttribute_OverridesTheDocumentPolicy()
    {
        var (request, _) = LoadIframe("https://example.com/other.html", headers: [new("Referrer-Policy", "unsafe-url")], iframeAttributes: "referrerpolicy=no-referrer");
        Assert.Null(request.Referrer);
        Assert.Equal("no-referrer", request.ReferrerPolicy);
    }

    [Fact]
    public void SrcdocDocument_InheritsThePolicyAndUsesTheUrlOfItsContainer()
    {
        DocumentRequest? request = null;
        using var context = new BrowsingContext(new BrowsingContextOptions { DocumentLoader = new RecordingLoader(r => request = r) });
        context.Navigate(PageUrl, _ => new DocumentResource("<iframe srcdoc=\"<iframe src='https://other.example/'></iframe>\"></iframe>") { Headers = [new("Referrer-Policy", "unsafe-url")] });
        context.EventLoop.RunUntilIdle();

        Assert.Equal("https://other.example/", request!.Url);
        Assert.Equal("https://example.com/dir/page.html?query", request.Referrer);
    }

    [Fact]
    public void ApplicationNavigation_HasNoReferrer()
    {
        DocumentRequest? request = null;
        using var context = new BrowsingContext(new BrowsingContextOptions { DocumentLoader = new RecordingLoader(r => request = r) });
        context.Navigate("https://example.com/");
        context.EventLoop.RunUntilIdle();

        Assert.Null(request!.Referrer);
        Assert.Equal("", context.Document.Referrer);
    }

    private sealed class RecordingLoader(Action<DocumentRequest> onRequest) : DocumentLoader
    {
        public override ValueTask<DocumentResource?> LoadAsync(DocumentRequest request, CancellationToken cancellationToken)
        {
            onRequest(request);
            return ValueTask.FromResult<DocumentResource?>(new DocumentResource(""));
        }
    }
}
