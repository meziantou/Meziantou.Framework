---
title: Options
description: Turn off or customize the features configured by the service defaults with MeziantouServiceDefaultsOptions.
---

Pass a delegate to `UseMeziantouConventions` to change the defaults:

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

The delegate runs immediately, during the `UseMeziantouConventions` call, and the resulting
`MeziantouServiceDefaultsOptions` is registered as a singleton. `MapMeziantouDefaultEndpoints` reads the same
instance, so the options of the builder decide what the application maps.

## All options

| Option | Default | Effect |
| --- | --- | --- |
| `ValidateDependencyContainersOnStartup` | `true` | Builds the service provider with `ValidateOnBuild` and `ValidateScopes` |
| `Https.Enabled` | `true` | Adds `UseHttpsRedirection` |
| `Https.HstsEnabled` | `true` | Adds `UseHsts` outside Development, when `Https.Enabled` is also `true` |
| `OpenApi.Enabled` | `true` | Registers OpenAPI and maps the document |
| `OpenApi.ConfigureOpenApi` | `null` | Configures the `OpenApiOptions` of the document |
| `OpenApi.RoutePattern` | `/openapi/{documentName}.json` | The route of the OpenAPI document |
| `OpenTelemetry.ConfigureLogging` | `null` | Configures the `OpenTelemetryLoggerOptions`, after the defaults |
| `OpenTelemetry.ConfigureMetrics` | `null` | Configures the `MeterProviderBuilder`, after the defaults |
| `OpenTelemetry.ConfigureTracing` | `null` | Configures the `TracerProviderBuilder`, after the defaults |
| `ConfigureJsonOptions` | `null` | Configures the `JsonSerializerOptions` of minimal APIs and controllers, after the [conventions](04-JsonConventions.md) |
| `AntiForgery.Enabled` | `true` | Registers antiforgery and adds `UseAntiforgery` |
| `StaticAssets.Enabled` | `true` | Maps the static assets when the project has some |
| `ForwardedHeaders.ForwardedHeaders` | `XForwardedFor`, `XForwardedProto` and `XForwardedHost` | The headers processed by the forwarded headers middleware |
| `Caching.SetNoCacheWhenMissingCacheHeaders` | `true` | Adds the [no-cache middleware](../04-Middleware/02-NoCache.md) |

## Common changes

- **The application runs behind a proxy that terminates TLS** and forwards plain HTTP: keep `Https.Enabled` and make
  sure the proxy sends `X-Forwarded-Proto`, or set `Https.Enabled = false` when the proxy already redirects to HTTPS.
- **The application is reachable without a proxy**: restrict `ForwardedHeaders.ForwardedHeaders` or set it to
  `ForwardedHeaders.None`; see [Forwarded headers](03-DefaultEndpoints.md#forwarded-headers).
- **Your own `ActivitySource` or `Meter`** has a name that does not start with `Meziantou.`: add it with
  `ConfigureTracing` (`AddSource`) or `ConfigureMetrics` (`AddMeter`). The source named after the application
  (`IHostEnvironment.ApplicationName`) is already traced.
- **An API without OpenAPI**: set `OpenApi.Enabled = false`.
- **Long-lived cacheable responses everywhere**: set `Caching.SetNoCacheWhenMissingCacheHeaders = false`, or set
  `Cache-Control` on those responses.
- **A registration that cannot be validated at startup**, such as a service built from a factory that needs a running
  host: set `ValidateDependencyContainersOnStartup = false`. Keep it on otherwise: it turns a missing registration into
  an error on startup instead of on the first request that needs it.
