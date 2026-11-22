---
title: Web applications
description: Open the pages of an application in memory, follow links, submit forms, run the scripts of the pages, and check the DOM.
---

[WebAppBrowser](cref:Meziantou.AspNetCore.Testing.WebAppBrowser) opens the pages of a web application. With the
application in memory (a `TestServer`), a test runs the whole stack in one process: the server, the HTML, the CSS, the
scripts of the pages and the user input. It works with any ASP.NET Core application: MVC, Razor Pages, Blazor, minimal
APIs, or your own middleware.

<!-- ref:../../../tests/Meziantou.AspNetCore.Testing.Tests/WebAppBrowserTests.cs;region=WebAppBrowser_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
// The application runs in memory: Program is the entry point of the application under test
await using var factory = new WebApplicationFactory<Program>();
await using var browser = WebAppBrowser.Create(factory.Server);
var page = await browser.OpenAsync("/");
await Expect(page).ToHaveTitleAsync("Home");

// Follow a link, fill a form and submit it: the POST, the redirect and the cookie go through the application
await page.GetByRole("link", "Sign in").ClickAsync();
await page.GetByLabel("Name").FillAsync("Ada");
await page.GetByRole("button", "Sign in").ClickAsync();

await Expect(page).ToHaveUrlAsync("/profile");
await Expect(page.Locator("#greeting")).ToHaveTextAsync("Hello Ada");
```
<!-- endref -->

## Connect the browser

| Method | Application |
| --- | --- |
| `WebAppBrowser.Create(server)` | An application in memory: `WebApplicationFactory<Program>.Server` (package `Microsoft.AspNetCore.Mvc.Testing`), or `app.GetTestServer()` after `UseTestServer()` |
| `WebAppBrowser.Create(uri)` | A server on the network (Kestrel, a development server...) |
| `new WebAppBrowser(baseAddress, loader)` | Any [DocumentLoader](cref:Meziantou.Framework.WebPlatform.Dom.DocumentLoader), for instance one of [TestDocumentLoaders](cref:Meziantou.AspNetCore.Testing.TestDocumentLoaders) |

[TestDocumentLoaders.FromDirectories](cref:Meziantou.AspNetCore.Testing.TestDocumentLoaders.FromDirectories(System.Uri,System.Collections.Generic.IEnumerable{System.Collections.Generic.KeyValuePair{System.String,System.String}},System.String))
serves the files of folders, such as the output of `dotnet publish`: the URL of a folder serves its `index.html` file,
and the fallback file serves the routes of a single-page application, as `MapFallbackToFile` does. See
[Blazor WebAssembly](09-BlazorApplications.md#blazor-webassembly).

The pages handle the cookies and the redirects, as a browser does: the pages of a
[WebAppBrowser](cref:Meziantou.AspNetCore.Testing.WebAppBrowser) share their cookies
([Cookies](cref:Meziantou.AspNetCore.Testing.WebAppBrowser.Cookies)), and two browsers are two users.

## Replace the services of the application

Configure the application as in any integration test of ASP.NET Core, then open its pages:

<!-- ref:../../../tests/Meziantou.AspNetCore.Testing.Tests/WebAppBrowserTests.cs;region=WebAppBrowser_ReplaceServices;format=md-fenced-code-block;dedent=true -->
```csharp
await using var application = new WebApplicationFactory<Program>();
await using var factory = application.WithWebHostBuilder(builder =>
    builder.ConfigureTestServices(services => services.AddSingleton<GreetingService, FrenchGreetingService>()));
await using var browser = WebAppBrowser.Create(factory.Server);

// Sign in by setting the cookie of the application
browser.Cookies.SetCookie("http://localhost/", "user=Ada");
var page = await browser.OpenAsync("/profile");

