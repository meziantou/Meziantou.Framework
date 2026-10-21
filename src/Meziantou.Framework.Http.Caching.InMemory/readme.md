# Meziantou.Framework.Http.Caching.InMemory

In-memory cache store for `Meziantou.Framework.Http.Caching`.

## Installation

```bash
dotnet add package Meziantou.Framework.Http.Caching.InMemory
```

## Usage

`InMemoryHttpCacheStore` is in the `Meziantou.Framework.Http.Caching.InMemory` namespace.

<!-- ref:../../samples/Meziantou.Framework.Http.Samples/HttpCachingExamples.cs;region=InMemoryHttpCacheStore_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
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
<!-- endref -->
