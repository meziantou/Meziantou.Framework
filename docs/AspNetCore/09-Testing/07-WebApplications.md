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
[Locators and assertions](03-Locators.md), [User input](04-UserInput.md) and [Snapshots](05-Snapshots.md).

The pages run their scripts; set [EnableJavaScript](cref:Meziantou.AspNetCore.Testing.WebAppBrowserOptions.EnableJavaScript)
to false to test the pages as without JavaScript. The errors of the scripts are in [ScriptErrors](cref:Meziantou.AspNetCore.Testing.TestPage.ScriptErrors), their console in
[ConsoleMessages](cref:Meziantou.AspNetCore.Testing.TestPage.ConsoleMessages), and the violations of the content security policies (including the ones raised while the document
loads) in [ContentSecurityPolicyViolations](cref:Meziantou.AspNetCore.Testing.TestPage.ContentSecurityPolicyViolations): what the developer tools of a browser report.

The pages of a browser share its [Clipboard](cref:Meziantou.AspNetCore.Testing.WebAppBrowser.Clipboard), as they share
its cookies: read what a page copies, or write what a page pastes. Each browser has its own clipboard, so tests that run
in parallel do not see what the others copy.

## Browsing context options

[ConfigureBrowsingContext](cref:Meziantou.AspNetCore.Testing.WebAppBrowserOptions.ConfigureBrowsingContext) changes any
option of the browsing contexts of the pages ([BrowsingContextOptions](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContextOptions)): the user agent, the languages, the permissions,
the geolocation, the dialogs... It receives the options made from the other settings and returns the options to use,
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

## Waiting

Each action waits until the page is idle: no task in its event loop, and no request in progress. Some requests never
end while a page is open: the server-sent events and the long polling of SignalR do not count
([IsLongLivedRequest](cref:Meziantou.AspNetCore.Testing.WebAppBrowserOptions.IsLongLivedRequest)). When the result
depends on a message the server pushes later (SignalR, Blazor Server), wait for it with an
[assertion](03-Locators.md#assert-the-state-of-the-page) of `Expect`, which retries until it passes.
