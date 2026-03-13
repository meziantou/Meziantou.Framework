# Meziantou.Framework.Http.Caching.Sqlite

SQLite persistence provider for `Meziantou.Framework.Http.Caching`.

## Installation

```bash
dotnet add package Meziantou.Framework.Http.Caching.Sqlite
```

## Usage

```csharp
using Meziantou.Framework.Http;
using Microsoft.Data.Sqlite;

var connectionStringBuilder = new SqliteConnectionStringBuilder
{
    DataSource = Path.Combine(AppContext.BaseDirectory, "http-cache.db"),
    Mode = SqliteOpenMode.ReadWriteCreate,
    Cache = SqliteCacheMode.Shared,
    Pooling = false,
};

var provider = new SqliteHttpCachePersistenceProvider(connectionStringBuilder.ToString());

var options = new CachingOptions
{
    PersistenceProvider = provider,
};

using var cachingHandler = new CachingDelegateHandler(options);
using var httpClient = new HttpClient(cachingHandler);

// Optional: remove stale entries that cannot be reused
await provider.CleanupUnusableEntriesAsync();
```
