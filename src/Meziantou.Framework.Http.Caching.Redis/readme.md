# Meziantou.Framework.Http.Caching.Redis

Redis cache store for `Meziantou.Framework.Http.Caching`.

## Installation

```bash
dotnet add package Meziantou.Framework.Http.Caching.Redis
```

## Usage

`RedisHttpCacheStore` is in the `Meziantou.Framework.Http.Caching.Redis` namespace, and `ConnectionMultiplexer` in
`StackExchange.Redis`.

<!-- ref:../../samples/Meziantou.Framework.Http.Samples/HttpCachingExamples.cs;region=RedisHttpCacheStore_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
using var connection = await ConnectionMultiplexer.ConnectAsync("localhost:6379");
var cacheStore = new RedisHttpCacheStore(connection);

using var cachingHandler = new HttpCachingDelegateHandler(cacheStore);
using var httpClient = new HttpClient(cachingHandler);

// Optional: remove stale entries that cannot be reused
await cacheStore.PruneObsoleteEntriesAsync();
```
<!-- endref -->
