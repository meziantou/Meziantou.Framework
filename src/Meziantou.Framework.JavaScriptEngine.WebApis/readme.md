# Meziantou.Framework.JavaScriptEngine.WebApis

Installs the common web APIs in a realm of [Meziantou.Framework.JavaScriptEngine](../Meziantou.Framework.JavaScriptEngine/readme.md),
implemented as the browsers do: `console`, `setTimeout`, `setInterval`, `clearTimeout`, `clearInterval`,
`queueMicrotask`, `structuredClone`, `URL`, `URLSearchParams`, `TextEncoder`, `TextDecoder`, `atob`, `btoa` and
`DOMException`. They are the part of the [Minimum Common Web API](https://min-common-api.proposal.wintertc.org/) of
WinterTC that runs without network or file access.

<!-- ref:../../tests/Meziantou.Framework.JavaScriptEngine.WebApis.Tests/JsWebApisTests.cs;region=JsWebApis_Install;format=md-fenced-code-block;dedent=true -->
```csharp
using var engine = new JsEngine();
var realm = engine.CreateRealm();
var messages = new List<string>();
var webApis = JsWebApis.Install(realm, new JsWebApisOptions
{
    ConsoleMessage = message => messages.Add(message.Text),
});

realm.Execute("""
    const url = new URL('/docs?page=2', 'https://example.com');
    setTimeout(() => console.log(url.searchParams.get('page'), btoa('hi')), 10);
    """);

// Runs the jobs and the timers until there is nothing left to run
await webApis.RunUntilIdleAsync();
Assert.Equal(["2 aGk="], messages);
```
<!-- endref -->

Choose the APIs to add with `JsWebApisOptions`, and receive the messages of the console:

<!-- ref:../../tests/Meziantou.Framework.JavaScriptEngine.WebApis.Tests/ConsoleTests.cs;region=JsWebApis_ConsoleMessage;format=md-fenced-code-block;dedent=true -->
```csharp
using var engine = new JsEngine();
var realm = engine.CreateRealm();
var messages = new List<JsConsoleMessage>();
JsWebApis.Install(realm, new JsWebApisOptions { ConsoleMessage = messages.Add });

realm.Execute("""
    console.warn('%s has %d items', 'cart', 3, { total: 12.5 });
    """);

var message = Assert.Single(messages);
Assert.Equal("warn", message.Method);
Assert.Equal(JsConsoleMessageLevel.Warning, message.Level);
Assert.Equal("cart has 3 items { total: 12.5 }", message.Text);
```
<!-- endref -->

- `URL` and `URLSearchParams` follow the URL Standard, and `TextDecoder` decodes every encoding of the Encoding
  Standard. They share their implementation with [Meziantou.Framework.WebPlatform](../Meziantou.Framework.WebPlatform/readme.md).
- `RunUntilIdleAsync` runs the jobs and the timers until no timer is active. The delays are measured by the
  `TimeProvider` of the engine.
- The APIs never evaluate strings: `setTimeout('code')` throws a `TypeError`.
