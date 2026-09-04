# Meziantou.AspNetCore

Helpers for ASP.NET Core middleware diagnostics and response cache-control behavior.

## Features

- Capture and inspect the middleware pipeline
- Expose a debug endpoint returning the pipeline and endpoints as JSON
- Access middleware pipeline snapshots from code
- Add a default non-cacheable `Cache-Control` response header when none is set

## Usage

### Middleware pipeline debugging

```csharp
using Meziantou.AspNetCore.Diagnostics;

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

The default route is `/_debug/pipeline`; pass a different `pattern` to change it.

#### What is captured

Capture works through an `IStartupFilter`, which observes the **host** pipeline. This matters:

- Middleware registered from an `IStartupFilter` or a classic `Configure` method is captured individually,
  including `Map` / `MapWhen` / `UseWhen` branches.
- Middleware registered **directly on `WebApplication`** is *not* captured individually. `WebApplication` hands the
  host a single component representing its entire pipeline, so it appears as one entry named after that component
  (`...WebApplicationBuilder+WireSourcePipeline.CreateMiddleware`). Its branches are not visible either.

Names are resolved on a best-effort basis from the registration delegate. When a name cannot be resolved it degrades
to the delegate's declaring type and method — it is never inferred from unrelated state, so a name you see belongs to
the middleware it is listed against.

#### Snapshot timing

The pipeline is captured while the host builds it. A snapshot taken earlier reports
`IsPipelineCaptured == false` and an empty pipeline — including inside an `IHostedService`, which starts *before* the
web host. Read the snapshot after the host has started.

#### Securing the debug endpoint

The endpoint is **not authenticated**. It discloses every registered route, the middleware order and implementation
type names. The default (`developmentOnly: true`) responds only in Development. To expose it elsewhere, gate it:

```csharp
app.MapMiddlewarePipelineDebugEndpoint(developmentOnly: false)
   .RequireAuthorization("DiagnosticsPolicy");
```

Note that `MiddlewarePipelineDebugEndpoint.Endpoint` is excluded from serialization, so it is `null` on a snapshot
deserialized from the endpoint's JSON. It is only populated on a snapshot created in-process.

### Default no-cache response header

```csharp
using Meziantou.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseNoCache();

app.MapGet("/", static () => "Hello World!");

app.Run();
```

`UseNoCache` sets `Cache-Control: no-cache,no-store,must-revalidate` only when the response does not already define
`Cache-Control` (an empty value counts as not defined). Responses that set their own `Cache-Control` are never
modified, and if the response has already started the header is left alone rather than failing the request.

Two consequences worth knowing before adding it:

- Register it **after** anything serving cacheable content, such as `UseStaticFiles`. The static file middleware sets
  `ETag` and `Last-Modified` but no `Cache-Control`, so an earlier registration marks every static asset
  non-cacheable.
- Because the default includes `no-store`, responses it defaults are not stored by `UseResponseCaching`,
  `UseOutputCache` or a CDN.
