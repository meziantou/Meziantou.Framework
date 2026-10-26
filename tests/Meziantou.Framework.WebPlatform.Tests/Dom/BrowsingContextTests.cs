using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Formatting;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Dom;

public sealed class BrowsingContextTests
{
    [Fact]
    public void ReadmeExample_ParseAndQuery()
    {
        #region WebPlatform_ParseAndQuery
        var document = HtmlDocumentParser.ParseDocument("""
            <style>.important { color: red; font-weight: bold }</style>
            <ul>
              <li><a href="https://example.com/a" class="important">A</a>
              <li><a href="/b" style="color: blue">B</a>
            </ul>
            """);

        // CSS selectors, with typed overloads and LINQ
        var links = document.QuerySelectorAll<HtmlAnchorElement>("li > a");
        var external = links.Where(a => a.Href.StartsWith("https:", StringComparison.Ordinal)).Select(a => a.TextContent);
        var first = document.GetElementsByTagName("li")[0];
        Assert.Equal(["A"], external);
        Assert.Same(first, links[0].ParentElement);

        // XPath
        var result = document.Evaluate("//a[@class='important']", document, type: XPathResult.FirstOrderedNodeType);
        var important = (Element?)result.SingleNodeValue;
        Assert.Equal("A", important?.TextContent);

        // Inline style (style attribute)
        var color = links[1].Style.GetPropertyValue("color");
        Assert.Equal("blue", color);
        #endregion
    }

    [Fact]
    public void ReadmeExample_ComputedStyle()
    {
        #region WebPlatform_ComputedStyle
        var html = """
            <style>.important { color: red; font-weight: bold }</style>
            <p class="important">Hello</p>
            """;

        using var context = new BrowsingContext(new BrowsingContextOptions
        {
            Viewport = new ViewportOptions { Width = 1280, Height = 720 }, // optional, 800x600 by default
        });

        var document = context.Navigate("https://example.com/", _ => html);
        var style = context.Window.GetComputedStyle(document.QuerySelector(".important")!);
        Assert.Equal("rgb(255, 0, 0)", style.Color);
        Assert.Equal("700", style.GetPropertyValue("font-weight"));
        #endregion
    }

    [Fact]
    public void ReadmeExample_UpdateDom()
    {
        #region WebPlatform_UpdateDom
        var document = HtmlDocumentParser.ParseDocument("""
            <ul>
              <li><a href="/a">A</a>
              <li><a href="/b">B</a>
            </ul>
            """);
        var list = document.QuerySelector("ul")!;

        // Create elements
        var item = document.CreateElement<HtmlLIElement>();
        var link = document.CreateElement<HtmlAnchorElement>();
        link.Href = "/c";
        link.TextContent = "C";
        link.ClassList.Add("important");
        link.SetAttribute("data-id", "3");
        item.Append(link);
        list.Append(item);

        // Parse markup into an element, edit the styles, remove nodes
        list.InsertAdjacentHtml("beforeend", "<li><a href=/d>D</a>");
        link.Style.SetProperty("text-decoration", "underline");
        document.QuerySelector("a[href='/b']")?.Remove();

        // Serialize the document
        var html = document.GetHtml();
        var indented = document.ToHtml(new HtmlPrettyFormatter());
        var minified = document.ToHtml(new HtmlMinifyFormatter());
        Assert.StartsWith("<html><head></head><body><ul>", html, StringComparison.Ordinal);
        Assert.Equal("""
            <html>
              <head></head>
              <body>
                <ul>
                  <li>
                    <a href="/a">A</a>
                  </li>
                  <li></li>
                  <li>
                    <a href="/c" class="important" data-id="3" style="text-decoration: underline;">C</a>
                  </li>
                  <li>
                    <a href="/d">D</a>
                  </li>
                </ul>
              </body>
            </html>
            """, indented);
        Assert.Equal("""<ul><li><a href=/a>A</a><li><li><a href=/c class=important data-id=3 style="text-decoration: underline;">C</a><li><a href=/d>D</a></ul>""", minified);
        #endregion
    }

    private static Document Load(BrowsingContext context, string html, string url = "https://example.com/dir/page.html")
    {
        var document = context.Navigate(url, _ => html);
        context.EventLoop.RunUntilIdle();
        return document;
    }

    [Fact]
    public void ParsedDocument_HasNoBrowsingContext()
    {
        var document = HtmlDocumentParser.ParseDocument("<iframe srcdoc='<p>x</p>'></iframe>");
        Assert.Null(document.DefaultView);
        Assert.Null(document.Location);
        Assert.Equal(DocumentReadyState.Complete, document.ReadyState);

        var iframe = (HtmlIFrameElement)document.QuerySelector("iframe")!;
        Assert.Null(iframe.ContentDocument);
        Assert.Null(iframe.ContentWindow);
    }

    [Fact]
    public void InitialAboutBlankDocument()
    {
        using var context = new BrowsingContext();
        var document = context.Document;
        Assert.Equal("about:blank", document.Url);
        Assert.Same(context.Window, document.DefaultView);
        Assert.Same(document, context.Window.Document);
        Assert.Equal("BackCompat", document.CompatMode);
        Assert.NotNull(document.Body);
        Assert.Same(context.Window, context.Window.Top);
        Assert.Same(context.Window, context.Window.Parent);
        Assert.Null(context.Window.FrameElement);
        Assert.Null(context.Window.Opener);
        Assert.False(context.Window.Closed);
        Assert.Equal("null", context.Window.Origin);
    }

    [Fact]
    public async Task Navigate_StreamedResponse_IsParsedAsItsChunksArrive()
    {
        using var context = new BrowsingContext();
        var chunks = System.Threading.Channels.Channel.CreateUnbounded<ReadOnlyMemory<byte>>();
        var document = context.Navigate("https://example.com/", _ => new DocumentResource("<p id=a>caf\u00E9 "u8[..^2].ToArray())
        {
            Headers = [new("Content-Type", "text/html; charset=utf-8")],
            RemainingContentBytes = chunks.Reader.ReadAllAsync(),
        });

        // The parser waits for the next chunk: a character split between two chunks is decoded once complete
        context.EventLoop.RunUntilIdle();
        Assert.Equal("caf", document.GetElementById("a")!.TextContent);
        Assert.Equal(DocumentReadyState.Loading, document.ReadyState);

        chunks.Writer.TryWrite("\u00E9 "u8[^2..].ToArray());
        chunks.Writer.TryWrite("<p id=b>b"u8.ToArray());
        await context.EventLoop.RunUntilAsync(() => document.GetElementById("b") is not null, TestContext.Current.CancellationToken);
        Assert.Equal("caf\u00E9 ", document.GetElementById("a")!.TextContent);
        Assert.Equal(DocumentReadyState.Loading, document.ReadyState);

        chunks.Writer.Complete();
        await context.EventLoop.RunUntilIdleAsync(TestContext.Current.CancellationToken);
        Assert.Equal("b", document.GetElementById("b")!.TextContent);
        Assert.Equal(DocumentReadyState.Complete, document.ReadyState);
    }

    [Fact]
    public void Navigate_FiresLoadEvents()
    {
        using var context = new BrowsingContext();
        var document = context.Navigate("https://example.com/", _ => "<p>test</p>");
        Assert.Same(document, context.Document);
        Assert.Equal(DocumentReadyState.Interactive, document.ReadyState);

        var events = new List<string>();
        EventTarget? loadTarget = null;
        document.AddEventListener("DOMContentLoaded", e => events.Add("DOMContentLoaded"));
        document.AddEventListener("readystatechange", e => events.Add("readystatechange:" + document.ReadyState));
        context.Window.AddEventListener("load", e =>
        {
            events.Add("load");
            loadTarget = e.Target;
        });
        context.Window.AddEventListener("pageshow", e => events.Add("pageshow:" + e.Bubbles + ":" + e.Cancelable));
        context.EventLoop.RunUntilIdle();

        // https://html.spec.whatwg.org/multipage/nav-history-apis.html#fire-a-page-transition-event: pageshow bubbles and is cancelable
        Assert.Equal(["DOMContentLoaded", "readystatechange:Complete", "load", "pageshow:True:True"], events);
        Assert.Same(document, loadTarget);
        Assert.Equal(DocumentReadyState.Complete, document.ReadyState);
        Assert.Equal("https://example.com", context.Window.Origin);
        Assert.True(context.Window.IsSecureContext);
    }

    [Fact]
    public void Navigate_TextPlain_TheEncodingOfTheDocumentIsTheCharset()
    {
        using var context = new BrowsingContext();
        var document = context.Navigate("https://example.com/", _ => new DocumentResource(new byte[] { 0x61, 0xA1 }) { Headers = [new("Content-Type", "text/plain;charset=iso-8859-2")] });
        Assert.Equal("ISO-8859-2", document.CharacterSet);
        Assert.Equal("a\U00000104", document.QuerySelector("pre")!.TextContent);
    }

    [Fact]
    public void Navigate_EncodingChange_ReplacesTheDocumentInTheSameWindow()
    {
        // The meta element is after the prescan: the parser restarts with the declared encoding
        using var context = new BrowsingContext();
        var html = "<!DOCTYPE html><!--" + new string('-', 1100) + "--><meta charset=utf-8><p>é</p>";
        var document = context.Navigate("https://example.com/", _ => new DocumentResource(System.Text.Encoding.UTF8.GetBytes(html)));
        context.EventLoop.RunUntilIdle();

        Assert.Equal("UTF-8", document.CharacterSet);
        Assert.Equal("é", document.QuerySelector("p")!.TextContent);
        Assert.Same(document, context.Document);
        Assert.Same(document, context.Window.Document);
        Assert.Equal(1u, context.Window.History.Length);
        Assert.Equal(DocumentReadyState.Complete, document.ReadyState);
    }

