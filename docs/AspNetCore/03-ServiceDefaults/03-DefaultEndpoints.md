---
title: Default endpoints and middleware
description: The middleware and endpoints added by MapMeziantouDefaultEndpoints, their order, and what to check before production.
---

Call [MapMeziantouDefaultEndpoints](cref:Meziantou.AspNetCore.ServiceDefaults.MeziantouServiceDefaults.MapMeziantouDefaultEndpoints) right after `builder.Build()`, before your own middleware and endpoints:

<!-- ref:../../../samples/Meziantou.AspNetCore.Samples/ServiceDefaultsExamples.cs;region=ServiceDefaults_BasicSetup;format=md-fenced-code-block;dedent=true -->
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

## Middleware, in order

[MapMeziantouDefaultEndpoints](cref:Meziantou.AspNetCore.ServiceDefaults.MeziantouServiceDefaults.MapMeziantouDefaultEndpoints) adds these middleware to the pipeline, so they run before the middleware you add after
the call:

1. **Forwarded headers**, with the headers of `ForwardedHeaders.ForwardedHeaders`.
2. **No-cache default**, when `Caching.SetNoCacheWhenMissingCacheHeaders` is `true`: responses without `Cache-Control`
   get `no-cache,no-store,must-revalidate`. See [Default no-cache header](../04-Middleware/02-NoCache.md).
3. **HTTPS redirection**, when `Https.Enabled` is `true`.
4. **Outside Development**: an exception handler that re-executes the request on `/Error` in a new scope, and HSTS when
   `Https.Enabled` and `Https.HstsEnabled` are `true`.
5. **Antiforgery**, when `AntiForgery.Enabled` is `true`.

The exception handler re-executes failed requests on `/Error`: map an endpoint or a page with that path to render the
error response that production users see.

## Endpoints

| Path | Content |
| --- | --- |
| `/health` | The result of every health check |
| `/alive` | The result of the checks tagged `live`; the built-in `self` check always passes |
| `OpenApi.RoutePattern` (`/openapi/{documentName}.json`) | The OpenAPI document, when `OpenApi.Enabled` is `true` |
| The static assets | When `StaticAssets.Enabled` is `true` and the build produced a `<ApplicationName>.staticwebassets.endpoints.json` manifest next to the application |

Use `/alive` for a liveness probe: it only tells that the process answers. Use `/health` for a readiness probe, and add
your own checks with `builder.Services.AddHealthChecks()`; they run on `/health` unless you tag them `live`.

These endpoints are mapped in every environment and do not require authorization. Put the OpenAPI document behind a
flag or turn it off when the API must not describe itself publicly.

## Forwarded headers

The forwarded headers middleware trusts every proxy: the list of known proxies and networks is cleared, so the
`X-Forwarded-For`, `X-Forwarded-Proto` and `X-Forwarded-Host` headers of any client are applied. This is what an
application behind a load balancer or a container ingress needs, because the proxy address is not known in advance.

When clients can reach the application directly, without going through the proxy, they can set these headers
themselves and change the client IP address, the scheme or the host that the application sees. In that case, restrict
the processed headers, or set `ForwardedHeaders.ForwardedHeaders` to `ForwardedHeaders.None` and add your own
`UseForwardedHeaders` with the known proxies:

<!-- ref:../../../tests/Meziantou.AspNetCore.ServiceDefaults.Tests/ServiceDefaultTests.cs;region=ServiceDefaults_ConfigureOptions;format=md-fenced-code-block;dedent=true -->
```csharp
builder.UseMeziantouConventions(options =>
{
    options.OpenApi.RoutePattern = "/swagger/{documentName}.json";
    options.Https.HstsEnabled = false;
    options.AntiForgery.Enabled = false;
    options.ForwardedHeaders.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ConfigureJsonOptions = json => json.WriteIndented = true;
    options.OpenTelemetry.ConfigureTracing = tracing => tracing.AddSource("MyCompany.*");
});
```
<!-- endref -->

## Static assets

`MapStaticAssets` is called only when the static web assets manifest of the application exists, so an API without
`wwwroot` does not fail. The manifest is produced by the build of a project that has static web assets (a `wwwroot`
folder, Razor class libraries, Blazor).
