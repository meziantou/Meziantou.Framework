# Meziantou.Framework.WebPlatform.JavaScript

Runs the scripts of the documents of [Meziantou.Framework.WebPlatform](../Meziantou.Framework.WebPlatform/readme.md) with
[Meziantou.Framework.JavaScriptEngine](../Meziantou.Framework.JavaScriptEngine/readme.md): a `ScriptEngine` with the
WebIDL bindings of the DOM.

<!-- ref:../../tests/Meziantou.Framework.WebPlatform.Tests/JavaScript/JavaScriptScriptEngineTests.cs;region=WebPlatformJavaScript_ExecuteScripts;format=md-fenced-code-block;dedent=true -->
```csharp
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
```
<!-- endref -->

The windows of the browsing contexts that use an engine share its `JsEngine` (the similar-origin window agent); each
window is the global object of its own realm. A worker gets its own engine, with the same options, on its own thread.
`JavaScriptScriptEngineOptions` limits the resources of each entry into JavaScript (a script, an event listener, a timer
callback...): `Timeout` (10 seconds by default), `MemoryLimit` (256 MB allocated by default), `MaxStatements`,
`RecursionLimit` (512 calls by default; null is the limit of the engine, 10,000 calls) and `RegexTimeout`.
`TimeProvider` is the clock of `Date`, and `ConfigureEngine` sets the other options of the engine (e.g. the time zone and
the default locale of `Intl`). The scripts only see the DOM and the JavaScript built-ins: the engine has no access to
.NET types.

A subclass can add its own functions to the global objects in `OnRealmCreated`:

<!-- ref:../../tests/Meziantou.Framework.WebPlatform.Tests/JavaScript/JavaScriptScriptEngineTests.cs;region=WebPlatformJavaScript_HostFunctions;format=md-fenced-code-block;dedent=true -->
```csharp
/// <summary>A script engine whose windows have a <c>hostAdd</c> function.</summary>
private sealed class HostFunctionsScriptEngine : JavaScriptScriptEngine
{
    protected override void OnRealmCreated(GlobalScope global, JsObject globalObject)
    {
        var realm = Engine.CurrentRealm!;
        globalObject.SetProperty("hostAdd", realm.CreateFunction("hostAdd", 2, (_, arguments) => arguments[0].AsNumber() + arguments[1].AsNumber()));
    }
}
```
<!-- endref -->

See [scripting.md](../Meziantou.Framework.WebPlatform/docs/scripting.md) for the threat model and the integration with
the processing model of the HTML Standard.