    [Fact]
    public void Iframe_InsertedByADomContentLoadedListener_DelaysTheLoadEvent()
    {
        // https://html.spec.whatwg.org/multipage/parsing.html#the-end: the load event is delayed after the DOMContentLoaded event
        using var context = new BrowsingContext();
        var document = context.Navigate("https://example.com/", _ => "<body>");
        var events = new List<string>();
        document.AddEventListener("DOMContentLoaded", _ =>
        {
            var iframe = (HtmlIFrameElement)document.CreateElement("iframe");
            iframe.SetAttribute("srcdoc", "<p>x</p>");
            iframe.AddEventListener("load", _ => events.Add("iframe load:" + iframe.ContentDocument!.Url));
            document.Body!.AppendChild(iframe);
        });
        context.Window.AddEventListener("load", _ => events.Add("load"));
        context.EventLoop.RunUntilIdle();

        Assert.Equal(["iframe load:about:srcdoc", "load"], events);
    }

    [Fact]
    public void Iframe_WithoutSrc_FiresLoadEventSynchronously()
    {
        using var context = new BrowsingContext();
        var document = Load(context, "<body>");
        var iframe = (HtmlIFrameElement)document.CreateElement("iframe");
        var loaded = 0;
        iframe.AddEventListener("load", _ => loaded++);
        document.Body!.AppendChild(iframe);

        Assert.Equal(1, loaded);
        var contentDocument = iframe.ContentDocument!;
        Assert.Equal("about:blank", contentDocument.Url);
        Assert.Equal("https://example.com/dir/page.html", contentDocument.BaseUri);
        Assert.Equal("https://example.com", iframe.ContentWindow!.Origin);
        Assert.Same(iframe.ContentWindow, context.Window[0]);
        Assert.Same(context.Window, context.Window.Self);
        Assert.Equal(1u, context.Window.Length);
        Assert.Same(context.Window, iframe.ContentWindow.Parent);
        Assert.Same(context.Window, iframe.ContentWindow.Top);
        Assert.Same(iframe, iframe.ContentWindow.FrameElement);
        Assert.Same(context, iframe.ContentWindow.BrowsingContext!.Parent);
    }

    [Fact]
    public void Iframe_Srcdoc()
    {
        using var context = new BrowsingContext();
        var document = context.Navigate("https://example.com/", _ => "<base href='https://example.com/base/'><iframe name=frame srcdoc='<p>Hello</p>'></iframe>");
        var iframe = (HtmlIFrameElement)document.QuerySelector("iframe")!;
        Assert.Equal("about:blank", iframe.ContentDocument!.Url);

        var events = new List<string>();
        iframe.AddEventListener("load", _ => events.Add("iframe load"));
        context.Window.AddEventListener("load", _ => events.Add("window load"));
        context.EventLoop.RunUntilIdle();

        // The iframe delays the load event of its document
        Assert.Equal(["iframe load", "window load"], events);
        var contentDocument = iframe.ContentDocument!;
        Assert.Equal("about:srcdoc", contentDocument.Url);
        Assert.Equal("Hello", contentDocument.QuerySelector("p")!.TextContent);
        Assert.Equal("CSS1Compat", contentDocument.CompatMode);
        Assert.Equal("https://example.com/base/", contentDocument.BaseUri);
        Assert.Equal(DocumentReadyState.Complete, contentDocument.ReadyState);
        Assert.Same(iframe.ContentWindow, context.Window.GetNamedItem("frame"));
        Assert.Equal("frame", iframe.ContentWindow!.Name);
    }

    [Fact]
    public void NamedProperty_CrossOriginIframe()
    {
        using var context = new BrowsingContext(new BrowsingContextOptions
        {
            DocumentLoader = new DelegateDocumentLoader((_, _) => ValueTask.FromResult<DocumentResource?>(new DocumentResource("<title>Child</title>"))),
        });

        Load(context, "<iframe name=a src='https://other.example/'></iframe><iframe id=b name=b src='https://other.example/'></iframe>");
        var iframes = context.Document.QuerySelectorAll("iframe").OfType<HtmlIFrameElement>().ToArray();

        // The target name of a cross-origin navigable is not a supported property name, but the navigable is a named object
        Assert.Null(context.Window.GetNamedItem("a"));
        Assert.Same(iframes[1].ContentWindow, context.Window.GetNamedItem("b"));
    }

    [Fact]
    public void Iframe_Src_UsesDocumentLoader()
    {
        var requests = new List<string>();
        using var context = new BrowsingContext(new BrowsingContextOptions
        {
            DocumentLoader = new DelegateDocumentLoader((url, _) =>
            {
                requests.Add(url);
                return ValueTask.FromResult<DocumentResource?>(url.EndsWith(".svg", StringComparison.Ordinal)
                    ? new DocumentResource("<svg xmlns='http://www.w3.org/2000/svg'><rect/></svg>") { Headers = [new("Content-Type", "image/svg+xml")] }
                    : new DocumentResource("<title>Child</title>"));
            }),
        });

        var document = Load(context, "<iframe src='child.html'></iframe><iframe src='image.svg'></iframe>");
        Assert.Equal(["https://example.com/dir/child.html", "https://example.com/dir/image.svg"], requests);

        var iframes = document.QuerySelectorAll("iframe").OfType<HtmlIFrameElement>().ToArray();
        Assert.Equal("Child", iframes[0].ContentDocument!.Title);
        Assert.Equal("https://example.com/dir/child.html", iframes[0].ContentDocument!.Url);
        Assert.Null(iframes[0].GetSvgDocument());

        var svg = iframes[1].GetSvgDocument()!;
        Assert.Equal("image/svg+xml", svg.ContentType);
        Assert.Equal("rect", svg.DocumentElement!.FirstElementChild!.LocalName);
    }

    [Fact]
    public void Embed_Src_DisplaysTheResourceInItsContentNavigable()
    {
        var loader = new DelegateDocumentLoader((_, _) => ValueTask.FromResult<DocumentResource?>(new DocumentResource("<svg xmlns='http://www.w3.org/2000/svg'><rect/></svg>") { Headers = [new("Content-Type", "image/svg+xml")] }));
        using var context = new BrowsingContext(new BrowsingContextOptions { DocumentLoader = loader });
        var events = new List<string>();
        var document = context.Navigate("https://example.com/dir/page.html", _ => "<embed name=e src='image.svg'>");
        var embed = (HtmlEmbedElement)document.QuerySelector("embed")!;
        embed.AddEventListener("load", _ => events.Add("embed load"));
        context.Window.AddEventListener("load", _ => events.Add("window load"));
        context.EventLoop.RunUntilIdle();

        // The resource is fetched once, and the embed element delays the load event of its document
        var request = Assert.Single(loader.Requests);
        Assert.Equal(RequestDestination.Embed, request.Destination);
        Assert.Equal(RequestMode.Navigate, request.Mode);
        Assert.Equal(["embed load", "window load"], events);

        var svg = embed.GetSvgDocument()!;
        Assert.Equal("https://example.com/dir/image.svg", svg.Url);
        Assert.Equal("rect", svg.DocumentElement!.FirstElementChild!.LocalName);
        Assert.Same(svg.DefaultView, context.Window[0]);
        Assert.Same(svg.DefaultView, context.Window.GetNamedItem("e"));
        Assert.Equal("e", svg.DefaultView!.Name);
        Assert.Same(embed, svg.DefaultView.FrameElement);

        // The content navigable is destroyed when the element is removed
        embed.Remove();
        Assert.Null(embed.GetSvgDocument());
        Assert.Equal(0u, context.Window.Length);
    }

    [Fact]
    public void Object_Data_DisplaysADocumentInItsContentNavigable()
    {
        var loader = new DelegateDocumentLoader((_, _) => ValueTask.FromResult<DocumentResource?>(new DocumentResource("<title>Child</title>")));
        using var context = new BrowsingContext(new BrowsingContextOptions { DocumentLoader = loader });
        var events = new List<string>();
        var document = context.Navigate("https://example.com/dir/page.html", _ => "<object name=o data='child.html'><embed src='fallback.html'></object>");
        var obj = (HtmlObjectElement)document.QuerySelector("object")!;
        obj.AddEventListener("load", _ => events.Add("object load"));
        context.Window.AddEventListener("load", _ => events.Add("window load"));
        context.EventLoop.RunUntilIdle();

        // The fallback content (the embed element) is not loaded
        var request = Assert.Single(loader.Requests);
        Assert.Equal("https://example.com/dir/child.html", request.Url);
        Assert.Equal(RequestDestination.Object, request.Destination);
        Assert.Equal(["object load", "window load"], events);
        Assert.Equal("Child", obj.ContentDocument!.Title);
        Assert.Equal("o", obj.ContentWindow!.Name);
        Assert.Same(obj.ContentWindow, context.Window[0]);

        // The name of the content navigable does not change with the attribute
        obj.Name = "other";
        Assert.Equal("o", obj.ContentWindow.Name);
    }

    [Fact]
    public void Object_LoadFailed_FiresErrorAndShowsItsFallbackContent()
    {
        var loader = new DelegateDocumentLoader((url, _) => ValueTask.FromResult<DocumentResource?>(url.EndsWith("missing.html", StringComparison.Ordinal)
            ? new DocumentResource("Not found") { StatusCode = 404 }
            : new DocumentResource("<title>Fallback</title>")));
        using var context = new BrowsingContext(new BrowsingContextOptions { DocumentLoader = loader });
        var events = new List<string>();
        var document = context.Navigate("https://example.com/dir/page.html", _ => "<object data='missing.html'><embed src='fallback.html'></object>");
        var obj = (HtmlObjectElement)document.QuerySelector("object")!;
        obj.AddEventListener("error", _ => events.Add("object error"));
        obj.AddEventListener("load", _ => events.Add("object load"));
        context.Window.AddEventListener("load", _ => events.Add("window load"));
        context.EventLoop.RunUntilIdle();

        Assert.Equal(["object error", "window load"], events);
        Assert.Null(obj.ContentWindow);

        // The embed element of the fallback content is loaded, before the load event of the document
        Assert.Equal(["https://example.com/dir/missing.html", "https://example.com/dir/fallback.html"], loader.Requests.Select(r => r.Url));
        Assert.Equal("Fallback", context.Window[0]!.Document.Title);
    }

