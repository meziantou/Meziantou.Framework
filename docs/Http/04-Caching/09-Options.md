---
title: Options
description: Limit the size of the stored responses, choose which ones to store, control the clock of the cache, and observe the store failures.
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

## Choose which responses to store

[ShouldCacheResponse](cref:Meziantou.Framework.Http.Caching.HttpCachingOptions.ShouldCacheResponse) is called for each response the HTTP caching rules allow to store. When it returns `false`, the
response is not stored. It cannot make the cache store a response that the rules do not allow, such as one with
`Cache-Control: no-store`.

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
