---
title: Getting started
description: Install the package, map an endpoint, create an HttpClient that calls the mock, and dispose the mock.
---

## Install the package

Add the `Meziantou.Framework.HttpClientMock` package to a test project:

<InstallNuGetPackage Id="Meziantou.Framework.HttpClientMock" />

The package references the ASP.NET Core shared framework and the test server, so the project does not need to be a web
project. The examples of this documentation need `using Meziantou.Framework;` and `using Microsoft.AspNetCore.Http;`.

## Mock an endpoint

Create the mock, map the endpoints the code under test calls, and create an `HttpClient` from the mock:

<!-- ref:../../tests/Meziantou.Framework.HttpClientMock.Tests/MockTests.cs;region=HttpClientMock_Example;format=md-fenced-code-block;dedent=true -->
```csharp
await using var mock = new HttpClientMock();
mock.MapGet("/api/users", () => Results.Ok(new { name = "John" }));
using var httpClient = mock.CreateHttpClient();
using var response = await httpClient.GetAsync(new Uri("/api/users", UriKind.Relative));
Assert.Equal(HttpStatusCode.OK, response.StatusCode);
Assert.Equal("""{"name":"John"}""", await response.Content.ReadAsStringAsync());
```
<!-- endref -->

- [MapGet](cref:Meziantou.Framework.HttpClientMock.MapGet) takes the URL to match and a handler, as `app.MapGet` does in a minimal API. The handler returns the
  response: here an object that `Results.Ok` serializes as JSON. [MapPost](cref:Meziantou.Framework.HttpClientMock.MapPost), [MapPut](cref:Meziantou.Framework.HttpClientMock.MapPut), [MapPatch](cref:Meziantou.Framework.HttpClientMock.MapPatch), [MapDelete](cref:Meziantou.Framework.HttpClientMock.MapDelete),
  [MapHead](cref:Meziantou.Framework.HttpClientMock.MapHead) and [MapOptions](cref:Meziantou.Framework.HttpClientMock.MapOptions) map the other methods. See [Matching requests](03-MatchingRequests.md).
- [CreateHttpClient](cref:Meziantou.Framework.HttpClientMock.CreateHttpClient) starts the mock on its first call and returns an `HttpClient` that sends its requests to the mock.
  Its base address is `http://localhost/`, so relative URLs work; absolute URLs, with any host, reach the mock too.
- A request that matches no endpoint gets a `404 Not Found` response.

Map the endpoints before you create the first client.

## Give the mock to the code under test

The code under test rarely creates its own `HttpClient` from a mock. Depending on how it gets its client:

- Pass it the `HttpClient` returned by [CreateHttpClient](cref:Meziantou.Framework.HttpClientMock.CreateHttpClient).
- Pass it an `HttpMessageHandler` from [CreateHttpMessageHandler](cref:Meziantou.Framework.HttpClientMock.CreateHttpMessageHandler), for code that builds its own `HttpClient` from a
  handler, or to put the mock at the end of a chain of `DelegatingHandler`.
- Register the mock in the service collection when the code uses `IHttpClientFactory` or typed clients. See
  [Dependency injection](06-DependencyInjection.md).

## Dispose the mock

[HttpClientMock](cref:Meziantou.Framework.HttpClientMock) is `IAsyncDisposable`: declare it with `await using`, as in the example, to stop the application that
runs it. Dispose the clients and handlers created from it before the mock, which `using` declarations do when they come
after the mock's declaration.

## Next steps

- [Responses](04-Responses.md): status codes, raw JSON, headers, and responses built from the request.
- [Verifying calls](05-VerifyingCalls.md): check what the code under test sent.
