using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.Tests.Scripting;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.JavaScript;

/// <summary>The joint session history (https://html.spec.whatwg.org/multipage/browsing-the-web.html#session-history-infrastructure) and the unloading of the documents.</summary>
public sealed class SessionHistoryTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly List<ScriptError> _errors = [];
    private readonly RecordingLoader _loader = new();
    private readonly ScriptEngine _engine = TestScriptEngines.Create();

    public void Dispose()
    {
        _contexts.Dispose();
        TestScriptEngines.Dispose(_engine);
    }

    private Document Load(string html)
    {
        var context = _contexts.Create(new BrowsingContextOptions
        {
            ScriptEngine = _engine,
            DocumentLoader = _loader,
            UnhandledScriptError = (_, error) => _errors.Add(error),
        });

        var document = context.Navigate("https://example.com/", _ => html);
        for (var i = 0; i < 5; i++)
        {
            _contexts.EventLoop.RunUntilIdle();
            Thread.Sleep(10);
        }

        Assert.Empty(_errors);
        return document;
    }

    [Fact]
    public void NavigationOfAnIframe_AddsAnEntryToTheJointSessionHistory()
    {
        _loader.Resources["https://example.com/a.html"] = new DocumentResource("a");
        _loader.Resources["https://example.com/b.html"] = new DocumentResource("b");
        var document = Load("""
            <iframe src="a.html"></iframe>
            <script>
              const log = [history.length];
              window.onload = () => {
                log.push(history.length);
                const frame = document.querySelector('iframe');
                frame.onload = () => {
                  log.push(history.length, frames[0].history.length);
                  frame.onload = () => { log.push(frames[0].location.pathname, history.length); document.title = log.join(); };
                  history.back();
                };
                setTimeout(() => frame.src = 'b.html', 0);
              };
            </script>
            """);

        // The initial navigation of the iframe replaces its initial about:blank entry; back() traverses the iframe
        Assert.Equal("1,1,2,2,/a.html,2", document.Title);
    }

    [Fact]
    public void FragmentNavigationOfAnIframe_BackFiresPopstateInTheIframe()
    {
        _loader.Resources["https://example.com/a.html"] = new DocumentResource("<script>onpopstate = () => parent.document.title = 'popstate:' + location.hash;</script>");
        var document = Load("""
            <iframe src="a.html"></iframe>
            <script>
              window.onload = () => {
                frames[0].location.hash = 'x';
                setTimeout(() => history.back(), 0);
              };
            </script>
            """);

        Assert.Equal("popstate:", document.Title);
    }

    [Fact]
    public void BeforeUnloadAndUnload_AreFiredBeforeTheNewDocument()
    {
        _loader.Resources["https://example.com/a.html"] = new DocumentResource("""
            <script>
              onbeforeunload = () => parent.log.push('beforeunload');
              onpagehide = () => parent.log.push('pagehide ' + document.visibilityState + ' ' + (parent !== null));
              onunload = () => parent.log.push('unload ' + document.visibilityState);
            </script>
            """);
        _loader.Resources["https://example.com/b.html"] = new DocumentResource("<script>parent.log.push('b'); parent.document.title = parent.log.join();</script>");
        var document = Load("""
            <iframe src="a.html"></iframe>
            <script>
              var log = [];
              window.onload = () => {
                frames[0].location.href = 'b.html';
                log.push('navigated');
              };
            </script>
            """);

        Assert.Equal("beforeunload,navigated,pagehide visible true,unload hidden,b", document.Title);
    }

    [Fact]
    public void RemovalOfAnIframe_UnloadsItsDocumentBeforeTheRemoval()
    {
        _loader.Resources["https://example.com/a.html"] = new DocumentResource("""
            <iframe srcdoc="<script>onpagehide = () => parent.parent.log.push('nested pagehide');</script>"></iframe>
            <script>
              onpagehide = () => parent.log.push('pagehide ' + (parent.frame.parentNode !== null));
              onunload = () => parent.log.push('unload');
            </script>
            """);
        var document = Load("""
            <div><iframe src="a.html"></iframe></div>
            <script>
              var log = [];
              var frame = document.querySelector('iframe');
              window.onload = () => {
                frame.parentNode.remove();
                log.push(String(frame.contentWindow));
                document.title = log.join();
              };
            </script>
            """);

        // The documents are unloaded once, while the iframe is still in the document (the initial about:blank documents are not unloaded)
        Assert.Equal("nested pagehide,pagehide true,unload,null", document.Title);
    }

    [Fact]
    public void RemovalOfAnIframe_TheUnloadEventHandlersCanMoveTheRemovedNode()
    {
        var document = Load("""
            <div id=a></div><p id=b><iframe srcdoc="b"></iframe></p>
            <script>
              var a = document.getElementById('a');
              var b = document.getElementById('b');
              window.onload = () => {
                frames[0].onpagehide = () => b.appendChild(a);
                a.appendChild(b);
                document.title = [a.parentNode.id, String(b.parentNode), a.contains(b)].join();
              };
            </script>
            """);

        // The handler moves the new parent into the node: the node is removed but not inserted, so the tree has no cycle
        Assert.Equal("b,null,false", document.Title);
    }

    [Fact]
    public void SandboxedIframe_CannotNavigateItsParent()
    {
        var document = Load("""
            <iframe sandbox="allow-scripts allow-same-origin" srcdoc="<script>try { parent.location.href = 'https://example.com/other'; parent.document.title = 'navigated'; } catch (e) { parent.document.title = e.name; }</script>"></iframe>
            """);

        Assert.Equal("SecurityError", document.Title);
    }

    [Fact]
    public void SandboxedIframe_CannotNavigateAnAncestorThatIsNotTopLevel()
    {
        // The nested iframe inherits the sandboxing flags of its parent, which is not a top-level traversable
        var document = Load("""
            <iframe sandbox="allow-scripts allow-same-origin" srcdoc="<iframe srcdoc=&quot;<script>try { parent.location.href = 'https://example.com/other'; top.document.title = 'navigated'; } catch (e) { top.document.title = e.name; }</script>&quot;></iframe>"></iframe>
            """);

        Assert.Equal("SecurityError", document.Title);
    }

    [Fact]
    public void PushState_StructuredSerializesTheState()
    {
        var document = Load("""
            <script>
              const log = [];
              const state = { n: 1, re: /a/g };
              state.re.lastIndex = 1;
              history.pushState(state, '');
              log.push(history.state !== state, history.state === history.state, history.state.n, history.state.re.lastIndex);
              try {
                history.pushState({ f() {} }, '', '#f');
              } catch (e) {
                log.push(e.name, location.hash === '');
              }

              document.title = log.join();
            </script>
            """);

        // The state is a clone (a RegExp is cloned without its lastIndex), and the serialization fails before the URL is updated
        Assert.Equal("true,true,1,0,DataCloneError,true", document.Title);
    }

    [Fact]
    public void Traversal_RestoresTheStateOfTheEntryInTheDocumentLoadedAgain()
    {
        _loader.Resources["https://example.com/a.html"] = new DocumentResource("a");
        _loader.Resources["https://example.com/a.html?pushed"] = new DocumentResource("a");
        _loader.Resources["https://example.com/b.html"] = new DocumentResource("b");
        var document = Load("""
            <iframe src="a.html"></iframe>
            <script>
              window.onload = () => {
                const frame = document.querySelector('iframe');
                frames[0].history.pushState({ n: 1 }, '', 'a.html?pushed');
                frame.onload = () => {
                  frame.onload = () => document.title = [frames[0].location.search, frames[0].history.state.n].join();
                  history.back();
                };
                setTimeout(() => frame.src = 'b.html', 0);
              };
            </script>
            """);

        Assert.Equal("?pushed,1", document.Title);
    }

    [Fact]
    public void HistoryGo_IsIgnoredWhileAPreviousTraversalIsLoadingTheTopLevelDocument()
    {
        // Each page navigates to the next one until c.html, which goes back twice in the same task (window.name stops the chain)
        _loader.Resources["https://example.com/b.html"] = new DocumentResource("<script>onload = () => setTimeout(() => { if (window.name !== 'done') location.href = 'c.html'; }, 0);</script>");
        _loader.Resources["https://example.com/c.html"] = new DocumentResource("<script>onload = () => setTimeout(() => { window.name = 'done'; history.back(); history.back(); }, 0);</script>");
        _loader.Resources["https://example.com/"] = new DocumentResource("<script>onload = () => setTimeout(() => { if (window.name !== 'done') location.href = 'b.html'; }, 0);</script>");
        var context = _contexts.Create(new BrowsingContextOptions
        {
            ScriptEngine = _engine,
            DocumentLoader = _loader,
            UnhandledScriptError = (_, error) => _errors.Add(error),
        });

        context.Navigate("https://example.com/");
        for (var i = 0; i < 10; i++)
        {
            _contexts.EventLoop.RunUntilIdle();
            Thread.Sleep(10);
        }

        // As in Chromium and Firefox, the second traversal is not applied from the step of the first one
        Assert.Empty(_errors);
        Assert.Equal("https://example.com/b.html", context.Document.Url);
    }

    [Fact]
    public void PushState_IsRateLimited()
    {
        var document = Load("""
            <script>
              let updates = 0;
              for (let i = 0; i < 300; i++) {
                const previousUrl = location.href;
                history.pushState(null, '', '#' + i);
                if (location.href !== previousUrl) {
                  updates++;
                }
              }

              document.title = updates;
            </script>
            """);

        Assert.Equal("200", document.Title);
    }

    [Fact]
    public void LocationHash_SettingTheEmptyStringWithoutFragmentDoesNotNavigate()
    {
        var document = Load("""
            <script>
              const length = history.length;
              location.hash = '';
              document.title = [location.href, history.length - length].join();
            </script>
            """);

        Assert.Equal("https://example.com/,0", document.Title);
    }

    [Fact]
    public void AncestorOrigins_AreMaskedByTheReferrerPolicyOfTheIframe()
    {
        // The child of the "no-referrer" iframe masks the origin of its parent; the grandchild sees the origin of the child,
        // and the masked origin of the top-level document
        var document = Load("""
            <iframe referrerpolicy="no-referrer" srcdoc="<script>top.log.push([...location.ancestorOrigins].join(' '))</script><iframe srcdoc=&quot;<script>top.log.push([...location.ancestorOrigins].join(' ')); top.document.title = top.log.join();</script>&quot;></iframe>"></iframe>
            <script>var log = [];</script>
            """);

        Assert.Equal("null,https://example.com null", document.Title);
    }

    private BrowsingContext CreateBackForwardCacheContext(int backForwardCacheSize = 6, TimeProvider? timeProvider = null)
    {
        return _contexts.Create(new BrowsingContextOptions
        {
            ScriptEngine = _engine,
            DocumentLoader = _loader,
            UnhandledScriptError = (_, error) => _errors.Add(error),
            BackForwardCacheSize = backForwardCacheSize,
            TimeProvider = timeProvider,
        });
    }

    private void RunEventLoop()
    {
        for (var i = 0; i < 5; i++)
        {
            _contexts.EventLoop.RunUntilIdle();
            Thread.Sleep(10);
        }
    }

    private const string PageTransitionLogger = """
        <script>
          var log = [];
          addEventListener('pageshow', e => { log.push('pageshow ' + e.persisted); document.title = log.join(); });
          addEventListener('pagehide', e => { log.push('pagehide ' + e.persisted); document.title = log.join(); });
        </script>
        """;

    [Fact]
    public void BackForwardCache_TraversalRestoresTheDocument()
    {
        var context = CreateBackForwardCacheContext();
        var a = context.Navigate("https://example.com/a.html", _ => PageTransitionLogger + "<script>window.state = 'kept'; addEventListener('pageshow', e => { if (e.persisted) document.title += ',' + window.state; });</script>");
        RunEventLoop();
        var b = context.Navigate("https://example.com/b.html", _ => "b");
        RunEventLoop();
        Assert.Equal(1, context.BackForwardCacheCount);

        context.Window.History.Back();
        RunEventLoop();

        // The document is not loaded again: the pagehide and pageshow events are persisted, and the unload event is not fired
        Assert.Empty(_errors);
        Assert.Same(a, context.Document);
        Assert.Equal("pageshow false,pagehide true,pageshow true,kept", a.Title);
        Assert.Empty(_loader.Requests);

        // The document of the forward entry is cached in turn
        Assert.Equal(1, context.BackForwardCacheCount);
        context.Window.History.Forward();
        RunEventLoop();
        Assert.Same(b, context.Document);
    }

    [Fact]
    public void BackForwardCache_TasksAreDeferredUntilTheDocumentIsRestored()
    {
        var context = CreateBackForwardCacheContext();
        var a = context.Navigate("https://example.com/a.html", _ => PageTransitionLogger + """
            <script>
              addEventListener('pagehide', e => setTimeout(() => { log.push('timeout'); document.title = log.join(); }, 0));
            </script>
            """);
        RunEventLoop();
        context.Navigate("https://example.com/b.html", _ => "b");
        RunEventLoop();
        Assert.Equal("pageshow false,pagehide true", a.Title);

        context.Window.History.Back();
        RunEventLoop();

        Assert.Empty(_errors);
        Assert.Equal("pageshow false,pagehide true,pageshow true,timeout", a.Title);
    }

    [Fact]
    public void BackForwardCache_TimersAreSuspended()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var context = CreateBackForwardCacheContext(timeProvider: clock);
        var a = context.Navigate("https://example.com/a.html", _ => "<script>setTimeout(() => document.title = 'fired', 1000);</script>");
        context.EventLoop.RunUntilIdle();
        context.Navigate("https://example.com/b.html", _ => "b");
        context.EventLoop.RunUntilIdle();

        // The time spent in the back/forward cache does not count
        clock.Advance(TimeSpan.FromSeconds(5));
        context.EventLoop.RunUntilIdle();
        context.Window.History.Back();
        context.EventLoop.RunUntilIdle();
        Assert.Same(a, context.Document);
        clock.Advance(TimeSpan.FromMilliseconds(900));
        context.EventLoop.RunUntilIdle();
        Assert.Equal("", a.Title);

        clock.Advance(TimeSpan.FromMilliseconds(200));
        context.EventLoop.RunUntilIdle();
        Assert.Empty(_errors);
        Assert.Equal("fired", a.Title);
    }

    [Theory]
    [InlineData("<script>addEventListener('unload', () => {});</script>", null)]
    [InlineData("", "no-store")]
    [InlineData("", "max-age=0, No-Store")]
    public void BackForwardCache_IneligibleDocumentIsLoadedAgain(string script, string? cacheControl)
    {
        var html = PageTransitionLogger + script;
        _loader.Resources["https://example.com/a.html"] = new DocumentResource(html);
        var context = CreateBackForwardCacheContext();
        var a = context.Navigate("https://example.com/a.html", _ => new DocumentResource(html) { Headers = cacheControl is null ? [] : [new("Cache-Control", cacheControl)] });
        RunEventLoop();
        context.Navigate("https://example.com/b.html", _ => "b");
        RunEventLoop();
        Assert.Equal(0, context.BackForwardCacheCount);

        context.Window.History.Back();
        RunEventLoop();

        Assert.Empty(_errors);
        Assert.Equal("pageshow false,pagehide false", a.Title);
        Assert.NotSame(a, context.Document);
        Assert.Equal("https://example.com/a.html", context.Document.Url);
        Assert.Equal("pageshow false", context.Document.Title);
    }

    [Fact]
    public void BackForwardCache_SizeZeroDisablesTheCache()
    {
        _loader.Resources["https://example.com/a.html"] = new DocumentResource(PageTransitionLogger);
        var context = CreateBackForwardCacheContext(backForwardCacheSize: 0);
        var a = context.Navigate("https://example.com/a.html", _ => PageTransitionLogger);
        RunEventLoop();
        context.Navigate("https://example.com/b.html", _ => "b");
        RunEventLoop();
        context.Window.History.Back();
        RunEventLoop();

        Assert.Empty(_errors);
        Assert.Equal("pageshow false,pagehide false", a.Title);
        Assert.NotSame(a, context.Document);
    }

    [Fact]
    public void BackForwardCache_DocumentsAreEvicted()
    {
        _loader.Resources["https://example.com/a.html"] = new DocumentResource("a");
        var context = CreateBackForwardCacheContext(backForwardCacheSize: 1);
        var a = context.Navigate("https://example.com/a.html", _ => "a");
        context.Navigate("https://example.com/b.html", _ => "b");
        RunEventLoop();
        var c = context.Navigate("https://example.com/c.html", _ => "c");
        RunEventLoop();

        // The cache is full: the document cached first is evicted
        Assert.Equal(1, context.BackForwardCacheCount);
        Assert.Null(a.BrowsingContext);

        // The navigation from b.html clears the forward entry of c.html: its document is evicted
        context.Window.History.Back();
        RunEventLoop();
        Assert.Equal("https://example.com/b.html", context.Document.Url);
        context.Navigate("https://example.com/d.html", _ => "d");
        RunEventLoop();
        Assert.Null(c.BrowsingContext);
        Assert.Equal(1, context.BackForwardCacheCount);

        // The traversal to the entry of an evicted document loads it again
        context.Window.History.Go(-2);
        RunEventLoop();
        Assert.Empty(_errors);
        Assert.Equal("https://example.com/a.html", context.Document.Url);
        Assert.NotSame(a, context.Document);
        Assert.Equal(["https://example.com/a.html"], _loader.Requests.ConvertAll(request => request.Url));
    }
}
