---
title: Matching requests
description: Match requests by method, path, route parameters and query string, and by scheme, host and port with absolute URLs.
---

Each `Map*` method maps one HTTP method: [MapGet](cref:Meziantou.Framework.HttpClientMock.MapGet), [MapPost](cref:Meziantou.Framework.HttpClientMock.MapPost), [MapPut](cref:Meziantou.Framework.HttpClientMock.MapPut), [MapPatch](cref:Meziantou.Framework.HttpClientMock.MapPatch), [MapDelete](cref:Meziantou.Framework.HttpClientMock.MapDelete), [MapHead](cref:Meziantou.Framework.HttpClientMock.MapHead) and
[MapOptions](cref:Meziantou.Framework.HttpClientMock.MapOptions). The first argument is the URL to match; the second is the handler, either a delegate whose parameters
are bound as in a minimal API, or a `RequestDelegate` that takes the `HttpContext`.

## Paths and route parameters

A relative URL matches the path on any host. The path is an ASP.NET Core route template, so it can contain route
parameters, and the parameters of the handler are bound from the route, the query string, the headers or the body as in a
minimal API:

<!-- ref:../../tests/Meziantou.Framework.HttpClientMock.Tests/MockTests.cs;region=HttpClientMock_RouteParameters;format=md-fenced-code-block;dedent=true -->
```csharp
await using var mock = new HttpClientMock();
mock.MapGet("/users/{id}", (string id) => "user " + id);        // a route parameter
mock.MapGet("/search", (string q) => "results for " + q);       // bound from the query string

using var httpClient = mock.CreateHttpClient();
Assert.Equal("user 42", await httpClient.GetStringAsync(new Uri("/users/42", UriKind.Relative)));
Assert.Equal("results for dotnet", await httpClient.GetStringAsync(new Uri("/search?q=dotnet", UriKind.Relative)));
```
<!-- endref -->

Route constraints (`{id:int}`), optional parameters (`{id?}`) and catch-all parameters (`{**path}`) work the same way.
Route parameters only work in relative URLs: an absolute URL with a route parameter, such as
`https://example.com/users/{id}`, matches no request.

## Query strings

A URL that contains a query string only matches requests with exactly the same query parameters:

<!-- ref:../../tests/Meziantou.Framework.HttpClientMock.Tests/MockTests.cs;region=HttpClientMock_MatchQueryString;format=md-fenced-code-block;dedent=true -->
```csharp
await using var mock = new HttpClientMock();
mock.MapGet("/todos", () => "all");
mock.MapGet("/todos?status=done", () => "done");
mock.MapGet("/todos?status=open&sort=date", () => "open, by date");

using var httpClient = mock.CreateHttpClient();
Assert.Equal("done", await httpClient.GetStringAsync(new Uri("/todos?status=done", UriKind.Relative)));

// The order of the parameters does not matter
Assert.Equal("open, by date", await httpClient.GetStringAsync(new Uri("/todos?sort=date&status=open", UriKind.Relative)));

// A query string that no route matches exactly falls back to the route without a query string
Assert.Equal("all", await httpClient.GetStringAsync(new Uri("/todos?status=open", UriKind.Relative)));
Assert.Equal("all", await httpClient.GetStringAsync(new Uri("/todos", UriKind.Relative)));
```
<!-- endref -->

- The request must have the same set of parameters, with the same values: an extra or a missing parameter does not
  match.
- The order of the parameters does not matter. When a parameter is repeated (`?tag=a&tag=b`), the request must repeat it
  with the same values, in any order.
- An endpoint with a query string takes precedence over the endpoint with the same path and no query string. The
  endpoint without a query string matches every request to the path, whatever its query string.

## Absolute URLs

An absolute URL also matches the scheme, the host and the port of the request:

<!-- ref:../../tests/Meziantou.Framework.HttpClientMock.Tests/MockTests.cs;region=HttpClientMock_MatchAbsoluteUrl;format=md-fenced-code-block;dedent=true -->
```csharp
await using var mock = new HttpClientMock();
mock.MapGet("https://api.example.com/status", () => "api");
mock.MapGet("https://www.example.com/status", () => "www");
mock.MapGet("http://www.example.com/status", () => "www over http");
mock.MapGet("/status", () => "any other host");

using var httpClient = mock.CreateHttpClient();
Assert.Equal("api", await httpClient.GetStringAsync(new Uri("https://api.example.com/status")));
Assert.Equal("www", await httpClient.GetStringAsync(new Uri("https://www.example.com/status")));
Assert.Equal("www over http", await httpClient.GetStringAsync(new Uri("http://www.example.com/status")));
Assert.Equal("any other host", await httpClient.GetStringAsync(new Uri("https://cdn.example.com/status")));
```
<!-- endref -->

- `https://api.example.com/status` only matches HTTPS requests to `api.example.com`. Write the port when the service
  uses a specific one: `http://localhost:5000/status`.
- An endpoint with a scheme takes precedence over a relative one, so `/status` here only gets the requests that no
  absolute URL matches.
- An absolute URL can contain a query string, matched as above: `https://example.com/path?search=text`.

Use absolute URLs when the code under test calls several services through the same client, or when one mock stands for
several hosts.

A URL with a fragment (`#section`) is rejected with an `ArgumentException`: the fragment is not sent to the server.

## Use the full ASP.NET Core routing

The [Application](cref:Meziantou.Framework.HttpClientMock.Application) property returns the `WebApplication` that runs the mock. Use it for what the `Map*` methods do not
cover, such as `Application.MapFallback(...)` or `Application.MapMethods(...)` with several methods. Endpoints mapped
on [Application](cref:Meziantou.Framework.HttpClientMock.Application) directly do not get the scheme and query string matching described above.

Every `Map*` method returns the `IEndpointConventionBuilder` of the endpoint, as in ASP.NET Core.
