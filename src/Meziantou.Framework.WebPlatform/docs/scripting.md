# Scripting

The library never executes scripts by itself. A JavaScript engine can be plugged into the
[browsing contexts](browsing-context.md) with the `BrowsingContextOptions.ScriptEngine` option: the library implements
the processing model of the HTML Standard (script elements, event handlers, `javascript:` URLs, error reporting, the
event loop) and calls the engine at the points the standard defines, so that the engine only runs code.

Without an engine, nothing changes: the scripts are never fetched or run, the event handler content attributes
(`onclick="..."`) have no value, and the `javascript:` URLs are ignored.

## Opt-in with Meziantou.Framework.JavaScriptEngine

The `Meziantou.Framework.WebPlatform.JavaScript` package runs the scripts with
[Meziantou.Framework.JavaScriptEngine](../../Meziantou.Framework.JavaScriptEngine/readme.md), the ECMAScript engine of
this repository (a parser, a bytecode compiler and an interpreter written in .NET, validated by test262), see
[its readme](../../Meziantou.Framework.WebPlatform.JavaScript/readme.md):

```csharp
using Meziantou.Framework.WebPlatform;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.JavaScript;

using var engine = new JavaScriptScriptEngine(new JavaScriptScriptEngineOptions
{
    Timeout = TimeSpan.FromSeconds(5),            // per script or callback
});

using var context = new BrowsingContext(new BrowsingContextOptions
{
    ScriptEngine = engine,
    DocumentLoader = DocumentLoader.Create(),     // the documents and the external scripts (see the threat model below)
    UnhandledScriptError = (window, error) => Console.Error.WriteLine(error),
    ConsoleMessage = (window, message) => Console.WriteLine(message.Text), // console.log(), console.count()...
});

var document = await context.NavigateAsync("https://example.com/", _ => """
    <ul id=list></ul>
    <script>
      for (const name of ['a', 'b']) {
        document.getElementById('list').append(Object.assign(document.createElement('li'), { textContent: name }));
      }
    </script>
    """);

// Run the tasks and the timers (e.g. setTimeout) until there is nothing left
do { await context.EventLoop.RunUntilIdleAsync(); } while (await context.EventLoop.WaitForNextTimerAsync());

Console.WriteLine(document.Body!.InnerHtml); // <ul id="list"><li>a</li><li>b</li></ul>...
```

- The windows of the browsing contexts that use an engine share its `JsEngine` (`JavaScriptScriptEngine.Engine`, the
  similar-origin window agent). Each window is the global object of its own realm (the `iframe` elements have their own
  globals), created when a script first runs in it. `JavaScriptScriptEngine.Evaluate(window, code)` runs code in the realm
  of a window, as an indirect `eval` (it does not run the microtasks), and `OnRealmCreated` lets a subclass add its own
  functions to the global objects. `ConfigureEngine` sets the other options of the engine (`JsEngineOptions`, e.g. its
  `TimeZone` and its `DefaultLocale`); the host hooks and the module loader are the ones of the library.
