---
title: Web APIs
description: Install console, the timers, queueMicrotask, structuredClone, URL, TextEncoder, TextDecoder, atob and btoa in a realm with one call.
---

Most scripts and npm packages use a few APIs that browsers, Node.js, Deno and Bun all provide: `console`, `setTimeout`,
`URL`, `TextEncoder`... The `Meziantou.Framework.JavaScriptEngine.WebApis` package adds them to a realm, implemented as
the browsers do. They are the part of the
[Minimum Common Web API](https://min-common-api.proposal.wintertc.org/) of WinterTC that runs without network or
file access.

<InstallNuGetPackage Id="Meziantou.Framework.JavaScriptEngine.WebApis" />

Add `using Meziantou.Framework.JavaScriptEngine.WebApis;` to the examples.

## Install the APIs in a realm

Call [JsWebApis.Install](cref:Meziantou.Framework.JavaScriptEngine.WebApis.JsWebApis.Install) once per realm, before
running its scripts. It returns the [JsWebApiHost](cref:Meziantou.Framework.JavaScriptEngine.WebApis.JsWebApiHost) of the
realm, which runs the timers:

<!-- ref:../../../tests/Meziantou.Framework.JavaScriptEngine.WebApis.Tests/JsWebApisTests.cs;region=JsWebApis_Install;format=md-fenced-code-block;dedent=true -->
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

- The APIs capture the built-ins of the realm when they are installed: a script that replaces `Array.prototype.push`
  or `Map.prototype.get` afterwards does not change them.
- The globals have the attributes of a browser: the classes and `console` are not enumerable, the functions are.

## Run the timers

The engine runs code only when your application calls it. [RunUntilIdleAsync](cref:Meziantou.Framework.JavaScriptEngine.WebApis.JsWebApiHost.RunUntilIdleAsync)
is the event loop of the realm: it runs the jobs (the promises and `queueMicrotask`), then each timer once its delay has
elapsed, followed by the jobs it starts, until no timer is active.

- The delays are measured by the [TimeProvider](cref:Meziantou.Framework.JavaScriptEngine.JsEngineOptions.TimeProvider)
  of the engine, the clock of `Date.now()`. A test can advance a fake clock and call
  [RunJobs](cref:Meziantou.Framework.JavaScriptEngine.JsEngine.RunJobs): the timers whose delay has elapsed run.
- An interval keeps the loop running until it is cleared. Pass a `CancellationToken` to stop waiting, and set the
  [limits](../07-LimitsAndSandboxing.md) of the engine for untrusted scripts.
- An exception thrown by a timer, a microtask or a promise job stops the loop: `RunUntilIdleAsync` throws the
  [JsException](cref:Meziantou.Framework.JavaScriptEngine.JsException). The other timers stay active, an interval
  included, and the next call continues with them.
- Call the engine from one place at a time: the loop can continue on another thread after an `await`, which is allowed
  because no other thread uses the engine meanwhile.
- [ActiveTimerCount](cref:Meziantou.Framework.JavaScriptEngine.WebApis.JsWebApiHost.ActiveTimerCount) is the number of
  timers that still have to run.

The timers follow the HTML specification: they run in the order of their due time, a timer set by a timer nested more
than five levels deep waits at least 4 ms, and `this` is the global object. The ids are positive numbers, as in
browsers (Node.js returns objects). A string handler (`setTimeout('code')`) throws a `TypeError`: the package never
evaluates strings.

To schedule your own work on the same clock, use
[EnqueueTimeoutJob](cref:Meziantou.Framework.JavaScriptEngine.JsEngine.EnqueueTimeoutJob): the job runs from `RunJobs`
once its delay has elapsed, and the returned [JsTimeoutJob](cref:Meziantou.Framework.JavaScriptEngine.JsTimeoutJob)
cancels it.

## Read the console messages

By default, `console` writes to the standard output, and its warnings and errors to the standard error. Set
[ConsoleMessage](cref:Meziantou.Framework.JavaScriptEngine.WebApis.JsWebApisOptions.ConsoleMessage) to receive the
messages instead:

<!-- ref:../../../tests/Meziantou.Framework.JavaScriptEngine.WebApis.Tests/ConsoleTests.cs;region=JsWebApis_ConsoleMessage;format=md-fenced-code-block;dedent=true -->
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

- A [JsConsoleMessage](cref:Meziantou.Framework.JavaScriptEngine.WebApis.JsConsoleMessage) has the method that wrote
  it, its [level](cref:Meziantou.Framework.JavaScriptEngine.WebApis.JsConsoleMessageLevel), its text, and the number of
  `console.group` it is in.
- The text is formatted as the [Console Standard](https://console.spec.whatwg.org/#formatter) specifies (`%s`, `%d`,
  `%i`, `%f`, `%o`, `%O`, `%c`), and the objects are shown as Node.js shows them: `{ a: 1 }`, `[ 1, 'x' ]`,
  `Map(1) { 'k' => 1 }`. An error is shown with its stack trace.
- Showing an object runs no code of the script: its kind comes from its internal slots, the getters are shown as
  `[Getter]`, a proxy as `[Proxy]`, the objects deeper than two levels as `[Object]`, and the collections show their
  first 100 items.
- `console.table` writes its data as `console.log` does, and `console.trace` writes the stack trace of the call.

## Choose the APIs

[JsWebApisOptions](cref:Meziantou.Framework.JavaScriptEngine.WebApis.JsWebApisOptions) enables each group of APIs.
They are all enabled by default:

<!-- ref:../../../tests/Meziantou.Framework.JavaScriptEngine.WebApis.Tests/JsWebApisTests.cs;region=JsWebApis_Options;format=md-fenced-code-block;dedent=true -->
```csharp
using var engine = new JsEngine();
var realm = engine.CreateRealm();
JsWebApis.Install(realm, new JsWebApisOptions
{
    Timers = false,
    Console = false,
});

var types = realm.Execute("[typeof setTimeout, typeof console, typeof URL].join()").AsString();
Assert.Equal("undefined,undefined,function", types);
```
<!-- endref -->

| Option | Globals |
| --- | --- |
| `Console` | `console` |
| `Timers` | `setTimeout`, `setInterval`, `clearTimeout`, `clearInterval` |
| `QueueMicrotask` | `queueMicrotask` |
| `StructuredClone` | `structuredClone` |
| `Url` | `URL`, `URLSearchParams` |
| `Encoding` | `TextEncoder`, `TextDecoder` |
| `Base64` | `atob`, `btoa` |
| `DomException` | `DOMException` |

`atob` and `structuredClone` throw `DOMException` objects even when the `DOMException` class is not added.

## What the APIs support

- `URL` and `URLSearchParams` implement the [URL Standard](https://url.spec.whatwg.org/), with the internationalized
  domain names (`https://Bücher.example` is `https://xn--bcher-kva.example/`), `URL.parse` and `URL.canParse`.
- `TextDecoder` decodes every encoding of the [Encoding Standard](https://encoding.spec.whatwg.org/) (`windows-1252`,
  `shift_jis`, `gb18030`, `iso-2022-jp`...), with the `fatal`, `ignoreBOM` and `stream` options. The result does not
  depend on the operating system: the tables are part of the package. `TextEncoder` encodes UTF-8, the lone surrogates
  as U+FFFD.
- `structuredClone` copies the values the HTML specification lists: the primitives, the objects and arrays (cycles
  included), `Date`, `RegExp`, `Map`, `Set`, `ArrayBuffer` and its views, `SharedArrayBuffer` (the clone shares the
  memory), the errors and `DOMException`. The `transfer` option moves `ArrayBuffer` objects. A function, a symbol or
  a `WeakMap` throws a `DataCloneError`, and the clone of a class instance is a plain object.
- `atob` and `btoa` implement the forgiving base64 of the HTML specification: `atob` ignores the white space and
  accepts a missing padding.

The other APIs of the Minimum Common Web API are not provided: `fetch`, the streams, `EventTarget`, `AbortController`,
`Blob`, `crypto`, `performance`... Add the ones your scripts need with
[Functions](04-Functions.md) and [Exposing .NET objects](05-ExposingDotNetObjects.md), as
[Console, timers and other host APIs](07-HostApis.md) shows.
