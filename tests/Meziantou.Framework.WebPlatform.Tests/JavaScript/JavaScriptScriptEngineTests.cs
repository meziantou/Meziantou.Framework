using Meziantou.Framework.JavaScriptEngine;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.JavaScript;
using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.Tests.Scripting;

namespace Meziantou.Framework.WebPlatform.Tests.JavaScript;

/// <summary>End-to-end tests of the script engine of Meziantou.Framework.JavaScriptEngine: the scripts of a document run and use the DOM.</summary>
public sealed class JavaScriptScriptEngineTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly List<ScriptError> _errors = [];
    private readonly JavaScriptScriptEngine _engine = new();

    public void Dispose()
    {
        _contexts.Dispose();
        _engine.Dispose();
    }

    private Document Load(string html, JavaScriptScriptEngine? engine = null, DocumentLoader? loader = null)
    {
        var context = _contexts.Create(new BrowsingContextOptions
        {
            ScriptEngine = engine ?? _engine,
            DocumentLoader = loader,
            UnhandledScriptError = (_, error) => _errors.Add(error),
        });

        var document = context.Navigate("https://example.com/", _ => html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    [Fact]
    public async Task ReadmeExample_ExecuteScripts()
    {
        #region WebPlatformJavaScript_ExecuteScripts
        using var engine = new JavaScriptScriptEngine(new JavaScriptScriptEngineOptions
        {
            Timeout = TimeSpan.FromSeconds(5), // per script or callback
        });

        using var context = new BrowsingContext(new BrowsingContextOptions
        {
            ScriptEngine = engine,
            DocumentLoader = DocumentLoader.Create(), // optional: external scripts, modules, fetch, XMLHttpRequest, WebSocket, EventSource...
            UnhandledScriptError = (window, error) => Console.Error.WriteLine(error),
            ConsoleMessage = (window, message) => Console.WriteLine(message.Text), // console.log(), console.count()...
        });

        var document = await context.NavigateAsync("https://example.com/", _ => """
            <title>Example</title>
            <ul id=list></ul>
            <script>
              setTimeout(() => document.getElementById('list').innerHTML = '<li>a<li>b', 100);
            </script>
            """);

        // Run the tasks and the timers until there is nothing left
        do { await context.EventLoop.RunUntilIdleAsync(); } while (await context.EventLoop.WaitForNextTimerAsync());

        Assert.Equal("<li>a</li><li>b</li>", document.GetElementById("list")!.InnerHtml);

        // Evaluate code in the realm of a window
        var title = engine.Evaluate(context.Window, "document.title");
        Assert.Equal("Example", title.AsString());
        #endregion
    }

    [Fact]
    public void RecursionLimit_Null_UnboundedRecursionThrowsARangeError()
    {
        using var engine = new JavaScriptScriptEngine(new JavaScriptScriptEngineOptions { RecursionLimit = null, MemoryLimit = null });
        var document = Load("<script>try { (function f() { 1 + f(); })(); } catch (e) { document.title = e.constructor.name; }</script>", engine);
        Assert.Equal("RangeError", document.Title);
        Assert.Empty(_errors);
    }

    [Fact]
    public void Evaluate_RunsCodeInTheRealmOfAWindow()
    {
        var document = Load("<script>var x = 40;</script>");
        Assert.Equal(42, _engine.Evaluate(document.DefaultView!, "x + 2").AsNumber());
        Assert.Empty(_errors);
    }

    [Fact]
    public void Timeout_StopsTheScript()
    {
        using var engine = new JavaScriptScriptEngine(new JavaScriptScriptEngineOptions { Timeout = TimeSpan.FromMilliseconds(100) });
        var document = Load("<script>while (true) {}</script><script>document.title = 'next'</script>", engine);
        Assert.Equal("next", document.Title);
        Assert.NotEmpty(_errors);
    }

    [Fact]
    public void MaxStatements_StopsTheScript()
    {
        using var engine = new JavaScriptScriptEngine(new JavaScriptScriptEngineOptions { MaxStatements = 1000 });
        var document = Load("<script>function f(n) { return n < 2 ? n : f(n - 1) + f(n - 2); } f(40);</script><script>document.title = 'next'</script>", engine);
        Assert.Equal("next", document.Title);
        Assert.NotEmpty(_errors);
    }

    [Fact]
    public void Errors_AreLocatedInTheDocument()
    {
        var document = Load("""
            <script>
              var a = 1;
              null.x;
            </script>
            <script>
              var = 1;
            </script>
            <div onclick="undefinedFunction()"></div>
            <script>document.querySelector('div').click();</script>
            """);
        Assert.Equal(3, _errors.Count);
        Assert.Equal(("TypeError", "https://example.com/", 3), (TestScriptEngines.GetErrorName(_errors[0]), _errors[0].Filename, _errors[0].LineNumber));
        Assert.Equal(("SyntaxError", "https://example.com/", 6), (TestScriptEngines.GetErrorName(_errors[1]), _errors[1].Filename, _errors[1].LineNumber));
        Assert.Equal(("ReferenceError", "https://example.com/"), (TestScriptEngines.GetErrorName(_errors[2]), _errors[2].Filename));
        Assert.NotNull(document.Body);
    }

    [Fact]
    public void Microtasks_RunAfterTheScript()
    {
        var document = Load("""
            <script>
              const log = [];
              Promise.resolve().then(() => log.push('promise'));
              queueMicrotask(() => log.push('microtask'));
              log.push('script');
              setTimeout(() => document.title = log.join(), 0);
            </script>
            """);
        Assert.Equal("script,promise,microtask", document.Title);
        Assert.Empty(_errors);
    }

    [Fact]
    public void UnhandledRejections_AreReported()
    {
        var document = Load("""
            <script>
              window.onunhandledrejection = e => document.title = 'unhandled ' + e.reason.message;
              Promise.reject(new Error('boom'));
            </script>
            """);
        Assert.Equal("unhandled boom", document.Title);
    }

    [Fact]
    public void EventHandlers_UseTheScopeOfTheElementTheFormAndTheDocument()
    {
        var document = Load("""
            <form name=f><input name=i value=v onclick="document.title = [value, f.name, typeof event, this === i].join()"></form>
            <script>document.forms[0].elements[0].click();</script>
            """);
        Assert.Equal("v,f,object,true", document.Title);
        Assert.Empty(_errors);
    }

    [Fact]
    public void OnRealmCreated_ExposesTheGlobalObject()
    {
        using var engine = new HostFunctionsScriptEngine();
        var document = Load("<script>document.title = hostAdd(40, 2);</script>", engine);
        Assert.Equal("42", document.Title);
        Assert.Empty(_errors);
    }

    [Fact]
    public void ContentSecurityPolicy_BlocksEval()
    {
        var document = Load("""
            <meta http-equiv="Content-Security-Policy" content="script-src 'unsafe-inline'">
            <script>
              const results = [];
              for (const code of [() => eval('1'), () => new Function('return 1'), () => (0, eval)('1')]) {
                try { code(); results.push('allowed'); } catch (e) { results.push(e.name); }
              }
              document.title = results.join();
            </script>
            """);
        Assert.Equal("EvalError,EvalError,EvalError", document.Title);
    }

    private sealed class HostFunctionsScriptEngine : JavaScriptScriptEngine
    {
        protected override void OnRealmCreated(GlobalScope global, JsObject globalObject)
        {
            var realm = Engine.CurrentRealm!;
            globalObject.SetProperty("hostAdd", realm.CreateFunction("hostAdd", 2, (_, arguments) => arguments[0].AsNumber() + arguments[1].AsNumber()));
        }
    }
}
