using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Tests.Scripting;

/// <summary>https://html.spec.whatwg.org/multipage/scripting.html#prepare-the-script-element (steps 31 and 32) and the pausable parser.</summary>
public sealed class ScriptOrderingTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly RecordingScriptEngine _engine = new();
    private readonly AsyncLoader _loader = new();
    private readonly List<string> _log = [];

    public ScriptOrderingTests()
    {
        foreach (var name in new[] { "a", "b", "c", "d", "inline" })
        {
            _engine.Behaviors[name + "()"] = script =>
            {
                _log.Add(name + " " + ((Window)script.Global).Document.ReadyState.ToString().ToLowerInvariant());
                return null;
            };
        }
    }

    public void Dispose() => _contexts.Dispose();

    private BrowsingContext CreateContext() => _contexts.Create(new BrowsingContextOptions { ScriptEngine = _engine, DocumentLoader = _loader });

    private Document Parse(string html, BrowsingContext? context = null)
    {
        var document = (context ?? CreateContext()).Navigate("https://example.com/", _ => html);
        document.AddEventListener("DOMContentLoaded", _ => _log.Add("DOMContentLoaded"));
        document.DefaultView!.AddEventListener("load", _ => _log.Add("load"));
        return document;
    }

    private void RunUntilIdle() => _contexts.EventLoop.RunUntilIdle();

    [Fact]
    public void ParserBlockingScript_PausesTheParser()
    {
        var document = Parse("<script src=a.js></script><p id=after></p><script>inline()</script>");
        Assert.Equal(DocumentReadyState.Loading, document.ReadyState);
        Assert.Null(document.GetElementById("after"));
        Assert.Same(document, document.BrowsingContext!.Document);

        _loader.Complete("a.js", "a()");
        RunUntilIdle();
        Assert.NotNull(document.GetElementById("after"));
        Assert.Equal(["a loading", "inline loading", "DOMContentLoaded", "load"], _log);
    }

    [Fact]
    public void ParserBlockingScripts_RunInOrder_WhenTheirFetchesCompleteInAnotherOrder()
    {
        Parse("<script src=a.js></script><script src=b.js></script>");
        _loader.Complete("a.js", "a()");
        RunUntilIdle();
        Assert.Equal(["a loading"], _log);

        _loader.Complete("b.js", "b()");
        RunUntilIdle();
        Assert.Equal(["a loading", "b loading", "DOMContentLoaded", "load"], _log);
    }

    [Fact]
    public void DeferredScripts_RunAfterParsingInOrder()
    {
        var document = Parse("<script defer src=a.js></script><script defer src=b.js></script><script>inline()</script>");
        Assert.Equal(["inline loading"], _log);
        Assert.Equal(DocumentReadyState.Interactive, document.ReadyState);

        _loader.Complete("b.js", "b()");
        RunUntilIdle();
        Assert.Equal(["inline loading"], _log);

        _loader.Complete("a.js", "a()");
        RunUntilIdle();
        Assert.Equal(["inline loading", "a interactive", "b interactive", "DOMContentLoaded", "load"], _log);
    }

    [Fact]
    public void AsyncScripts_RunWhenReady_AndDelayTheLoadEvent()
    {
        Parse("<script async src=a.js></script><script async src=b.js></script><script>inline()</script>");
        RunUntilIdle();
        Assert.Equal(["inline loading", "DOMContentLoaded"], _log);

        _loader.Complete("b.js", "b()");
        RunUntilIdle();
        Assert.Equal(["inline loading", "DOMContentLoaded", "b interactive"], _log);

        _loader.Complete("a.js", "a()");
        RunUntilIdle();
        Assert.Equal(["inline loading", "DOMContentLoaded", "b interactive", "a interactive", "load"], _log);
    }

    [Fact]
    public void ScriptInsertedScripts_WithAsyncFalse_RunInInsertionOrder()
    {
        var document = Parse("<body>");
        RunUntilIdle();
        _log.Clear();
        foreach (var name in new[] { "a", "b", "c" })
        {
            var script = (HtmlScriptElement)document.CreateElement("script");
            script.Async = false;
            script.Src = name + ".js";
            document.Body!.AppendChild(script);
        }

        _loader.Complete("c.js", "c()");
        _loader.Complete("b.js", "b()");
        RunUntilIdle();
        Assert.Empty(_log);

        _loader.Complete("a.js", "a()");
        RunUntilIdle();
        Assert.Equal(["a complete", "b complete", "c complete"], _log);
    }

    [Fact]
    public void ScriptInsertedScripts_AreAsyncByDefault()
    {
        var document = Parse("<body>");
        RunUntilIdle();
        _log.Clear();
        foreach (var name in new[] { "a", "b" })
        {
            var script = (HtmlScriptElement)document.CreateElement("script");
            script.Src = name + ".js";
            document.Body!.AppendChild(script);
        }

        _loader.Complete("b.js", "b()");
        _loader.Complete("a.js", "a()");
        RunUntilIdle();
        Assert.Equal(["b complete", "a complete"], _log);
    }

    [Fact]
    public void SynchronousLoader_DoesNotPauseTheParser()
    {
        _loader.Synchronous["a.js"] = "a()";
        _loader.Synchronous["b.js"] = "b()";
        var document = Parse("<script src=a.js></script><script async src=b.js></script><p id=after></p>");
        Assert.NotNull(document.GetElementById("after"));
        Assert.Equal(["a loading"], _log);

        // The async script is marked as ready by a task
        RunUntilIdle();
        Assert.Equal(["a loading", "b interactive", "DOMContentLoaded", "load"], _log);
    }

    [Fact]
    public async Task NavigateAsync_WaitsForTheParser()
    {
        var context = CreateContext();
        var task = context.NavigateAsync("https://example.com/", _ => "<script src=a.js></script><p id=after></p>", TestContext.Current.CancellationToken);
        Assert.False(task.IsCompleted);

        _loader.Complete("a.js", "a()");
        var document = await task;
        Assert.NotNull(document.GetElementById("after"));
        Assert.Equal(["a loading"], _log);
    }

    [Fact]
    public void XmlParser_PausesAtParserBlockingScripts()
    {
        var context = CreateContext();
        var document = context.Navigate("https://example.com/", _ => new DocumentResource("<html xmlns='http://www.w3.org/1999/xhtml'><script src='a.js'/><p id='after'/></html>") { Headers = [new("Content-Type", "application/xhtml+xml")] });
        Assert.Null(document.GetElementById("after"));

        _loader.Complete("a.js", "a()");
        RunUntilIdle();
        Assert.NotNull(document.GetElementById("after"));
        Assert.Equal(["a loading"], _log);
        Assert.Equal(DocumentReadyState.Complete, document.ReadyState);
    }

    [Fact]
    public void XmlParser_XmlStyleSheetBlocksScripts()
    {
        var context = CreateContext();
        var document = context.Navigate("https://example.com/", _ => new DocumentResource("<?xml-stylesheet href='style.css' type='text/css'?><html xmlns='http://www.w3.org/1999/xhtml'><script>inline()</script><p id='after'/></html>") { Headers = [new("Content-Type", "application/xhtml+xml")] });
        var processingInstruction = (ProcessingInstruction)document.FirstChild!;
        Assert.Null(processingInstruction.Sheet);
        Assert.Null(document.GetElementById("after"));
        Assert.Empty(_log);

        _loader.Complete("style.css", "p { z-index: 3 }", "text/css");
        RunUntilIdle();
        Assert.Equal(["inline loading"], _log);
        Assert.Equal(DocumentReadyState.Complete, document.ReadyState);
        var sheet = processingInstruction.Sheet!;
        Assert.Equal("https://example.com/style.css", sheet.Href);
        Assert.Same(processingInstruction, sheet.OwnerNode);
        Assert.Same(sheet, Assert.Single(document.StyleSheets));
        Assert.Equal("3", document.DefaultView!.GetComputedStyle(document.GetElementById("after")!).ZIndex);

        // The style sheet is removed when the node is no longer part of the prolog
        document.AppendChild(processingInstruction);
        Assert.Null(processingInstruction.Sheet);
        Assert.Null(sheet.OwnerNode);
        Assert.Empty(document.StyleSheets);
    }

    [Fact]
    public void DeferredScript_WaitsForTheStyleSheetsThatAreBlockingScripts()
    {
        _loader.Synchronous["a.js"] = "a()";
        var document = Parse("<link rel=stylesheet href=style.css><script defer src=a.js></script>");
        RunUntilIdle();
        Assert.Empty(_log);

        _loader.Complete("style.css", "p { z-index: 3 }", "text/css");
        RunUntilIdle();
        Assert.Equal("a interactive", _log[0]);
        Assert.Equal(DocumentReadyState.Complete, document.ReadyState);
    }

    [Fact]
    public void XmlParser_LinkStyleSheetBlocksScripts()
    {
        var context = CreateContext();
        var document = context.Navigate("https://example.com/", _ => new DocumentResource("<html xmlns='http://www.w3.org/1999/xhtml'><head><link rel='stylesheet' href='style.css'/></head><script>inline()</script><p id='after'/></html>") { Headers = [new("Content-Type", "application/xhtml+xml")] });
        Assert.Null(document.GetElementById("after"));
        Assert.Empty(_log);

        _loader.Complete("style.css", "p { z-index: 3 }", "text/css");
        RunUntilIdle();
        Assert.Equal(["inline loading"], _log);
        Assert.Equal("3", document.DefaultView!.GetComputedStyle(document.GetElementById("after")!).ZIndex);
    }

    [Fact]
    public void RemovedIframe_AbortsItsParser()
    {
        _loader.Synchronous["frame.html"] = "<script src=a.js></script><p>";
        var document = Parse("<iframe src=frame.html></iframe>");
        RunUntilIdle();
        var iframe = (HtmlIFrameElement)document.QuerySelector("iframe")!;
        var frameDocument = iframe.ContentDocument!;
        Assert.Equal(DocumentReadyState.Loading, frameDocument.ReadyState);

        iframe.Remove();
        _loader.Complete("a.js", "a()");
        RunUntilIdle();
        Assert.DoesNotContain(_log, l => l.StartsWith('a', StringComparison.Ordinal));
        Assert.Null(frameDocument.QuerySelector("p"));
    }

    /// <summary>A loader whose script responses are completed by the test.</summary>
    private sealed class AsyncLoader : DocumentLoader
    {
        private readonly Dictionary<string, TaskCompletionSource<DocumentResource?>> _pending = new(StringComparer.Ordinal);

        public Dictionary<string, string> Synchronous { get; } = new(StringComparer.Ordinal);

        public void Complete(string name, string content, string contentType = "text/javascript") => GetCompletion("https://example.com/" + name).SetResult(new DocumentResource(content) { Headers = [new("Content-Type", contentType)] });

        private TaskCompletionSource<DocumentResource?> GetCompletion(string url)
        {
            if (!_pending.TryGetValue(url, out var completion))
            {
                completion = new TaskCompletionSource<DocumentResource?>();
                _pending[url] = completion;
            }

            return completion;
        }

        public override ValueTask<DocumentResource?> LoadAsync(DocumentRequest request, CancellationToken cancellationToken)
        {
            if (Synchronous.TryGetValue(request.Url["https://example.com/".Length..], out var content))
                return ValueTask.FromResult<DocumentResource?>(new DocumentResource(content) { Headers = [new("Content-Type", request.Destination == RequestDestination.Script ? "text/javascript" : "text/html")] });

            return new ValueTask<DocumentResource?>(GetCompletion(request.Url).Task);
        }
    }
}
