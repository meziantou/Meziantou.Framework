---
title: Options
description: Limit the size of the stored responses, choose which ones to store, share the cache between users, coalesce concurrent requests, control the clock of the cache, and observe the store failures.
---

[HttpCachingOptions](cref:Meziantou.Framework.Http.Caching.HttpCachingOptions) configures a [HttpCachingDelegateHandler](cref:Meziantou.Framework.Http.Caching.HttpCachingDelegateHandler). Pass it as the last argument of the constructor:

<!-- ref:../../../tests/Meziantou.Framework.Http.Caching.Tests/AdvancedCachingTests.cs;region=HttpCaching_Options;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new HttpCachingOptions
{
    // Do not store the responses larger than 1 MB. The default is 5 MB, and null removes the limit.
    MaximumResponseSize = 1024 * 1024,

    // Only store the successful responses, even when the server allows storing the others
    ShouldCacheResponse = response => response.IsSuccessStatusCode,
};
```
<!-- endref -->

| Option | Default | What it does |
| --- | --- | --- |
| [MaximumResponseSize](cref:Meziantou.Framework.Http.Caching.HttpCachingOptions.MaximumResponseSize) | 5 MB (5,242,880 bytes) | The largest response stored, in bytes. `null` removes the limit |
| [ShouldCacheResponse](cref:Meziantou.Framework.Http.Caching.HttpCachingOptions.ShouldCacheResponse) | `null` | A predicate that can prevent storing a response |
| [IsSharedCache](cref:Meziantou.Framework.Http.Caching.HttpCachingOptions.IsSharedCache) | `false` | Follow the rules of a shared cache, which serves several users |
| [CoalesceRequests](cref:Meziantou.Framework.Http.Caching.HttpCachingOptions.CoalesceRequests) | `false` | Send a single request to the server for concurrent requests for the same URL |
| [TimeProvider](cref:Meziantou.Framework.Http.Caching.HttpCachingOptions.TimeProvider) | `TimeProvider.System` | The clock used to compute the age and the freshness of the responses |
| [OnStoreError](cref:Meziantou.Framework.Http.Caching.HttpCachingOptions.OnStoreError) | `null` | A callback invoked when the store fails |

## Limit the size of the stored responses

The cache reads the body of a response to store it, so a large response costs memory and space in the store.
[MaximumResponseSize](cref:Meziantou.Framework.Http.Caching.HttpCachingOptions.MaximumResponseSize) bounds that:

- A response whose `Content-Length` is larger than the limit is not read by the cache at all.
- A response without `Content-Length` is read up to the limit, then left unstored.
- The limit applies to the stored entry, which holds the headers and an encoded copy of the body: a body slightly
  smaller than the limit can still be too large.

In every case, the caller receives the full response. Only the storage is skipped.

This limit applies to each response. To bound the number of entries or their total size, give limits to the store. See
[Bounding the size of a store](03-CacheStores.md#bounding-the-size-of-a-store).

## Choose which responses to store

[ShouldCacheResponse](cref:Meziantou.Framework.Http.Caching.HttpCachingOptions.ShouldCacheResponse) is called for each response the HTTP caching rules allow to store. When it returns `false`, the
response is not stored. It cannot make the cache store a response that the rules do not allow, such as one with
`Cache-Control: no-store`.

## Share the cache between users

By default, the cache is private: it belongs to a single user. Set [IsSharedCache](cref:Meziantou.Framework.Http.Caching.HttpCachingOptions.IsSharedCache) when one cache serves
several users, for instance in a proxy or in a service that forwards the requests of its own clients:

<!-- ref:../../../tests/Meziantou.Framework.Http.Caching.Tests/ComprehensiveRFC7234Tests.cs;region=HttpCaching_SharedCache;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new HttpCachingOptions
{
    // Follow the rules of a shared cache: the cache serves several users
    IsSharedCache = true,
};
```
<!-- endref -->

A shared cache follows the rules of RFC 9111 for shared caches:

- A response with `Cache-Control: private` is not stored. With `private="<field>"`, the response is stored without the
  listed header fields.
- The response to a request with an `Authorization` header is only stored when the response has `public`, `s-maxage` or
  `must-revalidate`.