Assert.Equal("Bonjour Ada", page.Find("#greeting").TextContent);
```
<!-- endref -->

## Pages

[BrowserPage](cref:Meziantou.AspNetCore.Testing.BrowserPage) navigates (`GoToAsync`, `GoBackAsync`, `GoForwardAsync`,
`ReloadAsync`) and waits until the new document is loaded. The queries, the actions, the waits and the snapshots are the
ones of [TestPage](cref:Meziantou.AspNetCore.Testing.TestPage), shared with the tests of Blazor components: see
[Locators and assertions](03-Locators.md), [User input](04-UserInput.md), [Dialogs](05-Dialogs.md) and [Snapshots](06-Snapshots.md).

The pages run their scripts; set [EnableJavaScript](cref:Meziantou.AspNetCore.Testing.WebAppBrowserOptions.EnableJavaScript)
to false to test the pages as without JavaScript. The errors of the scripts are in [ScriptErrors](cref:Meziantou.AspNetCore.Testing.TestPage.ScriptErrors), their console in
[ConsoleMessages](cref:Meziantou.AspNetCore.Testing.TestPage.ConsoleMessages), and the violations of the content security policies (including the ones raised while the document
loads) in [ContentSecurityPolicyViolations](cref:Meziantou.AspNetCore.Testing.TestPage.ContentSecurityPolicyViolations): what the developer tools of a browser report.

## Browsing context options

[ConfigureBrowsingContext](cref:Meziantou.AspNetCore.Testing.WebAppBrowserOptions.ConfigureBrowsingContext) changes any
option of the browsing contexts of the pages ([BrowsingContextOptions](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContextOptions)): the user agent, the languages, the permissions,
the geolocation, the dialogs (see [Dialogs](05-Dialogs.md) for the answers of a page)... It receives the options made from the other settings and returns the options to use,
usually with a `with` expression. The pages still record their console messages, script errors and content security
policy violations, and call the callbacks that the function sets. The function cannot replace the document loader nor
the script engine, which the pages depend on to wait and to run the JavaScript interop: use the loader of the browser and
[EnableJavaScript](cref:Meziantou.AspNetCore.Testing.WebAppBrowserOptions.EnableJavaScript) instead.

<!-- ref:../../../tests/Meziantou.AspNetCore.Testing.Tests/WebAppBrowserTests.cs;region=WebAppBrowser_ConfigureBrowsingContext;format=md-fenced-code-block;dedent=true -->
```csharp
var reports = new List<ContentSecurityPolicyViolationReport>();
await using var factory = new WebApplicationFactory<Program>();
await using var browser = WebAppBrowser.Create(factory.Server, new WebAppBrowserOptions
{
    // Change any option of the browsing contexts of the pages
    ConfigureBrowsingContext = options => options with
    {
        UserAgent = "TestBrowser/1.0",
        ContentSecurityPolicyViolation = (_, report) => reports.Add(report),
    },
});
var page = await browser.OpenAsync("/secure");

Assert.Equal("TestBrowser/1.0", page.BrowsingContext.Window.Navigator.UserAgent);

// The page still records what it reports, and calls the callbacks of the options
Assert.Single(page.ContentSecurityPolicyViolations);
Assert.Single(reports);
```
<!-- endref -->

[ComponentTestHostOptions](cref:Meziantou.AspNetCore.Testing.Components.ComponentTestHostOptions) has the same function for the page that hosts the components.

## WebAssembly

The pages run no WebAssembly by default: the `WebAssembly` namespace of their scripts is not defined, so a page that
instantiates a module fails, and a Blazor WebAssembly application does not start. Set
[WebAssemblyRuntime](cref:Meziantou.AspNetCore.Testing.WebAppBrowserOptions.WebAssemblyRuntime) to a runtime: the
[WasmWebAssemblyRuntime](cref:Meziantou.Framework.WebPlatform.Wasm.WasmWebAssemblyRuntime) of the
[Meziantou.Framework.WebPlatform.Wasm](https://www.nuget.org/packages/Meziantou.Framework.WebPlatform.Wasm)
package runs the modules with Meziantou.Framework.WasmEngine (see [WebAssembly](../../WebPlatform/06-Scripting/05-WebAssembly.md) for its
options). The pages share the runtime and the browser does not dispose it: dispose it after the browser.

<!-- ref:../../../tests/Meziantou.AspNetCore.Testing.Tests/WebAppBrowserTests.cs;region=WebAppBrowser_WebAssemblyRuntime;format=md-fenced-code-block;dedent=true -->
```csharp
// The runtime of the Meziantou.Framework.WebPlatform.Wasm package: the pages share it
using var runtime = new WasmWebAssemblyRuntime();
await using var factory = new WebApplicationFactory<Program>();
await using var browser = WebAppBrowser.Create(factory.Server, new WebAppBrowserOptions { WebAssemblyRuntime = runtime });

// The page instantiates a WebAssembly module and calls its add function
var page = await browser.OpenAsync("/wasm");

