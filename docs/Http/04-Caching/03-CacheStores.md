---
title: Cache stores
description: Keep the cache entries in memory, in a JSON file, in SQLite or in Redis, remove the obsolete ones, or write your own store.
---

[HttpCachingDelegateHandler](cref:Meziantou.Framework.Http.Caching.HttpCachingDelegateHandler) keeps its entries in the [IHttpCacheStore](cref:Meziantou.Framework.Http.Caching.IHttpCacheStore) you pass to its constructor. Choose the store
from where the entries must live:

| Store | Package | Use it when |
| --- | --- | --- |
| [InMemoryHttpCacheStore](cref:Meziantou.Framework.Http.Caching.InMemory.InMemoryHttpCacheStore) | `Meziantou.Framework.Http.Caching.InMemory` | The cache lives as long as the process, optionally saved to a file between runs |
| [SqliteHttpCacheStore](cref:Meziantou.Framework.Http.Caching.Sqlite.SqliteHttpCacheStore) | `Meziantou.Framework.Http.Caching.Sqlite` | The cache must survive restarts without an explicit save |
| [RedisHttpCacheStore](cref:Meziantou.Framework.Http.Caching.Redis.RedisHttpCacheStore) | `Meziantou.Framework.Http.Caching.Redis` | The cache lives in a Redis server |
| Your own [IHttpCacheStore](cref:Meziantou.Framework.Http.Caching.IHttpCacheStore) | `Meziantou.Framework.Http.Caching` | The entries go elsewhere |

