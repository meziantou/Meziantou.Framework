# Meziantou.Framework.Http.Caching.InMemory

In-memory persistence provider for `Meziantou.Framework.Http.Caching`.

## Installation

```bash
dotnet add package Meziantou.Framework.Http.Caching.InMemory
```

## Usage

```csharp
using Meziantou.Framework.Http;

var provider = new InMemoryHttpCachePersistenceProvider();
await provider.LoadFromFileAsync(Path.Combine(AppContext.BaseDirectory, "http-cache.json"));

var options = new CachingOptions
{
    PersistenceProvider = provider,
};

using var cachingHandler = new CachingDelegateHandler(options);
using var httpClient = new HttpClient(cachingHandler);

await provider.SaveToFileAsync(Path.Combine(AppContext.BaseDirectory, "http-cache.json"));
```
