---
title: Applying the defaults automatically
description: Use the AutoRegister source generator to apply the service defaults to every WebApplicationBuilder.Build() call.
---

`Meziantou.AspNetCore.ServiceDefaults.AutoRegister` is a source generator. Install it next to
`Meziantou.AspNetCore.ServiceDefaults`, and every `WebApplicationBuilder.Build()` call of the project also applies the
service defaults:

```bash
dotnet add package Meziantou.AspNetCore.ServiceDefaults
dotnet add package Meziantou.AspNetCore.ServiceDefaults.AutoRegister
```

<!-- ref:../../../tests/Meziantou.AspNetCore.ServiceDefaults.AutoRegister.GeneratorTests/Tests.cs;region=ServiceDefaultsAutoRegister_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build(); // Also calls UseMeziantouConventions and MapMeziantouDefaultEndpoints

app.MapGet("/", () => "Hello World!");
```
<!-- endref -->

The application has its health checks, OpenAPI document and security middleware without a call to
[UseMeziantouConventions](cref:Meziantou.AspNetCore.ServiceDefaults.MeziantouServiceDefaults.UseMeziantouConventions) or [MapMeziantouDefaultEndpoints](cref:Meziantou.AspNetCore.ServiceDefaults.MeziantouServiceDefaults.MapMeziantouDefaultEndpoints).

## How it works

The generator uses C# interceptors: it replaces each `WebApplicationBuilder.Build()` call of the project with code that

1. calls [TryUseMeziantouConventions()](cref:Meziantou.AspNetCore.ServiceDefaults.MeziantouServiceDefaults.TryUseMeziantouConventions) on the builder,
2. calls `Build()`,
3. calls [MapMeziantouDefaultEndpoints()](cref:Meziantou.AspNetCore.ServiceDefaults.MeziantouServiceDefaults.MapMeziantouDefaultEndpoints) on the application, and returns it.

Because [MapMeziantouDefaultEndpoints](cref:Meziantou.AspNetCore.ServiceDefaults.MeziantouServiceDefaults.MapMeziantouDefaultEndpoints) runs right after `Build()`, its middleware come before everything you add to the
application. The package enables the interceptors it needs in the project; there is nothing to configure.

The generator does nothing when the project does not reference `Meziantou.AspNetCore.ServiceDefaults`. It only
intercepts `WebApplicationBuilder.Build()`: a host built with `Host.CreateApplicationBuilder` is not changed.

## Configuring the defaults

Call [UseMeziantouConventions](cref:Meziantou.AspNetCore.ServiceDefaults.MeziantouServiceDefaults.UseMeziantouConventions) with your options before `Build()`. The generated code calls
[TryUseMeziantouConventions](cref:Meziantou.AspNetCore.ServiceDefaults.MeziantouServiceDefaults.TryUseMeziantouConventions), which does nothing when the conventions are already registered, so your options are kept:

<!-- ref:../../../tests/Meziantou.AspNetCore.ServiceDefaults.AutoRegister.GeneratorTests/Tests.cs;region=ServiceDefaultsAutoRegister_Configure;format=md-fenced-code-block;dedent=true -->
```csharp
var builder = WebApplication.CreateBuilder(args);
builder.UseMeziantouConventions(options => options.OpenApi.Enabled = false);

var app = builder.Build(); // Keeps these options: the generated code only adds the conventions when they are missing
```
<!-- endref -->

Calling [MapMeziantouDefaultEndpoints](cref:Meziantou.AspNetCore.ServiceDefaults.MeziantouServiceDefaults.MapMeziantouDefaultEndpoints) yourself after `Build()` is harmless: the second call does nothing. It does not
move the middleware, though: they are already added by the generated code.

See [Options](02-Options.md) for the available options.
