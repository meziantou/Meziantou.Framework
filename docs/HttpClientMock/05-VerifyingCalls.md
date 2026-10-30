---
title: Verifying calls
description: Count the requests the mock received, capture what the client sent, and return a different response on each call.
---

## Count the requests

`RequestCounter` counts the requests the mock received, in total and per endpoint. It is a service of the mock: get it
from `Application.Services` to check the number of calls once the code under test has run.

<!-- ref:../../tests/Meziantou.Framework.HttpClientMock.Tests/MockTests.cs;region=HttpClientMock_TotalCount;format=md-fenced-code-block;dedent=true -->
```csharp
await using var mock = new HttpClientMock();
mock.MapGet("/status", () => "ok");
mock.MapPost("/events", () => Results.Accepted());

using var httpClient = mock.CreateHttpClient();
_ = await httpClient.GetStringAsync(new Uri("/status", UriKind.Relative));
using var response = await httpClient.PostAsync(new Uri("/events", UriKind.Relative), content: null);

// RequestCounter is a singleton of the mock's service provider
var counter = mock.Application.Services.GetRequiredService<RequestCounter>();
Assert.Equal(2, counter.TotalCount);
```
<!-- endref -->

`TotalCount` counts every request, including the ones that match no endpoint.

## Return a different response on each call

A handler can take the `RequestCounter` as a parameter. `Get()` returns the number of requests received by the endpoint
that handles the current request:

<!-- ref:../../tests/Meziantou.Framework.HttpClientMock.Tests/MockTests.cs;region=HttpClientMock_SequenceOfResponses;format=md-fenced-code-block;dedent=true -->
```csharp
await using var mock = new HttpClientMock();

// The first call fails and the next ones succeed, to test the retry logic of the code under test
mock.MapGet("/flaky", (RequestCounter counter) => counter.Get() is 1 ? Results.StatusCode(503) : Results.Ok("success"));

using var httpClient = mock.CreateHttpClient();
using var first = await httpClient.GetAsync(new Uri("/flaky", UriKind.Relative));
Assert.Equal(HttpStatusCode.ServiceUnavailable, first.StatusCode);

using var second = await httpClient.GetAsync(new Uri("/flaky", UriKind.Relative));
Assert.Equal(HttpStatusCode.OK, second.StatusCode);
```
<!-- endref -->

A request is counted before its handler runs: in the handler, `Get()` returns `1` for the first request. A request whose
handler throws is counted too.

`Get(HttpContext)` and `Get(Endpoint)` return the count of another endpoint, from the `HttpContext` of one of its requests
or from the endpoint itself.

## Capture the requests

To check what the client sent, such as a header, a query string or a body, store it from the handler and assert on it
after the call:

<!-- ref:../../tests/Meziantou.Framework.HttpClientMock.Tests/MockTests.cs;region=HttpClientMock_CaptureRequests;format=md-fenced-code-block;dedent=true -->
```csharp
await using var mock = new HttpClientMock();
var authorizationHeaders = new ConcurrentQueue<string>();
mock.MapGet("/me", (HttpRequest request) =>
{
    authorizationHeaders.Enqueue(request.Headers.Authorization.ToString());
    return "John";
});

using var httpClient = mock.CreateHttpClient();
httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "token");
_ = await httpClient.GetStringAsync(new Uri("/me", UriKind.Relative));

Assert.Equal(["Bearer token"], authorizationHeaders.ToArray());
```
<!-- endref -->

The mock can handle several requests at the same time: use a thread-safe collection, such as `ConcurrentQueue<T>`,
when the code under test sends requests in parallel.
