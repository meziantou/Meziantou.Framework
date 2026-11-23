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

    private BrowsingContext CreateBackForwardCacheContext(int backForwardCacheSize = 6, TimeProvider? timeProvider = null, UserAgentUIBackend? userAgentUI = null)
    {
        return _contexts.Create(new BrowsingContextOptions
        {
            ScriptEngine = _engine,
            DocumentLoader = _loader,
            UnhandledScriptError = (_, error) => _errors.Add(error),
            BackForwardCacheSize = backForwardCacheSize,
            TimeProvider = timeProvider,
            UserAgentUI = userAgentUI,
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
    public void BackForwardCache_ClosesThePromptsOfTheUserAgent()
    {
        // The wallet of a digital credential request is closed when the page enters the back/forward cache: the request is
        // not answered after the page is restored
        var wallet = new PendingWallet();
        var context = CreateBackForwardCacheContext(userAgentUI: wallet);
        var a = context.Navigate("https://example.com/a.html", _ => PageTransitionLogger + """
            <script>
              window.request = () => navigator.credentials.get({ digital: { requests: [{ protocol: 'org-iso-mdoc', data: {} }] } })
                .then(c => log.push('credential ' + c.protocol), e => log.push(e.name));
            </script>
            """);
        RunEventLoop();
        Window.NotifyActivation(a);
        TestScriptEngines.Evaluate(_engine, a.DefaultView!, "request()");
        RunEventLoop();
        Assert.True(wallet.CancellationToken.CanBeCanceled);
        Assert.False(wallet.CancellationToken.IsCancellationRequested);

        context.Navigate("https://example.com/b.html", _ => "b");
        RunEventLoop();
        Assert.Equal(1, context.BackForwardCacheCount);
        Assert.True(wallet.CancellationToken.IsCancellationRequested);

        wallet.Answer.SetResult(new DigitalCredentialResponse("org-iso-mdoc", "{}"));
        context.Window.History.Back();
        RunEventLoop();

        Assert.Empty(_errors);
        Assert.Same(a, context.Document);
        Assert.Equal("pageshow false,pagehide true,AbortError,pageshow true", TestScriptEngines.Evaluate(_engine, a.DefaultView!, "log.join()"));
    }

    private sealed class PendingWallet : UserAgentUIBackend
    {
        public TaskCompletionSource<DigitalCredentialResponse?> Answer { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CancellationToken CancellationToken { get; private set; }

        public override ValueTask<DigitalCredentialResponse?> RequestDigitalCredentialAsync(DigitalCredentialPrompt prompt, CancellationToken cancellationToken)
        {
            CancellationToken = cancellationToken;
            return new(Answer.Task);
        }
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

    // Records the user activation of the window when its first script runs: [transient, sticky]
    private const string UserActivationRecorder = "<script>var firstScript = [navigator.userActivation.isActive, navigator.userActivation.hasBeenActive].join();</script>";

    [Theory]
    [InlineData("location.href = 'b.html'", "https://example.com/b.html", "false,true")]
    [InlineData("location.replace('b.html')", "https://example.com/b.html", "false,true")]
    [InlineData("location.reload()", "https://example.com/a.html", "false,true")]
    [InlineData("location.href = 'same-origin-redirect.html'", "https://example.com/b.html", "false,true")]
    [InlineData("location.href = 'https://other.example/b.html'", "https://other.example/b.html", "false,false")]
    [InlineData("location.href = 'https://other.example/redirect.html'", "https://example.com/b.html", "false,false")]
    public void StickyActivation_IsCarriedOverBySameOriginNavigations(string navigation, string expectedUrl, string expectedActivation)
    {
        _loader.Resources["https://example.com/a.html"] = new DocumentResource(UserActivationRecorder);
        _loader.Resources["https://example.com/b.html"] = new DocumentResource(UserActivationRecorder);
        _loader.Resources["https://other.example/b.html"] = new DocumentResource(UserActivationRecorder);
        _loader.Resources["https://example.com/same-origin-redirect.html"] = new DocumentResource("") { StatusCode = 302, Headers = [new("Location", "https://example.com/b.html")] };
        _loader.Resources["https://other.example/redirect.html"] = new DocumentResource("") { StatusCode = 302, Headers = [new("Location", "https://example.com/b.html")] };
        var context = CreateBackForwardCacheContext(backForwardCacheSize: 0);
        var a = context.Navigate("https://example.com/a.html", _ => UserActivationRecorder);
        RunEventLoop();
        Window.NotifyActivation(a);
        Assert.Equal("true", TestScriptEngines.Evaluate(_engine, context.Window, "navigator.userActivation.hasBeenActive"));

        TestScriptEngines.Evaluate(_engine, context.Window, navigation);
        RunEventLoop();

        // The transient activation is not carried over. A cross-origin document cannot give the activation by redirecting to
        // a same-origin document.
        Assert.Empty(_errors);
        Assert.NotSame(a, context.Document);
        Assert.Equal(expectedUrl, context.Document.Url);
        Assert.Equal(expectedActivation, TestScriptEngines.Evaluate(_engine, context.Window, "firstScript"));
    }

    [Fact]
    public void StickyActivation_IsNotCarriedOverByANavigationWithoutInitiator()
    {
        var context = CreateBackForwardCacheContext(backForwardCacheSize: 0);
        var a = context.Navigate("https://example.com/a.html", _ => UserActivationRecorder);
        RunEventLoop();
        Window.NotifyActivation(a);

        // The application navigates the browsing context, as the user of a browser does with its user interface
        context.Navigate("https://example.com/b.html", _ => UserActivationRecorder);
        RunEventLoop();

        Assert.Empty(_errors);
        Assert.Equal("false,false", TestScriptEngines.Evaluate(_engine, context.Window, "firstScript"));
    }

    [Theory]
    [InlineData(true, "false false,true true")]
    [InlineData(false, "false false,true false")]
    public void StickyActivation_IsCarriedOverToADocumentRestoredFromTheBackForwardCache(bool initiatedByADocument, string expectedPageShows)
    {
        const string Page = UserActivationRecorder + """
            <script>
              var pageShows = [];
              addEventListener('pageshow', e => pageShows.push(e.persisted + ' ' + navigator.userActivation.hasBeenActive));
            </script>
            """;
        _loader.Resources["https://example.com/a.html"] = new DocumentResource(Page);
        _loader.Resources["https://example.com/b.html"] = new DocumentResource("b");
        var context = CreateBackForwardCacheContext();
        if (initiatedByADocument)
        {
            context.Navigate("https://example.com/", _ => "start");
            RunEventLoop();
            TestScriptEngines.Evaluate(_engine, context.Window, "location.href = 'a.html'");
        }
        else
        {
            context.Navigate("https://example.com/a.html", _ => Page);
        }

        RunEventLoop();
        var a = context.Document;
        Assert.Equal("https://example.com/a.html", a.Url);
        TestScriptEngines.Evaluate(_engine, context.Window, "location.href = 'b.html'");
        RunEventLoop();
        var b = context.Document;
        Assert.Equal("https://example.com/b.html", b.Url);
        Window.NotifyActivation(b);

        context.Window.History.Back();
        RunEventLoop();

        // The activation is set before the pageshow event of the reactivation. It is not carried over to the entry of a
        // navigation that no document initiated (its initiator origin is null).
        Assert.Empty(_errors);
        Assert.Same(a, context.Document);
        Assert.Equal("false,false", TestScriptEngines.Evaluate(_engine, context.Window, "firstScript"));
        Assert.Equal(expectedPageShows, TestScriptEngines.Evaluate(_engine, context.Window, "pageShows.join()"));
        Assert.Equal("false", TestScriptEngines.Evaluate(_engine, context.Window, "navigator.userActivation.isActive"));
    }
}
