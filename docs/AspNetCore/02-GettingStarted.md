---
title: Getting started
description: Install the ASP.NET Core packages, set up the service defaults of a web application, and add Blazor components.
---

## Prerequisites

- An ASP.NET Core application for the server packages, or a Blazor application for the component packages.

## Install a package

Add the package you need to your project, for instance the service defaults:

```bash
dotnet add package Meziantou.AspNetCore.ServiceDefaults
```

The other packages are installed the same way: `Meziantou.AspNetCore`, `Meziantou.AspNetCore.Authentication.HttpBasic`,
`Meziantou.AspNetCore.Mvc`, `Meziantou.AspNetCore.Components`, `Meziantou.AspNetCore.Components.LogViewer`,
`Meziantou.AspNetCore.Components.WebAssembly` and `Meziantou.AspNetCore.ServiceDefaults.AutoRegister`.

## Set up a web application

Call `UseMeziantouConventions` on the builder, then `MapMeziantouDefaultEndpoints` on the application (both are in
the [Meziantou.AspNetCore.ServiceDefaults](cref:Meziantou.AspNetCore.ServiceDefaults) namespace):

<!-- ref:../../samples/Meziantou.AspNetCore.Samples/ServiceDefaultsExamples.cs;region=ServiceDefaults_BasicSetup;format=md-fenced-code-block;dedent=true -->
```csharp
var builder = WebApplication.CreateBuilder(args);

// Add Meziantou service defaults
builder.UseMeziantouConventions();

var app = builder.Build();

// Map default endpoints (health checks, OpenAPI, etc.)
app.MapMeziantouDefaultEndpoints();

app.MapGet("/", () => "Hello World!");

app.Run();
```
<!-- endref -->

- `UseMeziantouConventions` registers OpenTelemetry, health checks, OpenAPI, service discovery, HTTP client
  resilience, problem details and the JSON conventions.
- `MapMeziantouDefaultEndpoints` adds the middleware (forwarded headers, HTTPS redirection, error handling, HSTS,
  antiforgery) and maps `/health`, `/alive` and the OpenAPI document. The application refuses to start when you forget
  it.

The application now answers `Healthy` on `/health` and serves its OpenAPI document on `/openapi/v1.json`. See
[Service defaults](03-ServiceDefaults/01-Overview.md) for everything these two calls configure.

## Add Blazor components

Import the namespace in `_Imports.razor`:

<!-- ref:../../samples/BlazorAppSample/ReadmeExamples/_Imports.razor;format=md-fenced-code-block -->
```razor
@using Meziantou.AspNetCore.Components
```
<!-- endref -->

Register the browser services you use in `Program.cs`:

<!-- ref:../../samples/BlazorAppSample/ReadmeExamples/ComponentsReadmeExamples.cs;region=Components_RegisterServices;format=md-fenced-code-block;dedent=true -->
```csharp
builder.Services.AddClipboard();
builder.Services.AddQueryStringParameters();
builder.Services.AddTimeZoneServices();
```
<!-- endref -->

Then use a component in a page. `Repeater` renders a template for each item, a separator between them, and other
templates while the items load or when there are none:

<!-- ref:../../samples/BlazorAppSample/ReadmeExamples/RepeaterExample.razor;format=md-fenced-code-block -->
```razor
<Repeater Items="items">
    <ItemTemplate Context="item">
        <span>@item</span>
    </ItemTemplate>
    <ItemSeparatorTemplate>, </ItemSeparatorTemplate>
    <EmptyTemplate><em>No item</em></EmptyTemplate>
    <LoadingTemplate><em>Loading...</em></LoadingTemplate>
</Repeater>

@code {
    private List<string>? items = new() { "A", "B", "C" };
}
```
<!-- endref -->

See [Blazor components](07-BlazorComponents/01-Overview.md) for the other components and services.

## Next steps

- [Service defaults](03-ServiceDefaults/01-Overview.md): what is configured, and how to change it.
- [HTTP Basic authentication](05-HttpBasicAuthentication/01-Overview.md): protect an endpoint with a username and a
  password.
- [MVC tag helpers](06-MvcTagHelpers.md): the tag helpers for MVC views and Razor Pages.