    [Fact]
    public void Object_Image_FiresLoadWithoutContentNavigable()
    {
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52, 0, 0, 0, 1, 0, 0, 0, 1];
        var loader = new DelegateDocumentLoader((url, _) => ValueTask.FromResult<DocumentResource?>(url.Contains(".png", StringComparison.Ordinal)
            ? new DocumentResource(png) { Headers = [new("Content-Type", "image/png")] }
            : new DocumentResource("x") { Headers = [new("Content-Type", "application/x-unknown")] }));
        using var context = new BrowsingContext(new BrowsingContextOptions { DocumentLoader = loader });
        var document = Load(context, "<object id=image data='image.png'></object><object id=unknown data='file.bin'></object>");
        var image = (HtmlObjectElement)document.GetElementById("image")!;
        var unknown = (HtmlObjectElement)document.GetElementById("unknown")!;
        var events = new List<string>();
        image.AddEventListener("load", _ => events.Add("image load"));
        unknown.AddEventListener("load", _ => events.Add("unknown load"));
        unknown.AddEventListener("error", _ => events.Add("unknown error"));
        image.Data = "image.png?2";
        unknown.Data = "file.bin?2";
        context.EventLoop.RunUntilIdle();

        // An unsupported type shows the fallback content, without an event
        Assert.Equal(["image load"], events);
        Assert.Null(image.ContentWindow);
        Assert.Null(unknown.ContentWindow);
        Assert.Equal(0u, context.Window.Length);
    }

    [Fact]
    public async Task Iframe_Src_AsynchronousDocumentLoader()
    {
        var loaderRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var context = new BrowsingContext(new BrowsingContextOptions
        {
            DocumentLoader = new DelegateDocumentLoader(async (url, cancellationToken) =>
            {
                await loaderRelease.Task.WaitAsync(cancellationToken);
                return new DocumentResource("<title>" + url + "</title>");
            }),
        });

        var eventLoop = context.EventLoop;
        var document = context.Navigate("https://example.com/", _ => "<iframe src='child.html'></iframe>");
        var events = new List<string>();
        var iframe = (HtmlIFrameElement)document.QuerySelector("iframe")!;
        iframe.AddEventListener("load", _ => events.Add("iframe load"));
        context.Window.AddEventListener("load", _ => events.Add("window load"));

        // The load event of the document waits for the document of the iframe
        eventLoop.RunUntilIdle();
        Assert.True(eventLoop.HasPendingOperations);
        Assert.Equal(DocumentReadyState.Interactive, document.ReadyState);
        Assert.Empty(events);

        loaderRelease.SetResult();
        await eventLoop.RunUntilIdleAsync(TestContext.Current.CancellationToken);
        Assert.False(eventLoop.HasPendingOperations);
        Assert.Equal(["iframe load", "window load"], events);
        Assert.Equal("https://example.com/child.html", iframe.ContentDocument!.Title);
        Assert.Equal(DocumentReadyState.Complete, document.ReadyState);
    }

    [Fact]
    public async Task Iframe_Removal_CancelsTheDocumentLoader()
    {
        var canceled = new TaskCompletionSource();
        using var context = new BrowsingContext(new BrowsingContextOptions
        {
            DocumentLoader = new DelegateDocumentLoader(async (url, cancellationToken) =>
            {
                cancellationToken.Register(() => canceled.TrySetResult());
                await Task.Delay(Timeout.Infinite, cancellationToken);
                return null;
            }),
        });

        var eventLoop = context.EventLoop;
        var document = context.Navigate("https://example.com/", _ => "<iframe src='child.html'></iframe>");
        eventLoop.RunUntilIdle();
        Assert.True(eventLoop.HasPendingOperations);

        // The removed iframe no longer delays the load event
        document.QuerySelector("iframe")!.Remove();
        await canceled.Task.WaitAsync(TestContext.Current.CancellationToken);
        await eventLoop.RunUntilIdleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(DocumentReadyState.Complete, document.ReadyState);
    }

    [Fact]
    public void Iframe_Src_WithoutDocumentLoader_IsANetworkError()
    {
        using var context = new BrowsingContext();
        var document = Load(context, "<iframe src='https://other.example/'></iframe>");
        var iframe = (HtmlIFrameElement)document.QuerySelector("iframe")!;

        // The error document has an opaque origin
        Assert.NotNull(iframe.ContentWindow);
        Assert.Null(iframe.ContentDocument);
        Assert.Equal(DocumentReadyState.Complete, document.ReadyState);
    }

    [Fact]
    public void Iframe_NavigationToTheUrlOfTwoAncestors_IsBlocked()
    {
        var requests = new List<string>();
        using var context = new BrowsingContext(new BrowsingContextOptions
        {
            DocumentLoader = new DelegateDocumentLoader((url, _) =>
            {
                requests.Add(url);
                return ValueTask.FromResult<DocumentResource?>(new DocumentResource("<iframe></iframe>"));
            }),
        });

        var document = Load(context, "<iframe></iframe>");

        // As in the browsers, one level of self-reference is allowed
        var iframe = (HtmlIFrameElement)document.QuerySelector("iframe")!;
        iframe.ContentWindow!.Location.Href = "https://example.com/dir/page.html#child";
        context.EventLoop.RunUntilIdle();
        Assert.Equal("https://example.com/dir/page.html#child", iframe.ContentDocument!.Url);

        // The next level is not loaded, whatever the fragment
        var nestedIframe = (HtmlIFrameElement)iframe.ContentDocument.QuerySelector("iframe")!;
        nestedIframe.ContentWindow!.Location.Href = "https://example.com/dir/page.html#nested";
        context.EventLoop.RunUntilIdle();
        Assert.Equal("about:blank", nestedIframe.ContentDocument!.Url);
        Assert.Equal(["https://example.com/dir/page.html#child"], requests);

        // Another URL is loaded
        nestedIframe.ContentWindow!.Location.Href = "https://example.com/dir/page.html?nested";
        context.EventLoop.RunUntilIdle();
        Assert.Equal("https://example.com/dir/page.html?nested", nestedIframe.ContentDocument!.Url);
    }

    [Fact]
    public void Iframe_SrcOfTwoAncestors_IsNotLoaded()
    {
        var requests = new List<string>();
        using var context = new BrowsingContext(new BrowsingContextOptions
        {
            DocumentLoader = new DelegateDocumentLoader((url, _) =>
            {
                requests.Add(url);
                return ValueTask.FromResult<DocumentResource?>(new DocumentResource("<iframe src='page.html#nested'></iframe>"));
            }),
        });

        // As in the browsers (the specification blocks any self-reference), one level of self-reference is allowed and the
        // next level is not loaded, whatever the fragment
        var document = Load(context, "<iframe src='page.html#child'></iframe>");
        var iframe = (HtmlIFrameElement)document.QuerySelector("iframe")!;
        Assert.Equal("https://example.com/dir/page.html#child", iframe.ContentDocument!.Url);

        var nestedIframe = (HtmlIFrameElement)iframe.ContentDocument.QuerySelector("iframe")!;
        Assert.Equal("about:blank", nestedIframe.ContentDocument!.Url);
        Assert.Equal(["https://example.com/dir/page.html#child"], requests);
    }

    [Fact]
    public void Iframe_Removal_DestroysTheBrowsingContext()
    {
        using var context = new BrowsingContext();
        var document = Load(context, "<iframe></iframe>");
        var iframe = (HtmlIFrameElement)document.QuerySelector("iframe")!;
        var window = iframe.ContentWindow!;
        var childDocument = iframe.ContentDocument!;

        iframe.Remove();
        Assert.Null(iframe.ContentWindow);
        Assert.Null(iframe.ContentDocument);
        Assert.True(window.Closed);
        Assert.Null(window.Parent);
        Assert.Null(childDocument.DefaultView);
        Assert.Equal(0u, context.Window.Length);
        Assert.Empty(context.Children);

        // A new browsing context is created when the iframe is inserted again
        document.Body!.AppendChild(iframe);
        Assert.NotNull(iframe.ContentWindow);
        Assert.NotSame(window, iframe.ContentWindow);
    }

    [Fact]
    public void Location_HashNavigation()
    {
        using var context = new BrowsingContext();
        var document = Load(context, "<div id=a></div><div id=b></div>");
        var window = context.Window;
        var events = new List<string>();
        window.AddEventListener("popstate", e => events.Add("popstate"));
        window.AddEventListener("hashchange", e => events.Add("hashchange " + ((HashChangeEvent)e).OldUrl + " " + ((HashChangeEvent)e).NewUrl));

        Assert.Null(document.QuerySelector(":target"));
        window.Location.Hash = "b";
        Assert.Equal("https://example.com/dir/page.html#b", document.Url);
        Assert.Equal("#b", window.Location.Hash);
        Assert.Equal("b", document.QuerySelector(":target")!.Id);
        Assert.Equal(["popstate"], events);

        context.EventLoop.RunUntilIdle();
        Assert.Equal(["popstate", "hashchange https://example.com/dir/page.html https://example.com/dir/page.html#b"], events);
        Assert.Equal(2u, window.History.Length);

        // Setting the same fragment does not navigate
        window.Location.Hash = "#b";
        Assert.Equal(2u, window.History.Length);

        window.History.Back();
        context.EventLoop.RunUntilIdle();
        Assert.Equal("https://example.com/dir/page.html", document.Url);
        Assert.Null(document.QuerySelector(":target"));
    }

    [Fact]
    public void Location_HashNavigation_InvalidatesTargetStyles()
    {
        using var context = new BrowsingContext();
        var document = Load(context, "<style>div:target { z-index: 1 }</style><div id=a></div><div id=b></div>");
        var window = context.Window;
        var a = document.GetElementById("a")!;
        var b = document.GetElementById("b")!;
        Assert.Equal("auto", window.GetComputedStyle(a).ZIndex);
        Assert.Equal("auto", window.GetComputedStyle(b).ZIndex);

        window.Location.Hash = "a";
        Assert.Equal("1", window.GetComputedStyle(a).ZIndex);
        Assert.Equal("auto", window.GetComputedStyle(b).ZIndex);

        window.Location.Hash = "b";
        Assert.Equal("auto", window.GetComputedStyle(a).ZIndex);
        Assert.Equal("1", window.GetComputedStyle(b).ZIndex);

        window.Location.Hash = "missing";
        Assert.Equal("auto", window.GetComputedStyle(a).ZIndex);
        Assert.Equal("auto", window.GetComputedStyle(b).ZIndex);
    }

    [Fact]
    public void History_PushStateAndReplaceState()
    {
        using var context = new BrowsingContext();
        var document = Load(context, "");
        var history = context.Window.History;
        Assert.Equal(1u, history.Length);
        Assert.Null(history.State);
        Assert.Equal(ScrollRestoration.Auto, history.ScrollRestoration);

        history.PushState("state", "", "other.html?query");
        Assert.Equal("https://example.com/dir/other.html?query", document.Url);
        Assert.Equal("state", history.State);
        Assert.Equal(2u, history.Length);

        // The state is structured-serialized: an object that is not serializable throws before the URL is updated
        var cloneError = Assert.Throws<DomException>(() => history.PushState(new object(), "", "third.html"));
        Assert.Equal("DataCloneError", cloneError.Name);
        Assert.Equal("https://example.com/dir/other.html?query", document.Url);

        history.ReplaceState("replaced", "");
        Assert.Equal("replaced", history.State);
        Assert.Equal(2u, history.Length);

        var ex = Assert.Throws<DomException>(() => history.PushState(null, "", "https://other.example/"));
        Assert.Equal("SecurityError", ex.Name);

        object? popState = "not fired";
        context.Window.AddEventListener("popstate", e => popState = ((PopStateEvent)e).State);
        history.Go(-1);
        context.EventLoop.RunUntilIdle();
        Assert.Null(popState);
        Assert.Equal("https://example.com/dir/page.html", document.Url);

        history.Forward();
        context.EventLoop.RunUntilIdle();
        Assert.Equal("replaced", popState);
    }

    [Fact]
    public void DocumentLoader_ReceivesTheCookieJar()
    {
        var cookies = new CookieJar();
        var loader = new DelegateDocumentLoader((url, _) => ValueTask.FromResult<DocumentResource?>(new DocumentResource("")));
        using var context = new BrowsingContext(new BrowsingContextOptions { DocumentLoader = loader, CookieJar = cookies });
        Load(context, "<iframe src='child.html'></iframe>");

        Assert.Equal("https://example.com/dir/child.html", loader.LastRequest!.Url);
        Assert.Same(cookies, loader.LastRequest.CookieJar);
        Assert.Equal("https://example.com", loader.LastRequest.SiteForCookies);
        Assert.Equal("https://example.com", loader.LastRequest.TopLevelOrigin);
    }

    [Fact]
    public void DocumentLoader_ReceivesTheOriginOfTheInitiator()
    {
        var loader = new DelegateDocumentLoader((url, _) => ValueTask.FromResult<DocumentResource?>(new DocumentResource("")));
        using var context = new BrowsingContext(new BrowsingContextOptions { DocumentLoader = loader });
        Load(context, "<iframe src='https://other.example.com/child.html'></iframe>");
        Assert.Equal("https://other.example.com/child.html", loader.LastRequest!.Url);
        Assert.Equal("https://example.com", loader.LastRequest.Origin);

        // The application navigates: no initiator
        context.Navigate("next.html");
        context.EventLoop.RunUntilIdle();
        Assert.Equal("https://example.com/dir/next.html", loader.LastRequest.Url);
        Assert.Null(loader.LastRequest.Origin);
    }

    [Fact]
    public void Location_CrossDocumentNavigation()
    {
        using var context = new BrowsingContext(new BrowsingContextOptions
        {
            DocumentLoader = new DelegateDocumentLoader((url, _) => ValueTask.FromResult<DocumentResource?>(new DocumentResource("<title>" + url + "</title>"))),
        });

        var document = Load(context, "");
        context.Window.Location.Assign("next.html");
        Assert.Same(document, context.Document);

        context.EventLoop.RunUntilIdle();
        Assert.NotSame(document, context.Document);
        Assert.Equal("https://example.com/dir/next.html", context.Document.Title);
        Assert.Equal(2u, context.Window.History.Length);
        Assert.True(document.IsInBackForwardCache);

        context.Navigate("https://example.com/final.html");
        context.EventLoop.RunUntilIdle();
        Assert.Equal("https://example.com/final.html", context.Document.Url);
    }

    [Fact]
    public void History_Traversal_RestoresFormControlState()
    {
        // The document is loaded again by the traversal: it is not kept in the back/forward cache
        using var context = new BrowsingContext(new BrowsingContextOptions
        {
            BackForwardCacheSize = 0,
            DocumentLoader = new DelegateDocumentLoader((url, _) => ValueTask.FromResult<DocumentResource?>(new DocumentResource(url.EndsWith("page.html", StringComparison.Ordinal)
                ? "<form><input id=text><input id=off autocomplete=off><input id=password type=password><input id=check type=checkbox><textarea id=area></textarea><select id=select><option>a<option>b</select><select id=multiple multiple><option>x<option>y<option>z</select></form><form autocomplete=off><input id=formoff></form>"
                : "<title>next</title>"))),
        });

        context.Navigate("https://example.com/dir/page.html");
        context.EventLoop.RunUntilIdle();
        void SetValues()
        {
            var document = context.Document;
            ((HtmlInputElement)document.GetElementById("text")!).Value = "text";
            ((HtmlInputElement)document.GetElementById("off")!).Value = "off";
            ((HtmlInputElement)document.GetElementById("password")!).Value = "password";
            ((HtmlInputElement)document.GetElementById("check")!).Checked = true;
            ((HtmlTextAreaElement)document.GetElementById("area")!).Value = "area";
            ((HtmlSelectElement)document.GetElementById("select")!).Value = "b";
            var multiple = (HtmlSelectElement)document.GetElementById("multiple")!;
            multiple.Options[1]!.Selected = true;
            multiple.Options[2]!.Selected = true;
            ((HtmlInputElement)document.GetElementById("formoff")!).Value = "formoff";
        }

        SetValues();
        context.Window.Location.Assign("next.html");
        context.EventLoop.RunUntilIdle();
        Assert.Equal("next", context.Document.Title);

        context.Window.History.Back();
        context.EventLoop.RunUntilIdle();
        var restored = context.Document;
        Assert.Equal("text", ((HtmlInputElement)restored.GetElementById("text")!).Value);
        Assert.Equal("", ((HtmlInputElement)restored.GetElementById("off")!).Value);
        Assert.Equal("", ((HtmlInputElement)restored.GetElementById("password")!).Value);
        Assert.True(((HtmlInputElement)restored.GetElementById("check")!).Checked);
        Assert.Equal("area", ((HtmlTextAreaElement)restored.GetElementById("area")!).Value);
        Assert.Equal("b", ((HtmlSelectElement)restored.GetElementById("select")!).Value);
        Assert.Equal(["y", "z"], ((HtmlSelectElement)restored.GetElementById("multiple")!).SelectedOptions.OfType<HtmlOptionElement>().Select(option => option.Value));
        Assert.Equal("", ((HtmlInputElement)restored.GetElementById("formoff")!).Value);

        // A reload does not restore the state of the form controls
        SetValues();
        context.Window.Location.Reload();
        context.EventLoop.RunUntilIdle();
        Assert.NotSame(restored, context.Document);
        Assert.Equal("", ((HtmlInputElement)context.Document.GetElementById("text")!).Value);
        Assert.Equal("a", ((HtmlSelectElement)context.Document.GetElementById("select")!).Value);
    }

    [Fact]
    public void History_Traversal_BackForwardCacheResetsTheAutocompleteOffControls()
    {
        using var context = new BrowsingContext(new BrowsingContextOptions
        {
            DocumentLoader = new DelegateDocumentLoader((url, _) => ValueTask.FromResult<DocumentResource?>(new DocumentResource(url.EndsWith("page.html", StringComparison.Ordinal)
                ? "<form><input id=text><input id=off autocomplete=off value=initial></form><form autocomplete=off><input id=formoff></form>"
                : "<title>next</title>"))),
        });

        context.Navigate("https://example.com/dir/page.html");
        context.EventLoop.RunUntilIdle();
        var document = context.Document;
        ((HtmlInputElement)document.GetElementById("text")!).Value = "text";
        ((HtmlInputElement)document.GetElementById("off")!).Value = "off";
        ((HtmlInputElement)document.GetElementById("formoff")!).Value = "formoff";
        context.Window.Location.Assign("next.html");
        context.EventLoop.RunUntilIdle();
        Assert.Equal("next", context.Document.Title);

        context.Window.History.Back();
        context.EventLoop.RunUntilIdle();

        // https://html.spec.whatwg.org/multipage/browsing-the-web.html#reactivate-a-document: the document is restored from the
        // back/forward cache, and its controls whose autofill field name is "off" are reset
        Assert.Same(document, context.Document);
        Assert.Equal("text", ((HtmlInputElement)document.GetElementById("text")!).Value);
        Assert.Equal("initial", ((HtmlInputElement)document.GetElementById("off")!).Value);
        Assert.Equal("", ((HtmlInputElement)document.GetElementById("formoff")!).Value);
    }

    [Fact]
    public void History_Traversal_RestoresTheEntriesOfTheChildNavigables()
    {
        var requests = new List<string>();
        using var context = new BrowsingContext(new BrowsingContextOptions
        {
            // The document is loaded again by the traversal: it is not kept in the back/forward cache
            BackForwardCacheSize = 0,
            DocumentLoader = new DelegateDocumentLoader((url, _) =>
            {
                requests.Add(url);
                return ValueTask.FromResult<DocumentResource?>(new DocumentResource(url.EndsWith("page.html", StringComparison.Ordinal) ? "<iframe src='child.html'></iframe>" : "<title>" + url + "</title>"));
            }),
        });

        context.Navigate("https://example.com/dir/page.html");
        context.EventLoop.RunUntilIdle();
        var iframe = (HtmlIFrameElement)context.Document.QuerySelector("iframe")!;
        iframe.ContentWindow!.Location.Assign("other.html");
        context.EventLoop.RunUntilIdle();
        Assert.Equal("https://example.com/dir/other.html", iframe.ContentDocument!.Url);
        Assert.Equal(2u, context.Window.History.Length);

        context.Window.Location.Assign("next.html");
        context.EventLoop.RunUntilIdle();
        Assert.Equal(3u, context.Window.History.Length);

        // The iframe of the document loaded again restores the entry it had navigated to, instead of loading its src
        requests.Clear();
        context.Window.History.Back();
        context.EventLoop.RunUntilIdle();
        iframe = (HtmlIFrameElement)context.Document.QuerySelector("iframe")!;
        Assert.Equal("https://example.com/dir/other.html", iframe.ContentDocument!.Url);
        Assert.Equal(["https://example.com/dir/page.html", "https://example.com/dir/other.html"], requests);
        Assert.Equal(3u, context.Window.History.Length);

        // The iframe keeps its session history
        var document = context.Document;
        context.Window.History.Back();
        context.EventLoop.RunUntilIdle();
        Assert.Same(document, context.Document);
        Assert.Same(iframe, context.Document.QuerySelector("iframe"));
        Assert.Equal("https://example.com/dir/child.html", iframe.ContentDocument!.Url);

        // A reload loads the src of the iframe
        context.Window.History.Forward();
        context.EventLoop.RunUntilIdle();
        Assert.Equal("https://example.com/dir/other.html", iframe.ContentDocument!.Url);
        context.Window.Location.Reload();
        context.EventLoop.RunUntilIdle();
        iframe = (HtmlIFrameElement)context.Document.QuerySelector("iframe")!;
        Assert.Equal("https://example.com/dir/child.html", iframe.ContentDocument!.Url);
        Assert.Equal(3u, context.Window.History.Length);
    }

    [Fact]
    public void Hyperlink_Click_NavigatesTheTargetNavigable()
    {
        using var context = new BrowsingContext(new BrowsingContextOptions
        {
            DocumentLoader = new DelegateDocumentLoader((url, _) => ValueTask.FromResult<DocumentResource?>(new DocumentResource("<title>" + url + "</title>"))),
        });

        var document = Load(context, "<iframe name=frame></iframe><a id=self href='next.html'>x</a><a id=frame href='frame.html' target=frame>x</a><a id=blank href='blank.html' target=_blank>x</a><a id=download href='file.txt' download>x</a>");
        var frame = context.Children[0];

        // _blank needs an auxiliary browsing context (not supported), and the downloads are not followed
        ((HtmlElement)document.GetElementById("blank")!).Click();
        ((HtmlElement)document.GetElementById("download")!).Click();
        context.EventLoop.RunUntilIdle();
        Assert.Same(document, context.Document);

        ((HtmlElement)document.GetElementById("frame")!).Click();
        context.EventLoop.RunUntilIdle();
        Assert.Same(document, context.Document);
        Assert.Equal("https://example.com/dir/frame.html", frame.Document.Title);

        ((HtmlElement)document.GetElementById("self")!).Click();
        context.EventLoop.RunUntilIdle();
        Assert.Equal("https://example.com/dir/next.html", context.Document.Title);
    }

    [Fact]
    public void Hyperlink_Click_EditableLinkDoesNotNavigate()
    {
        using var context = new BrowsingContext(new BrowsingContextOptions
        {
            DocumentLoader = new DelegateDocumentLoader((url, _) => ValueTask.FromResult<DocumentResource?>(new DocumentResource("<title>" + url + "</title>"))),
        });

        var document = Load(context, "<a href='next.html' contenteditable>x</a>");
        ((HtmlElement)document.QuerySelector("a")!).Click();
        context.EventLoop.RunUntilIdle();
        Assert.Same(document, context.Document);
    }

    [Fact]
    public void Hyperlink_Click_SendsPings()
    {
        var loader = new DelegateDocumentLoader((url, _) => ValueTask.FromResult<DocumentResource?>(new DocumentResource("<title>" + url + "</title>")));
        using var context = new BrowsingContext(new BrowsingContextOptions { DocumentLoader = loader });

        // The ping URLs are encoding-parsed relative to the document; the URLs that are not HTTP(S) URLs are ignored
        var html = "<iframe name=frame></iframe><a href='next.html' target=frame ping='ping.py?q=\u00FF ftp://example.com/ping https://other.example/ping'>x</a>";
        var document = context.Navigate("https://example.com/dir/page.html", _ => new DocumentResource(System.Text.Encoding.Latin1.GetBytes(html)) { Headers = [new("Content-Type", "text/html; charset=windows-1252")] });
        context.EventLoop.RunUntilIdle();

        ((HtmlElement)document.QuerySelector("a")!).Click();
        context.EventLoop.RunUntilIdle();

        var pings = loader.Requests.Where(request => request.Destination == RequestDestination.Ping).ToList();
        Assert.Equal(["https://example.com/dir/ping.py?q=%FF", "https://other.example/ping"], pings.Select(request => request.Url));
        Assert.All(pings, request =>
        {
            Assert.Equal("POST", request.Method);
            Assert.Equal("PING"u8.ToArray(), request.Body!.Value.ToArray());
            Assert.Equal("text/ping", request.BodyContentType);
            Assert.Null(request.Referrer);
        });

        // The document is https: so the cross-origin ping has no Ping-From header
        Assert.Equal([new("Origin", "https://example.com"), new("Ping-From", "https://example.com/dir/page.html"), new("Ping-To", "https://example.com/dir/next.html")], GetPingHeaders(pings[0]));
        Assert.Equal([new("Origin", "https://example.com"), new("Ping-To", "https://example.com/dir/next.html")], GetPingHeaders(pings[1]));
        Assert.All(pings, request => Assert.Contains(new KeyValuePair<string, string>("Content-Type", "text/ping"), request.Headers));
        Assert.Equal("https://example.com/dir/next.html", context.Children[0].Document.Title);
    }

    private static List<KeyValuePair<string, string>> GetPingHeaders(DocumentRequest request)
        => [.. request.Headers.Where(header => header.Key is "Origin" or "Ping-From" or "Ping-To").OrderBy(header => header.Key, StringComparer.Ordinal)];

    [Fact]
    public void MathMLHyperlink_Click_NavigatesAndSendsPings()
    {
        var loader = new DelegateDocumentLoader((url, _) => ValueTask.FromResult<DocumentResource?>(new DocumentResource("<title>" + url + "</title>")));
        using var context = new BrowsingContext(new BrowsingContextOptions { DocumentLoader = loader });
        var document = Load(context, "<math><a href='next.html' ping='ping.py'><mi>x</mi></a></math>");

        // https://w3c.github.io/mathml-core/#the-a-element: the attributes have the same semantics as for the HTML a element
        var link = Assert.IsType<MathMLAnchorElement>(document.QuerySelector("a"));
        Assert.Equal(0, link.TabIndex);
        Assert.True(link.Matches(":any-link"));
        link.QuerySelector("mi")!.DispatchEvent(new MouseEvent("click", new MouseEventInit { Bubbles = true, Cancelable = true }));
        context.EventLoop.RunUntilIdle();

        Assert.Equal(["https://example.com/dir/ping.py"], loader.Requests.Where(request => request.Destination == RequestDestination.Ping).Select(request => request.Url));
        Assert.Equal("https://example.com/dir/next.html", context.Document.Title);
    }

    [Fact]
    public void Hyperlink_UsesTheTargetOfTheBaseElement()
    {
        using var context = new BrowsingContext(new BrowsingContextOptions
        {
            DocumentLoader = new DelegateDocumentLoader((url, _) => ValueTask.FromResult<DocumentResource?>(new DocumentResource("<title>" + url + "</title>"))),
        });

        var document = Load(context, "<base target=frame><iframe name=frame></iframe><a href='frame.html'>x</a>");
        ((HtmlElement)document.QuerySelector("a")!).Click();
        context.EventLoop.RunUntilIdle();
        Assert.Same(document, context.Document);
        Assert.Equal("https://example.com/dir/frame.html", context.Children[0].Document.Title);
    }

    [Fact]
    public void Hyperlink_EmptyTargetUsesTheTargetOfTheBaseElement()
    {
        using var context = new BrowsingContext(new BrowsingContextOptions
        {
            DocumentLoader = new DelegateDocumentLoader((url, _) => ValueTask.FromResult<DocumentResource?>(new DocumentResource("<title>" + url + "</title>"))),
        });

        var document = Load(context, "<base target=frame><iframe name=frame></iframe><a href='frame.html' target=''>x</a>");
        ((HtmlElement)document.QuerySelector("a")!).Click();
        context.EventLoop.RunUntilIdle();
        Assert.Same(document, context.Document);
        Assert.Equal("https://example.com/dir/frame.html", context.Children[0].Document.Title);
    }

    [Fact]
    public void BaseUri_IgnoresDataAndJavaScriptBaseUrls()
    {
        using var context = new BrowsingContext();
        var document = Load(context, "<base href='data:/,test'><base href='https://example.org/'>");
        Assert.Equal("https://example.com/dir/page.html", document.BaseUri);

        document.QuerySelector("base")!.Remove();
        Assert.Equal("https://example.org/", document.BaseUri);

        document.QuerySelector("base")!.SetAttribute("href", "javascript:void(0)");
        Assert.Equal("https://example.com/dir/page.html", document.BaseUri);
    }

    [Fact]
    public void BaseUri_OfAClone_IsTheBaseUrlOfTheSource()
    {
        using var context = new BrowsingContext();
        var document = Load(context, "<base href='https://example.org/base/'>");
        var shallow = (Document)document.CloneNode(subtree: false);
        var deep = (Document)document.CloneNode(subtree: true);
        document.QuerySelector("base")!.Remove();
        Assert.Equal("https://example.com/dir/page.html", document.BaseUri);
        Assert.Equal("https://example.org/base/", shallow.BaseUri);
        Assert.Equal("https://example.org/base/", deep.BaseUri);

        deep.QuerySelector("base")!.SetAttribute("href", "sub/");
        Assert.Equal("https://example.com/dir/sub/", deep.BaseUri);

        var srcdoc = context.Navigate("https://example.com/", _ => "<base href='https://example.com/base/'><iframe srcdoc='x'></iframe>");
        context.EventLoop.RunUntilIdle();
        var frameDocument = ((HtmlIFrameElement)srcdoc.QuerySelector("iframe")!).ContentDocument!;
        var clone = (Document)frameDocument.CloneNode(subtree: false);
        Assert.Equal("https://example.com/base/", clone.BaseUri);

        var baseElement = clone.CreateElement("base");
        baseElement.SetAttribute("href", "sub/");
        clone.AppendChild(baseElement);
        Assert.Equal("https://example.com/base/sub/", clone.BaseUri);
    }

    [Fact]
    public void Navigate_DataUrl()
    {
        using var context = new BrowsingContext();
        var document = Load(context, "<iframe src='data:text/html,<p>data</p>#fragment'></iframe>");
        var frame = context.Children[0];

        // The document of a data: URL has an opaque origin
        Assert.Equal("data:text/html,<p>data</p>#fragment", frame.Document.Url);
        Assert.Equal("<p>data</p>", frame.Document.Body!.InnerHtml);
        Assert.Equal("null", frame.Document.SerializeOrigin());
        Assert.Null(((HtmlIFrameElement)document.QuerySelector("iframe")!).ContentDocument);
    }

    [Fact]
    public void Navigate_ResolvesTheUrlAgainstTheActiveDocument()
    {
        using var context = new BrowsingContext();
        Load(context, "<p>first</p>");

        Url? requestedUrl = null;
        var document = context.Navigate("next.html#top", url =>
        {
            requestedUrl = url;
            return "<p>second</p>";
        });

        Assert.Equal("https://example.com/dir/next.html#top", requestedUrl!.Href);
        Assert.Equal("https://example.com/dir/next.html#top", document.Url);
        Assert.Same(document, context.Document);
        Assert.Same(context.Window, document.DefaultView);
    }

    [Fact]
    public void Navigate_WithoutUrl_LoadsAboutBlank()
    {
        using var context = new BrowsingContext();
        Url? requestedUrl = null;
        var document = context.Navigate(url =>
        {
            requestedUrl = url;
            return "<p>content</p>";
        });

        Assert.Equal("about:blank", requestedUrl!.Href);
        Assert.Equal("about:blank", document.Url);
        Assert.Same(document, context.Document);
        Assert.Equal("content", document.QuerySelector("p")!.TextContent);

        var xml = context.Navigate(_ => new DocumentResource("<root/>") { Headers = [new("Content-Type", "application/xml")] });
        Assert.Same(xml, context.Document);
        Assert.Equal("root", xml.DocumentElement!.LocalName);
    }

    [Fact]
    public async Task NavigateAsync_WithoutUrl()
    {
        using var context = new BrowsingContext();
        var document = await context.NavigateAsync(_ => "<p>content</p>", TestContext.Current.CancellationToken);
        Assert.Same(document, context.Document);
        Assert.Equal("about:blank", document.Url);

        var resource = await context.NavigateAsync(_ => new DocumentResource("<p>resource</p>"), TestContext.Current.CancellationToken);
        Assert.Same(resource, context.Document);
        Assert.Equal("resource", resource.QuerySelector("p")!.TextContent);
    }

    [Fact]
    public void Navigate_HistoryHandling()
    {
        // The navigation from the initial about:blank document replaces its entry
        using var context = new BrowsingContext();
        var first = Load(context, "<p>first</p>");
        Assert.Equal(1u, context.Window.History.Length);

        var second = Load(context, "<p>second</p>", "https://example.com/next.html");
        Assert.Equal(2u, context.Window.History.Length);
        Assert.True(first.IsInBackForwardCache);
        Assert.NotNull(second.DefaultView);

        // A navigation to the same URL replaces the entry
        Load(context, "<p>third</p>", "https://example.com/next.html");
        Assert.Equal(2u, context.Window.History.Length);
        Assert.Equal("third", context.Document.QuerySelector("p")!.TextContent);
    }

    [Fact]
    public void Navigate_DocumentResource_Bytes()
    {
        using var context = new BrowsingContext();
        var document = context.Navigate("https://example.com/", _ => new DocumentResource(System.Text.Encoding.Latin1.GetBytes("<p>é</p>")) { Headers = [new("Content-Type", "text/html; charset=iso-8859-1")] });
        Assert.Equal("windows-1252", document.CharacterSet);
        Assert.Equal("é", document.QuerySelector("p")!.TextContent);
    }

    [Theory]
    [InlineData(null, "text/html")]
    [InlineData("invalid", "text/plain")]
    [InlineData("*/*", "text/plain")]
    [InlineData("TEXT/XML", "text/xml")]
    [InlineData("application/xhtml+xml; charset=utf-8", "application/xhtml+xml")]
    [InlineData("text/plain, application/xml", "application/xml")]
    [InlineData("application/xml, */*", "application/xml")]
    public void Navigate_DocumentResource_ContentType(string? contentType, string expected)
    {
        using var context = new BrowsingContext();
        var document = context.Navigate("https://example.com/", _ => new DocumentResource("<root/>") { Headers = contentType is null ? [] : [new("content-type", contentType)] });
        Assert.Equal(expected, document.ContentType);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("*/*")]
    public void Navigate_DocumentResource_ContentTypeWithoutMimeType_SniffsHtml(string contentType)
    {
        // https://mimesniff.spec.whatwg.org/#rules-for-identifying-an-unknown-mime-type: a Content-Type that gives no MIME type is sniffed
        using var context = new BrowsingContext();
        var document = context.Navigate("https://example.com/", _ => new DocumentResource("  <!DOCTYPE html><p>sniffed</p>") { Headers = [new("content-type", contentType)] });
        Assert.Equal("text/html", document.ContentType);
        Assert.Equal("sniffed", document.QuerySelector("p")!.TextContent);
    }

    [Fact]
    public void Navigate_DocumentResource_CharsetOfSeveralContentTypeHeaders()
    {
        // https://fetch.spec.whatwg.org/#concept-header-extract-mime-type: the charset of a previous header with the same essence is kept
        using var context = new BrowsingContext();
        var document = context.Navigate("https://example.com/", _ => new DocumentResource(System.Text.Encoding.Latin1.GetBytes("<p>é</p>"))
        {
            Headers = [new("Content-Type", "text/html; charset=iso-8859-1"), new("Content-Type", "text/html")],
        });
        Assert.Equal("windows-1252", document.CharacterSet);
    }

    [Fact]
    public void Navigate_DocumentResource_ErrorStatus_DisplaysTheBody()
    {
        using var context = new BrowsingContext();
        var document = context.Navigate("https://example.com/", _ => new DocumentResource("<p>not found</p>") { StatusCode = 404 });
        Assert.Equal("not found", document.QuerySelector("p")!.TextContent);
    }

    [Theory]
    [InlineData(204, null)]
    [InlineData(205, null)]
    [InlineData(200, "attachment")]
    [InlineData(200, " ATTACHMENT ; filename=\"a.html\"")]
    public void Navigate_ResponseWithoutDocument_KeepsTheActiveDocument(int statusCode, string? contentDisposition)
    {
        // https://html.spec.whatwg.org/multipage/browsing-the-web.html#populating-a-session-history-entry
        using var context = new BrowsingContext(new BrowsingContextOptions
        {
            DocumentLoader = new DelegateDocumentLoader((_, _) => ValueTask.FromResult<DocumentResource?>(new DocumentResource("<p>new</p>")
            {
                StatusCode = statusCode,
                Headers = contentDisposition is null ? [] : [new("Content-Disposition", contentDisposition)],
            })),
        });

        var document = Load(context, "<p>current</p>");
        context.Window.Location.Assign("next.html");
        context.EventLoop.RunUntilIdle();
        Assert.Same(document, context.Document);
        Assert.Equal("https://example.com/dir/page.html", document.Url);
        Assert.Equal(1u, context.Window.History.Length);

        Assert.Same(document, context.Navigate("other.html", _ => new DocumentResource("<p>new</p>") { StatusCode = statusCode, Headers = contentDisposition is null ? [] : [new("Content-Disposition", contentDisposition)] }));
        Assert.Same(document, context.Document);
    }

    [Fact]
    public void Navigate_InlineContentDisposition_CreatesADocument()
    {
        using var context = new BrowsingContext();
        var document = context.Navigate("https://example.com/", _ => new DocumentResource("<p>new</p>") { Headers = [new("Content-Disposition", "inline; filename=attachment")] });
        Assert.Equal("new", document.QuerySelector("p")!.TextContent);
    }

    [Fact]
    public async Task Iframe_NoContentResponse_DoesNotDelayTheLoadEvent()
    {
        using var context = new BrowsingContext(new BrowsingContextOptions
        {
            DocumentLoader = new DelegateDocumentLoader(async (_, _) =>
            {
                await Task.Yield();
                return new DocumentResource("<p>new</p>") { StatusCode = 204 };
            }),
        });

        var document = context.Navigate("https://example.com/", _ => "<iframe src='child.html'></iframe>");
        var iframe = (HtmlIFrameElement)document.QuerySelector("iframe")!;
        var initialDocument = iframe.ContentDocument;
        var events = new List<string>();
        context.Window.AddEventListener("load", _ => events.Add("window load"));
        await context.EventLoop.RunUntilIdleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["window load"], events);
        Assert.Same(initialDocument, iframe.ContentDocument);
        Assert.Equal("about:blank", iframe.ContentDocument!.Url);
    }

    [Fact]
    public void Iframe_Bytes_InheritsTheEncodingOfASameOriginParent()
    {
        var child = LoadIframeDocument("<iframe src='child.html'></iframe>"u8.ToArray(), "text/html; charset=windows-1251", [.. "<p>"u8, 0xE9]);
        Assert.Equal("windows-1251", child.CharacterSet);
        Assert.Equal("\U00000439", child.QuerySelector("p")!.TextContent);
    }

    [Fact]
    public void Iframe_Bytes_DoesNotInheritTheEncodingOfACrossOriginParent()
    {
        var child = LoadIframeDocument("<iframe src='https://other.example/child.html'></iframe>"u8.ToArray(), "text/html; charset=windows-1251", [.. "<p>"u8, 0xE9]);
        Assert.Equal("windows-1252", child.CharacterSet);
        Assert.Equal("é", child.QuerySelector("p")!.TextContent);
    }

    [Fact]
    public void Iframe_Bytes_DoesNotInheritUtf16()
    {
        var child = LoadIframeDocument([0xFF, 0xFE, .. System.Text.Encoding.Unicode.GetBytes("<iframe src='child.html'></iframe>")], "text/html", [.. "<p>"u8, 0xE9]);
        Assert.Equal("windows-1252", child.CharacterSet);
        Assert.Equal("é", child.QuerySelector("p")!.TextContent);
    }

    [Fact]
    public void Iframe_Bytes_MetaCharsetOverridesTheEncodingOfTheParent()
    {
        var child = LoadIframeDocument("<iframe src='child.html'></iframe>"u8.ToArray(), "text/html; charset=utf-8", [.. "<meta charset=windows-1251><p>"u8, 0xE9]);
        Assert.Equal("windows-1251", child.CharacterSet);
        Assert.Equal("\U00000439", child.QuerySelector("p")!.TextContent);
    }

    private static Document LoadIframeDocument(byte[] parent, string parentContentType, byte[] child)
    {
        using var context = new BrowsingContext(new BrowsingContextOptions
        {
            DocumentLoader = new DelegateDocumentLoader((_, _) => ValueTask.FromResult<DocumentResource?>(new DocumentResource(child))),
        });

        var document = context.Navigate("https://example.com/", _ => new DocumentResource(parent) { Headers = [new("Content-Type", parentContentType)] });
        context.EventLoop.RunUntilIdle();
        // The navigable gives access to the document of a cross-origin iframe (contentDocument is null)
        return ((HtmlIFrameElement)document.QuerySelector("iframe")!).ContentNavigable!.Document;
    }

    [Fact]
    public void Navigate_DocumentResource_Xml()
    {
        using var context = new BrowsingContext();
        var document = context.Navigate("https://example.com/image.svg", _ => new DocumentResource("<svg xmlns='http://www.w3.org/2000/svg'><rect/></svg>") { Headers = [new("Content-Type", "image/svg+xml")] });
        context.EventLoop.RunUntilIdle();
        Assert.Same(document, context.Document);
        Assert.Equal("image/svg+xml", document.ContentType);
        Assert.Equal("svg", document.DocumentElement!.LocalName);
        Assert.Equal(DocumentReadyState.Complete, document.ReadyState);
    }

    [Fact]
    public void Navigate_DocumentResource_Url()
    {
        using var context = new BrowsingContext();
        var document = context.Navigate("https://example.com/", _ => new DocumentResource("") { Url = "https://example.com/redirected.html" });
        Assert.Equal("https://example.com/redirected.html", document.Url);
    }

    [Fact]
    public void Navigate_DocumentResource_Null_IsANetworkError()
    {
        using var context = new BrowsingContext();
        var document = context.Navigate("https://example.com/", _ => (DocumentResource?)null);
        Assert.Same(document, context.Document);
        Assert.Equal("null", context.Window.Origin);
    }

    [Fact]
    public async Task Navigate_AbortsTheOngoingNavigation()
    {
        var pending = new TaskCompletionSource<DocumentResource?>();
        CancellationToken loaderToken = default;
        using var context = new BrowsingContext(new BrowsingContextOptions
        {
            DocumentLoader = new DelegateDocumentLoader((url, cancellationToken) =>
            {
                loaderToken = cancellationToken;
                return new ValueTask<DocumentResource?>(pending.Task);
            }),
        });

        context.Navigate("https://example.com/loaded.html");
        context.EventLoop.RunUntilIdle();
        Assert.False(loaderToken.IsCancellationRequested);

        var document = Load(context, "<p>provided</p>");
        Assert.True(loaderToken.IsCancellationRequested);

        pending.SetResult(new DocumentResource("<p>loaded</p>"));
        await context.EventLoop.RunUntilIdleAsync(TestContext.Current.CancellationToken);
        Assert.Same(document, context.Document);
    }

    [Fact]
    public void Navigate_InvalidArguments()
    {
        using var context = new BrowsingContext();
        Assert.Throws<ArgumentException>(() => context.Navigate("javascript:'x'", _ => "<p>x</p>"));
        Assert.Throws<ArgumentException>(() => context.Navigate("http://[invalid", _ => "<p>x</p>"));
        Assert.Throws<InvalidOperationException>(() => context.Navigate("https://example.com/", _ => (string)null!));

        Load(context, "<iframe></iframe>");
        var child = context.Children[0];
        context.Document.QuerySelector("iframe")!.Remove();
        Assert.True(child.IsDestroyed);
        Assert.Throws<InvalidOperationException>(() => child.Navigate("https://example.com/", _ => "<p>x</p>"));
    }

    [Fact]
    public void Navigate_NestedBrowsingContext()
    {
        using var context = new BrowsingContext();
        var document = Load(context, "<iframe></iframe>");
        var iframe = (HtmlIFrameElement)document.QuerySelector("iframe")!;
        var events = new List<string>();
        iframe.AddEventListener("load", _ => events.Add("iframe load"));

        var frameDocument = context.Children[0].Navigate("frame.html", _ => "<p>frame</p>");
        Assert.Same(frameDocument, iframe.ContentDocument);
        Assert.Equal("https://example.com/dir/frame.html", frameDocument.Url);

        context.EventLoop.RunUntilIdle();
        Assert.Equal(["iframe load"], events);
    }

    [Fact]
    public void Window_Base64()
    {
        using var context = new BrowsingContext();
        var window = context.Window;
        Assert.Equal("aGVsbG8=", window.Btoa("hello"));
        Assert.Equal("hello", window.Atob(" aGVs bG8= "));
        Assert.Equal("InvalidCharacterError", Assert.Throws<DomException>(() => window.Btoa("Ā")).Name);
        Assert.Equal("InvalidCharacterError", Assert.Throws<DomException>(() => window.Atob("a")).Name);
    }

    [Fact]
    public void EventLoop_IsSharedByTheBrowsingContextGroup()
    {
        using var context = new BrowsingContext();
        using var other = new BrowsingContext();
        var document = Load(context, "<iframe srcdoc='<p>x</p>'></iframe>");
        var nested = ((HtmlIFrameElement)document.QuerySelector("iframe")!).ContentWindow!.BrowsingContext!;

        Assert.NotSame(context.EventLoop, other.EventLoop);
        Assert.Same(context.EventLoop, nested.EventLoop);
    }

    [Fact]
    public void EventLoop_RunsOnlyTheTasksOfItsBrowsingContextGroup()
    {
        using var context = new BrowsingContext();
        using var other = new BrowsingContext();
        var log = new List<string>();
        context.Window.SetTimeout(() => log.Add("context"), 0);
        other.Window.SetTimeout(() => log.Add("other"), 0);

        context.EventLoop.RunUntilIdle();
        Assert.Equal(["context"], log);

        other.EventLoop.RunUntilIdle();
        Assert.Equal(["context", "other"], log);
    }

    [Fact]
    public void EventLoop_TimeProviderIsNotShared()
    {
        var clock = new FakeTimeProvider();
        using var context = new BrowsingContext(new BrowsingContextOptions { TimeProvider = clock });
        using var other = new BrowsingContext();

        Assert.Same(clock, context.EventLoop.TimeProvider);
        Assert.Same(TimeProvider.System, other.EventLoop.TimeProvider);
    }

    [Fact]
    public void DocumentWithoutBrowsingContext_MutationObserversAreNotNotified()
    {
        var document = HtmlDocumentParser.ParseDocument("<body>");
        var calls = 0;
        var observer = new MutationObserver((_, _) => calls++);
        observer.Observe(document.Body!, new MutationObserverInit { ChildList = true });

        document.Body!.AppendChild(document.CreateElement("p"));
        Assert.Equal(0, calls);
        Assert.Single(observer.TakeRecords());
    }

    [Fact]
    public void MutationObserver_IsNotifiedByTheEventLoopOfTheObservedDocument()
    {
        using var context = new BrowsingContext();
        var document = Load(context, "<body>");
        var calls = 0;
        var observer = new MutationObserver((records, _) => calls += records.Count);
        observer.Observe(document.Body!, new MutationObserverInit { ChildList = true });

        document.Body!.AppendChild(document.CreateElement("p"));
        Assert.Equal(0, calls);
        context.EventLoop.PerformMicrotaskCheckpoint();
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Dispose_DiscardsThePendingTasksAndDestroysTheDocuments()
    {
        using var context = new BrowsingContext();
        var document = Load(context, "<iframe srcdoc='<p>x</p>'></iframe>");
        var frameDocument = ((HtmlIFrameElement)document.QuerySelector("iframe")!).ContentDocument!;
        var eventLoop = context.EventLoop;
        var log = new List<string>();
        context.Window.SetTimeout(() => log.Add("timeout"), 0);
        context.Window.QueueMicrotask(() => log.Add("microtask"));
        eventLoop.QueueTask(() => log.Add("task"));

        context.Dispose();
        Assert.True(context.IsDestroyed);
        Assert.True(eventLoop.IsDisposed);
        Assert.Null(document.DefaultView);
        Assert.Null(frameDocument.DefaultView);
        Assert.False(eventLoop.HasPendingWork);
        Assert.Null(eventLoop.PromoteDueTimers());

        // The tasks queued after the disposal are ignored
        eventLoop.QueueTask(() => log.Add("late task"));
        eventLoop.QueueMicrotask(() => log.Add("late microtask"));
        eventLoop.RunUntilIdle();
        Assert.Empty(log);

        context.Dispose();
    }

    [Fact]
    public async Task Dispose_CancelsTheDocumentLoaderAndStopsTheEventLoop()
    {
        var canceled = new TaskCompletionSource();
        using var context = new BrowsingContext(new BrowsingContextOptions
        {
            DocumentLoader = new DelegateDocumentLoader(async (_, cancellationToken) =>
            {
                // Not a callback registered on the token: the continuation of Task.Delay can run synchronously in Cancel, before
                // the callbacks registered first, and leaving the method would then unregister the callback before it runs
                try
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    canceled.TrySetResult();
                    throw;
                }

                return null;
            }),
        });

        context.Navigate("https://example.com/", _ => "<iframe src='child.html'></iframe>");
        var eventLoop = context.EventLoop;
        eventLoop.RunUntilIdle();
        Assert.True(eventLoop.HasPendingOperations);

        // RunUntilIdleAsync waits for the document of the iframe, until the browsing context is disposed
        var run = eventLoop.RunUntilIdleAsync(TestContext.Current.CancellationToken);
        Assert.False(run.IsCompleted);
        context.Dispose();
        await run.WaitAsync(TestContext.Current.CancellationToken);
        await canceled.Task.WaitAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public void Dispose_NestedBrowsingContext_Throws()
    {
        using var context = new BrowsingContext();
        var document = Load(context, "<iframe></iframe>");
        var nested = ((HtmlIFrameElement)document.QuerySelector("iframe")!).ContentWindow!.BrowsingContext!;

        Assert.Throws<InvalidOperationException>(nested.Dispose);
        Assert.False(nested.IsDestroyed);
    }

    [Fact]
    public void Options_DocumentActivated_IsInvokedForTheNavigationsButNotForTheInitialDocuments()
    {
        var activated = new List<string>();
        using var context = new BrowsingContext(new BrowsingContextOptions
        {
            DocumentActivated = (browsingContext, document) => activated.Add((browsingContext.Parent is null ? "top " : "frame ") + document.Url),
        });

        Assert.Empty(activated);
        Load(context, "<iframe srcdoc='<p>x'></iframe>", "https://example.com/a");
        Load(context, "<p>b", "https://example.com/b");

        Assert.Equal(["top https://example.com/a", "frame about:srcdoc", "top https://example.com/b"], activated);
    }

    [Fact]
    public void Options_BrowsingContextCreatedAndDestroyed_AreInvokedForTheIframes()
    {
        var calls = new List<string>();
        using var context = new BrowsingContext(new BrowsingContextOptions
        {
            BrowsingContextCreated = browsingContext => calls.Add($"created {browsingContext.Name} {browsingContext.Document.Url} {browsingContext.Parent is not null}"),
            BrowsingContextDestroyed = browsingContext => calls.Add($"destroyed {browsingContext.Name} {browsingContext.IsDestroyed}"),
        });

        var document = Load(context, "<iframe name=child></iframe>");
        Assert.Equal(["created child about:blank True"], calls);

        document.QuerySelector("iframe")!.Remove();
        Assert.Equal(["created child about:blank True", "destroyed child True"], calls);

        context.Dispose();
        Assert.Equal(["created child about:blank True", "destroyed child True", "destroyed  True"], calls);
    }

    [Fact]
    public void Options_SameDocumentNavigated_IsInvokedForTheFragmentNavigations()
    {
        var urls = new List<string>();
        using var context = new BrowsingContext(new BrowsingContextOptions
        {
            SameDocumentNavigated = browsingContext => urls.Add(browsingContext.Document.Url),
        });
        var document = Load(context, "<p id=a>a", "https://example.com/page");

        document.DefaultView!.Location.Hash = "a";
        context.EventLoop.RunUntilIdle();

        Assert.Equal(["https://example.com/page#a"], urls);
    }

    [Fact]
    public void Viewport_CanBeChangedAtRuntime()
    {
        var clock = new FakeTimeProvider();
        using var context = new BrowsingContext(new BrowsingContextOptions { TimeProvider = clock });
        var document = Load(context, "<style>@media (prefers-color-scheme: dark) { p { color: white } }</style><p>x");
        var window = document.DefaultView!;
        var resizes = 0;
        window.AddEventListener("resize", _ => resizes++);
        Assert.Same(ViewportOptions.Default, context.Viewport);

        context.Viewport = context.Viewport with { Width = 1024, Height = 768, PreferredColorScheme = PreferredColorScheme.Dark };
        clock.Advance(TimeSpan.FromMilliseconds(50));
        context.EventLoop.RunUntilIdle();

        Assert.Equal((1024, 768), (window.InnerWidth, window.InnerHeight));
        Assert.True(window.MatchMedia("(prefers-color-scheme: dark)").Matches);
        Assert.Equal("rgb(255, 255, 255)", window.GetComputedStyle(document.QuerySelector("p")!).Color);
        Assert.Equal(1, resizes);
    }

    [Fact]
    public void Viewport_NestedBrowsingContext_Throws()
    {
        using var context = new BrowsingContext();
        var document = Load(context, "<iframe></iframe>");
        var nested = ((HtmlIFrameElement)document.QuerySelector("iframe")!).ContentWindow!.BrowsingContext!;

        Assert.Equal(ViewportOptions.Default, nested.Viewport);
        Assert.Throws<InvalidOperationException>(() => nested.Viewport = new ViewportOptions { Width = 10, Height = 10 });
    }

    [Fact]
    public void IsOffline_ChangesNavigatorOnLineAndFiresTheEvents()
    {
        using var context = new BrowsingContext();
        var document = Load(context, "<iframe></iframe>");
        var window = document.DefaultView!;
        var frameWindow = ((HtmlIFrameElement)document.QuerySelector("iframe")!).ContentWindow!;
        var events = new List<string>();
        window.AddEventListener("offline", e => events.Add("top " + e.Type));
        window.AddEventListener("online", e => events.Add("top " + e.Type));
        frameWindow.AddEventListener("offline", e => events.Add("frame " + e.Type));

        context.IsOffline = true;
        context.EventLoop.RunUntilIdle();
        Assert.False(window.Navigator.OnLine);
        Assert.False(frameWindow.Navigator.OnLine);
        Assert.True(frameWindow.BrowsingContext!.IsOffline);

        context.IsOffline = false;
        context.EventLoop.RunUntilIdle();
        Assert.True(window.Navigator.OnLine);
        Assert.Equal(["top offline", "frame offline", "top online"], events);
    }

    [Fact]
    public void Reload_LoadsTheDocumentAgain()
    {
        var count = 0;
        var loader = new DelegateDocumentLoader((_, _) => ValueTask.FromResult<DocumentResource?>(new DocumentResource($"<p>{++count}")));
        using var context = new BrowsingContext(new BrowsingContextOptions { DocumentLoader = loader });
        context.Navigate("https://example.com/");
        context.EventLoop.RunUntilIdle();
        Assert.Equal("1", context.Document.Body!.TextContent);

        context.Reload();
        context.EventLoop.RunUntilIdle();

        Assert.Equal("2", context.Document.Body!.TextContent);
        Assert.Equal("https://example.com/", context.Document.Url);
    }

    [Fact]
    public void IsNavigating_IsTrueUntilTheNavigationEnds()
    {
        var loader = new DelegateDocumentLoader((url, _) => ValueTask.FromResult<DocumentResource?>(url.EndsWith("/empty", StringComparison.Ordinal) ? new DocumentResource("") { StatusCode = 204 } : new DocumentResource("<p>x")));
        using var context = new BrowsingContext(new BrowsingContextOptions { DocumentLoader = loader });
        Assert.False(context.IsNavigating);

        context.Navigate("https://example.com/");
        Assert.True(context.IsNavigating);
        context.EventLoop.RunUntilIdle();
        Assert.False(context.IsNavigating);
        Assert.Equal("https://example.com/", context.Document.Url);

        context.Navigate("https://example.com/empty");
        Assert.True(context.IsNavigating);
        context.EventLoop.RunUntilIdle();
        Assert.False(context.IsNavigating);
        Assert.Equal("https://example.com/", context.Document.Url);
    }

    [Fact]
    public void Languages_AreTheLanguagesOfTheNavigatorAndOfTheRequests()
    {
        var loader = new DelegateDocumentLoader((_, _) => ValueTask.FromResult<DocumentResource?>(new DocumentResource("<p>x")));
        using var context = new BrowsingContext(new BrowsingContextOptions { DocumentLoader = loader, Languages = ["fr-FR", "fr", "en"] });
        context.Navigate("https://example.com/");
        context.EventLoop.RunUntilIdle();

        var navigator = context.Window.Navigator;
        Assert.Equal("fr-FR", navigator.Language);
        Assert.Equal(["fr-FR", "fr", "en"], navigator.Languages);
        Assert.Contains(new KeyValuePair<string, string>("Accept-Language", "fr-FR,fr;q=0.9,en;q=0.8"), loader.LastRequest!.Headers);
    }

    [Fact]
    public void Languages_DefaultLanguages()
    {
        using var context = new BrowsingContext();

        Assert.Equal("en-US", context.Window.Navigator.Language);
        Assert.Equal(["en-US", "en"], context.Window.Navigator.Languages);
    }

    [Fact]
    public void DocumentRequest_BrowsingContext_IsTheBrowsingContextOfTheRequest()
    {
        var loader = new DelegateDocumentLoader((url, _) => ValueTask.FromResult<DocumentResource?>(new DocumentResource(url.EndsWith("/child", StringComparison.Ordinal) ? "<p>child" : "<iframe src=child></iframe><img src=image.png>")));
        using var context = new BrowsingContext(new BrowsingContextOptions { DocumentLoader = loader });
        context.Navigate("https://example.com/");
        context.EventLoop.RunUntilIdle();
        var frame = context.Children[0];

        var requests = loader.Requests.Select(request => (request.Url, request.Mode, request.BrowsingContext)).ToList();
        Assert.Contains(("https://example.com/", RequestMode.Navigate, context), requests);
        Assert.Contains(("https://example.com/child", RequestMode.Navigate, frame), requests);
        Assert.Contains(("https://example.com/image.png", RequestMode.NoCors, context), requests);
    }

    [Fact]
    public void IsNetworkErrorDocument_IsTrueForTheDocumentOfAFailedNavigation()
    {
        var loader = new DelegateDocumentLoader((url, _) => ValueTask.FromResult<DocumentResource?>(url.EndsWith("/ok", StringComparison.Ordinal) ? new DocumentResource("<p>ok") : null));
        using var context = new BrowsingContext(new BrowsingContextOptions { DocumentLoader = loader });

        context.Navigate("https://example.com/ok");
        context.EventLoop.RunUntilIdle();
        Assert.False(context.Document.IsNetworkErrorDocument);

        context.Navigate("https://example.com/error");
        context.EventLoop.RunUntilIdle();
        Assert.True(context.Document.IsNetworkErrorDocument);
    }

    private sealed class DelegateDocumentLoader(Func<string, CancellationToken, ValueTask<DocumentResource?>> load) : DocumentLoader
    {
        public DocumentRequest? LastRequest { get; private set; }

        public List<DocumentRequest> Requests { get; } = [];

        public override ValueTask<DocumentResource?> LoadAsync(DocumentRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            Requests.Add(request);
            return load(request.Url, cancellationToken);
        }
    }
}