No store limits its size or evicts entries on its own: an entry stays until a request replaces or invalidates it, or
until you prune it. See [Removing obsolete entries](#removing-obsolete-entries).

## In memory

[InMemoryHttpCacheStore](cref:Meziantou.Framework.Http.Caching.InMemory.InMemoryHttpCacheStore) keeps the entries in memory. [SaveToFileAsync](cref:Meziantou.Framework.Http.Caching.InMemory.InMemoryHttpCacheStore.SaveToFileAsync) writes them to a JSON file, and
[LoadFromFileAsync](cref:Meziantou.Framework.Http.Caching.InMemory.InMemoryHttpCacheStore.LoadFromFileAsync) replaces the entries of the store with the ones of a file, so the cache can survive a restart:

<!-- ref:../../../tests/Meziantou.Framework.Http.Caching.Tests/PersistenceProvidersTests.cs;region=HttpCaching_InMemorySaveAndLoad;format=md-fenced-code-block;dedent=true -->
```csharp
var cacheStore = new InMemoryHttpCacheStore();

// Restore the entries saved by a previous run. A missing file leaves the store empty.
await cacheStore.LoadFromFileAsync(cacheFilePath);

using var cachingHandler = new HttpCachingDelegateHandler(originHandler, cacheStore);
using var httpClient = new HttpClient(cachingHandler);
using var response = await httpClient.GetAsync("https://example.com/data");

// Drop the entries that can no longer be used, then save the others for the next run
await cacheStore.PruneObsoleteEntriesAsync();
await cacheStore.SaveToFileAsync(cacheFilePath);
```
<!-- endref -->

- [LoadFromFileAsync](cref:Meziantou.Framework.Http.Caching.InMemory.InMemoryHttpCacheStore.LoadFromFileAsync) empties the store when the file does not exist or is not a valid cache file.
- [SaveToFileAsync](cref:Meziantou.Framework.Http.Caching.InMemory.InMemoryHttpCacheStore.SaveToFileAsync) creates the directory if needed. It writes a temporary file next to the destination, then moves it
  into place, so an interrupted save does not destroy the previous file.
- Nothing is saved automatically: call [SaveToFileAsync](cref:Meziantou.Framework.Http.Caching.InMemory.InMemoryHttpCacheStore.SaveToFileAsync) when it suits your application, for instance when it stops.

## SQLite

[SqliteHttpCacheStore](cref:Meziantou.Framework.Http.Caching.Sqlite.SqliteHttpCacheStore) keeps the entries in a SQLite database. Install `Meziantou.Framework.Http.Caching.Sqlite`; the
store is in the [Meziantou.Framework.Http.Caching.Sqlite](cref:Meziantou.Framework.Http.Caching.Sqlite) namespace and `SqliteConnectionStringBuilder` in
`Microsoft.Data.Sqlite`:

<!-- ref:../../../tests/Meziantou.Framework.Http.Caching.Tests/PersistenceProvidersTests.cs;region=HttpCaching_SqliteStore;format=md-fenced-code-block;dedent=true -->
```csharp
var connectionString = new SqliteConnectionStringBuilder
{
    DataSource = databasePath,
    Mode = SqliteOpenMode.ReadWriteCreate,
    Pooling = false,
}.ToString();

using var cacheStore = new SqliteHttpCacheStore(connectionString);

// Remove the entries that can no longer be used, for instance when the application starts
await cacheStore.PruneObsoleteEntriesAsync();

using var cachingHandler = new HttpCachingDelegateHandler(originHandler, cacheStore);
using var httpClient = new HttpClient(cachingHandler);
using var response = await httpClient.GetAsync("https://example.com/data");
```
<!-- endref -->

- The constructor takes a SQLite connection string. The store creates its table, `HttpCacheEntries`, on first use, and
  every operation is written to the database as it happens.
- The store keeps one connection open for its lifetime, and [Dispose](cref:Meziantou.Framework.Http.Caching.Sqlite.SqliteHttpCacheStore.Dispose) closes it. With `Pooling = false`, the other
  connections are closed after each operation, so the database file is released when the store is disposed.
- A shared in-memory database (`Mode = SqliteOpenMode.Memory` and `Cache = SqliteCacheMode.Shared`) works too: the
  connection the store keeps open keeps the database alive until the store is disposed.

## Redis

[RedisHttpCacheStore](cref:Meziantou.Framework.Http.Caching.Redis.RedisHttpCacheStore) keeps the entries in Redis. Install `Meziantou.Framework.Http.Caching.Redis`; the store is in the
[Meziantou.Framework.Http.Caching.Redis](cref:Meziantou.Framework.Http.Caching.Redis) namespace and takes an `IConnectionMultiplexer` from `StackExchange.Redis`:

<!-- ref:../../../samples/Meziantou.Framework.Http.Samples/HttpCachingExamples.cs;region=HttpCaching_RedisStore;format=md-fenced-code-block;dedent=true -->
```csharp
await using var connection = await ConnectionMultiplexer.ConnectAsync("localhost:6379");

// The key prefix isolates the entries of this cache from the other data of the Redis database
var cacheStore = new RedisHttpCacheStore(connection, keyPrefix: "MyApp:HttpCache");

using var cachingHandler = new HttpCachingDelegateHandler(new SocketsHttpHandler(), cacheStore);
using var httpClient = new HttpClient(cachingHandler);
using var response = await httpClient.GetAsync("https://example.com/data");

// Redis keeps the entries until they are removed: remove the obsolete ones from time to time
await cacheStore.PruneObsoleteEntriesAsync();
```
<!-- endref -->

- Every key the store writes starts with `keyPrefix`, `Meziantou:HttpCache` by default. Use a different prefix for each
  cache that shares a Redis database.
- The store does not set an expiration on the keys it writes: Redis keeps the entries until a request replaces or
  invalidates them, or until [PruneObsoleteEntriesAsync](cref:Meziantou.Framework.Http.Caching.IHttpCacheStore.PruneObsoleteEntriesAsync) removes them.

## Removing obsolete entries

A stale entry is still useful when the cache can revalidate it (it has an `ETag` or a `Last-Modified` date) or serve it
on errors (`stale-if-error`, see [Serving stale responses on errors](07-StaleIfError.md)). An entry with none of them
is obsolete once it is stale: the cache can never use it again.

The three stores have a [PruneObsoleteEntriesAsync](cref:Meziantou.Framework.Http.Caching.IHttpCacheStore.PruneObsoleteEntriesAsync) method that removes the obsolete entries, and keeps the fresh ones
and the ones that can still be used. It compares the entries with the current time, `DateTimeOffset.UtcNow`; an
overload takes the time to use instead, for instance the time of the [TimeProvider](cref:Meziantou.Framework.Http.Caching.HttpCachingOptions.TimeProvider) of
[the options](09-Options.md#control-the-clock). Call it from time to time, or before saving an in-memory store.

## Write your own store

[IHttpCacheStore](cref:Meziantou.Framework.Http.Caching.IHttpCacheStore) has four methods:

| Method | What it does |
| --- | --- |
| `GetEntriesAsync(primaryKey, cancellationToken)` | Returns the entries stored under a key, or an empty collection |
| `SetEntryAsync(primaryKey, entry, cancellationToken)` | Adds an entry, or replaces the entry stored for the same variant |
| `RemoveEntriesAsync(primaryKey, cancellationToken)` | Removes every entry stored under a key |
| `PruneObsoleteEntriesAsync(cancellationToken)` | Removes the obsolete entries. Optional: the default implementation does nothing |

A key identifies a request method and URL. Several entries can share a key: one per variant of the response, for
instance one per `Accept-Language` when the response has `Vary: Accept-Language` (see
[Vary and No-Vary-Search](05-VaryAndNoVarySearch.md)). This store keeps them in a dictionary:

<!-- ref:../../../tests/Meziantou.Framework.Http.Caching.Tests/PersistenceProvidersTests.cs;region=HttpCaching_CustomStore;format=md-fenced-code-block;dedent=true -->
```csharp
private sealed class DictionaryHttpCacheStore : IHttpCacheStore
{
    private readonly ConcurrentDictionary<string, ImmutableArray<HttpCachePersistenceEntry>> _entries = new(StringComparer.Ordinal);

    public ValueTask<IReadOnlyCollection<HttpCachePersistenceEntry>> GetEntriesAsync(string primaryKey, CancellationToken cancellationToken)
    {
        IReadOnlyCollection<HttpCachePersistenceEntry> entries = _entries.TryGetValue(primaryKey, out var value) ? value : [];
        return ValueTask.FromResult(entries);
    }

    public ValueTask SetEntryAsync(string primaryKey, HttpCachePersistenceEntry entry, CancellationToken cancellationToken)
    {
        // An entry replaces the one stored for the same variant of the response (same Vary headers and query)
        _entries.AddOrUpdate(
            primaryKey,
            _ => [entry],
            (_, existing) => [.. existing.Where(item => !HttpCachePersistenceEntry.HasSameSecondaryKey(item, entry)), entry]);
        return ValueTask.CompletedTask;
    }

    public ValueTask RemoveEntriesAsync(string primaryKey, CancellationToken cancellationToken)
    {
        _entries.TryRemove(primaryKey, out _);
        return ValueTask.CompletedTask;
    }
}
```
<!-- endref -->

[HttpCachePersistenceEntry](cref:Meziantou.Framework.Http.Caching.HttpCachePersistenceEntry) holds what the cache needs to use an entry without reading the response: the variant it
answers, its dates, its directives and validators, and the serialized response. It also has helpers for a store:

- `HasSameSecondaryKey(left, right)` tells whether two entries are the same variant, so [SetEntryAsync](cref:Meziantou.Framework.Http.Caching.IHttpCacheStore.SetEntryAsync) knows which
  entry to replace. [ComputeSecondaryKeyHash()](cref:Meziantou.Framework.Http.Caching.HttpCachePersistenceEntry.ComputeSecondaryKeyHash) gives a stable hash of the variant, to use as a storage key.
- `IsObsolete(now)` tells whether the entry can be removed, and [GetStaleTime()](cref:Meziantou.Framework.Http.Caching.HttpCachePersistenceEntry.GetStaleTime) when it becomes stale, which a store can
  index to prune without reading every entry. [IsUnusableWhenStale](cref:Meziantou.Framework.Http.Caching.HttpCachePersistenceEntry.IsUnusableWhenStale) tells whether it can be removed as soon as it is
  stale.
- [Clone()](cref:Meziantou.Framework.Http.Caching.HttpCachePersistenceEntry.Clone) copies the entry, for a store that keeps the instances it receives and must not share them.

A store can fail: the cache treats a failed lookup as a miss and skips a failed write, so the request still succeeds.
See [Options](09-Options.md#observe-store-failures).