- The adapter uses the public embedding API of the engine only, and the engine implements the host hooks of the HTML
  Standard: the code and the original arguments of `eval` and of the `Function` constructors for the content security
  policies and Trusted Types (`HostEnsureCanCompileStrings`, `HostGetCodeForEval`, see
  [Compilation of strings](browsing-context.md#compilation-of-strings-and-of-webassembly)), the incumbent settings object
  and the active script of the promise jobs and the continuation state of the schedulers (`HostMakeJobCallback`,
  `HostCallJobCallback`), the rejection tracking (`HostPromiseRejectionTracker`), the generic and timeout jobs (the
  cleanup of a `FinalizationRegistry`, `Atomics.waitAsync()`: tasks and timers of the event loop of the realm,
  `HostEnqueueGenericJob`, `HostEnqueueTimeoutJob`), the module loading and `import.meta`. The functions of the WebIDL bindings run in the
  realm of their caller.
- The `console` namespace of the [Console Standard](https://console.spec.whatwg.org/) is implemented by the WebIDL
  bindings (`Scripting/WebIdl/WebIdlRealm.Console.cs`): its formatter works on the JavaScript values. The messages are
  given to `BrowsingContextOptions.ConsoleMessage` (a `ConsoleMessage`: the method, the level, the formatted text, the
  arguments, the group depth and the stack of `console.trace()`). The objects are formatted as a browser console does,
  without calling their getters, and the arrays and objects are truncated to their first 100 items. When there is no
  callback, the messages are not formatted (the labels of the counting and timing methods are still converted).
- The JavaScript object of a node is created in the realm where the node was created: the realm of its node document
  (for a document without browsing context, the realm of the document that created it, e.g. the `DOMImplementation` of
  `createHTMLDocument()`), which a node adopted by a document of another realm keeps, even when it is first accessed after
  the adoption.
- The DOM is exposed with the WebIDL bindings of the library (interface objects, prototypes, attributes, operations,
  overloads, dictionaries, unions, callbacks, legacy platform objects, `[CEReactions]`...), generated at build time from
  webref by `tools/Meziantou.Framework.WebPlatform.SpecGen`: see [Writing an engine adapter](#writing-an-engine-adapter). The package implements their
  adapter with Meziantou.Framework.JavaScriptEngine; it uses no reflection and is trimming- and AOT-compatible. A member
  whose type is an abstract dictionary (e.g. the `body` of a `Report`, a `ReportBody`) is converted with the dictionary
  of its runtime type.
- Custom elements defined by the scripts (`customElements.define(name, class)`) are supported.
- The global object of a realm whose agent cluster is not cross-origin isolated has no `SharedArrayBuffer` property
  (https://html.spec.whatwg.org/multipage/webappapis.html#creating-a-new-javascript-realm): see
  [Cross-document messaging](browsing-context.md#cross-document-messaging).
- The Web Cryptography API (`crypto.subtle`, `crypto.getRandomValues()`, `crypto.randomUUID()`), with the secure curves and
  the modern algorithms (ML-KEM, ML-DSA...): see [Web Cryptography](#web-cryptography).
- Module scripts (`<script type="module">`), `import()`, import maps, JSON modules, CSS modules and text modules are supported: see
  [Module scripts](#module-scripts).
- The Streams Standard (`ReadableStream`, `WritableStream`, `TransformStream`...), `TextEncoderStream`,
  `TextDecoderStream`, `CompressionStream`, `DecompressionStream`, `Blob.stream()` and `Blob.textStream()` are supported:
  see [Streams](#streams).
- `Function.prototype.toString()` returns the source text of the functions (https://tc39.es/ecma262/#sec-function.prototype.tostring):
  the compiled functions reference the source of their script, so the source of a script stays in memory as long as one
  of its functions is alive. This is not configurable. A function without source text (a built-in function, or a
  function implemented in .NET by the bindings) returns `function name() { [native code] }`.

### Threat model

A document may be untrusted: its scripts run in the process.

- The engine has no CLR interop: the scripts only see the DOM and the JavaScript built-ins (and the functions that a
  subclass adds in `OnRealmCreated`).
- Resource exhaustion: `JavaScriptScriptEngineOptions` limits each entry into JavaScript (a script, an event listener, a
  timer callback...): `Timeout` (10 s by default), `MemoryLimit` (256 MB allocated by default), `RecursionLimit` (512 by
  default; null is the limit of the engine, 10,000 calls: the frames of the engine are not on the .NET stack, so there is
  always a limit), `RegexTimeout` (2 s by default) and `MaxStatements` (no limit by default; the engine counts the
  iterations of the loops and the calls). When a limit is exceeded, the script stops and the error is reported (a deeper
  call throws a `RangeError`). A page can still keep the event loop busy (e.g. `setInterval(f, 0)`): bound the time the
  host runs the event loop (e.g. with the cancellation token of `RunUntilIdleAsync`, and by not waiting for the timers
  indefinitely).
- Requests: the scripts, the `iframe` elements and the navigations request URLs through the `DocumentLoader`. The HTTP
  loader of `DocumentLoader.Create()` requests any URL: do not use it with untrusted documents when the requests can reach
  private resources (server-side request forgery). Use a loader that filters the destinations.

## Opt-in with another engine

Any implementation of `ScriptEngine` can be set as `BrowsingContextOptions.ScriptEngine`:

```csharp
using var context = new BrowsingContext(new BrowsingContextOptions { ScriptEngine = engine });
```

When an engine is set, scripting is enabled for the documents of the browsing contexts (`BrowsingContextOptions.ScriptingEnabled`
is true by default): `<noscript>` is parsed as text.

Do not run untrusted documents unless the engine limits the resources the scripts can use (time, memory, recursion),
and the `DocumentLoader` limits the resources the documents can request (server-side request forgery): see
[browsing-context.md](browsing-context.md#the-document-loader).

## The script engine contract

`Meziantou.Framework.WebPlatform.Scripting.ScriptEngine` is an abstract class. All its methods are called on the thread
of the `DomEventLoop` of the browsing contexts. Each worker has its own engine (`CreateWorkerScriptEngine`), whose methods
are called on the thread of the worker. The global object of a realm is a `GlobalScope`: a `Window`, the
`WorkerGlobalScope` of a worker, or the `WorkletGlobalScope` of a worklet (a realm of the engine of its document, see
[Worklets](#worklets)).

| Member | Called when |
|---|---|
| `CreateRealm(GlobalScope)` | A new window is created for a browsing context, before its document is activated, the script of a worker is fetched, or a global scope of a worklet is created. The window (or the worker or worklet global scope) is the global object of the realm. The engine may create the realm lazily. |
| `OnDocumentActivated(Document)` | A document becomes the active document of its browsing context (the window of an initial `about:blank` document is reused by a same-origin document). |
| `DestroyRealm(GlobalScope)` | The browsing context is discarded (e.g. its `iframe` is removed), the worker ends, or the global scopes of the worklets of a document are terminated. |
| `CreateWorkerScriptEngine(CancellationToken)` | A worker is created: the engine of the new agent, used on the thread of the worker (null by default: the workers do not run scripts). The token is canceled when the worker is terminated: the engine aborts the running script. |
| `OnChildNavigablesChanged(Window)` | An `iframe` is inserted or removed: the indexed properties of the `WindowProxy` (`window[0]`) changed (the `WindowProxy` of `JavaScriptScriptEngine` reads them when they are accessed). |
| `ExecuteClassicScript(ClassicScript)` | A classic script runs ("run a classic script"): a `script` element, a `javascript:` URL, a string passed to `setTimeout()`, the script of a worker or of `importScripts()`. It returns the completion value (a .NET `string` for a JavaScript string), or throws. The source text of an inline script created by the HTML parser starts at `SourceTextLine`/`SourceTextColumn` in its document: `JavaScriptScriptEngine` parses it with these offsets, so the lines and the columns of its errors and of the violations of the content security policies are the ones in the document. |
| `ParseClassicScript(ClassicScript)` | The script of a worker is fetched: the engine returns its parse error, if any (the worker object then fires an `error` event), or null (the default: the parse errors are thrown by `ExecuteClassicScript`). |
| `CompileEventHandler(EventHandlerSource)` | An event handler content attribute is used for the first time (lazily). |
| `QueueMicrotask(Action)` | The library queues a microtask (e.g. a mutation observer notification): it must run in order with the jobs of the engine (promise reactions). |
| `PerformMicrotaskCheckpoint()` | The library performs a microtask checkpoint: the engine runs its jobs until its queue is empty. |
| `EnterNestedMicrotaskQueue()` | Optional. The layout runs a callback of a layout worklet (see [Worklets](#worklets)): until the returned scope is disposed, the jobs go to a separate queue, the only one that `PerformMicrotaskCheckpoint()` runs. The default implementation does nothing. |
| `IsScriptRunning` | The library needs to know whether the JavaScript execution context stack is empty ("clean up after running script", the microtask checkpoint of the parser before a custom element constructor or a script). |
| `ExtractErrorInformation(object?)` | The library reports an exception: the engine gives the message, the location and the error value of the `ErrorEvent`. `JavaScriptScriptEngine` gives the error string (`TypeError: a message`, as the browsers do), and the location where the exception was thrown or, for an error object that was not thrown (the reason of a rejected promise, the argument of `reportError()`), where it was created (the first frame of its `stack`). |
| `IsPromiseHandled(object)` | The library fires the `unhandledrejection` events. |
| `SupportsModuleScripts` | The library decides whether the module scripts are fetched and run (false by default). |
| `LoadModuleScriptGraph(ModuleScript, Action<bool>)` | A module script is fetched: the engine parses it, resolves its imports (`ModuleSpecifierResolution`), fetches its descendants with the library (`ModuleFetching`) and calls the callback when the graph is loaded (false when a module cannot be fetched). A parse error is stored in `ModuleScript.ErrorToRethrow`. |
| `FetchModuleScriptGraph(GlobalScope, string, ModuleFetchOptions, Element?, Action<ModuleScript?>)` | An external module script is fetched ("fetch a module script graph"): the default implementation fetches the root with the library, then calls `LoadModuleScriptGraph`; an engine with a module map reuses the module of the URL (it is fetched, parsed and evaluated once, and its parse error is remembered). The callback gets null when a module of the graph cannot be fetched. |
| `PreloadModuleScript(GlobalScope, string, ModuleType, RequestDestination, ModuleFetchOptions, Element?, Action<bool>)` | A `<link rel=modulepreload>` fetches a module ("fetch a modulepreload module script graph"): the default implementation fetches the module with the library and ignores it; an engine with a module map adds the module to it, without loading its descendants nor running it. The callback gets false when the module cannot be fetched. |
| `ExecuteModuleScript(ModuleScript)` | A module script runs ("run a module script"): the engine links and evaluates the graph and reports the errors (including the errors of a top-level `await`). |
| `GetActiveScriptBaseUrl()` | A string is compiled by `setTimeout()` or `setInterval()`: the base URL of the active script, used to resolve the module specifiers of `import()`. |
| `GetCurrentScriptLocation()` | A content security policy is violated: the URL, the line and the column of the running code (`ScriptLocation`), the `sourceFile`, `lineNumber` and `columnNumber` of the violation, or null (the default). `JavaScriptScriptEngine` gives the position of the running instruction (e.g. the call of the method that found the violation), as the top frame of its stack traces. |
| `StructuredSerialize(GlobalScope, object?)` / `StructuredDeserialize(GlobalScope, object?)` | A value is sent to another realm or cloned (e.g. `BroadcastChannel.postMessage()`, `MessagePort.postMessage()`, `structuredClone()`): the structured clone of the value (https://html.spec.whatwg.org/multipage/structured-data.html). The default implementations only support the .NET primitive values and the serializable platform objects; the WebIDL bindings implement the structured clone of the JavaScript values (primitives, wrapper objects, `Date`, `RegExp`, the buffers and views, `Map`, `Set`, the errors with their stack, arrays, ordinary objects, and the serializable platform objects: `Blob`, `File`, `FileList`, `DOMException`, `DOMPoint`, `DOMRect`, `DOMQuad`, `DOMMatrix` and their read-only variants). The other platform objects (e.g. `Request`, `Response`, the streams) throw a `DataCloneError`. A `SharedArrayBuffer` throws a `DataCloneError` unless the current global object is cross-origin isolated; it is deserialized as a new `SharedArrayBuffer` that shares the memory of the original (with `IJsAdapter.GetSharedDataBlock` and `SetSharedDataBlock`), in the realms of the same agent cluster only (another agent cluster throws a `DataCloneError`, e.g. a `messageerror` event). The windows of a cross-origin isolated browsing context group and their dedicated workers are in origin-keyed agent clusters; a shared worker has its own agent cluster. |
| `StructuredSerialize(GlobalScope, object?, IReadOnlyList<object>)` | `structuredClone(value, { transfer })` and `window.postMessage()`: the objects of the transfer list are transferred (https://html.spec.whatwg.org/multipage/structured-data.html#structuredserializewithtransfer). The default implementation throws a `DataCloneError` when the list is not empty; the WebIDL bindings transfer the `ArrayBuffer` objects (resizable buffers keep their maximum length, length-tracking views keep tracking) and the `MessagePort` objects. |
| `StructuredSerializeForAgent(GlobalScope, object?, IReadOnlyList<object>)` | A message is posted to a `MessagePort`, a `BroadcastChannel` or a worker: the result is deserialized by another engine instance, on another thread, so it must not reference the values of the engine (the WebIDL bindings convert their records to .NET values). The default implementation calls `StructuredSerialize`. |

The engine calls back into the library:

- `GlobalScope.ReportException(ScriptError)`: "report an exception", e.g. for an exception thrown by a callback (an event
  listener). An `ErrorEvent` is fired at the global object (`onerror` gets the message, the file name, the line, the
  column and the error). When it is not canceled, the error is given to `BrowsingContextOptions.UnhandledScriptError`
  (the error of a dedicated worker is first fired at its `Worker` object).
  An exception thrown while an error is reported (e.g. by an error listener) does not fire another event.
- `GlobalScope.TrackPromiseRejection(promise, reason, operation)`: the HostPromiseRejectionTracker implementation. The
  `unhandledrejection` events are fired after the next microtask checkpoint, and the `rejectionhandled` events by a task.
  An unhandled rejection whose event is not canceled is given to `UnhandledScriptError`.
- `DomEventLoop.PerformMicrotaskCheckpoint()`: after invoking a callback when no script is running ("clean up after
  running a callback").

### Script elements

The library implements [prepare the script element](https://html.spec.whatwg.org/multipage/scripting.html#prepare-the-script-element):
the type, `nomodule`, `event`/`for`, Trusted Types, the "already started", "parser document" and "force async" flags.
The external classic scripts are fetched with the `DocumentLoader` of the browsing context: `DocumentRequest.Destination`
is `RequestDestination.Script`, `DocumentRequest.Initiator` is the element and `DocumentRequest.Origin` is the origin of
its document. The response is decoded with its byte
order mark, the `charset` parameter of its MIME type, then the encoding of the document. A network error (a null
resource or an exception) fires `error` at the element. The errors of a script fetched from another origin without
`crossorigin` are muted: the reported error is `"Script error."`, without information (also when the code of the script
is called later by another script or by a callback: with `JavaScriptScriptEngine`, the errors thrown by the code of a
script whose errors are muted are muted, as in the browsers). A script with a `crossorigin`
attribute is a CORS request (`DocumentRequest.Mode` is `RequestMode.Cors`): a response from another origin must allow
the origin with its `Access-Control-Allow-Origin` header (and `Access-Control-Allow-Credentials` for `use-credentials`),
else it is a network error (the library performs the check with the headers of the `DocumentResource`).

The `data:` URLs are processed by the library (as fetch does), without the loader.

The scripts run in the order the standard defines:

- An inline script runs immediately.
- An external script inserted by the parser without `async` or `defer` is a parser-blocking script: the parser pauses
  until it is fetched, then runs it and resumes.
- The `defer` scripts inserted by the parser run in order when the document has finished parsing (`readyState` is
  `interactive`), before `DOMContentLoaded`.
- The `async` scripts, and the scripts inserted by a script (which are async by default), run as soon as they are fetched.
  The scripts inserted by a script whose `async` attribute was set to false run in their insertion order.
- The `load` event of the document waits for the scripts that are being fetched or that will execute as soon as possible.

The fetch of a script completes from a task of the event loop, even when the loader completes synchronously, except for
the parser-blocking and `defer` scripts: with a synchronous loader, the parser never pauses.

The SVG `script` elements run when the parser reaches their end tag (inline scripts, or the script referenced by `href`).
As in browsers, an SVG `script` element that is not parser-inserted runs, once, like an HTML `script` element: when it
becomes connected, when a child is inserted, or when `href` is set. Its `type` must be empty or a JavaScript MIME type,
and its `language` attribute is ignored. The SVG scripts have a Trusted Types script text, set by the parser. The scripts
parsed by the fragment parsing algorithm (e.g. `innerHTML`) never run, the ones of `createContextualFragment()` run when
they are inserted.

The `speculationrules` scripts are the speculation rules of the document (see
[Speculative loading](browsing-context.md#speculative-loading)); they do not need a script engine.

### Module scripts

When the engine supports them (`ScriptEngine.SupportsModuleScripts`, true for `JavaScriptScriptEngine`), the module scripts run as specified
(https://html.spec.whatwg.org/multipage/webappapis.html#module-script):

- `<script type="module">` (inline or external) is deferred: a script inserted by the parser runs when the document has
  finished parsing, in order with the `defer` scripts; with `async`, it runs as soon as its graph is loaded; a module
  script inserted by a script runs as soon as possible (or in insertion order without `async`). `document.currentScript`
  is null while it runs, and the `load` event of an external script is fired after it runs. As in the browsers, the script
  is marked as ready from a task once its graph is loaded (https://github.com/whatwg/html/issues/9864): it runs outside of
  the microtask checkpoint, so its microtasks and the report of its evaluation error come before its `load` event.
- The modules are fetched with the `DocumentLoader`: CORS requests (`RequestMode.Cors`, `RequestDestination.Script`, or
  `Json`, `Style` and `Text` for the JSON, CSS and text modules) whose response must allow the origin of the document
  when it comes from another origin, must have an ok status and a JavaScript MIME type (a JSON MIME type for
  `with { type: "json" }`, `text/css` for `with { type: "css" }`, any MIME type for `with { type: "text" }`), and is
  decoded as UTF-8 (a UTF-8 BOM is removed, whatever the `charset` of the response or the encoding of the document). A module that cannot be fetched fires `error` at the
  script element; a parse error or a link error is reported to the window when the script runs.
- The module specifiers are resolved with the import map of the window
  (https://html.spec.whatwg.org/multipage/webappapis.html#resolve-a-module-specifier): the inline
  `<script type="importmap">` elements are parsed (`imports`, `scopes` and `integrity`) and merged (the first mapping of a
  specifier wins; an invalid import map reports a `SyntaxError` or a `TypeError`). The specifiers that were already
  resolved (the resolved module set of the window) cannot be changed by an import map registered afterwards: its rules
  that apply to them are ignored, for the top-level imports as for the scopes (the specification removes the top-level
  rules whose key *starts with* a resolved specifier; the browsers and the tests remove the rules that match it, the
  prefix rules included). External import maps are not supported.
- The integrity metadata of a module is the `integrity` attribute of its `script` element, else the entry of its URL in
  the `integrity` of the import map (also for the static and the dynamic imports): a module whose response does not match
  is a network error (see [The fetch algorithm](browsing-context.md#the-fetch-algorithm)).
- Each window has its own module map: a module is fetched and evaluated once per window, and shared by the external
  `<script type="module">` elements, the static imports and `import()` (the module map of a global object is keyed by
  the URL and the type of the modules). `import()` is available in the classic scripts, the module scripts, the event
  handlers and the strings compiled by `eval()` or `setTimeout()`; its base URL is the one of the referencing module, else the base URL of the
  document in an event handler (it has no script), else the one of the active script (`about:blank` for a classic script
  whose errors are muted, e.g. a cross-origin script fetched without CORS; the base URL of the document when an inline
  script was prepared). When no script runs (e.g. in a promise job), the active script is the script or module of the
  innermost running function.
- `import.meta.url` is the URL of the module (the base URL of the document for an inline module), and
  `import.meta.resolve(specifier)` resolves a specifier.
- An import attribute other than `type` makes the importing module fail with a `SyntaxError`, as a parse error.
- A JSON module exports the parsed JSON value; a CSS module exports a constructed `CSSStyleSheet` whose base URL is the
  URL of the module; a text module exports its source text (a string). The CSS modules are not allowed in the workers (`CSSStyleSheet` is not exposed there): they are not
  fetched, and the import fails.

`<link rel="modulepreload">` fetches a module into the module map without running it (see
[Preloads](browsing-context.md#preloads)). The module workers
(`new Worker(url, { type: "module" })`) load their graph with the same module loader, in the realm of the worker.

### Worklets

The worklets (https://html.spec.whatwg.org/multipage/worklets.html) run module scripts in global scopes that have no
access to the DOM. The `Worklet` interface and its infrastructure are implemented, with three kinds of worklets: the
paint worklet of the CSS Painting API (`CSS.paintWorklet`, `Document.PaintWorklet`; see
[Paint worklet images](rendering.md#paint-worklet-images)), the layout worklet of the CSS Layout API (`CSS.layoutWorklet`,
`Document.LayoutWorklet`; see [Layout API containers](layout.md#layout-api-containers-csslayoutlayoutapi-csslayoutapi))
and the animation worklet of the CSS Animation Worklet API (`CSS.animationWorklet`, `Document.AnimationWorklet`; see
[Animation worklet](animations.md#animation-worklet)). The audio worklets are not implemented (no Web Audio).

```js
await CSS.paintWorklet.addModule('paint.js', { credentials: 'same-origin' });
```

- **Where the scripts run**: the specification lets a user agent run the global scopes of a worklet on any thread. Here, a
  `WorkletGlobalScope` is the global object of a realm of the script engine of its document
  (`BrowsingContextOptions.ScriptEngine`, which must support the module scripts; the engine needs several realms, as for the
  `iframe` elements): the modules and the callbacks of a worklet run on the thread of the event loop of the document, never
  concurrently with its scripts, and deterministically. The realms share no object with the windows: a worklet global scope
  only exposes the interfaces of its kind (`[Exposed=Worklet]`, `[Exposed=PaintWorklet]`... in the IDL: the Typed OM values,
  the streams, `console`, `WebAssembly`), and has no timer, no `fetch()` and no `self`. A worklet is in its own agent
  cluster for the specification: `SharedArrayBuffer` is not exposed. The limits of the engine (e.g. `JavaScriptScriptEngineOptions.Timeout`)
  apply to each module and each callback.
- **Global scopes**: they are created by the first call of `addModule()` (two for each kind of worklet, as the CSS Painting
  API and the CSS Layout API require, so that the classes cannot rely on the state of a global scope; the animator
  instances of the animation worklet are migrated between its two global scopes), and terminated when the document is
  destroyed (`ScriptEngine.DestroyRealm`). A global scope created later runs the modules that were added before.
- **`addModule(url, { credentials })`** ([fetch a worklet script
  graph](https://html.spec.whatwg.org/multipage/webappapis.html#fetch-a-worklet-script-graph)): the module and its static
  imports are fetched as CORS requests with the destination of the worklet (`RequestDestination.PaintWorklet`,
  `Sec-Fetch-Dest: paintworklet`; `RequestDestination.Script` for the layout and animation worklets, which have no
  destination in Fetch), the credentials mode of the option (`omit`, `same-origin` or `include`), and the
  document as the fetch client: its content security policies (`script-src`, not `worker-src`), its mixed content and
  upgrade-insecure-requests checks, its referrer policy (the referrer of an import is the URL of the importing module,
  with the `Referrer-Policy` of its response), its service worker and its resource timing entries apply. The response of a
  URL is fetched once for the worklet (the module responses map), and evaluated in each global scope, which has its own
  module map. `import()` rejects with a `TypeError`: the specification does not allow it in a worklet.
- **The promise** is rejected with a `SyntaxError` `DOMException` when the URL is invalid, with an `AbortError` when a module
  cannot be fetched (also when there is no script engine, in a document that is not a secure context, and once the document
  is destroyed), and with the error of a module that cannot be parsed or linked: the error belongs to the realm of the
  worklet, so, as in the browsers, the promise is rejected with its structured clone in the realm of the document (e.g. a
  `SyntaxError` of the window). A module that throws when it is evaluated is still added: the promise is fulfilled, by a
  task queued after the tasks the module queued (e.g. the registration of a paint in the document).
- **Errors**: an exception of a module or of a callback is reported to its global scope, which fires no event (a worklet
  global scope is not an event target for the scripts): it is given to `BrowsingContextOptions.UnhandledScriptError` with
  the `WorkletGlobalScope`. The messages of `console` are given to `ConsoleMessage` with the global scope too.
- **Microtasks**: the specification runs a worklet in its own agent, which has its own microtask queue. The callbacks of
  the layout worklet are async functions that the layout of the document runs to completion, synchronously, possibly while
  a script of the document reads a geometry: `ScriptEngine.EnterNestedMicrotaskQueue()` gives them a separate queue (the
  jobs queued until the returned scope is disposed go to a new queue, which `PerformMicrotaskCheckpoint()` runs; the pending
  microtasks of the document are kept and do not run in the scope). `JavaScriptScriptEngine` gives its engine a new job
  queue for the scope (`JsEngine.EnterNestedJobQueue()`); the default implementation of an engine does nothing (the
  microtasks of the document then run with the ones of the worklet).
- **Feature toggles**: `WebPlatformFeatures.PaintWorkletEnabled`, `LayoutWorkletEnabled` and `AnimationWorkletEnabled`
  hide `CSS.paintWorklet`, `CSS.layoutWorklet` and `CSS.animationWorklet` (and `WorkletAnimation`); the `Worklet` interface
  is hidden when the three are disabled. They are only exposed in the secure contexts (the `CSS` attributes as in Chromium:
  the specifications do not annotate them).

The classes that a script registers in a worklet global scope (`registerPaint(name, class)`, `registerLayout(name,
class)`, `registerAnimator(name, class)`) are `WorkletClassConstructor` objects, created by the WebIDL bindings: the
library reads their static properties and their prototype, constructs them, invokes their callbacks, observes the promises
they return and converts their results through them, so the registration and invocation algorithms are implemented in .NET
and do not depend on the engine.

### Streams

The Streams Standard (https://streams.spec.whatwg.org/) is implemented in JavaScript by the WebIDL bindings
(`Scripting/WebIdl/Streams.js`, an embedded resource), so it is available with every engine adapter and its promises and
microtasks are the ones of the engine:

- `ReadableStream` (default and byte streams, default and BYOB readers with `read(view, { min })`, `tee()`, `pipeTo()`,
  `pipeThrough()`, async iteration and `ReadableStream.from()`), `WritableStream`, `TransformStream`, their controllers,
  `ByteLengthQueuingStrategy` and `CountQueuingStrategy`.
- `TextEncoderStream` and `TextDecoderStream` (https://encoding.spec.whatwg.org/#interface-textdecoderstream).
- `CompressionStream` and `DecompressionStream` (https://compression.spec.whatwg.org/) with the `brotli`, `deflate`,
  `deflate-raw` and `gzip` formats. The compression contexts are implemented in .NET (`Compression/`): the compression
  uses the streams of `System.IO.Compression`, the Brotli decompression uses `BrotliDecoder`, and the other formats are
  decompressed by an incremental DEFLATE decoder (`Inflater`), as the decoders of `System.IO.Compression` do not report
  where the compressed data ends (the input after the end of the data is an error) and do not reject the zlib preset
  dictionaries, the incorrect gzip header CRC and the gzip members after the first one. The output of a chunk is
  enqueued as `Uint8Array` objects of 64 KiB at most. The large window Brotli format is not supported.
- `Blob.stream()` (a byte stream of chunks of 64 KiB) and `Blob.textStream()`.
- The script is evaluated in a realm when one of its interfaces is first used. The algorithms use the intrinsics captured
  at that time, so they are not affected by the objects patched by the scripts (e.g. `Promise.prototype.then`), and the
  internal slots of the objects are not observable. The streams of the realms of an agent (e.g. an `iframe` and its
  parent) can be used together; an object created by an algorithm belongs to the realm of its stream.

The streams cannot be transferred (`postMessage()` or `structuredClone()` with a transfer list), and the
`ReadableStream` of type `owning` (a tentative proposal) is not supported.

### Fetch

`Request`, `Response` and `fetch()` (https://fetch.spec.whatwg.org/#fetch-api) are implemented in JavaScript by the
WebIDL bindings (`Scripting/WebIdl/Fetch.js`, an embedded resource), as their bodies are the streams of Streams.js; the
fetch algorithm and the `Headers` interface are implemented in .NET (see
[The fetch algorithm](browsing-context.md#the-fetch-algorithm)):

- The `Request` constructor (all the members of `RequestInit`; a `ReadableStream` body requires `duplex: "half"`),
  `clone()`, `signal` (a dependent signal of the signal of the request), and the `Response` constructor,
  `Response.error()`, `Response.redirect()`, `Response.json()` and `clone()`.
- The body mixin: `body`, `bodyUsed`, `arrayBuffer()`, `blob()`, `bytes()`, `formData()` (`multipart/form-data` and
  `application/x-www-form-urlencoded`), `json()`, `text()` and `textStream()`. The bodies created from bytes are byte
  streams.
- `fetch()` with an `AbortSignal`: the promise is rejected with the reason of the signal, and the body of the response is
  errored.
- The script is evaluated in a realm when `Request`, `Response` or `fetch` is first used, like Streams.js.

`fetchLater()` and `FetchLaterResult` are defined by the same script: the request is created by the `Request`
constructor, and the library queues the deferred fetch (see
[browsing-context.md](browsing-context.md#the-fetch-algorithm), which also describes the `keepalive` requests and their
quota).

### Web Cryptography

The Web Cryptography API (https://w3c.github.io/webcrypto/) is implemented in JavaScript by the WebIDL bindings
(`Scripting/WebIdl/WebCrypto.js`, an embedded resource), as the algorithm normalization converts the dictionaries to a
type that depends on the algorithm name, and the attributes of `CryptoKey` return cached objects. The cryptographic
primitives and the key structures are implemented in .NET (`Cryptography/`):

- `crypto` (`WindowOrWorkerGlobalScope`), `crypto.getRandomValues()`, and in the secure contexts `crypto.randomUUID()`,
  `crypto.subtle`, `SubtleCrypto` and `CryptoKey`.
- All the methods of `SubtleCrypto`, with the algorithms of Web Cryptography Level 2: RSASSA-PKCS1-v1_5, RSA-PSS,
  RSA-OAEP, ECDSA and ECDH (P-256, P-384, P-521), Ed25519, X25519, AES-CTR, AES-CBC, AES-GCM, AES-KW, HMAC, SHA-1/256/384/512,
  HKDF and PBKDF2, in the `raw`, `spki`, `pkcs8` and `jwk` formats (with compressed EC points).
- Ed448 and X448 (https://wicg.github.io/webcrypto-secure-curves/).
- The modern algorithms (https://wicg.github.io/webcrypto-modern-algos/): `encapsulateKey()`, `encapsulateBits()`,
  `decapsulateKey()`, `decapsulateBits()`, `getPublicKey()`, `SubtleCrypto.supports()`, the `raw-public`, `raw-seed` and
  `raw-secret` formats, ML-KEM, the hybrid KEMs (`MLKEM768-P256`, `MLKEM768-X25519`, `MLKEM1024-P384`), ML-DSA, AES-OCB,
  ChaCha20-Poly1305, SHA-3, cSHAKE, TurboSHAKE, KangarooTwelve, KMAC and Argon2. SLH-DSA is not implemented.
- `CryptoKey` objects are serializable (`postMessage()`, `structuredClone()`), including to the workers; they cannot be
  deserialized in a context that is not secure.
- The operations run synchronously, and their promises are settled by a task (the crypto task source).

The results do not depend on the operating system: the base class library is only used for the primitives it supports
everywhere (SHA-1/SHA-2, HMAC, HKDF, PBKDF2, the AES block cipher, the RSA key generation with the exponent 65537, ECDSA,
ECDH, ChaCha20-Poly1305). The other ones are managed implementations: the AES-CTR and AES-GCM modes (any IV length and
the short tags), AES-KW (RFC 3394), AES-OCB, the RSA padding schemes (OAEP labels and any PSS salt length), the NIST curve
point validation and decompression, the CFRG curves, the Keccak family, ML-KEM, ML-DSA, the hybrid KEMs, BLAKE2b and
Argon2. They are not audited, and some of them are not constant-time (e.g. the RSA operations use `BigInteger`): do not use
them to protect secrets against an attacker who can measure the time of the operations.

### WebAssembly

The `WebAssembly` namespace (https://webassembly.github.io/spec/js-api/) is defined when a WebAssembly runtime is set in
`BrowsingContextOptions.WebAssemblyRuntime` (and `WebPlatformFeatures.WebAssemblyEnabled` is true). The
`Meziantou.Framework.WebPlatform.Wasmtime` package provides one, based on [Wasmtime](https://wasmtime.dev/), which compiles
the modules to native code:

```csharp
using Meziantou.Framework.WebPlatform.Wasmtime;

using var runtime = new WasmtimeRuntime();        // can be shared by the browsing contexts and the script engines
using var context = new BrowsingContext(new BrowsingContextOptions
{
    ScriptEngine = engine,
    WebAssemblyRuntime = runtime,
});
```

The JavaScript interface is implemented by the WebIDL bindings (`Scripting/WebIdl/WebIdlRealm.WebAssembly.cs` and
`WebAssemblyBindings.cs`), on the runtime abstraction of `WebAssembly/` (a runtime compiles the modules, and each agent
has its own store of instances, memories, tables, globals and tags):

- `validate()`, `compile()`, `instantiate()`, `compileStreaming()`, `instantiateStreaming()` (a `Response` of type
  `application/wasm`), `JSTag`, and the `Module`, `Instance`, `Memory`, `Table`, `Global`, `Tag` and `Exception`
  interfaces, the `CompileError`, `LinkError` and `RuntimeError` constructors, the exported functions and the host
  functions (the imported JavaScript functions), with the value conversions of the specification (`i64` values are
  BigInts, `externref` values are JavaScript values, the structs and arrays of the GC types are opaque objects).
- The JavaScript exceptions thrown through WebAssembly can be caught by WebAssembly (the JavaScript exception tag) and are
  thrown again unchanged; the exceptions thrown by WebAssembly are `WebAssembly.Exception` objects.
- The bytes of a memory are a .NET array that its `ArrayBuffer` shares: the writes of the scripts are seen by WebAssembly,
  and the buffer is detached when the memory grows (by `grow()` or by `memory.grow`). The buffers cannot be transferred.
- The modules are compiled on the thread of the agent, and the promises are settled by a task.
- A `WebAssembly.Module` is serializable within its agent cluster (`postMessage()` to a window or a dedicated worker):
  the compiled module is shared.
- The running code is interrupted by the limits of the script engine (e.g. `JavaScriptScriptEngineOptions.Timeout`), and
  a stack overflow throws a `RangeError`.

The Wasmtime runtime enables the features of WebAssembly 3.0 (the reference types, the typed function references, the
GC types, the exceptions, the tail calls, memory64, the multiple memories, the SIMD and relaxed SIMD instructions) and
the wide arithmetic. Not implemented: the threads (a shared `WebAssembly.Memory` is only a growable `SharedArrayBuffer`
that cannot be imported by a module), the JS String Builtins (`builtins`, `importedStringConstants`), the ESM
integration (`import` of a module), JavaScript Promise Integration, the type reflection (`type()`), the resizable buffers
of the memories (`toResizableBuffer()`): see the [WPT coverage of wasm](wpt-coverage/wasm.md).

The runtime does not use the signal handlers of Wasmtime (the traps are explicit checks) and does not register native
unwind information for the compiled code (`native_unwind_info` is disabled): on macOS, `__register_frame` and
`__deregister_frame` take the write lock of the cache of libunwind that .NET reads each time an exception is thrown, and
a garbage collection that started while threads waited for that lock could hang the process (the collection never
managed to suspend the waiting threads). A native debugger or profiler cannot unwind through the WebAssembly frames.

### The parser

When a parser-blocking script is not fetched yet (the loader completes asynchronously), `BrowsingContext.Navigate(url, ...)`
returns once the parser pauses: the document is partially parsed and its `readyState` is `loading`. The parser resumes
from the task that marks the script as ready, so the rest of the document is parsed by the tasks of the `DomEventLoop`
(`DomEventLoop.RunUntilIdleAsync()`). `BrowsingContext.NavigateAsync(url, ...)` returns once the parser has finished,
running the tasks of the event loop meanwhile.

While the parser waits for a script, the resources of the rest of the document can be fetched speculatively (see
[Speculative parsing](browsing-context.md#speculative-parsing)).

The parser of a document is aborted when the document is unloaded (e.g. its `iframe` is removed) while it waits for a
script. A document without a script engine is parsed exactly as without scripting: the parser never pauses and nothing
is fetched.

### Dynamic markup insertion

`document.write()` and `document.writeln()` insert markup in the input stream at the insertion point
(https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html): from a script run by the parser, the markup is
parsed right after the `script` element (a token may span several calls, and the markup may continue with the rest of
the document); from another script (e.g. a timer, once the document is loaded), `document.open()` is called implicitly:
the document is emptied (its nodes and event listeners are removed) and parsed by a script-created parser, whose input
ends when `document.close()` is called. `document.open()` is ignored in a script run by the parser, and the external
scripts cannot replace the document (the ignore-destructive-writes counter). The methods throw an `InvalidStateError` in
an XML document, and while the parser runs a custom element constructor. With Trusted Types, the strings are checked
as `TrustedHTML` (the `Document write` and `Document writeln` sinks).

The methods can be used from .NET, without a script engine (`document.Open(); document.Write("<p>a</p>"); document.Close();`).
`document.open(url, name, features)` runs the window open steps, as `window.open()` (see
[Auxiliary browsing contexts](browsing-context.md#auxiliary-browsing-contexts)), but, as in the browsers, the target
(e.g. `_self`) is chosen from the browsing context of the document instead of the one of the entry document.

### Event handlers

The event handler content attributes (e.g. `<button onclick="...">`) are compiled when the event handler is first used.
`EventHandlerSource` gives the body, the parameters (`event`, or `event, source, lineno, colno, error` for the `onerror`
handler of a window), and the scopes: the document, the form owner and the element, which the engine adds to the scope
chain of the function (e.g. with `with` statements). An exception thrown by `CompileEventHandler` (a syntax error) is
reported to the window, and the value of the event handler is null.

The engine returns an `EventHandlerCallback`, which the library invokes with the event handler processing algorithm, then
it processes the return value: `false` cancels the event, `true` cancels an error event handled by the `onerror` handler of
a window, and a non-null value of an `onbeforeunload` handler cancels the event and sets its `returnValue`.

### `javascript:` URLs

A navigation to a `javascript:` URL (e.g. `iframe.src = "javascript:..."`, `location.href = "javascript:..."`, a click on
a link whose `href` is a `javascript:` URL) runs the
script in the realm of the active document of the navigable, from a task. When the result is a string, it replaces the
document (as an HTML document whose URL is the URL of the previous document). The Trusted Types pre-navigation check
applies the default policy to the script.

## Writing an engine adapter

The library contains the WebIDL bindings of the DOM (the interface objects, the prototypes, the attributes and
operations with their overload resolution, the dictionaries, the enumerations, the unions, the callbacks, the legacy
platform objects...), in the `Meziantou.Framework.WebPlatform.Scripting` namespace. They are generated by
`tools/Meziantou.Framework.WebPlatform.SpecGen` from the webref IDL and the `[IdlInterface]`/`[IdlMember]` annotations of the .NET types, without
runtime reflection nor dynamic code (they are trimming- and AOT-compatible). An engine exposes the DOM by implementing
`IJsAdapter<TValue>`, a small set of primitives of the engine, with a struct:

```csharp
public readonly struct MyAdapter(MyEngine engine) : IJsAdapter<MyValue>
{
    public MyValue Undefined => MyValue.Undefined;
    public MyValue FromString(string value) => new MyString(value);
    public double ToNumber(MyValue value) => engine.ToNumber(value); // may run JavaScript code (valueOf)
    // ...
}
```

The bindings are generic over the adapter (`where TAdapter : struct, IJsAdapter<TValue>`), so the JIT specializes them
for each engine: the calls to the adapter are direct calls that can be inlined, without boxing nor interface dispatch.

### The primitives

- Values: the type tests (`IsUndefined`, `IsString`, `IsCallable`...), the creation of the primitive values, and the
  abstract operations `ToNumber`, `ToString`, `ToBoolean`, `ToBigInt` (https://tc39.es/ecma262/#sec-type-conversion).
- Objects: the internal methods (`Get`, `Set`, `DefineOwnProperty`, `GetOwnProperty`, `HasProperty`, `Delete`,
  `OwnKeys`, `GetPrototypeOf`, `SetPrototypeOf`), the creation of ordinary objects, arrays and built-in functions
  (`CreateFunction`: a function implemented in .NET; `CreateConstructor`: a built-in constructor, used for the interface
  objects and the legacy factory functions such as `Image`), `Call` and `Construct`.
- Platform objects: `CreatePlatformObject` creates the JavaScript object of a .NET object. When the bindings pass an
  `IJsHostObjectHandler<TValue>`, the object is a host exotic object: its internal methods call the handler first (the
  indexed and named properties of the legacy platform objects), and the `Ordinary*` methods perform the ordinary
  internal methods, bypassing the handler. `CreateHtmlDdaPlatformObject` creates `document.all`, a legacy platform
  object with a [[Call]] internal method and an [[IsHTMLDDA]] internal slot (typeof is "undefined", it is falsy and
  loosely equal to null); the default implementation is a `Proxy` of a function, which cannot be an [[IsHTMLDDA]]
  object. The named properties object of the window (`WindowProperties`, between
  `Window.prototype` and `EventTarget.prototype`) is a `Proxy` created by the bindings, as its prototype is immutable.
- Immutable prototypes (https://webidl.spec.whatwg.org/#es-immutable-prototype): the global object of a window, a worker
  or a worklet, the interface prototype object of its interface and the ones of the inherited interfaces (e.g.
  `ServiceWorkerGlobalScope.prototype`, `WorkerGlobalScope.prototype` and `EventTarget.prototype`) are immutable
  prototype exotic objects. The adapter cannot define the `[[SetPrototypeOf]]` internal method of these objects, so the
  bindings replace the built-in functions that call it in each realm (`Object.setPrototypeOf()`,
  `Reflect.setPrototypeOf()` and the setter of `Object.prototype.__proto__`) by built-in functions that throw (or return
  false) when the prototype of one of these objects would change, and call the original functions otherwise.
- Errors: `CreateException` returns the .NET exception that throws a JavaScript value in the engine, and
  `TryGetThrownValue` converts an exception of the engine back to the thrown value.
- Realms: `CurrentRealm`, `RunInRealm`, `GetGlobalObject`, and `Evaluate`, which evaluates code in a realm as an
  indirect eval. The bindings define a few helpers in JavaScript (e.g. the named properties object of the window), and
  the default implementations of some primitives are written in JavaScript: the engine must support ECMAScript 2020.
- Buffer sources (https://webidl.spec.whatwg.org/#idl-buffer-source-types): `TryGetBufferSource` returns the kind
  (`ArrayBuffer`, `SharedArrayBuffer`, a typed array or `DataView`) and a view of the bytes of an object, without running
  JavaScript code; the bindings pass the view to the .NET members (`ReadOnlySpan<byte>`, `Span<byte>` for the members that
  write into the buffer, e.g. `TextEncoder.encodeInto()`, `byte[]` or `float[]` copies...), and check `[AllowShared]`.
  `CreateArrayBuffer` and `CreateArrayBufferView` create the results (e.g. the `Uint8Array` of `TextEncoder.encode()`).
  The default implementations do not support the buffer source types (`TryGetBufferSource` returns false) and create the
  results with the constructors of the global object. `GetSharedDataBlock` and `SetSharedDataBlock` share the memory of a
  `SharedArrayBuffer` with a new `SharedArrayBuffer` (of any engine instance of the adapter, e.g. a worker): without them,
  a `SharedArrayBuffer` is only shared with the realms of the same engine instance (as the same object).
- Errors: `TrySetErrorStack` sets the [[Stack]] internal slot of a deserialized error (read by the `Error.prototype.stack`
  accessor of https://tc39.es/proposal-error-stack-accessor/); without it, the stack is an own data property.

The adapter must return the same `TValue` instance for the same JavaScript object: the bindings compare the objects by
reference and cache data per object.

### Wiring the adapter

A `WebIdlAgent<TAdapter, TValue>` holds the realms and the JavaScript objects of the .NET objects (one wrapper per
object, shared by the realms of the agent). The script engine creates it with its adapter and an `IWebIdlHost`, which
reports the exceptions of the callbacks and performs a microtask checkpoint after a callback when no script is running.
`WebIdlAgent.GetRealm(window)` creates the realm of a window: it defines the interface objects on the global object, and
sets the `WindowProxy` of the browsing context (`IJsAdapter.CreateWindowProxy`, an exotic object whose internal methods
call an `IJsWindowProxyHandler`) as the this value of the global code (`IJsAdapter.TrySetGlobalThis`). The JavaScript
object of a window is its `WindowProxy` for every realm; the `Location` objects are created by
`IJsAdapter.CreateCrossOriginPlatformObject`. An engine that does not support them exposes a window by its global object.

```csharp
sealed class MyScriptEngine : ScriptEngine, IWebIdlHost
{
    private readonly WebIdlAgent<MyAdapter, MyValue> _agent;

    public MyScriptEngine(MyEngine engine)
    {
        _agent = new WebIdlAgent<MyAdapter, MyValue>(new MyAdapter(engine), this) { RealmFactory = engine.CreateRealm };
    }

    protected override void CreateRealm(Window window) => _agent.GetRealm(window);
    // ...
}
```

### Versioning of the adapter

The adapter is an interface, so that the bindings can be specialized for a struct: an abstract base class would not
allow it. New primitives are added as default interface members implemented with the existing primitives, so that the
existing adapters keep compiling, and an engine overrides them when it can do better (e.g. `DefineLazyDataProperty`).

A default interface member is not free on a struct: the constrained call boxes the adapter, because the default
implementation receives `this` as the interface. Measured with BenchmarkDotNet on .NET 10 (Apple M-series, 1,000 calls):

| Call                                         | Time     | Allocated |
| -------------------------------------------- | -------: | --------: |
| Member implemented by the struct             | 0.87 µs  | 0 B       |
| Default interface member                     | 2.63 µs  | 24,000 B  |
| Default interface member (struct with state) | 3.80 µs  | 24,000 B  |

So the default interface members are only a compatibility fallback: the primitives the bindings call on every
conversion or property access are regular members, and an adapter should implement every member, including the
default ones, before its next release. A primitive that becomes required is made abstract in a major version.

### Testing an adapter

The WebIDL conversions are tested with an in-memory adapter that has no JavaScript engine
(`tests/Meziantou.Framework.WebPlatform.Tests/Scripting/InMemoryJsAdapter.cs`). An engine adapter is validated by the
web-platform-tests (see [web-platform-tests.md](web-platform-tests.md#javascript-engine)).
