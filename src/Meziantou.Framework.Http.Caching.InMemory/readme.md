# Meziantou.Framework.Http.Caching.InMemory

In-memory cache store for `Meziantou.Framework.Http.Caching`.

## Installation

```bash
dotnet add package Meziantou.Framework.Http.Caching.InMemory
```

## Usage

```csharp
using Meziantou.Framework.Http.Caching;
using Meziantou.Framework.Http.Caching.InMemory;

var persistenceProvider = new InMemoryHttpCacheStore();

// Optional: restore data from a previous session
await persistenceProvider.LoadFromFileAsync(Path.Combine(AppContext.BaseDirectory, "http-cache.json"));

using var cachingHandler = new HttpCachingDelegateHandler(persistenceProvider);
using var httpClient = new HttpClient(cachingHandler);

// Optional: remove stale entries that cannot be reused
await persistenceProvider.PruneObsoleteEntriesAsync();

// Persist cache to disk when needed
await persistenceProvider.SaveToFileAsync(Path.Combine(AppContext.BaseDirectory, "http-cache.json"));
```