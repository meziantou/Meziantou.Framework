---
title: Blazor components
description: Render a component in a browser page, click and type like a user, and check the result.
---

[ComponentTestHost](cref:Meziantou.AspNetCore.Testing.Components.ComponentTestHost), in the
[Meziantou.AspNetCore.Testing.Components](cref:Meziantou.AspNetCore.Testing.Components) namespace, renders Blazor components in a
browser page without an application. The components are interactive, as in Blazor WebAssembly, and their output is a
real DOM: the CSS of the page applies, the layout computes the boxes, and the input goes through the hit testing of the
page. A test clicks a button only when a user could click it.

[ComponentTestHost](cref:Meziantou.AspNetCore.Testing.Components.ComponentTestHost) is the browser. Render a component, act on
its elements, and check the DOM:

<!-- ref:../../../tests/Meziantou.AspNetCore.Testing.Tests/ComponentTestHostTests.cs;region=ComponentTestHost_Counter;format=md-fenced-code-block;dedent=true -->
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

- [RenderAsync](cref:Meziantou.AspNetCore.Testing.Components.ComponentTestHost.RenderAsync) renders the component and waits
  until it is idle: its asynchronous lifecycle methods are completed. [Render](cref:Meziantou.AspNetCore.Testing.Components.ComponentTestHost.Render)
  returns after the first render, to check a loading state.
- Find the elements as a user finds them, by role, label or text (`GetByRole`, `GetByLabel`, `GetByText`...): these
  [locators](03-Locators.md) search the elements of the component, and the actions wait until the element is actionable.
  `Find` returns the DOM element of WebPlatform that matches a CSS selector now.
- Each action waits until the components are idle again.
- [ComponentParameterCollectionBuilder](cref:Meziantou.AspNetCore.Testing.Components.ComponentParameterCollectionBuilder{TComponent})
  sets the parameters, with their names checked by the compiler. An `EventCallback` parameter takes an `Action` or an
  `async` lambda: the component awaits the task of an `async` lambda, so the actions wait until it completes.

## Services

Add the services of the components to [Services](cref:Meziantou.AspNetCore.Testing.Components.ComponentTestHost.Services)
before the first render. The host registers `IJSRuntime`, `NavigationManager` and the services of the router.

## Waiting

The continuations of the components run on the event loop of the page, and the test runs the event loop while it waits:

<!-- ref:../../../tests/Meziantou.AspNetCore.Testing.Tests/ComponentTestHostTests.cs;region=ComponentTestHost_Wait;format=md-fenced-code-block;dedent=true -->
```csharp
await using var host = new ComponentTestHost();
var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
var loader = host.Render<AsyncLoader>(parameters => parameters.Add(c => c.Load, () => completion.Task));
Assert.Equal("Loading...", loader.Find("p").TextContent);

// The assertion runs the event loop until the component renders the data
completion.SetResult("Loaded");
await Expect(loader.Locator(".data")).ToHaveTextAsync("Loaded");
```
<!-- endref -->

The [assertions](03-Locators.md#assert-the-state-of-the-page) of `Expect` retry until they pass. For other conditions,
`WaitForAsync`, `WaitForAssertionAsync` and `WaitForIdleAsync` run the event loop too. They all fail after
[DefaultTimeout](cref:Meziantou.AspNetCore.Testing.TestPage.DefaultTimeout) (10 seconds).

An exception that a component does not handle (no error boundary) fails the action that caused it with a
[ComponentException](cref:Meziantou.AspNetCore.Testing.Components.ComponentException).

## Routing

The host intercepts the clicks on the links of the base URL once a `Router` is rendered, so a whole application can be
tested:

<!-- ref:../../../tests/Meziantou.AspNetCore.Testing.Tests/ComponentTestHostTests.cs;region=ComponentTestHost_Routing;format=md-fenced-code-block;dedent=true -->
```csharp
await using var host = new ComponentTestHost();
var app = await host.RenderAsync<App>();
Assert.Equal("Home", app.Find("h1").TextContent);
Assert.Equal("active", app.GetByText("Home link").GetAttribute("class"));

await app.GetByRole("link", "About").ClickAsync();
Assert.Equal("About", app.Find("h1").TextContent);
Assert.Equal("https://localhost/about", host.NavigationManager.Uri);
Assert.Equal("https://localhost/about", host.BrowsingContext.Window.Location.Href);

await app.GetByRole("button", "Back").ClickAsync();
Assert.Equal("Home", app.Find("h1").TextContent);

await host.NavigateAsync("/missing");
Assert.Equal("Not found", app.Find("p").TextContent);
```
<!-- endref -->

## Pages

- [Locators and assertions](03-Locators.md): find the elements, act on them once they are actionable, and assert their state.
- [User input](04-UserInput.md): the mouse, the keyboard and the touch screen.
- [Snapshots](05-Snapshots.md): the HTML, the accessibility tree and the screenshots.
- [JavaScript interop](06-JavaScriptInterop.md): set up the results of the calls, or run the scripts of the page.
