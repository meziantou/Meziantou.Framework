# Meziantou.Framework.Http.Caching.Sqlite

SQLite cache store for `Meziantou.Framework.Http.Caching`.

## Installation

```bash
dotnet add package Meziantou.Framework.Http.Caching.Sqlite
```

## Usage

```csharp
using Meziantou.Framework.Http.Caching;
using Meziantou.Framework.Http.Caching.Sqlite;
using Microsoft.Data.Sqlite;

var connectionStringBuilder = new SqliteConnectionStringBuilder
{
    DataSource = Path.Combine(AppContext.BaseDirectory, "http-cache.db"),
    Mode = SqliteOpenMode.ReadWriteCreate,
    Cache = SqliteCacheMode.Shared,
    Pooling = false,
};

var provider = new SqliteHttpCacheStore(connectionStringBuilder.ToString());

using var cachingHandler = new HttpCachingDelegateHandler(provider);
using var httpClient = new HttpClient(cachingHandler);

// Optional: remove stale entries that cannot be reused
await provider.PruneObsoleteEntriesAsync();
```
