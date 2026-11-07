using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Tests.Scripting;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Dom;

/// <summary>The render-blocking mechanism (https://html.spec.whatwg.org/multipage/dom.html#render-blocking-mechanism).</summary>
public sealed class RenderBlockingTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly FakeTimeProvider _clock = new();
    private readonly RecordingScriptEngine _engine = new();
    private readonly AsyncLoader _loader = new();

    public void Dispose() => _contexts.Dispose();

    private Document Load(string html, bool scripting = true)
    {
        var context = _contexts.Create(new BrowsingContextOptions { ScriptEngine = scripting ? _engine : null, DocumentLoader = _loader, TimeProvider = _clock });
        return context.Navigate("https://example.com/", _ => html);
    }

    /// <summary>Runs the tasks, then the rendering updates of the next frames.</summary>
    private void RunFrames()
    {
        for (var i = 0; i < 3; i++)
        {
            _contexts.EventLoop.RunUntilIdle();
            _clock.Advance(TimeSpan.FromMilliseconds(20));
        }

        _contexts.EventLoop.RunUntilIdle();
    }

    private int CountFrames(Document document)
    {
        var frames = 0;
        document.DefaultView!.RequestAnimationFrame(_ => frames++);
        RunFrames();
        return frames;
    }

    [Fact]
    public void ParserInsertedStyleSheet_BlocksTheRenderingUntilItIsLoaded()
    {
        var document = Load("<!DOCTYPE html><link rel=stylesheet href=style.css><body><p>text</p>");
        Assert.True(document.IsRenderBlocked);

        var frames = 0;
        document.DefaultView!.RequestAnimationFrame(_ => frames++);
        RunFrames();
        Assert.Equal(0, frames);

        _loader.Complete("style.css", "p { color: green }", "text/css");
        RunFrames();
        Assert.False(document.IsRenderBlocked);
        Assert.Equal(1, frames);
    }

    [Fact]
    public void StyleSheetLinkInTheBody_DoesNotBlock()
    {
        var document = Load("<!DOCTYPE html><body><link rel=stylesheet href=style.css blocking=render><p>text</p>");
        Assert.False(document.IsRenderBlocked);
        Assert.Equal(1, CountFrames(document));
    }

    [Fact]
    public void RenderBlockingElementInsertedOnceTheBodyExists_DoesNotBlock()
    {
        var document = Load("<!DOCTYPE html><head></head>");
        Assert.False(document.IsRenderBlocked);

        // The body element exists: nothing can be added to the render-blocking element set
        var link = (HtmlLinkElement)document.CreateElement("link");
        link.SetAttribute("rel", "stylesheet");
        link.SetAttribute("blocking", "render");
        link.SetAttribute("href", "late.css");
        document.Head!.AppendChild(link);
        Assert.False(document.IsRenderBlocked);
        Assert.Equal(1, CountFrames(document));
    }

    [Fact]
    public void RenderBlockingScript_BlocksUntilItRuns_OrIsRemoved_OrLosesItsAttribute()
    {
        var document = Load("<!DOCTYPE html><script async blocking=render src=a.js></script><script async blocking=render src=b.js></script><script async src=c.js></script><body>");
        Assert.True(document.IsRenderBlocked);
        var scripts = document.QuerySelectorAll("script").Cast<Element>().ToList();

        var frames = 0;
        document.DefaultView!.RequestAnimationFrame(_ => frames++);
        RunFrames();
        Assert.Equal(0, frames);

        // The script that is no longer potentially render-blocking, then the script that is removed
        scripts[0].RemoveAttribute("blocking");
        RunFrames();
        Assert.True(document.IsRenderBlocked);
        Assert.Equal(0, frames);

        scripts[1].Remove();
        Assert.False(document.IsRenderBlocked);
        RunFrames();
        Assert.Equal(1, frames);
    }

    [Fact]
    public void ParserBlockingScript_BlocksTheRendering()
    {
        var document = Load("<!DOCTYPE html><script src=a.js></script><body>");
        Assert.True(document.IsRenderBlocked);

        var frames = 0;
        document.DefaultView!.RequestAnimationFrame(_ => frames++);
        RunFrames();
        Assert.Equal(0, frames);

        _loader.Complete("a.js", "a()");
        RunFrames();
        Assert.False(document.IsRenderBlocked);
        Assert.Equal(1, frames);
    }

    [Fact]
    public void ExpectLink_BlocksUntilTheExpectedElementIsParsed()
    {
        // The parser pauses on the script: the expected element is not parsed yet
        var document = Load("<!DOCTYPE html><link rel=expect href=#target blocking=render><body><div id=first></div><script src=a.js></script><div id=target><script src=b.js></script><p></p></div><script src=c.js></script><div id=last></div>");
        Assert.NotNull(document.GetElementById("first"));
        Assert.True(document.IsRenderBlocked);

        var frames = 0;
        document.DefaultView!.RequestAnimationFrame(_ => frames++);
        RunFrames();
        Assert.Equal(0, frames);

        // The expected element is inserted, but it is still on the stack of open elements
        _loader.Complete("a.js", "a()");
        RunFrames();
        Assert.NotNull(document.GetElementById("target"));
        Assert.True(document.IsRenderBlocked);
        Assert.Equal(0, frames);

        // Its end tag is parsed: the rendering is no longer blocked, although the document is not completely parsed
        _loader.Complete("b.js", "b()");
        RunFrames();
        Assert.Null(document.GetElementById("last"));
        Assert.False(document.IsRenderBlocked);
        Assert.Equal(1, frames);
    }

    [Fact]
    public void ExpectLink_StopsBlockingWhenTheDocumentIsParsed()
    {
        var document = Load("<!DOCTYPE html><link rel=expect href=#missing blocking=render><link rel=expect href=#other><link rel=expect href=other.html#target blocking=render><body><p></p>", scripting: false);
        Assert.Equal(1, CountFrames(document));
        Assert.False(document.IsRenderBlocked);
    }

    [Fact]
    public void RenderBlockedDocument_IsStillLaidOutOnDemand()
    {
        var document = Load("<!DOCTYPE html><link rel=stylesheet href=style.css><body><p style='width: 100px; height: 20px'>text</p>");
        Assert.True(document.IsRenderBlocked);

        // The synchronous queries (and a screenshot asked by the host) do not wait for the render-blocking resources
        var rect = document.QuerySelector("p")!.GetBoundingClientRect();
        Assert.Equal(100, rect.Width);
        Assert.Equal(20, rect.Height);
    }

    [Fact]
    public void DocumentsThatAreNotLoadedByANavigation_AreNeverRenderBlocked()
    {
        // A document without browsing context, and the initial about:blank document of a browsing context
        var detached = HtmlDocumentParser.ParseDocument("<!DOCTYPE html><link rel=stylesheet href=style.css><script src=a.js></script>");
        Assert.False(detached.IsRenderBlocked);

        var context = _contexts.Create(new BrowsingContextOptions { ScriptEngine = _engine, DocumentLoader = _loader, TimeProvider = _clock });
        var initial = context.Document;
        initial.Body?.Remove();
        Assert.False(initial.IsRenderBlocked);
        Assert.Equal(1, CountFrames(initial));
    }

    [Fact]
    public void CompletelyLoadedDocument_IsNoLongerRenderBlocked()
    {
        var document = Load("<!DOCTYPE html><body><p>text</p>", scripting: false);
        RunFrames();
        Assert.Equal(DocumentReadyState.Complete, document.ReadyState);

        // A document without body element allows adding render-blocking elements, but not once it is completely loaded
        document.Body!.Remove();
        Assert.False(document.IsRenderBlocked);
        Assert.Equal(1, CountFrames(document));
    }

    /// <summary>A loader whose responses are completed by the test.</summary>
    private sealed class AsyncLoader : DocumentLoader
    {
        private readonly Dictionary<string, TaskCompletionSource<DocumentResource?>> _pending = new(StringComparer.Ordinal);

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

        public override ValueTask<DocumentResource?> LoadAsync(DocumentRequest request, CancellationToken cancellationToken) => new(GetCompletion(request.Url).Task);
    }
}
