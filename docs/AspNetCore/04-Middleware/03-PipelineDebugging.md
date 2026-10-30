---
title: Middleware pipeline debugging
description: Capture the middleware pipeline and the endpoints of an application, read them from code or from a debug endpoint.
---

Register the capture before `builder.Build()`, then read the snapshot from code or map the debug endpoint:

<!-- ref:../../../samples/Meziantou.AspNetCore.Samples/AspNetCoreExamples.cs;region=MiddlewarePipelineDebugging_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddMiddlewarePipelineDebugging(); // must run before builder.Build()

var app = builder.Build();

app.UseRouting();
app.MapGet("/hello", static () => "hello");

// Maps GET /_debug/pipeline. By default it responds only in Development and returns 404 elsewhere.
app.MapMiddlewarePipelineDebugEndpoint();

app.MapGet("/pipeline.txt", () =>
{
    var snapshot = app.GetMiddlewarePipelineDebugSnapshot();
    return Results.Text(snapshot.ToString(), "text/plain");
});

app.Run();
```
<!-- endref -->

- `AddMiddlewarePipelineDebugging` registers the capture. Without it, `MapMiddlewarePipelineDebugEndpoint` throws an
  `InvalidOperationException`, in every environment.
- `MapMiddlewarePipelineDebugEndpoint` maps `GET /_debug/pipeline`, which returns the snapshot as JSON. Pass `pattern`
  to use another route.
- `GetMiddlewarePipelineDebugSnapshot` returns the same snapshot from code. Its `ToString()` renders the pipeline and
  the endpoints as an indented text tree.

The snapshot is also available from dependency injection: inject `MiddlewarePipelineDebugInfoProvider` and call
`GetSnapshot()`.

## What a snapshot contains

| Member | Content |
| --- | --- |
| `IsPipelineCaptured` | `false` until the host has built the pipeline |
| `Pipeline.Middlewares` | The middleware, in order: `Name`, `DelegateType` and `DelegateMethod` of the registration, and `Branches` |
| `Endpoints` | Every endpoint: `DisplayName`, `EndpointType`, `RoutePattern`, `Order`, `HttpMethods`, and the `Endpoint` itself |

`Endpoint` is not serialized: it is `null` on a snapshot read back from the JSON of the debug endpoint, and only set on
a snapshot created in the process.

## What is captured

The capture observes the pipeline of the **host**, through an `IStartupFilter`:

- Middleware registered from an `IStartupFilter` or a `Configure` method of a startup class is captured one by one,
  including the branches of `Map`, `MapWhen` and `UseWhen`.
- Middleware registered **directly on `WebApplication`** is not captured one by one. `WebApplication` gives the host a
  single component for its whole pipeline, so it appears as one entry named after that component
  (`...WebApplicationBuilder+WireSourcePipeline.CreateMiddleware`), without its branches.

Names are resolved from the registration delegate. When a name cannot be resolved, the entry falls back to the type
and method of the delegate: a name is never guessed, so the name you see belongs to the middleware it is listed for.

## When to read the snapshot

The pipeline is captured while the host builds it. Before that, `IsPipelineCaptured` is `false` and the pipeline is
empty. This includes an `IHostedService`, which starts before the web host: read the snapshot after the application
has started, for instance in a request or in `IHostApplicationLifetime.ApplicationStarted`.

## Securing the debug endpoint

The endpoint is not authenticated, and it discloses every route, the middleware order and implementation type names.
By default (`developmentOnly: true`) it answers only in Development and returns `404 Not Found` elsewhere. To expose it
in another environment, require an authorization policy:

<!-- ref:../../../samples/Meziantou.AspNetCore.Samples/AspNetCoreExamples.cs;region=MiddlewarePipelineDebugging_SecureEndpoint;format=md-fenced-code-block;dedent=true -->
```csharp
app.MapMiddlewarePipelineDebugEndpoint(developmentOnly: false)
   .RequireAuthorization("DiagnosticsPolicy");
```
<!-- endref -->

The route is registered in every environment, so the returned builder can always be chained.

## Trimming and Native AOT

`MapMiddlewarePipelineDebugEndpoint` serializes the snapshot with reflection-based JSON serialization, so it is not
compatible with trimming or Native AOT (it is annotated with `RequiresUnreferencedCode` and `RequiresDynamicCode`). In
such an application, use `GetMiddlewarePipelineDebugSnapshot` and its `ToString()` instead.
