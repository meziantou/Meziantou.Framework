---
title: JavaScript interop
description: Set up the results of the JavaScript calls of the components, or run the scripts of the page.
---

By default, the page runs no JavaScript: set up the calls of the components with
[JSInterop](cref:Meziantou.AspNetCore.Testing.Components.ComponentTestHost.JSInterop), and check the calls they made:

<!-- ref:../../../tests/Meziantou.AspNetCore.Testing.Tests/ComponentTestHostTests.cs;region=ComponentTestHost_JSInterop;format=md-fenced-code-block;dedent=true -->
```csharp
await using var host = new ComponentTestHost();
host.JSInterop.Setup("getGreeting", invocation => "Hello " + invocation.Arguments[1]);
var component = await host.RenderAsync<FocusInput>();

await component.GetByRole("button", "Focus").ClickAsync();
Assert.Same(component.Find("#focus-target"), host.Document.ActiveElement);

await component.GetByRole("button", "Call JS").ClickAsync();
Assert.Equal("Hello Ada", component.Find("#result").TextContent);

var invocation = Assert.Single(host.JSInterop.GetInvocations("getGreeting"));
Assert.Same(component.Find("#focus-target"), host.GetElement((ElementReference)invocation.Arguments[0]!));
```
<!-- endref -->

A call that is not set up returns the default value of its type, or throws an
[UnplannedJSInvocationException](cref:Meziantou.AspNetCore.Testing.Components.JSInterop.UnplannedJSInvocationException)
in [JSInteropMode.Strict](cref:Meziantou.AspNetCore.Testing.Components.JSInterop.JSInteropMode.Strict).
`ElementReference.FocusAsync()` focuses the element without set up.

## Run the scripts of the page

With [EnableJavaScript](cref:Meziantou.AspNetCore.Testing.Components.ComponentTestHostOptions.EnableJavaScript), the page
runs its scripts, and the calls that are not set up call its functions. The element references are the elements of the
page, the `IJSObjectReference` results and the `import()` of the JavaScript modules work, and the scripts call the .NET
methods of a `DotNetObjectReference` with `invokeMethodAsync()`:

<!-- ref:../../../tests/Meziantou.AspNetCore.Testing.Tests/ComponentTestHostTests.cs;region=ComponentTestHost_PageJavaScript;format=md-fenced-code-block;dedent=true -->
```csharp
await using var host = new ComponentTestHost(new ComponentTestHostOptions
{
    EnableJavaScript = true,
    HostPage = """
        <!DOCTYPE html>
        <html>
        <head>
        <script>
          window.getGreeting = async (element, name) => element.id + ': Hello ' + name;
        </script>
        </head>
        <body></body>
        </html>
        """,
});
var component = await host.RenderAsync<FocusInput>();

await component.GetByRole("button", "Call JS").ClickAsync();
Assert.Equal("focus-target: Hello Ada", component.Find("#result").TextContent);
```
<!-- endref -->

The scripts run in the process of the tests: only load scripts you trust.
