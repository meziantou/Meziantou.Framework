---
title: Blazor applications
description: Test the static rendering, the enhanced navigation and forms, and the Interactive Server and WebAssembly components of a Blazor application.
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
- The Interactive WebAssembly components need a WebAssembly runtime, which the pages do not have by default: without
  it, `blazor.webassembly.js` fails to start. See [WebAssembly](08-WebApplications.md#webassembly).

## Blazor WebAssembly

A Blazor WebAssembly application (standalone, or the Interactive WebAssembly components of a Blazor Web App) runs the
.NET runtime in WebAssembly, in the page:

- Set [WebAssemblyRuntime](cref:Meziantou.AspNetCore.Testing.WebAppBrowserOptions.WebAssemblyRuntime) to the
  [WasmWebAssemblyRuntime](cref:Meziantou.Framework.WebPlatform.Wasm.WasmWebAssemblyRuntime) of the
  [Meziantou.Framework.WebPlatform.Wasm](https://www.nuget.org/packages/Meziantou.Framework.WebPlatform.Wasm)
  package.
- The application must serve the files of the runtime (`_framework/`): for a standalone application, serve the
  `wwwroot` folder of its published output (`dotnet publish`) with
  [FromDirectories](cref:Meziantou.AspNetCore.Testing.TestDocumentLoaders.FromDirectories(System.Uri,System.Collections.Generic.IEnumerable{System.Collections.Generic.KeyValuePair{System.String,System.String}},System.String)).

<!-- ref:../../../tests/Meziantou.AspNetCore.Testing.Tests/WebAppBrowserTests.cs;region=WebAppBrowser_BlazorWebAssembly;format=md-fenced-code-block;dedent=true -->
```csharp
// The wwwroot folder of the published application (dotnet publish)
var wwwroot = typeof(WebAppBrowserTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "WasmAppPath").Value!;

// The runtime of .NET runs in the page, on Meziantou.Framework.WasmEngine. The runtime can be shared between the tests
using var wasmRuntime = new WasmWebAssemblyRuntime();

// The fallback file serves the routes of the application (/accounts/42), as MapFallbackToFile does
var origin = new Uri("https://localhost/");
var loader = TestDocumentLoaders.FromDirectories(origin, [new("/", wwwroot)], fallbackPath: "/index.html");
await using var browser = new WebAppBrowser(origin, loader, new WebAppBrowserOptions
{
    DefaultTimeout = TimeSpan.FromSeconds(30),
    WebAssemblyRuntime = wasmRuntime,
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
- The applications of .NET 10 and .NET 11 run.
- The runtime of .NET is a large module: its functions are interpreted, and the ones that run often are compiled to
  .NET code. Share the [WasmWebAssemblyRuntime](cref:Meziantou.Framework.WebPlatform.Wasm.WasmWebAssemblyRuntime)
  between the tests. Publishing the application of the tests without AOT compilation (`-p:RunAOTCompilation=false`)
  makes the module much smaller, and the start faster.
- Publish the application with its default options. With `WasmEnableExceptionHandling=false`, the runtime of .NET 10
  fails at startup (a stack overflow in `PackedSimd`), in the browsers too.
- Mono, the runtime of .NET in the browser, does not run the methods compiled with runtime async
  (`<Features>runtime-async=on</Features>`): the application fails before `Main` starts.

To test a component alone, without an application, render it with
[ComponentTestHost](02-BlazorComponents.md).