- `s-maxage` gives the freshness lifetime, before `max-age` and `Expires`, and makes the response storable on its own.
- `proxy-revalidate` and `s-maxage` mean the same as `must-revalidate`: a stale response is never served without a
  successful revalidation, even for `max-stale` or `stale-if-error`.

The stores keep an entry until it is stale for both kinds of cache, so they do not need to know which one wrote it.

## Coalesce concurrent requests

When a popular response expires, every concurrent request for it goes to the server, multiplying the load on the server
when the cache should protect it. Set [CoalesceRequests](cref:Meziantou.Framework.Http.Caching.HttpCachingOptions.CoalesceRequests) to send a single request instead:

<!-- ref:../../../tests/Meziantou.Framework.Http.Caching.Tests/ThreadSafetyTests.cs;region=HttpCaching_CoalesceRequests;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new HttpCachingOptions
{
    // Concurrent requests for the same URL share a single request to the server
    CoalesceRequests = true,
};

using var cachingHandler = new HttpCachingDelegateHandler(originHandler, new InMemoryHttpCacheStore(), options);
using var httpClient = new HttpClient(cachingHandler);
```
<!-- endref -->

- When a request must go to the server (nothing stored, or a stale response to revalidate), the concurrent requests with
  the same method and URL wait for it, then look up the cache again. A stale response is revalidated once, with a single
  conditional request.
- A waiting request that the stored response cannot answer, because the response could not be stored or varies on a
  header the request sends differently, goes to the server itself. It does not wait a second time, so requests whose
  responses are never stored are not queued behind each other.
- Fresh responses are served from the cache without waiting, and requests with a `Range` header are not coalesced.
- Coalescing happens within one [HttpCachingDelegateHandler](cref:Meziantou.Framework.Http.Caching.HttpCachingDelegateHandler). Handlers that share a store, in other processes
  or with `IHttpClientFactory`, which creates a handler per `HttpClient` lifetime, still send their own requests.

## Control the clock

The cache computes the age and the freshness of the responses with [TimeProvider](cref:Meziantou.Framework.Http.Caching.HttpCachingOptions.TimeProvider). Replace it with a
`FakeTimeProvider` to test how your application behaves when responses become stale, without waiting:

<!-- ref:../../../tests/Meziantou.Framework.Http.Caching.Tests/StaleResponseTests.cs;region=HttpCaching_TimeProvider;format=md-fenced-code-block;dedent=true -->
```csharp
// FakeTimeProvider comes from the Microsoft.Extensions.TimeProvider.Testing package
var timeProvider = new FakeTimeProvider();
var options = new HttpCachingOptions
{
    TimeProvider = timeProvider,
};

using var cachingHandler = new HttpCachingDelegateHandler(originHandler, new InMemoryHttpCacheStore(), options);
using var httpClient = new HttpClient(cachingHandler);

// The server answers with Cache-Control: max-age=300
Assert.Equal("v1", await httpClient.GetStringAsync("https://example.com/data"));

// Five minutes later, the stored response is stale, so the cache asks the server again
timeProvider.Advance(TimeSpan.FromMinutes(5));
Assert.Equal("v2", await httpClient.GetStringAsync("https://example.com/data"));
```
<!-- endref -->

[PruneObsoleteEntriesAsync](cref:Meziantou.Framework.Http.Caching.IHttpCacheStore.PruneObsoleteEntriesAsync) does not use this clock: pass the time to the overload that takes one,
`PruneObsoleteEntriesAsync(timeProvider.GetUtcNow())`. See
[Cache stores](03-CacheStores.md#removing-obsolete-entries).

## Observe store failures

A failing [IHttpCacheStore](cref:Meziantou.Framework.Http.Caching.IHttpCacheStore) never fails the request. A lookup that throws is treated as a cache miss, and a write that
throws is skipped, so a Redis outage or a locked SQLite file degrades to no caching rather than to a broken
`HttpClient`. Set [OnStoreError](cref:Meziantou.Framework.Http.Caching.HttpCachingOptions.OnStoreError) to log these failures:

<!-- ref:../../../tests/Meziantou.Framework.Http.Caching.Tests/StoreFailureTests.cs;region=HttpCachingOptions_OnStoreError;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new HttpCachingOptions
{
    OnStoreError = ex => logger.LogWarning(ex, "HTTP cache store failure"),
};
```
<!-- endref -->

Without it, the failures are silent. A cancellation requested by the caller is not a store failure: it still cancels
the request.
