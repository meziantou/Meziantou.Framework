using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Tests.Css;

public sealed class CssImportTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();

    public void Dispose() => _contexts.Dispose();

    private const string BaseUrl = "https://example.com/dir/page.html";

    private Document Parse(string html, DocumentLoader? loader, string url = BaseUrl)
    {
        var context = _contexts.Create(new BrowsingContextOptions { DocumentLoader = loader });
        var document = context.Navigate(url, _ => html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    private static string GetStyle(Document document, string property, string selector = "#target") =>
        document.DefaultView!.GetComputedStyle(document.QuerySelector(selector)!).GetPropertyValue(property);

    private static CssImportRule GetImportRule(Document document, int index = 0) => (CssImportRule)document.StyleSheets[0].CssRules[(uint)index]!;

    [Fact]
    public void Import_AppliesTheRulesOfTheImportedStyleSheet()
    {
        var loader = new FileLoader { ["https://example.com/dir/theme.css"] = "#target { color: green }" };
        var document = Parse("<style>@import \"theme.css\";</style><div id=target></div>", loader);

        Assert.Equal("rgb(0, 128, 0)", GetStyle(document, "color"));
        var request = Assert.Single(loader.Requests);
        Assert.Equal("https://example.com/dir/theme.css", request.Url);
        Assert.Equal(RequestDestination.Style, request.Destination);
        Assert.Same(document.QuerySelector("style"), request.Initiator);
    }

    [Fact]
    public void Import_StyleElementTextParsedInSeveralRuns()
    {
        // The tokenizer emits the text of the style element in several runs ("<" starts a new run)
        var loader = new FileLoader { ["https://example.com/dir/theme.css"] = "#target { color: red }" };
        var document = Parse("<style>@import \"theme.css\"; /* < */ #target { color: green }</style><div id=target></div>", loader);

        Assert.Equal(2u, document.StyleSheets[0].CssRules.Length);
        Assert.Equal("rgb(0, 128, 0)", GetStyle(document, "color"));
        Assert.Single(loader.Requests);
    }

    [Fact]
    public void Import_StyleElementWrittenInSeveralCalls()
    {
        var loader = new FileLoader
        {
            ["https://example.com/dir/a.css"] = "#target { color: red }",
            ["https://example.com/dir/b.css"] = "#target { background-color: green }",
        };
        var engine = new Scripting.RecordingScriptEngine();
        engine.Behaviors["write"] = script =>
        {
            var document = ((Window)script.Global).Document;
            document.Write("<style>@import 'a.css';");
            document.Write("@import 'b.css'; #target { color: green }</style>");
            return null;
        };

        var context = _contexts.Create(new BrowsingContextOptions { DocumentLoader = loader, ScriptEngine = engine });
        var document = context.Navigate(BaseUrl, _ => "<script>write</script><div id=target></div>");
        _contexts.EventLoop.RunUntilIdle();

        Assert.Equal(3u, document.StyleSheets[0].CssRules.Length);
        Assert.Equal("rgb(0, 128, 0)", GetStyle(document, "color"));
        Assert.Equal("rgb(0, 128, 0)", GetStyle(document, "background-color"));
    }

    [Fact]
    public void Import_Cssom()
    {
        var loader = new FileLoader { ["https://example.com/dir/theme.css"] = "div { color: green }" };
        var document = Parse("<style>@import url(theme.css) screen;</style>", loader);

        var sheet = document.StyleSheets[0];
        var rule = GetImportRule(document);
        var imported = rule.StyleSheet!;
        Assert.Equal("https://example.com/dir/theme.css", imported.Href);
        Assert.Same(sheet, imported.ParentStyleSheet);
        Assert.Same(rule, imported.OwnerRule);
        Assert.Null(imported.OwnerNode);
        Assert.Same(rule.Media, imported.Media);
        Assert.Equal("div { color: green; }", imported.CssRules[0]!.CssText);

        // The imported style sheets are not in the list of the style sheets of the document
        Assert.Equal(1u, document.StyleSheets.Length);
    }

    [Fact]
    public void Import_RelativeUrlsAreResolvedAgainstTheImportedStyleSheet()
    {
        var loader = new FileLoader
        {
            ["https://example.com/css/main.css"] = "@import 'parts/colors.css'; #target { background-image: url(img.png) }",
            ["https://example.com/css/parts/colors.css"] = "#target { color: green }",
        };
        var document = Parse("<style>@import '/css/main.css';</style><div id=target></div>", loader);

        // (the image of the computed style of the rendered element is fetched before the load event)
        Assert.Equal(["https://example.com/css/main.css", "https://example.com/css/parts/colors.css", "https://example.com/css/img.png"], loader.Requests.Select(r => r.Url));
        Assert.Equal("rgb(0, 128, 0)", GetStyle(document, "color"));
        Assert.Equal("url(\"https://example.com/css/img.png\")", GetStyle(document, "background-image"));
    }

    [Theory]
    [InlineData("@import 'a.css' print;", "rgb(0, 0, 0)")]
    [InlineData("@import 'a.css' screen;", "rgb(0, 128, 0)")]
    [InlineData("@import 'a.css' supports(display: grid);", "rgb(0, 128, 0)")]
    [InlineData("@import 'a.css' supports(display: nonsense);", "rgb(0, 0, 0)")]
    [InlineData("@import 'a.css' supports(not (display: grid));", "rgb(0, 0, 0)")]
    [InlineData("@import 'a.css' supports(display: grid; x);", "rgb(0, 0, 0)")]
    public void Import_Conditions(string import, string expected)
    {
        var loader = new FileLoader { ["https://example.com/dir/a.css"] = "#target { color: green }" };
        var document = Parse("<style>" + import + "</style><div id=target></div>", loader);
        Assert.Equal(expected, GetStyle(document, "color"));
    }

    [Fact]
    public void Import_UnsupportedCondition_IsNotFetched()
    {
        var loader = new FileLoader { ["https://example.com/dir/a.css"] = "#target { color: green }" };
        var document = Parse("<style>@import 'a.css' supports(display: nonsense);</style>", loader);
        Assert.Empty(loader.Requests);
        Assert.Null(GetImportRule(document).StyleSheet);
    }

    [Theory]
    [InlineData("@import 'a.css' layer(base); div { color: green }", "#target { color: red }", "rgb(0, 128, 0)")]
    [InlineData("@import 'a.css' layer; div { color: green }", "#target { color: red }", "rgb(0, 128, 0)")]
    [InlineData("@import 'a.css'; div { color: red }", "#target { color: green }", "rgb(0, 128, 0)")]
    [InlineData("@layer base, top; @import 'a.css' layer(top); @layer base { #target { color: red } }", "div { color: green }", "rgb(0, 128, 0)")]
    public void Import_Layers(string css, string importedCss, string expected)
    {
        var loader = new FileLoader { ["https://example.com/dir/a.css"] = importedCss };
        var document = Parse("<style>" + css + "</style><div id=target></div>", loader);
        Assert.Equal(expected, GetStyle(document, "color"));
    }

    [Fact]
    public void Import_Cycle_IsNotFetchedAgain()
    {
        var loader = new FileLoader
        {
            ["https://example.com/dir/a.css"] = "@import 'b.css'; #target { color: green }",
            ["https://example.com/dir/b.css"] = "@import 'a.css';",
        };
        var document = Parse("<style>@import 'a.css';</style><div id=target></div>", loader);

        Assert.Equal(["https://example.com/dir/a.css", "https://example.com/dir/b.css"], loader.Requests.Select(r => r.Url));
        Assert.Equal("rgb(0, 128, 0)", GetStyle(document, "color"));
    }

    [Fact]
    public void Import_WithoutLoader_IsNotFetched()
    {
        var document = Parse("<style>@import 'a.css';</style>", loader: null);
        Assert.Null(GetImportRule(document).StyleSheet);
    }

    [Fact]
    public void Import_DataUrl_IsProcessedWithoutLoader()
    {
        var document = Parse("<style>@import url(\"data:text/css,%23target%7Bcolor:green%7D\");</style><div id=target></div>", loader: null);
        Assert.Equal("rgb(0, 128, 0)", GetStyle(document, "color"));
    }

    [Theory]
    [InlineData("<!DOCTYPE html>", "text/plain", "rgb(0, 0, 0)")]
    [InlineData("", "text/plain", "rgb(0, 128, 0)")]
    [InlineData("<!DOCTYPE html>", "text/css; charset=utf-8", "rgb(0, 128, 0)")]
    [InlineData("<!DOCTYPE html>", "oops", "rgb(0, 128, 0)")] // Not a MIME type: sniffed as a style sheet
    public void Import_ContentType(string doctype, string contentType, string expected)
    {
        var loader = new FileLoader { ContentType = contentType, ["https://example.com/dir/a.css"] = "#target { color: green }" };
        var document = Parse(doctype + "<style>@import 'a.css';</style><div id=target></div>", loader);
        Assert.Equal(expected, GetStyle(document, "color"));
    }

    [Theory]
    [InlineData("", "text/plain", null, "rgb(0, 128, 0)")]
    [InlineData("", "text/plain", "nosniff", "rgb(0, 0, 0)")]
    [InlineData("<!DOCTYPE html>", "text/css", "nosniff", "rgb(0, 128, 0)")]
    [InlineData("<!DOCTYPE html>", "text/css;charset=utf-8", "nosniff", "rgb(0, 128, 0)")]
    [InlineData("<!DOCTYPE html>", "oops", "nosniff", "rgb(0, 0, 0)")]
    public void Import_Nosniff(string doctype, string contentType, string? contentTypeOptions, string expected)
    {
        // https://fetch.spec.whatwg.org/#should-response-to-request-be-blocked-due-to-nosniff?: the quirks mode exception does not apply
        var loader = new FileLoader
        {
            ContentType = contentType,
            ExtraHeaders = contentTypeOptions is null ? [] : [new("X-Content-Type-Options", contentTypeOptions)],
            ["https://example.com/dir/a.css"] = "#target { color: green }",
        };
        var document = Parse(doctype + "<style>@import 'a.css';</style><div id=target></div>", loader);
        Assert.Equal(expected, GetStyle(document, "color"));
    }

    [Theory]
    [InlineData(200, "rgb(0, 128, 0)")]
    [InlineData(299, "rgb(0, 128, 0)")]
    [InlineData(404, "rgb(0, 0, 0)")]
    [InlineData(500, "rgb(0, 0, 0)")]
    public void Import_StatusCode(int statusCode, string expected)
    {
        var loader = new FileLoader { StatusCode = statusCode, ["https://example.com/dir/a.css"] = "#target { color: green }" };
        var document = Parse("<!DOCTYPE html><style>@import 'a.css';</style><div id=target></div>", loader);
        Assert.Equal(expected, GetStyle(document, "color"));
    }

    [Fact]
    public void Import_CrossOrigin_RulesCannotBeRead()
    {
        var loader = new FileLoader { ["https://cdn.example.net/a.css"] = "#target { color: green }" };
        var document = Parse("<style>@import 'https://cdn.example.net/a.css';</style><div id=target></div>", loader);

        var imported = GetImportRule(document).StyleSheet!;
        var exception = Assert.Throws<DomException>(() => imported.CssRules);
        Assert.Equal("SecurityError", exception.Name);
        Assert.Throws<DomException>(() => imported.InsertRule("div {}"));

        // The rules apply
        Assert.Equal("rgb(0, 128, 0)", GetStyle(document, "color"));
    }

    [Fact]
    public void Import_Charset()
    {
        var loader = new FileLoader();
        loader.Bytes["https://example.com/dir/a.css"] = [.. "@charset \"windows-1252\"; #target { --x: \""u8, 0xE9, .. "\" }"u8];
        var document = Parse("<style>@import 'a.css';</style><div id=target></div>", loader);
        Assert.Equal("\"é\"", GetStyle(document, "--x"));
    }

    [Fact]
    public void InsertRule_FetchesTheImportedStyleSheet()
    {
        var loader = new FileLoader { ["https://example.com/dir/a.css"] = "#target { color: green }" };
        var document = Parse("<style></style><div id=target></div>", loader);
        var sheet = document.StyleSheets[0];
        sheet.InsertRule("@import 'a.css';");

        Assert.NotNull(GetImportRule(document).StyleSheet);
        Assert.Equal("rgb(0, 128, 0)", GetStyle(document, "color"));
    }

    [Fact]
    public void LoadEvent_IsAnErrorWhenAnImportCannotBeFetched()
    {
        var loader = new FileLoader();
        var document = Parse("", loader);
        var events = new List<string>();
        var style = (HtmlStyleElement)document.CreateElement("style");
        style.TextContent = "@import 'missing.css';";
        style.AddEventListener("load", _ => events.Add("load"));
        style.AddEventListener("error", _ => events.Add("error"));
        document.Head!.AppendChild(style);
        _contexts.EventLoop.RunUntilIdle();

        Assert.Equal(["error"], events);
    }

    [Fact]
    public void LoadEvent_DocumentWithoutBrowsingContextFetchesTheImports()
    {
        // A document created by a window fetches with the environment of the window (https://github.com/whatwg/html/issues/4547)
        var loader = new FileLoader { ["https://example.com/dir/theme.css"] = "#target { color: green }" };
        var document = Parse("", loader).Implementation.CreateHtmlDocument();
        var events = new List<string>();
        var style = (HtmlStyleElement)document.CreateElement("style");
        style.TextContent = "@import 'https://example.com/dir/theme.css';";
        style.AddEventListener("load", _ => events.Add("load"));
        document.Head!.AppendChild(style);
        _contexts.EventLoop.RunUntilIdle();

        Assert.Equal(["load"], events);
        Assert.Equal("https://example.com/dir/theme.css", Assert.Single(loader.Requests).Url);
        Assert.NotNull(((CssImportRule)style.Sheet!.CssRules[0]!).StyleSheet);
    }

    [Fact]
    public async Task LoadEvents_WaitForTheImportedStyleSheets()
    {
        var completion = new TaskCompletionSource<DocumentResource?>();
        var loader = new FileLoader { Pending = completion.Task };
        var context = _contexts.Create(new BrowsingContextOptions { DocumentLoader = loader });
        var eventLoop = _contexts.EventLoop;
        var document = context.Navigate(BaseUrl, _ => "<style>@import 'a.css';</style><div id=target></div>");
        var events = new List<string>();
        document.QuerySelector("style")!.AddEventListener("load", _ => events.Add("style load"));
        context.Window.AddEventListener("load", _ => events.Add("window load"));

        eventLoop.RunUntilIdle();
        Assert.True(eventLoop.HasPendingOperations);
        Assert.Equal(DocumentReadyState.Interactive, document.ReadyState);
        Assert.Empty(events);
        Assert.Equal("rgb(0, 0, 0)", GetStyle(document, "color"));

        completion.SetResult(new DocumentResource("#target { color: green }") { Headers = [new("Content-Type", "text/css")] });
        await eventLoop.RunUntilIdleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(["style load", "window load"], events);
        Assert.Equal(DocumentReadyState.Complete, document.ReadyState);
        Assert.Equal("rgb(0, 128, 0)", GetStyle(document, "color"));
    }

    [Fact]
    public async Task StyleBlockChange_CancelsThePendingImports()
    {
        var completion = new TaskCompletionSource<DocumentResource?>();
        var loader = new FileLoader { Pending = completion.Task };
        var document = Parse("<style>@import 'a.css';</style><div id=target></div>", loader);
        var style = document.QuerySelector("style")!;
        Assert.True(loader.Requests[0].CancellationToken.CanBeCanceled);

        style.TextContent = "#target { color: blue }";
        Assert.True(loader.Requests[0].CancellationToken.IsCancellationRequested);

        completion.SetResult(new DocumentResource("#target { color: red }") { Headers = [new("Content-Type", "text/css")] });
        await _contexts.EventLoop.RunUntilIdleAsync(TestContext.Current.CancellationToken);
        Assert.Equal("rgb(0, 0, 255)", GetStyle(document, "color"));
    }

    private sealed class FileLoader : DocumentLoader
    {
        private readonly Dictionary<string, string> _files = new(StringComparer.Ordinal);

        public List<(string Url, RequestDestination Destination, Element? Initiator, CancellationToken CancellationToken)> Requests { get; } = [];

        public Dictionary<string, byte[]> Bytes { get; } = new(StringComparer.Ordinal);

        public string ContentType { get; init; } = "text/css";

        public int StatusCode { get; init; } = 200;

        public KeyValuePair<string, string>[] ExtraHeaders { get; init; } = [];

        /// <summary>When set, the loader completes asynchronously with this task.</summary>
        public Task<DocumentResource?>? Pending { get; init; }

        public string this[string url]
        {
            get => _files[url];
            set => _files[url] = value;
        }

        public override ValueTask<DocumentResource?> LoadAsync(DocumentRequest request, CancellationToken cancellationToken)
        {
            Requests.Add((request.Url, request.Destination, request.Initiator, cancellationToken));
            if (Pending is not null)
                return new ValueTask<DocumentResource?>(Pending);

            if (Bytes.TryGetValue(request.Url, out var bytes))
                return ValueTask.FromResult<DocumentResource?>(new DocumentResource(bytes) { StatusCode = StatusCode, Headers = [new("Content-Type", ContentType), .. ExtraHeaders] });

            return ValueTask.FromResult(_files.TryGetValue(request.Url, out var content) ? new DocumentResource(content) { StatusCode = StatusCode, Headers = [new("Content-Type", ContentType), .. ExtraHeaders] } : null);
        }
    }
}
