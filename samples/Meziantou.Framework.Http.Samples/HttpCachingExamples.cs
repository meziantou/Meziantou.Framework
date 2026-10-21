#pragma warning disable CA2000 // Dispose objects before losing scope
#pragma warning disable CA2234 // Pass system uri objects instead of strings
using Meziantou.Framework.Http.Caching;
using Meziantou.Framework.Http.Caching.InMemory;
using Meziantou.Framework.Http.Caching.Redis;
using Meziantou.Framework.Http.Caching.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Time.Testing;
using StackExchange.Redis;

namespace Meziantou.Framework.Http.Samples;

/// <summary>
/// The examples of the readme files of Meziantou.Framework.Http.Caching and its stores that send real requests, read
/// files from the disk or need a Redis server.
/// </summary>
public static class HttpCachingExamples
{
    public static async Task BasicUsage()
    {
        #region HttpCachingDelegateHandler_BasicUsage
        // Create the caching handler
        var cacheStore = new InMemoryHttpCacheStore();
        var cachingHandler = new HttpCachingDelegateHandler(cacheStore);

        // Create HttpClient with the caching handler
        using var httpClient = new HttpClient(cachingHandler);

        // Make requests - responses will be cached automatically
        var response1 = await httpClient.GetAsync("https://api.example.com/data");
        var response2 = await httpClient.GetAsync("https://api.example.com/data"); // Served from cache
        #endregion
    }

    public static async Task CustomInnerHandler()
    {
        #region HttpCachingDelegateHandler_CustomInnerHandler
        var innerHandler = new HttpClientHandler();
        var cacheStore = new InMemoryHttpCacheStore();
        var cachingHandler = new HttpCachingDelegateHandler(innerHandler, cacheStore);

        using var httpClient = new HttpClient(cachingHandler);
        var response = await httpClient.GetAsync("https://api.example.com/data");
        #endregion
    }

    public static async Task PersistentCacheStorage()
    {
        #region HttpCachingDelegateHandler_PersistentCacheStorage
        var cacheStore = new InMemoryHttpCacheStore();
        await cacheStore.LoadFromFileAsync(Path.Combine(AppContext.BaseDirectory, "http-cache.json"));

        var options = new HttpCachingOptions
        {
            MaximumResponseSize = 5 * 1024 * 1024,
        };

        var cachingHandler = new HttpCachingDelegateHandler(cacheStore, options);
        using var httpClient = new HttpClient(cachingHandler);

        // Persist cache to disk when needed
        await cacheStore.SaveToFileAsync(Path.Combine(AppContext.BaseDirectory, "http-cache.json"));
        #endregion
    }

    public static async Task CustomTimeProvider()
    {
        #region HttpCachingDelegateHandler_CustomTimeProvider
        var fakeTimeProvider = new FakeTimeProvider();
        var cacheStore = new InMemoryHttpCacheStore();
        var options = new HttpCachingOptions
        {
            TimeProvider = fakeTimeProvider,
        };
        var cachingHandler = new HttpCachingDelegateHandler(cacheStore, options);

        using var httpClient = new HttpClient(cachingHandler);

        // Make a request
        var response1 = await httpClient.GetAsync("https://api.example.com/data");

        // Advance time
        fakeTimeProvider.Advance(TimeSpan.FromMinutes(5));

        // Make another request (cache freshness is evaluated with the fake time)
        var response2 = await httpClient.GetAsync("https://api.example.com/data");
        #endregion
    }

    public static async Task InMemoryStore()
    {
        #region InMemoryHttpCacheStore_Usage
        var persistenceProvider = new InMemoryHttpCacheStore();

        // Optional: restore data from a previous session
        await persistenceProvider.LoadFromFileAsync(Path.Combine(AppContext.BaseDirectory, "http-cache.json"));

        using var cachingHandler = new HttpCachingDelegateHandler(persistenceProvider);
        using var httpClient = new HttpClient(cachingHandler);

        // Optional: remove stale entries that cannot be reused
        await persistenceProvider.PruneObsoleteEntriesAsync();

        // Persist cache to disk when needed
        await persistenceProvider.SaveToFileAsync(Path.Combine(AppContext.BaseDirectory, "http-cache.json"));
        #endregion
    }

    public static async Task SqliteStore()
    {
        #region SqliteHttpCacheStore_Usage
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
        #endregion
    }

    public static async Task RedisStore()
    {
        #region RedisHttpCacheStore_Usage
        using var connection = await ConnectionMultiplexer.ConnectAsync("localhost:6379");
        var cacheStore = new RedisHttpCacheStore(connection);

        using var cachingHandler = new HttpCachingDelegateHandler(cacheStore);
        using var httpClient = new HttpClient(cachingHandler);

        // Optional: remove stale entries that cannot be reused
        await cacheStore.PruneObsoleteEntriesAsync();
        #endregion
    }
}
