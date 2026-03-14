using System.Net;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Meziantou.Framework.Http.Caching.InMemory;
using Meziantou.Framework.Http.Caching.Redis;
using Meziantou.Framework.Http.Caching.Sqlite;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace Meziantou.Framework.Http.Caching.Tests;

public class PersistenceProvidersTests
{
    [Fact]
    public async Task CustomPersistenceProviderCanBeConfiguredUsingCachingOptions()
    {
        var provider = new SpyPersistenceProvider();
        using var handler = new MockResponseHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("response-content"),
            };

            response.Headers.TryAddWithoutValidation("Cache-Control", "max-age=600");
            return response;
        });

        using var cache = new HttpCachingDelegateHandler(handler, provider);
        using var httpClient = new HttpClient(cache);

        using var response1 = await httpClient.GetAsync("http://example.com/test", CancellationToken.None);
        using var response2 = await httpClient.GetAsync("http://example.com/test", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        Assert.True(provider.GetEntriesCallCount >= 2);
        Assert.True(provider.SetEntryCallCount >= 1);
    }

    [Fact]
    public async Task InMemoryProviderCanSaveAndLoadEntriesBetweenHandlerInstances()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "meziantou-http-cache", Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        var filePath = Path.Combine(tempDirectory, "http-cache.json");

        try
        {
            var firstProvider = new InMemoryHttpCacheStore();
            var firstRequestCount = 0;
            using (var firstOriginHandler = new MockResponseHandler(_ =>
            {
                Interlocked.Increment(ref firstRequestCount);
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("persisted-content"),
                };

                response.Headers.TryAddWithoutValidation("Cache-Control", "max-age=600");
                return response;
            }))
            {
                using var firstCachingHandler = new HttpCachingDelegateHandler(firstOriginHandler, firstProvider);
                using var firstClient = new HttpClient(firstCachingHandler);

                using var firstResponse = await firstClient.GetAsync("http://example.com/persist", CancellationToken.None);
                Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
                Assert.Equal("persisted-content", await firstResponse.Content.ReadAsStringAsync(CancellationToken.None));
            }

            await firstProvider.SaveToFileAsync(filePath, CancellationToken.None);

            var secondProvider = new InMemoryHttpCacheStore();
            await secondProvider.LoadFromFileAsync(filePath, CancellationToken.None);

            var secondRequestCount = 0;
            using var secondOriginHandler = new MockResponseHandler(_ =>
            {
                Interlocked.Increment(ref secondRequestCount);
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            });

            using var secondCachingHandler = new HttpCachingDelegateHandler(secondOriginHandler, secondProvider);
            using var secondClient = new HttpClient(secondCachingHandler);

            using var secondResponse = await secondClient.GetAsync("http://example.com/persist", CancellationToken.None);
            Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
            Assert.Equal("persisted-content", await secondResponse.Content.ReadAsStringAsync(CancellationToken.None));
            Assert.Equal(1, firstRequestCount);
            Assert.Equal(0, secondRequestCount);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SqliteProviderPersistsEntriesBetweenHandlerInstances()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "meziantou-http-cache", Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        var databasePath = Path.Combine(tempDirectory, "http-cache.db");
        var connectionString = CreateSqliteConnectionString(databasePath);

        try
        {
            Directory.CreateDirectory(tempDirectory);

            var firstRequestCount = 0;
            using (var firstOriginHandler = new MockResponseHandler(_ =>
            {
                Interlocked.Increment(ref firstRequestCount);
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("sqlite-persisted-content"),
                };

                response.Headers.TryAddWithoutValidation("Cache-Control", "max-age=600");
                return response;
            }))
            {
                using var firstProvider = new SqliteHttpCacheStore(connectionString);
                using var firstCachingHandler = new HttpCachingDelegateHandler(firstOriginHandler, firstProvider);
                using var firstClient = new HttpClient(firstCachingHandler);

                using var firstResponse = await firstClient.GetAsync("http://example.com/sqlite-persist", CancellationToken.None);
                Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
                Assert.Equal("sqlite-persisted-content", await firstResponse.Content.ReadAsStringAsync(CancellationToken.None));
            }

            var secondRequestCount = 0;
            using var secondOriginHandler = new MockResponseHandler(_ =>
            {
                Interlocked.Increment(ref secondRequestCount);
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            });

            using var secondProvider = new SqliteHttpCacheStore(connectionString);
            using var secondCachingHandler = new HttpCachingDelegateHandler(secondOriginHandler, secondProvider);
            using var secondClient = new HttpClient(secondCachingHandler);

            using var secondResponse = await secondClient.GetAsync("http://example.com/sqlite-persist", CancellationToken.None);
            Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
            Assert.Equal("sqlite-persisted-content", await secondResponse.Content.ReadAsStringAsync(CancellationToken.None));
            Assert.Equal(1, firstRequestCount);
            Assert.Equal(0, secondRequestCount);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SqliteProviderSupportsInMemoryConnectionString()
    {
        var connectionString = CreateInMemorySqliteConnectionString();
        using var keepAliveConnection = CreateSqliteAnchorConnection(connectionString);
        using var provider = new SqliteHttpCacheStore(connectionString);

        var primaryKey = "http://example.com/in-memory";
        var now = new DateTimeOffset(2026, 03, 12, 12, 00, 00, TimeSpan.Zero);
        await provider.SetEntryAsync(primaryKey, CreatePersistenceEntry(now, maxAge: TimeSpan.FromMinutes(5), mustRevalidate: false), CancellationToken.None);

        var entries = await provider.GetEntriesAsync(primaryKey, CancellationToken.None);

        Assert.Single(entries);
    }

    [Fact]
    public async Task SqliteProviderInMemoryConnectionStringCanBeUsedByHttpCache()
    {
        var connectionString = CreateInMemorySqliteConnectionString();
        using var keepAliveConnection = CreateSqliteAnchorConnection(connectionString);
        var requestCount = 0;
        using var originHandler = new MockResponseHandler(_ =>
        {
            Interlocked.Increment(ref requestCount);

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("sqlite-in-memory-content"),
            };

            response.Headers.TryAddWithoutValidation("Cache-Control", "max-age=600");
            return response;
        });

        using var provider = new SqliteHttpCacheStore(connectionString);
        using var cachingHandler = new HttpCachingDelegateHandler(originHandler, provider);
        using var client = new HttpClient(cachingHandler);

        using var response1 = await client.GetAsync("http://example.com/in-memory-http-cache", CancellationToken.None);
        using var response2 = await client.GetAsync("http://example.com/in-memory-http-cache", CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        Assert.Equal("sqlite-in-memory-content", await response2.Content.ReadAsStringAsync(CancellationToken.None));
        Assert.Equal(1, requestCount);
    }

    [Fact]
    public async Task SqliteProviderSupportsConcurrentOperations()
    {
        var connectionString = CreateInMemorySqliteConnectionString();
        using var keepAliveConnection = CreateSqliteAnchorConnection(connectionString);
        using var provider = new SqliteHttpCacheStore(connectionString);
        var now = new DateTimeOffset(2026, 03, 12, 12, 00, 00, TimeSpan.Zero);

        const int EntryCount = 32;
        var writeTasks = new Task[EntryCount];
        for (var i = 0; i < EntryCount; i++)
        {
            var primaryKey = "http://example.com/concurrent/" + i.ToString(CultureInfo.InvariantCulture);
            var entry = CreatePersistenceEntry(now.AddSeconds(i), maxAge: TimeSpan.FromMinutes(5), mustRevalidate: false);
            writeTasks[i] = provider.SetEntryAsync(primaryKey, entry, CancellationToken.None).AsTask();
        }

        await Task.WhenAll(writeTasks);

        var readTasks = new Task[EntryCount];
        for (var i = 0; i < EntryCount; i++)
        {
            var primaryKey = "http://example.com/concurrent/" + i.ToString(CultureInfo.InvariantCulture);
            readTasks[i] = AssertSingleEntryAsync(primaryKey);
        }

        await Task.WhenAll(readTasks);

        async Task AssertSingleEntryAsync(string primaryKey)
        {
            var entries = await provider.GetEntriesAsync(primaryKey, CancellationToken.None);
            Assert.Single(entries);
        }
    }

    [Fact]
    public async Task RedisProviderPersistsEntriesBetweenHandlerInstances()
    {
        await using var redisContainer = await StartRedisContainerOrSkipAsync();

        var redisConnectionString = redisContainer.GetConnectionString();
        var keyPrefix = "meziantou-http-cache-tests:" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);

        var firstRequestCount = 0;
        using (var firstOriginHandler = new MockResponseHandler(_ =>
        {
            Interlocked.Increment(ref firstRequestCount);
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("redis-persisted-content"),
            };

            response.Headers.TryAddWithoutValidation("Cache-Control", "max-age=600");
            return response;
        }))
        {
            using var firstConnection = await ConnectionMultiplexer.ConnectAsync(redisConnectionString);
            var firstProvider = new RedisHttpCacheStore(firstConnection, keyPrefix: keyPrefix);
            using var firstCachingHandler = new HttpCachingDelegateHandler(firstOriginHandler, firstProvider);
            using var firstClient = new HttpClient(firstCachingHandler);

            using var firstResponse = await firstClient.GetAsync("http://example.com/redis-persist", CancellationToken.None);
            Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
            Assert.Equal("redis-persisted-content", await firstResponse.Content.ReadAsStringAsync(CancellationToken.None));
        }

        var secondRequestCount = 0;
        using var secondOriginHandler = new MockResponseHandler(_ =>
        {
            Interlocked.Increment(ref secondRequestCount);
            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        });

        using var secondConnection = await ConnectionMultiplexer.ConnectAsync(redisConnectionString);
        var secondProvider = new RedisHttpCacheStore(secondConnection, keyPrefix: keyPrefix);
        using var secondCachingHandler = new HttpCachingDelegateHandler(secondOriginHandler, secondProvider);
        using var secondClient = new HttpClient(secondCachingHandler);

        using var secondResponse = await secondClient.GetAsync("http://example.com/redis-persist", CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        Assert.Equal("redis-persisted-content", await secondResponse.Content.ReadAsStringAsync(CancellationToken.None));
        Assert.Equal(1, firstRequestCount);
        Assert.Equal(0, secondRequestCount);
    }

    [Fact]
    public async Task RedisProviderPruneRemovesExpiredUnusableEntries()
    {
        await using var redisContainer = await StartRedisContainerOrSkipAsync();

        using var connection = await ConnectionMultiplexer.ConnectAsync(redisContainer.GetConnectionString());
        var provider = new RedisHttpCacheStore(connection, keyPrefix: "meziantou-http-cache-tests:" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        var primaryKey = "http://example.com/cleanup";
        var now = new DateTimeOffset(2026, 03, 12, 12, 00, 00, TimeSpan.Zero);

        await provider.SetEntryAsync(primaryKey, CreatePersistenceEntry(now, maxAge: TimeSpan.FromMinutes(1), mustRevalidate: true), CancellationToken.None);

        await provider.PruneObsoleteEntriesAsync(now, CancellationToken.None);
        var remainingEntries = await provider.GetEntriesAsync(primaryKey, CancellationToken.None);

        Assert.Empty(remainingEntries);
    }

    [Fact]
    public async Task RedisProviderPruneKeepsExpiredEntriesThatCanBeRevalidated()
    {
        await using var redisContainer = await StartRedisContainerOrSkipAsync();

        using var connection = await ConnectionMultiplexer.ConnectAsync(redisContainer.GetConnectionString());
        var provider = new RedisHttpCacheStore(connection, keyPrefix: "meziantou-http-cache-tests:" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        var primaryKey = "http://example.com/cleanup";
        var now = new DateTimeOffset(2026, 03, 12, 12, 00, 00, TimeSpan.Zero);

        await provider.SetEntryAsync(primaryKey, CreatePersistenceEntry(now, maxAge: TimeSpan.FromMinutes(1), mustRevalidate: true, eTag: "\"etag\""), CancellationToken.None);

        await provider.PruneObsoleteEntriesAsync(now, CancellationToken.None);
        var remainingEntries = await provider.GetEntriesAsync(primaryKey, CancellationToken.None);

        Assert.Single(remainingEntries);
    }

    [Fact]
    public async Task InMemoryProviderPruneRemovesExpiredUnusableEntries()
    {
        var provider = new InMemoryHttpCacheStore();
        var primaryKey = "http://example.com/cleanup";
        var now = new DateTimeOffset(2026, 03, 12, 12, 00, 00, TimeSpan.Zero);

        await provider.SetEntryAsync(primaryKey, CreatePersistenceEntry(now, maxAge: TimeSpan.FromMinutes(1), mustRevalidate: true), CancellationToken.None);

        await provider.PruneObsoleteEntriesAsync(now, CancellationToken.None);
        var remainingEntries = await provider.GetEntriesAsync(primaryKey, CancellationToken.None);

        Assert.Empty(remainingEntries);
    }

    [Fact]
    public async Task InMemoryProviderPruneKeepsExpiredEntriesThatCanBeRevalidated()
    {
        var provider = new InMemoryHttpCacheStore();
        var primaryKey = "http://example.com/cleanup";
        var now = new DateTimeOffset(2026, 03, 12, 12, 00, 00, TimeSpan.Zero);

        await provider.SetEntryAsync(primaryKey, CreatePersistenceEntry(now, maxAge: TimeSpan.FromMinutes(1), mustRevalidate: true, eTag: "\"etag\""), CancellationToken.None);

        await provider.PruneObsoleteEntriesAsync(now, CancellationToken.None);
        var remainingEntries = await provider.GetEntriesAsync(primaryKey, CancellationToken.None);

        Assert.Single(remainingEntries);
    }

    [Fact]
    public async Task SqliteProviderPruneRemovesExpiredUnusableEntries()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "meziantou-http-cache", Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        var databasePath = Path.Combine(tempDirectory, "http-cache.db");
        var connectionString = CreateSqliteConnectionString(databasePath);
        var primaryKey = "http://example.com/cleanup";
        var now = new DateTimeOffset(2026, 03, 12, 12, 00, 00, TimeSpan.Zero);

        try
        {
            Directory.CreateDirectory(tempDirectory);

            using var provider = new SqliteHttpCacheStore(connectionString);

            await provider.SetEntryAsync(primaryKey, CreatePersistenceEntry(now, maxAge: TimeSpan.FromMinutes(1), mustRevalidate: true), CancellationToken.None);

            await provider.PruneObsoleteEntriesAsync(now, CancellationToken.None);
            var remainingEntries = await provider.GetEntriesAsync(primaryKey, CancellationToken.None);

            Assert.Empty(remainingEntries);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SqliteProviderPruneKeepsExpiredEntriesThatCanBeRevalidated()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "meziantou-http-cache", Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        var databasePath = Path.Combine(tempDirectory, "http-cache.db");
        var connectionString = CreateSqliteConnectionString(databasePath);
        var primaryKey = "http://example.com/cleanup";
        var now = new DateTimeOffset(2026, 03, 12, 12, 00, 00, TimeSpan.Zero);

        try
        {
            Directory.CreateDirectory(tempDirectory);

            using var provider = new SqliteHttpCacheStore(connectionString);

            await provider.SetEntryAsync(primaryKey, CreatePersistenceEntry(now, maxAge: TimeSpan.FromMinutes(1), mustRevalidate: true, eTag: "\"etag\""), CancellationToken.None);

            await provider.PruneObsoleteEntriesAsync(now, CancellationToken.None);
            var remainingEntries = await provider.GetEntriesAsync(primaryKey, CancellationToken.None);

            Assert.Single(remainingEntries);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SqliteProviderPruneHandlesLegacyRowsWithoutCleanupMetadata()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "meziantou-http-cache", Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        var databasePath = Path.Combine(tempDirectory, "http-cache.db");
        var connectionString = CreateSqliteConnectionString(databasePath);
        var primaryKey = "http://example.com/cleanup";
        var now = new DateTimeOffset(2026, 03, 12, 12, 00, 00, TimeSpan.Zero);

        try
        {
            Directory.CreateDirectory(tempDirectory);

            var entry = CreatePersistenceEntry(now, maxAge: TimeSpan.FromMinutes(1), mustRevalidate: true);
            var payload = JsonSerializer.SerializeToUtf8Bytes(entry);

            var connectionStringBuilder = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared,
                Pooling = false,
            };

            using (var connection = new SqliteConnection(connectionStringBuilder.ToString()))
            {
                await connection.OpenAsync(CancellationToken.None);

                using (var createCommand = connection.CreateCommand())
                {
                    createCommand.CommandText =
                        "CREATE TABLE HttpCacheEntries (" +
                        "PrimaryKey TEXT NOT NULL, " +
                        "SecondaryKey TEXT NOT NULL, " +
                        "Payload BLOB NOT NULL, " +
                        "PRIMARY KEY(PrimaryKey, SecondaryKey))";
                    await createCommand.ExecuteNonQueryAsync(CancellationToken.None);
                }

                using (var insertCommand = connection.CreateCommand())
                {
                    insertCommand.CommandText =
                        "INSERT INTO HttpCacheEntries (PrimaryKey, SecondaryKey, Payload) " +
                        "VALUES ($primaryKey, $secondaryKey, $payload)";
                    insertCommand.Parameters.AddWithValue("$primaryKey", primaryKey);
                    insertCommand.Parameters.AddWithValue("$secondaryKey", "legacy-row");
                    insertCommand.Parameters.AddWithValue("$payload", payload);
                    await insertCommand.ExecuteNonQueryAsync(CancellationToken.None);
                }
            }

            using var provider = new SqliteHttpCacheStore(connectionString);
            await provider.PruneObsoleteEntriesAsync(now, CancellationToken.None);
            var remainingEntries = await provider.GetEntriesAsync(primaryKey, CancellationToken.None);

            Assert.Empty(remainingEntries);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    private static HttpCachePersistenceEntry CreatePersistenceEntry(
        DateTimeOffset now,
        TimeSpan maxAge,
        bool mustRevalidate,
        string eTag = null)
    {
        var requestTime = now - TimeSpan.FromMinutes(3);
        var responseTime = now - TimeSpan.FromMinutes(2);

        return new HttpCachePersistenceEntry
        {
            RequestTime = requestTime,
            ResponseTime = responseTime,
            ResponseDate = responseTime,
            AgeValue = TimeSpan.Zero,
            MaxAge = maxAge,
            MustRevalidate = mustRevalidate,
            ETag = eTag,
            SerializedResponse = new byte[] { 1, 2, 3 },
        };
    }

    private static string CreateSqliteConnectionString(string databasePath)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = false,
        };

        return builder.ToString();
    }

    private static string CreateInMemorySqliteConnectionString()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = "MeziantouHttpCacheTests_" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture),
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared,
            Pooling = false,
        };

        return builder.ToString();
    }

    private static SqliteConnection CreateSqliteAnchorConnection(string connectionString)
    {
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        return connection;
    }

    private static async Task<RedisContainer> StartRedisContainerOrSkipAsync()
    {
        var redisContainer = new RedisBuilder("redis:8.2").Build();

        try
        {
            await redisContainer.StartAsync();
            return redisContainer;
        }
        catch (Exception ex) when (IsDockerUnavailable(ex))
        {
            await redisContainer.DisposeAsync();
            throw new Exception("Docker is not available on this machine", ex);
        }
    }

    private static bool IsDockerUnavailable(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            var typeName = current.GetType().FullName;
            if (typeName is not null && typeName.Contains("Docker", StringComparison.OrdinalIgnoreCase))
                return true;

            if (current.Message.Contains("docker", StringComparison.OrdinalIgnoreCase))
                return true;

            if (current.Message.Contains("podman", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private sealed class MockResponseHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFunc) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(responseFunc(request));
        }
    }

    private sealed class SpyPersistenceProvider : IHttpCacheStore
    {
        private readonly InMemoryHttpCacheStore _innerProvider = new();

        public int GetEntriesCallCount => _getEntriesCallCount;
        public int SetEntryCallCount => _setEntryCallCount;

        private int _getEntriesCallCount;
        private int _setEntryCallCount;

        public async ValueTask<IReadOnlyCollection<HttpCachePersistenceEntry>> GetEntriesAsync(string primaryKey, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _getEntriesCallCount);
            return await _innerProvider.GetEntriesAsync(primaryKey, cancellationToken).ConfigureAwait(false);
        }

        public ValueTask RemoveEntriesAsync(string primaryKey, CancellationToken cancellationToken)
        {
            return _innerProvider.RemoveEntriesAsync(primaryKey, cancellationToken);
        }

        public ValueTask SetEntryAsync(string primaryKey, HttpCachePersistenceEntry entry, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _setEntryCallCount);
            return _innerProvider.SetEntryAsync(primaryKey, entry, cancellationToken);
        }
    }
}