await Expect(page.Locator("output")).ToHaveTextAsync("2 + 3 = 5");
```
<!-- endref -->

[ComponentTestHostOptions.WebAssemblyRuntime](cref:Meziantou.AspNetCore.Testing.Components.ComponentTestHostOptions.WebAssemblyRuntime) is the runtime of the page that hosts the components in the same way, when it runs JavaScript.

## Clipboard

Each page has a clipboard of its own, so tests that run in parallel do not see what the others copy.
[FakeClipboard](cref:Meziantou.AspNetCore.Testing.FakeClipboard) supplies what the pages paste
([ProvideContent](cref:Meziantou.AspNetCore.Testing.FakeClipboard.ProvideContent)) and receives what they copy
([ContentChanged](cref:Meziantou.AspNetCore.Testing.FakeClipboard.ContentChanged)). Set it with
[ConfigureBrowsingContext](cref:Meziantou.AspNetCore.Testing.WebAppBrowserOptions.ConfigureBrowsingContext): the pages that
use the same instance share it. Without [ProvideContent](cref:Meziantou.AspNetCore.Testing.FakeClipboard.ProvideContent), the pages read what was last written.

<!-- ref:../../../tests/Meziantou.AspNetCore.Testing.Tests/WebAppBrowserTests.cs;region=FakeClipboard_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
var copied = new List<string?>();
var clipboard = new FakeClipboard
{
    // What the pages paste or read
    ProvideContent = () => [FakeClipboard.CreateTextItem("Pasted text")],

    // Called with what the pages copy or write
    ContentChanged = items => copied.Add(FakeClipboard.GetText(items)),
};

await using var factory = new WebApplicationFactory<Program>();
await using var browser = WebAppBrowser.Create(factory.Server, new WebAppBrowserOptions
{
    // The pages that use the same clipboard share it
    ConfigureBrowsingContext = options => options with { Clipboard = clipboard },
});
var page = await browser.OpenAsync("/secure");

await page.Find("#copy").ClickAsync();
await page.Find("#paste").ClickAsync();

Assert.Equal(["Copied text"], copied);
Assert.Equal("Pasted text", page.Find("output").TextContent);
```
<!-- endref -->

When the content returned by [ProvideContent](cref:Meziantou.AspNetCore.Testing.FakeClipboard.ProvideContent) changes, as when another application writes to the clipboard,
[NotifyContentChanged](cref:Meziantou.AspNetCore.Testing.FakeClipboard.NotifyContentChanged) fires the `clipboardchange`
events of the pages.

## Clock

[TimeProvider](cref:Meziantou.AspNetCore.Testing.WebAppBrowserOptions.TimeProvider) is the clock of the pages: the
timers (`setTimeout()`, the animations...) and the dates (`Date.now()`, `new Date()`, and `DateTime.Now` in a Blazor
WebAssembly application, which reads the dates of JavaScript), with the local time zone of the clock. Use a
`FakeTimeProvider` (Microsoft.Extensions.TimeProvider.Testing) to test the code that depends on the date:

<!-- ref:../../../tests/Meziantou.AspNetCore.Testing.Tests/WebAppBrowserTests.cs;region=WebAppBrowser_TimeProvider;format=md-fenced-code-block;dedent=true -->
```csharp
var clock = new FakeTimeProvider(new DateTimeOffset(2026, 1, 10, 12, 0, 0, TimeSpan.Zero));
clock.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("Test", TimeSpan.FromHours(2), "Test", "Test"));
await using var factory = new WebApplicationFactory<Program>();
await using var browser = WebAppBrowser.Create(factory.Server, new WebAppBrowserOptions { TimeProvider = clock });

// The page writes new Date().toISOString() and new Date().getTimezoneOffset()
var page = await browser.OpenAsync("/clock");

Assert.Equal("2026-01-10T12:00:00.000Z -120", page.Find("output").TextContent);
```
<!-- endref -->

[ComponentTestHostOptions.TimeProvider](cref:Meziantou.AspNetCore.Testing.Components.ComponentTestHostOptions.TimeProvider) is the clock of the page that hosts the components in the same way.

## Waiting

Each action waits until the page is idle: no task in its event loop, and no request in progress. Some requests never
end while a page is open: the server-sent events and the long polling of SignalR do not count
([IsLongLivedRequest](cref:Meziantou.AspNetCore.Testing.WebAppBrowserOptions.IsLongLivedRequest)). When the result
depends on a message the server pushes later (SignalR, Blazor Server), wait for it with an
[assertion](03-Locators.md#assert-the-state-of-the-page) of `Expect`, which retries until it passes.
