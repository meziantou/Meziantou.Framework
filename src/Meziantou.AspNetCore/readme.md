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
builder.Services.AddMiddlewarePipelineDebugging();

var app = builder.Build();

app.UseRouting();
app.MapGet("/hello", static () => "hello");

// Mapped by default only in Development
app.MapMiddlewarePipelineDebugEndpoint();

app.MapGet("/pipeline.txt", () =>
{
    var snapshot = app.GetMiddlewarePipelineDebugSnapshot();
    return Results.Text(snapshot.ToString(), "text/plain");
});

app.Run();
```

### Default no-cache response header

```csharp
using Meziantou.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseMiddleware<NoCacheMiddleware>();

app.MapGet("/", static () => "Hello World!");

app.Run();
```

`NoCacheMiddleware` sets `Cache-Control: no-cache,no-store,must-revalidate` only when the response does not already define `Cache-Control`.
