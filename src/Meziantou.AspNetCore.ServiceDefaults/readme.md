# Meziantou.AspNetCore.ServiceDefaults

A comprehensive ASP.NET Core library that provides opinionated defaults and best practices for building production-ready web applications. This library configures common features like OpenTelemetry, health checks, resilience, service discovery, and security settings with sensible defaults.

## Usage

### Basic Setup

Add the service defaults to your ASP.NET Core application (the extension methods are in the
`Meziantou.AspNetCore.ServiceDefaults` namespace):

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

### Try Pattern

Use `TryUseMeziantouConventions` to avoid registering the service defaults multiple times:

<!-- ref:../../tests/Meziantou.AspNetCore.ServiceDefaults.Tests/ServiceDefaultTests.cs;region=ServiceDefaults_TryPattern;format=md-fenced-code-block;dedent=true -->
```csharp
builder.UseMeziantouConventions();
builder.TryUseMeziantouConventions(); // Will not register again
```
<!-- endref -->

## Validation

The library validates that `MapMeziantouDefaultEndpoints()` is called. If you forget to call it, an `InvalidOperationException` will be thrown at startup.
