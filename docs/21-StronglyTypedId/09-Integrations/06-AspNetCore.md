---
title: ASP.NET Core
description: Bind IDs from routes and query strings in minimal APIs and controllers, and read and write them in JSON bodies.
---

## Bind route and query values

Minimal APIs bind a parameter from a route or a query string value when its type has a static `TryParse` method. Every
ID has one, so a handler can take IDs directly:

<!-- ref:../../../tests/Meziantou.Framework.StronglyTypedId.GeneratorTests/StronglyTypedIdTests.cs;region=StronglyTypedId_AspNetCoreMinimalApi;format=md-fenced-code-block;dedent=true -->
```csharp
var builder = WebApplication.CreateSlimBuilder();
builder.WebHost.UseTestServer(); // Runs the application in memory
await using var app = builder.Build();

// Route and query string values are parsed with the generated TryParse method
app.MapGet("/projects/{id}", (ProjectId id) => $"Project {id.Value}");
app.MapGet("/projects", (ProjectId? owner) => owner is { } value ? $"Projects of {value.Value}" : "All projects");
await app.StartAsync();

using var client = app.GetTestClient();
Assert.Equal("Project 42", await client.GetStringAsync(new Uri("/projects/42", UriKind.Relative)));
Assert.Equal("Projects of 7", await client.GetStringAsync(new Uri("/projects?owner=7", UriKind.Relative)));
Assert.Equal("All projects", await client.GetStringAsync(new Uri("/projects", UriKind.Relative)));

// A value that cannot be parsed is rejected
using var response = await client.GetAsync(new Uri("/projects/abc", UriKind.Relative));
Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
```
<!-- endref -->

- A route value or a query string value that does not parse, or that your
  [validating constructor](../06-ValidationAndCustomMembers.md#validate-the-value) rejects, makes the request fail with
  `400 Bad Request` before the handler runs.
- A nullable parameter (`ProjectId?`) is optional; a non-nullable one is required.
- The example runs the application in memory with `UseTestServer` to check the responses; your application uses
  `WebApplication.CreateBuilder(args)` and Kestrel as usual.

Controllers bind route and query string values to IDs too: MVC model binding converts simple types with their
`TypeConverter`, which the generator provides.

## Read and write JSON bodies

Request and response bodies use System.Text.Json, which uses the
[generated converter](02-SystemTextJson.md): an ID in a body is its value. A Native AOT application serializes with a
source-generated `JsonSerializerContext`, which ignores the converter. See
[Source generation and Native AOT](02-SystemTextJson.md#source-generation-and-native-aot).
