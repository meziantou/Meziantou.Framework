---
title: Blazor applications
description: Test the static rendering, the enhanced navigation and forms, and the Interactive Server components of a Blazor Web App, and the Blazor WebAssembly applications.
---

A page of a Blazor Web App runs the real `blazor.web.js` of the application: the enhanced navigation and forms of the
static rendering work, and the Interactive Server components start their circuit with the application in memory.
`WaitForInteractivityAsync`, in the [Meziantou.AspNetCore.Testing.Components](cref:Meziantou.AspNetCore.Testing.Components) namespace, waits until the prerendered components are
interactive: before that, a click on a prerendered button reaches no component.

<!-- ref:../../../tests/Meziantou.AspNetCore.Testing.Tests/BlazorAppTests.cs;region=BlazorApp_InteractiveServer;format=md-fenced-code-block;dedent=true -->
```csharp
// blazor.web.js runs in the page: it starts a circuit (SignalR) with the application in memory
var page = await browser.OpenAsync("/interactive");
await page.WaitForInteractivityAsync();
Assert.Equal("interactive", page.Find("#renderer").TextContent);

// The click goes to the circuit, the render batch comes back: wait for the new content
await page.GetByRole("button", "Click me").ClickAsync();
await Expect(page.GetByRole("status")).ToHaveTextAsync("Current count: 1");
```
<!-- endref -->

- The circuit uses the long polling of SignalR: the in-memory server does not accept WebSockets, and `blazor.web.js`
  falls back to long polling, as it does behind a proxy that blocks them.
- The render batches come from the server after the action: wait for the result with an
  [assertion](03-Locators.md#assert-the-state-of-the-page) of `Expect`.
- The Interactive WebAssembly components of a Blazor Web App are not supported: the pages of
  `WebAppBrowser.Create(server)` have no WebAssembly runtime, so the runtime of .NET does not start.

## Blazor WebAssembly applications

A standalone Blazor WebAssembly application runs in the page: open the output of `dotnet publish` with
[FromDirectories](cref:Meziantou.AspNetCore.Testing.TestDocumentLoaders.FromDirectories(System.Uri,System.Collections.Generic.IEnumerable{System.Collections.Generic.KeyValuePair{System.String,System.String}},System.String)),
and give the pages a WebAssembly runtime: the runtime of .NET (`dotnet.native.wasm`) runs on
[Wasmtime](../../WebPlatform/06-Scripting/05-WebAssembly.md), installed with the
`Meziantou.Framework.WebPlatform.Wasmtime` package.

<InstallNuGetPackage Id="Meziantou.Framework.WebPlatform.Wasmtime" />

<!-- ref:../../../tests/Meziantou.AspNetCore.Testing.Tests/WebAppBrowserTests.cs;region=WebAppBrowser_BlazorWebAssembly;format=md-fenced-code-block;dedent=true -->
```csharp
// The wwwroot folder of the published application (dotnet publish)
var wwwroot = typeof(WebAppBrowserTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "WasmAppPath").Value!;

// The runtime of .NET runs in the page, on Wasmtime. Share the runtime between the tests, and keep the compiled code
// of the modules in a folder of the tests: the first start compiles the runtime of .NET, the next ones load it.
using var wasmtime = new WasmtimeRuntime(new WasmtimeRuntimeOptions { ModuleCacheDirectory = Path.Combine(AppContext.BaseDirectory, "wasmtime-cache") });

// The fallback file serves the routes of the application (/accounts/42), as MapFallbackToFile does
var origin = new Uri("https://localhost/");
var loader = TestDocumentLoaders.FromDirectories(origin, [new("/", wwwroot)], fallbackPath: "/index.html");
await using var browser = new WebAppBrowser(origin, loader, new WebAppBrowserOptions
{
    DefaultTimeout = TimeSpan.FromSeconds(30),
    ConfigureBrowsingContext = options => options with { WebAssemblyRuntime = wasmtime },
});

var page = await browser.OpenAsync("/accounts/42");
await Expect(page.GetByRole("heading", "Account 42")).ToBeVisibleAsync();

// The router of Blazor handles the navigation in the page
await page.GetByRole("link", "Home").ClickAsync();
await page.GetByRole("button", "Click me").ClickAsync();
await Expect(page.GetByRole("status")).ToHaveTextAsync("Current count: 1");
await Expect(page).ToHaveUrlAsync("/");
```
<!-- endref -->

- The fallback file (`/index.html`) serves the routes of the application, so a test can open any page directly
  (`/accounts/42`), as `MapFallbackToFile` does on a server.
- The first start compiles the runtime of .NET to native code, which takes from a second to tens of seconds (an
  application published with AOT compilation has a much larger module). Share the
  [WasmtimeRuntime](cref:Meziantou.Framework.WebPlatform.Wasmtime.WasmtimeRuntime) between the tests, and set
  [ModuleCacheDirectory](cref:Meziantou.Framework.WebPlatform.Wasmtime.WasmtimeRuntimeOptions.ModuleCacheDirectory) to
  load the compiled code of the previous runs. Publishing the application of the tests without AOT compilation
  (`-p:RunAOTCompilation=false`) also makes the first start faster.
- Publish the application with its default options. With `WasmEnableExceptionHandling=false`, the runtime of .NET 10
  fails at startup (a stack overflow in `PackedSimd`), in the browsers too.
- Mono, the runtime of .NET in the browser, does not run the methods compiled with runtime async
  (`<Features>runtime-async=on</Features>`): the application fails before `Main` starts.

To test a component alone, without an application, render it with
[ComponentTestHost](02-BlazorComponents.md).
