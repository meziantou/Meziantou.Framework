---
title: Overview
description: What UseMeziantouConventions and MapMeziantouDefaultEndpoints configure in an ASP.NET Core application.
---

`Meziantou.AspNetCore.ServiceDefaults` gives a web application the setup that production needs, in two calls:
[UseMeziantouConventions](cref:Meziantou.AspNetCore.ServiceDefaults.MeziantouServiceDefaults.UseMeziantouConventions) on the builder registers the services, and [MapMeziantouDefaultEndpoints](cref:Meziantou.AspNetCore.ServiceDefaults.MeziantouServiceDefaults.MapMeziantouDefaultEndpoints) on the application
adds the middleware and the endpoints.

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

The extension methods are in the [Meziantou.AspNetCore.ServiceDefaults](cref:Meziantou.AspNetCore.ServiceDefaults) namespace. [UseMeziantouConventions](cref:Meziantou.AspNetCore.ServiceDefaults.MeziantouServiceDefaults.UseMeziantouConventions) works on
any `IHostApplicationBuilder`; [MapMeziantouDefaultEndpoints](cref:Meziantou.AspNetCore.ServiceDefaults.MeziantouServiceDefaults.MapMeziantouDefaultEndpoints) extends `WebApplication`.

## What the builder gets

[UseMeziantouConventions](cref:Meziantou.AspNetCore.ServiceDefaults.MeziantouServiceDefaults.UseMeziantouConventions) configures:

- **Dependency injection validation**: the service provider is built with `ValidateOnBuild` and `ValidateScopes`, so a
  missing registration or a scoped service injected into a singleton makes `builder.Build()` throw instead of failing
  on the first request.
- **OpenTelemetry**: logs (with the formatted message and the scopes), metrics (ASP.NET Core, `HttpClient`, the .NET
  runtime and the `Meziantou.*` meters) and traces (ASP.NET Core with SignalR, `HttpClient`, the `Meziantou.*` sources
  and a source named after the application). The `/health` and `/alive` requests are not traced. The service name is
  `OTEL_SERVICE_NAME` when it is set, the application name otherwise, and the service version is the version of the
  entry assembly. The OTLP exporter is enabled when `OTEL_EXPORTER_OTLP_ENDPOINT` is set.
- **Health checks**: a `self` check tagged `live`.
- **OpenAPI**: `AddOpenApi`, for the document served by [MapMeziantouDefaultEndpoints](cref:Meziantou.AspNetCore.ServiceDefaults.MeziantouServiceDefaults.MapMeziantouDefaultEndpoints).
- **HTTP clients**: service discovery, the standard resilience handler (retries, timeouts, circuit breaker) and a
  `User-Agent` header named after the application, on every client created by `IHttpClientFactory`.
- **JSON**: camelCase names, string enums and the other conventions described in [JSON conventions](04-JsonConventions.md),
  for minimal APIs and for controllers.
- **Problem details and validation**: `AddProblemDetails` and `AddValidation`.
- **Antiforgery**: `AddAntiforgery`.
- **Kestrel**: the `Server` response header is no longer sent.

Each feature that can be turned off has an option; see [Options](02-Options.md).

## What the application gets

[MapMeziantouDefaultEndpoints](cref:Meziantou.AspNetCore.ServiceDefaults.MeziantouServiceDefaults.MapMeziantouDefaultEndpoints) adds forwarded headers, the no-cache default, HTTPS redirection, an exception handler
and HSTS outside Development, and antiforgery, then maps `/health`, `/alive`, the static assets and the OpenAPI
document. The order and the details are in [Default endpoints and middleware](03-DefaultEndpoints.md).

## Forgetting a call

The two calls go together. When [MapMeziantouDefaultEndpoints](cref:Meziantou.AspNetCore.ServiceDefaults.MeziantouServiceDefaults.MapMeziantouDefaultEndpoints) is not called, the application throws an
`InvalidOperationException` that names the missing call when it starts, rather than running without its health checks
and security middleware.

## Calling it more than once

[UseMeziantouConventions](cref:Meziantou.AspNetCore.ServiceDefaults.MeziantouServiceDefaults.UseMeziantouConventions) registers everything again each time it is called. Code that cannot know whether the
conventions are already registered, such as a shared extension method, calls [TryUseMeziantouConventions](cref:Meziantou.AspNetCore.ServiceDefaults.MeziantouServiceDefaults.TryUseMeziantouConventions), which does
nothing when they are:

<!-- ref:../../../tests/Meziantou.AspNetCore.ServiceDefaults.Tests/ServiceDefaultTests.cs;region=ServiceDefaults_TryPattern;format=md-fenced-code-block;dedent=true -->
```csharp
builder.UseMeziantouConventions();
builder.TryUseMeziantouConventions(); // Will not register again
```
<!-- endref -->

When [TryUseMeziantouConventions](cref:Meziantou.AspNetCore.ServiceDefaults.MeziantouServiceDefaults.TryUseMeziantouConventions) does nothing, its options are ignored: the options of the first call apply.
[MapMeziantouDefaultEndpoints](cref:Meziantou.AspNetCore.ServiceDefaults.MeziantouServiceDefaults.MapMeziantouDefaultEndpoints) can be called more than once; the calls after the first do nothing.

## Applying the defaults without writing the calls

The `Meziantou.AspNetCore.ServiceDefaults.AutoRegister` package adds both calls to every `WebApplicationBuilder.Build()`
of your project. See [Applying the defaults automatically](05-AutoRegister.md).

## In this group

- [Options](02-Options.md): turn features off and customize OpenAPI, OpenTelemetry and JSON.
- [Default endpoints and middleware](03-DefaultEndpoints.md): the middleware order, the health endpoints and what to
  check before going to production.
- [JSON conventions](04-JsonConventions.md): how requests and responses are serialized.
- [Applying the defaults automatically](05-AutoRegister.md): the source generator that writes the calls for you.
