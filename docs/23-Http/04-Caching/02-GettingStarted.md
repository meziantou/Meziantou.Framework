---
title: Getting started
description: Install the caching packages and add HttpCachingDelegateHandler to an HttpClient, directly or with IHttpClientFactory.
---

## Install the packages

Add the cache and a store to a project that targets .NET 10 or later. The examples of this group use the in-memory
store:

```bash
dotnet add package Meziantou.Framework.Http.Caching
dotnet add package Meziantou.Framework.Http.Caching.InMemory
```

`HttpCachingDelegateHandler`, `HttpCachingOptions` and `IHttpCacheStore` are in the `Meziantou.Framework.Http.Caching`
namespace, and `InMemoryHttpCacheStore` in `Meziantou.Framework.Http.Caching.InMemory`.

## Add the cache to an HttpClient

Create the handler with the handler that sends the requests and a store, and give it to the `HttpClient`:

<!-- ref:../../../tests/Meziantou.Framework.Http.Caching.Tests/PersistenceProvidersTests.cs;region=HttpCaching_GettingStarted;format=md-fenced-code-block;dedent=true -->
```csharp
// originHandler sends the requests to the server, for instance a SocketsHttpHandler.
// The server answers with Cache-Control: max-age=600
var cacheStore = new InMemoryHttpCacheStore();
using var cachingHandler = new HttpCachingDelegateHandler(originHandler, cacheStore);
using var httpClient = new HttpClient(cachingHandler);

using var response1 = await httpClient.GetAsync("https://example.com/data");
using var response2 = await httpClient.GetAsync("https://example.com/data");

Assert.Equal("Hello", await response2.Content.ReadAsStringAsync());
Assert.Null(response1.Headers.Age);    // Sent by the server
Assert.NotNull(response2.Headers.Age); // Served from the cache
```
<!-- endref -->

- The first request goes to the server. Its response allows the cache to store it for 10 minutes
  (`Cache-Control: max-age=600`), so the cache stores it.
- The second request does not reach the server: the cache serves the stored response. A response served from the cache
  has an `Age` header, the number of seconds since the server sent it.
- The responses the server does not allow to store, such as `Cache-Control: no-store`, are returned unchanged and not
  stored. See [Freshness and validation](04-FreshnessAndValidation.md).

`HttpCachingDelegateHandler` is a `DelegatingHandler`: the handler that sends the requests is its inner handler, usually
a `SocketsHttpHandler`. Pass it to the constructor when you create the `HttpClient` yourself: without an inner handler,
the first request fails with an `InvalidOperationException` ("The inner handler has not been assigned").

Disposing the `HttpClient` disposes the caching handler, which disposes its inner handler. The store is not disposed:
it may be shared by several handlers.

Send the requests with the asynchronous methods (`GetAsync`, `SendAsync`, ...): the synchronous `HttpClient.Send`
throws a `NotSupportedException`. See [Limitations](10-Limitations.md).

## Use IHttpClientFactory

With `IHttpClientFactory`, register the store as a singleton and the handler as transient, and add the handler to the
client. The factory sets the inner handler:

<!-- ref:../../../tests/Meziantou.Framework.Http.Caching.Tests/PersistenceProvidersTests.cs;region=HttpCachingDelegateHandler_DependencyInjection;format=md-fenced-code-block;dedent=true -->
```csharp
services.AddSingleton<IHttpCacheStore, InMemoryHttpCacheStore>();
services.AddTransient<HttpCachingDelegateHandler>();

services.AddHttpClient("MyApi")
    .AddHttpMessageHandler<HttpCachingDelegateHandler>();
```
<!-- endref -->

The factory creates new handlers from time to time and disposes the old ones. The entries are in the store, not in the
handler, so a singleton store keeps them across these rotations.

## Next steps

- [Cache stores](03-CacheStores.md): keep the entries between runs, in a file, a SQLite database or
  Redis.
- [Freshness and validation](04-FreshnessAndValidation.md): what the cache stores and for how long.
- [Options](09-Options.md): limit the size of the stored responses and choose which ones to store.
