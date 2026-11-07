---
title: Responses
description: Return objects, raw JSON or XML, status codes and headers, build a response from the request, or forward the request to the real service.
---

A handler returns the response as in a minimal API: a string is sent as text, an object is serialized as JSON, and an
`IResult` from `Results` sets the status code, the headers and the body (`Results.Ok`, `Results.NotFound`,
`Results.StatusCode(503)`, `Results.Text`, `Results.Redirect`...).

## Raw JSON and XML

To return the exact text the real service returns, without defining a type for it, use `Results.Extensions.RawJson`
and `Results.Extensions.RawXml`:

<!-- ref:../../tests/Meziantou.Framework.HttpClientMock.Tests/MockTests.cs;region=HttpClientMock_RawContent;format=md-fenced-code-block;dedent=true -->
```csharp
await using var mock = new HttpClientMock();
mock.MapGet("/users/1", () => Results.Extensions.RawJson("""{"id":1,"name":"John"}"""));
mock.MapGet("/users/2", () => Results.Extensions.RawJson("""{"error":"not found"}""", statusCode: 404));
mock.MapGet("/feed", () => Results.Extensions.RawXml("<feed />"));

using var httpClient = mock.CreateHttpClient();
using var response = await httpClient.GetAsync(new Uri("/users/2", UriKind.Relative));
Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
Assert.Equal("""{"error":"not found"}""", await response.Content.ReadAsStringAsync());
```
<!-- endref -->

- [RawJson](cref:Meziantou.Framework.ResultExtensions.RawJson) sends the content with the `application/json` content type, and [RawXml](cref:Meziantou.Framework.ResultExtensions.RawXml) with `text/xml`.
- Both take an optional `statusCode`, `200` by default, and an optional `encoding`.

## Status codes and headers

A `RequestDelegate` handler writes the response itself, through the `HttpContext`:

<!-- ref:../../tests/Meziantou.Framework.HttpClientMock.Tests/MockTests.cs;region=HttpClientMock_RequestDelegate;format=md-fenced-code-block;dedent=true -->
```csharp
await using var mock = new HttpClientMock();
mock.MapGet("/report", async context =>
{
    context.Response.StatusCode = StatusCodes.Status200OK;
    context.Response.ContentType = "text/csv";
    context.Response.Headers.ETag = "\"v1\"";
    await context.Response.WriteAsync("id,name\n1,John\n", context.RequestAborted);
});

using var httpClient = mock.CreateHttpClient();
using var response = await httpClient.GetAsync(new Uri("/report", UriKind.Relative));
Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
Assert.Equal("\"v1\"", response.Headers.ETag?.Tag);
Assert.Equal("id,name\n1,John\n", await response.Content.ReadAsStringAsync());
```
<!-- endref -->

A delegate handler can also take the `HttpContext` or the `HttpResponse` as a parameter.

## Build the response from the request

The parameters of the handler are bound from the request. The body of a `POST`, `PUT` or `PATCH` request is bound to a
parameter of a complex type:

<!-- ref:../../tests/Meziantou.Framework.HttpClientMock.Tests/MockTests.cs;region=HttpClientMock_ReadRequestBody;format=md-fenced-code-block;dedent=true -->
```csharp
await using var mock = new HttpClientMock();

// The body of a POST request is bound to a parameter of a complex type, as in a minimal API
mock.MapPost("/todos", (Dictionary<string, string> todo) => Results.Created("/todos/1", new { id = 1, title = todo["title"] }));

using var httpClient = mock.CreateHttpClient();
using var response = await httpClient.PostAsJsonAsync(new Uri("/todos", UriKind.Relative), new { title = "Write the documentation" });
Assert.Equal(HttpStatusCode.Created, response.StatusCode);
Assert.Equal("/todos/1", response.Headers.Location?.OriginalString);
Assert.Equal("""{"id":1,"title":"Write the documentation"}""", await response.Content.ReadAsStringAsync());
```
<!-- endref -->

A handler can also take the `HttpRequest`, to read headers or the raw body. To check what the client sent, see
[Verifying calls](05-VerifyingCalls.md).

## Forward requests to the real service

A mock can answer some requests itself and send the others to the real service:

- [ForwardUnknownRequestsToUpstream()](cref:Meziantou.Framework.HttpClientMock.ForwardUnknownRequestsToUpstream) forwards every request that no endpoint matches.
- `Results.Extensions.ForwardToUpstream()`, returned by a handler, forwards the request of that endpoint.

<!-- ref:../../tests/Meziantou.Framework.HttpClientMock.Tests/MockTests.cs;region=HttpClientMock_ForwardUnknownRequests;format=md-fenced-code-block;dedent=true -->
```csharp
// The upstream server is another mock here, so the example does not depend on the network
await using var upstream = new HttpClientMock();
upstream.MapGet("https://example.com/users", () => "users from the upstream server");
upstream.MapGet("https://example.com/orders", () => "orders from the upstream server");
using var upstreamHttpClient = upstream.CreateHttpClient();

await using var mock = new HttpClientMock();
mock.MapGet("https://example.com/users", () => "mocked users");
mock.ForwardUnknownRequestsToUpstream(upstreamHttpClient);

using var httpClient = mock.CreateHttpClient();
Assert.Equal("mocked users", await httpClient.GetStringAsync(new Uri("https://example.com/users")));
Assert.Equal("orders from the upstream server", await httpClient.GetStringAsync(new Uri("https://example.com/orders")));
```
<!-- endref -->

The request is sent to the URL it was sent to, with its method, headers and body, and the response of the upstream
server is returned to the client. The hop-by-hop headers, such as `Connection` or `Transfer-Encoding`, are not forwarded.

The overloads without an argument send the request with an `HttpClient` from the `IHttpClientFactory` of the mock, which
reaches the network. The overloads that take an `HttpClient` send it with that client instead, as in the example.

Forwarded requests go to a real service, which can fail. To retry them, configure resilience on the clients that the
`IHttpClientFactory` of the mock creates, here with the `Microsoft.Extensions.Http.Resilience` package. This applies to
the overloads without an `HttpClient` argument:

<!-- ref:../../tests/Meziantou.Framework.HttpClientMock.Tests/MockTests.cs;region=HttpClientMock_ResiliencePolicies;format=md-fenced-code-block;dedent=true -->
```csharp
await using var mock = new HttpClientMock(configureLogging: null, configureServices: services =>
{
    services.ConfigureHttpClientDefaults(services => services.AddStandardResilienceHandler());
});
```
<!-- endref -->

`configureServices` configures the services of the mock's application. See
[Logging and services](07-LoggingAndServices.md).
