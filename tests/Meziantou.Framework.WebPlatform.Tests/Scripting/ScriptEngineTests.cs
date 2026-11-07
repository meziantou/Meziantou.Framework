using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.Framework.WebPlatform.Tests.Scripting;

/// <summary>The calls of the library to the <see cref="ScriptEngine"/> of the browsing contexts.</summary>
public sealed class ScriptEngineTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly RecordingScriptEngine _engine = new();
    private readonly RecordingLoader _loader = new();
    private readonly List<ScriptError> _unhandledErrors = [];

    public void Dispose() => _contexts.Dispose();

    private BrowsingContext CreateContext() => _contexts.Create(new BrowsingContextOptions
    {
        ScriptEngine = _engine,
        DocumentLoader = _loader,
        UnhandledScriptError = (_, error) => _unhandledErrors.Add(error),
    });

    private Document Load(string html, BrowsingContext? context = null)
    {
        var document = (context ?? CreateContext()).Navigate("https://example.com/page.html", _ => html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    [Fact]
    public void Realms_AreCreatedForTheNewWindows()
    {
        var context = CreateContext();
        Assert.Equal(["CreateRealm about:blank", "DocumentActivated about:blank"], _engine.Calls);

        _engine.Calls.Clear();
        _loader.Resources["https://example.com/frame.html"] = new DocumentResource("<p>");
        Load("<script>a()</script><iframe src=frame.html></iframe>", context);
        Assert.Equal([
            "CreateRealm https://example.com/page.html",
            "DocumentActivated https://example.com/page.html",
            "Checkpoint",
            "Execute a()",
            "Checkpoint",
            "CreateRealm about:blank",
            "DocumentActivated about:blank",
            "ChildNavigablesChanged 1",
        ], _engine.Calls.Take(8));

        // The window of the initial about:blank document of the iframe is not reused by the cross-origin document
        Assert.Contains("DocumentActivated https://example.com/frame.html", _engine.Calls);

        _engine.Calls.Clear();
        context.Document.QuerySelector("iframe")!.Remove();
        Assert.Equal(["DestroyRealm https://example.com/frame.html", "ChildNavigablesChanged 0"], _engine.Calls);
    }

    [Fact]
    public void ScriptingIsEnabledByTheEngine()
    {
        Assert.True(new BrowsingContextOptions { ScriptEngine = _engine }.ScriptingEnabled);
        Assert.False(new BrowsingContextOptions { ScriptEngine = _engine, ScriptingEnabled = false }.ScriptingEnabled);
        Assert.False(new BrowsingContextOptions().ScriptingEnabled);

        var document = Load("<noscript><b></b></noscript>");
        Assert.Null(document.QuerySelector("b"));
    }

    [Fact]
    public void EventHandlerContentAttributes_AreCompiledLazily()
    {
        var calls = new List<string>();
        _engine.HandlerBehaviors["click()"] = (thisValue, arguments) =>
        {
            calls.Add(((Element)thisValue).Id + " " + ((Event)arguments[0]!).Type);
            return null;
        };
        var document = Load("<form id=f><button id=b onclick='click()'></button></form>");
        Assert.DoesNotContain(_engine.Calls, c => c.StartsWith("Compile", StringComparison.Ordinal));

        var button = (HtmlButtonElement)document.GetElementById("b")!;
        button.DispatchEvent(new Event("click"));
        button.DispatchEvent(new Event("click"));
        Assert.Equal(["b click", "b click"], calls);
        Assert.Single(_engine.Calls, c => c == "Compile onclick click()");
    }

    [Fact]
    public void LegacyPrefixedEventHandlers_ListenToTheCamelCaseEventType()
    {
        var calls = new List<string>();
        _engine.HandlerBehaviors["end()"] = (thisValue, arguments) =>
        {
            calls.Add(((Event)arguments[0]!).Type);
            return null;
        };
        var document = Load("<div id=a onwebkitanimationend='end()'></div><math><mrow id=b></mrow></math>");

        // https://html.spec.whatwg.org/multipage/webappapis.html#globaleventhandlers: the event type of onwebkitanimationend is webkitAnimationEnd
        var div = document.GetElementById("a")!;
        div.DispatchEvent(new Event("webkitanimationend"));
        div.DispatchEvent(new Event("webkitAnimationEnd"));
        Assert.Equal(["webkitAnimationEnd"], calls);

        var mrow = (MathMLElement)document.GetElementById("b")!;
        mrow.OnWebkitTransitionEnd = e => calls.Add(e.Type);
        mrow.DispatchEvent(new Event("webkitTransitionEnd"));
        Assert.Equal(["webkitAnimationEnd", "webkitTransitionEnd"], calls);
    }

    [Fact]
    public void EventHandlerSource_HasTheScopesOfTheElement()
    {
        EventHandlerSource? source = null;
        _engine.HandlerBehaviors["a()"] = (_, _) => null;
        var engine = new SourceRecordingEngine(_engine, s => source = s);
        var context = _contexts.Create(new BrowsingContextOptions { ScriptEngine = engine });
        var document = context.Navigate("https://example.com/", _ => "<form id=f><input id=i onchange='a()'></form><body onerror='a()'>");

        _ = ((HtmlInputElement)document.GetElementById("i")!).OnChange;
        Assert.NotNull(source);
        Assert.Equal("onchange", source.Name);
        Assert.Equal(["event"], source.Parameters);
        Assert.Same(document.GetElementById("i"), source.Element);
        Assert.Same(document.GetElementById("f"), source.Form);
        Assert.Same(document, source.Document);
        Assert.Same(document.DefaultView, source.Window);

        // The Window-reflecting body element event handlers have no scope, and onerror gets the parameters of OnErrorEventHandler
        _ = document.DefaultView!.OnError;
        Assert.Equal("onerror", source.Name);
        Assert.Same(document.DefaultView, source.Target);
        Assert.Null(source.Element);
        Assert.Null(source.Document);
        Assert.Equal(["event", "source", "lineno", "colno", "error"], source.Parameters);
    }

    [Fact]
    public void EventHandler_ReturnFalseCancelsTheEvent()
    {
        _engine.HandlerBehaviors["return false"] = (_, _) => false;
        _engine.HandlerBehaviors["return true"] = (_, _) => true;
        var document = Load("<a id=a onclick='return false'></a><a id=b onclick='return true'></a>");
        Assert.False(document.GetElementById("a")!.DispatchEvent(new Event("click", new EventInit { Cancelable = true })));
        Assert.True(document.GetElementById("b")!.DispatchEvent(new Event("click", new EventInit { Cancelable = true })));
    }

    [Fact]
    public void WindowOnError_GetsTheErrorAttributesAndReturnTrueCancelsTheEvent()
    {
        object?[]? arguments = null;
        _engine.HandlerBehaviors["handle()"] = (_, args) =>
        {
            arguments = args;
            return true;
        };
        _engine.Behaviors["throw()"] = _ => throw new InvalidOperationException("boom");
        Load("<body onerror='handle()'><script>throw()</script>");

        Assert.NotNull(arguments);
        Assert.Equal("boom", arguments[0]);
        Assert.Equal("", arguments[1]);
        Assert.IsType<InvalidOperationException>(arguments[4]);

        // The error was handled
        Assert.Empty(_unhandledErrors);
    }

    [Fact]
    public void ScriptException_IsReportedToTheWindow()
    {
        _engine.Behaviors["throw()"] = _ => throw new InvalidOperationException("boom");
        var context = CreateContext();
        var errors = new List<ErrorEvent>();
        context.Window.AddEventListener("error", e => errors.Add((ErrorEvent)e));
        var document = context.Navigate("https://example.com/", _ => "<script>throw()</script>");
        Assert.Empty(errors);

        document.DefaultView!.AddEventListener("error", e => errors.Add((ErrorEvent)e));
        document.Body!.AppendChild(document.CreateElement("script")).TextContent = "throw()";
        var error = Assert.Single(errors);
        Assert.Equal("boom", error.Message);
        Assert.True(error.IsTrusted);
        Assert.True(error.Cancelable);
        Assert.Equal("boom", Assert.Single(_unhandledErrors.Skip(1)).Message);
    }

    [Fact]
    public void MutedErrors_HideTheErrorInformation()
    {
        _loader.Resources["https://other.example/a.js"] = new DocumentResource("throw()") { Headers = [new("Content-Type", "text/javascript")] };
        _loader.Resources["https://example.com/b.js"] = new DocumentResource("throw()") { Headers = [new("Content-Type", "text/javascript")] };
        _engine.Behaviors["throw()"] = _ => throw new InvalidOperationException("boom");
        Load("<script src=https://other.example/a.js></script><script src=b.js></script>");
        Assert.Equal(["Script error.", "boom"], _unhandledErrors.Select(e => e.Message));
        Assert.Null(_unhandledErrors[0].Error);
    }

    [Fact]
    public void MutedErrors_SameOriginScriptRedirectedThroughAnotherOrigin()
    {
        // The response of a no-CORS request redirected to another origin is opaque, even when it is redirected back
        _loader.Resources["https://example.com/a.js"] = new DocumentResource("throw()") { Headers = [new("Content-Type", "text/javascript")], Url = "https://example.com/c.js", RedirectUrls = ["https://other.example/b.js"] };
        _engine.Behaviors["throw()"] = _ => throw new InvalidOperationException("boom");
        Load("<script src=a.js></script>");
        Assert.Equal("Script error.", Assert.Single(_unhandledErrors).Message);
    }

    [Fact]
    public void EventHandlerSyntaxError_IsReportedAndTheHandlerIsNull()
    {
        var document = Load("<div id=a onclick='syntax error'></div>");
        var div = (HtmlDivElement)document.GetElementById("a")!;
        Assert.Null(div.OnClick);
        Assert.Equal("SyntaxError: syntax error", Assert.Single(_unhandledErrors).Message);
    }

    [Fact]
    public void Microtasks_AreQueuedInTheEngineAndRunAfterTheScripts()
    {
        var log = new List<string>();
        _engine.Behaviors["a()"] = script =>
        {
            var node = ((Window)script.Global).Document.CreateTextNode("");
            new MutationObserver((_, _) => log.Add("mutation observer")).Observe(node, new MutationObserverInit { CharacterData = true });
            node.Data = "x";
            _engine.QueueJob(() => log.Add("promise job"));
            log.Add("script");
            return null;
        };
        _engine.Behaviors["b()"] = _ =>
        {
            log.Add("script b");
            return null;
        };

        Load("<script>a()</script><script>b()</script>");
        Assert.Equal(["script", "mutation observer", "promise job", "script b"], log);
    }

    [Fact]
    public void ScriptInsertedByAScript_DoesNotPerformAMicrotaskCheckpoint()
    {
        var log = new List<string>();
        _engine.Behaviors["outer()"] = script =>
        {
            _engine.QueueJob(() => log.Add("job"));
            var inner = ((Window)script.Global).Document.CreateElement("script");
            inner.TextContent = "inner()";
            ((Window)script.Global).Document.Body!.AppendChild(inner);
            log.Add("outer end");
            return null;
        };
        _engine.Behaviors["inner()"] = _ =>
        {
            log.Add("inner");
            return null;
        };

        Load("<body><script>outer()</script>");
        Assert.Equal(["inner", "outer end", "job"], log);
    }

    [Fact]
    public void JavaScriptUrl_StringResultReplacesTheDocument()
    {
        _engine.Behaviors["'<p>hello'"] = _ => "<p>hello";
        _engine.Behaviors["void 0"] = _ => null;
        var context = CreateContext();
        var document = Load("<body>", context);

        context.Navigate("javascript:void 0");
        _contexts.EventLoop.RunUntilIdle();
        Assert.Same(document, context.Document);

        context.Navigate("javascript:'%3Cp%3Ehello'");
        _contexts.EventLoop.RunUntilIdle();
        Assert.Contains("Execute '<p>hello'", _engine.Calls);
        Assert.NotSame(document, context.Document);
        Assert.Equal("hello", context.Document.QuerySelector("p")!.TextContent);
        Assert.Equal("https://example.com/page.html", context.Document.Url);
    }

    [Fact]
    public void JavaScriptUrl_IsIgnoredWithoutEngine()
    {
        var context = _contexts.Create();
        var document = context.Navigate("https://example.com/", _ => "<body>");
        context.Navigate("javascript:'x'");
        _contexts.EventLoop.RunUntilIdle();
        Assert.Same(document, context.Document);
    }

    [Fact]
    public void UnhandledRejection_FiresAnEventAfterTheMicrotaskCheckpoint()
    {
        var document = Load("<body>");
        var window = document.DefaultView!;
        var events = new List<string>();
        window.AddEventListener("unhandledrejection", e => events.Add("unhandledrejection " + ((PromiseRejectionEvent)e).Reason));
        window.AddEventListener("rejectionhandled", e => events.Add("rejectionhandled " + ((PromiseRejectionEvent)e).Reason));

        // A promise handled before the microtask checkpoint is not reported
        var handled = new object();
        window.TrackPromiseRejection(handled, "handled", PromiseRejectionOperation.Reject);
        window.TrackPromiseRejection(handled, "handled", PromiseRejectionOperation.Handle);

        var promise = new object();
        window.TrackPromiseRejection(promise, "reason", PromiseRejectionOperation.Reject);
        _contexts.EventLoop.PerformMicrotaskCheckpoint();
        Assert.Empty(events);

        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal(["unhandledrejection reason"], events);
        Assert.Equal("reason", Assert.Single(_unhandledErrors).Message);

        window.TrackPromiseRejection(promise, "reason", PromiseRejectionOperation.Handle);
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal(["unhandledrejection reason", "rejectionhandled reason"], events);
    }

    [Fact]
    public void ReportError_FiresAnErrorEvent()
    {
        var document = Load("<body>");
        var messages = new List<string>();
        document.DefaultView!.AddEventListener("error", e =>
        {
            messages.Add(((ErrorEvent)e).Message);
            e.PreventDefault();
        });

        document.DefaultView.ReportError(new InvalidOperationException("boom"));
        Assert.Equal(["boom"], messages);
        Assert.Empty(_unhandledErrors);
    }

    [Fact]
    public void ErrorThrownByAnErrorListener_IsNotReportedAgain()
    {
        var document = Load("<body>");
        var count = 0;
        document.DefaultView!.AddEventListener("error", _ =>
        {
            count++;
            throw new InvalidOperationException("again");
        });

        document.DefaultView.ReportError(new InvalidOperationException("boom"));
        Assert.Equal(1, count);
        Assert.Equal(["again", "boom"], _unhandledErrors.Select(e => e.Message));
    }

    /// <summary>Records the source of the compiled event handlers, and forwards the calls to another engine.</summary>
    private sealed class SourceRecordingEngine(RecordingScriptEngine inner, Action<EventHandlerSource> onCompile) : ScriptEngine
    {
        protected internal override void CreateRealm(GlobalScope global) => inner.CreateRealm(global);

        protected internal override object? ExecuteClassicScript(ClassicScript script) => inner.ExecuteClassicScript(script);

        protected internal override EventHandlerCallback CompileEventHandler(EventHandlerSource source)
        {
            onCompile(source);
            return inner.CompileEventHandler(source);
        }

        protected internal override void QueueMicrotask(Action microtask) => inner.QueueMicrotask(microtask);

        protected internal override void PerformMicrotaskCheckpoint() => inner.PerformMicrotaskCheckpoint();

        protected internal override bool IsScriptRunning => inner.IsScriptRunning;
    }
}
