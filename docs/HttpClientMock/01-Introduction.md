---
title: Introduction
description: An in-memory HTTP server, defined with minimal API handlers, that replaces the services an HttpClient calls in tests.
---

`Meziantou.Framework.HttpClientMock` provides `HttpClientMock`, a mock HTTP server for testing code that uses
`HttpClient`. You describe the responses with the same handlers as in an ASP.NET Core minimal API (`MapGet`, `MapPost`,
`Results.Ok`, route parameters, parameter binding), and the mock gives you an `HttpClient` or an `HttpMessageHandler`
that sends the requests to it.

The mock runs an ASP.NET Core application on the in-memory test server (`Microsoft.AspNetCore.TestHost`): no port is
opened and no request leaves the process, unless you ask the mock to forward it.

## What you can do with it

- **Mock an HTTP service** and get an `HttpClient` that calls it. See [Getting started](02-GettingStarted.md).
- **Match requests** by method, path, route parameters, query string, scheme, host and port. See
  [Matching requests](03-MatchingRequests.md).
- **Return responses**: objects serialized as JSON, raw JSON or XML, status codes, headers, responses computed from the
  request; or forward requests to the real service. See [Responses](04-Responses.md).
- **Verify the calls** the code under test made: count them, capture what was sent, and return a different response
  on each call. See [Verifying calls](05-VerifyingCalls.md).
- **Replace the clients of `IHttpClientFactory`** in a service collection: the default client, named clients and typed
  clients, and fail on any client you forgot to mock. See [Dependency injection](06-DependencyInjection.md).
- **Configure the mock**: logging to the test output, services and configuration of the mock's application. See
  [Logging and services](07-LoggingAndServices.md).

## When to use it

Use `HttpClientMock` when a test needs to decide what the remote service answers: an error, a timeout-prone endpoint
that fails once, an edge case that the real service rarely returns. The handlers are code, so a response can depend on
the request or on the calls made before.

When you would rather replay what the real service answered, record the traffic once and replay it with
[HTTP recording](../Http/07-Recording/01-Overview.md). The other HTTP packages of this repository, such as caching or
HSTS handlers, are described in the [Http](../Http/01-Introduction.md) section.

## Namespace

`HttpClientMock`, `RequestCounter`, `HttpMockServiceBuilder` and the extension methods of the package are in the
[Meziantou.Framework](cref:Meziantou.Framework.HttpClientMock!N:Meziantou.Framework) namespace. The handlers use the ASP.NET Core types of `Microsoft.AspNetCore.Http`, such as
`Results` and `HttpContext`.

## Next steps

- [Getting started](02-GettingStarted.md): install the package and write your first test with a mock.
- [Dependency injection](06-DependencyInjection.md): mock the clients of an application that uses `IHttpClientFactory`.
