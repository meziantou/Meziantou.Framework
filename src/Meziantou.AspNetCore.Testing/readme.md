# Meziantou.AspNetCore.Testing

Tests of Blazor components and of ASP.NET Core applications (MVC, Razor Pages, Blazor, minimal APIs...) in a browser
engine written in .NET ([Meziantou.Framework.WebPlatform](https://www.nuget.org/packages/Meziantou.Framework.WebPlatform)):
the pages have a real DOM, the CSS applies, the scripts run, and the user input (mouse, keyboard, touch) goes through the
hit testing of the page. Compare the HTML, the accessibility tree and the screenshots with snapshots.

Render a Blazor component alone with `ComponentTestHost` (in the `Meziantou.AspNetCore.Testing.Components` namespace):

<!-- ref:../../tests/Meziantou.AspNetCore.Testing.Tests/ComponentTestHostTests.cs;region=ComponentTestHost_Counter;format=md-fenced-code-block;dedent=true -->
```csharp
await using var host = new ComponentTestHost();
var counter = await host.RenderAsync<Counter>(parameters => parameters.Add(c => c.IncrementAmount, 2));

// A real click: the pointer moves to the center of the button, the hit testing finds the button
await counter.Find("button").ClickAsync();
await counter.GetByRole("button", "Click me").ClickAsync();

Assert.Equal("Current count: 4", counter.GetByRole("status").TextContent);
Assert.Equal("""
    <p role="status">Current count: 4</p>
    <button class="btn">Click me</button>
    """, counter.Markup);
```
<!-- endref -->

Open the pages of an application, in memory with `TestServer` or on a server, with `WebAppBrowser`:

<!-- ref:../../tests/Meziantou.AspNetCore.Testing.Tests/WebAppBrowserTests.cs;region=WebAppBrowser_Usage;format=md-fenced-code-block;dedent=true -->
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

As in Playwright, `GetByRole`, `GetByLabel`, `GetByText` and the other locators find their element again each time they
are used, the actions wait until the element is visible, stable, enabled and not covered, and the assertions of
`Expectations.Expect` retry until they pass. The elements are the DOM elements of `Meziantou.Framework.WebPlatform.Dom`.
The `alert()`, `confirm()` and `prompt()` dialogs are answered in advance by the `DialogController` of the page
(`page.Dialogs.AcceptNext()`, `DismissNext()`, `HandleNext(...)`), which records them; an unexpected dialog fails the test.

## WebAssembly and Blazor WebAssembly

The pages run no WebAssembly by default: the `WebAssembly` namespace is not defined, so `blazor.webassembly.js` fails to
start. Set `WebAppBrowserOptions.WebAssemblyRuntime` (or `ComponentTestHostOptions.WebAssemblyRuntime`) to the
`WasmWebAssemblyRuntime` of the [Meziantou.Framework.WebPlatform.Wasm](https://www.nuget.org/packages/Meziantou.Framework.WebPlatform.Wasm)
package:

<!-- ref:../../tests/Meziantou.AspNetCore.Testing.Tests/WebAppBrowserTests.cs;region=WebAppBrowser_WebAssemblyRuntime;format=md-fenced-code-block;dedent=true -->
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

A Blazor WebAssembly application must also serve the files of its runtime (`_framework/`; for a standalone application,
the `wwwroot` folder of its published output, with `TestDocumentLoaders.FromDirectories` and a fallback file for its
routes). The applications of .NET 10 and .NET 11 run.
