---
title: Blazor applications
description: Test the static rendering, the enhanced navigation and forms, and the Interactive Server components of a Blazor Web App.
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
- The Interactive WebAssembly components are not supported: their runtime does not start in the page.

To test a component alone, without an application, render it with
[ComponentTestHost](02-BlazorComponents.md).
