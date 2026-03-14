````markdown
# Meziantou.Framework.Http.Caching.Redis

Redis cache store for `Meziantou.Framework.Http.Caching`.

## Installation

```bash
dotnet add package Meziantou.Framework.Http.Caching.Redis
```

## Usage

```csharp
using Meziantou.Framework.Http.Caching;
using Meziantou.Framework.Http.Caching.Redis;
using StackExchange.Redis;

using var connection = await ConnectionMultiplexer.ConnectAsync("localhost:6379");
var cacheStore = new RedisHttpCacheStore(connection);

using var cachingHandler = new HttpCachingDelegateHandler(cacheStore);
using var httpClient = new HttpClient(cachingHandler);

// Optional: remove stale entries that cannot be reused
await cacheStore.PruneObsoleteEntriesAsync();
```

````
