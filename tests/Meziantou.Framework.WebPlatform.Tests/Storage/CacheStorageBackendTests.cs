using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Tests.Storage;

/// <summary>The contract of <see cref="CacheStorageBackend"/>, checked for each implementation.</summary>
public abstract class CacheStorageBackendTests : IDisposable
{
    private const string Origin = "https://example.com";

    private CacheStorageBackend? _backend;

    protected CacheStorageBackend Backend => _backend ??= CreateBackend();

    /// <summary>Creates a backend on the storage of the test.</summary>
    protected abstract CacheStorageBackend CreateBackend();

    /// <summary>Releases the backend (e.g. closes its file).</summary>
    protected virtual void DisposeBackend(CacheStorageBackend backend)
    {
    }

    /// <summary>Returns a backend that reads the storage of <see cref="Backend"/> (a new instance for a persistent storage).</summary>
    protected virtual CacheStorageBackend Reopen()
    {
        DisposeBackend(Backend);
        _backend = CreateBackend();
        return _backend;
    }

    public void Dispose()
    {
        if (_backend is not null)
        {
            DisposeBackend(_backend);
        }

        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
    }

    private static CacheEntry Entry(string url, string body, string vary = "") => new()
    {
        Request = new CachedRequest { Url = url, Headers = [new("Accept", "text/plain")], Mode = "no-cors", Referrer = "https://example.com/page" },
        Response = new CachedResponse
        {
            Type = "basic",
            Status = 201,
            StatusText = "Created",
            Headers = vary.Length == 0 ? [new("Content-Type", "text/plain")] : [new("Content-Type", "text/plain"), new("Vary", vary)],
            UrlList = [url],
            Body = System.Text.Encoding.UTF8.GetBytes(body),
        },
    };

    [Fact]
    public void Caches_AreListedInCreationOrder()
    {
        Assert.True(Backend.CreateCache(Origin, "b"));
        Assert.True(Backend.CreateCache(Origin, "a\uDC00"));
        Assert.False(Backend.CreateCache(Origin, "b"));
        Assert.True(Backend.CreateCache("https://other.example", "c"));
        Assert.Equal(["b", "a\uDC00"], Backend.GetCacheNames(Origin));

        Assert.True(Backend.DeleteCache(Origin, "b"));
        Assert.False(Backend.DeleteCache(Origin, "b"));
        Assert.Equal(["a\uDC00"], Backend.GetCacheNames(Origin));
        Assert.Equal(["c"], Backend.GetCacheNames("https://other.example"));
    }

    [Fact]
    public void Entries_AreReturnedInInsertionOrderAndFilteredByUrl()
    {
        Backend.CreateCache(Origin, "v1");
        Backend.ApplyBatch(Origin, "v1", [], [Entry("https://example.com/a?x=1", "1"), Entry("https://example.com/b", "2"), Entry("https://example.com/a#f", "3")]);

        var all = Backend.GetEntries(Origin, "v1", urlWithoutQuery: null);
        Assert.Equal(["https://example.com/a?x=1", "https://example.com/b", "https://example.com/a#f"], all.Select(entry => entry.Request.Url));
        Assert.True(all[0].Id < all[1].Id && all[1].Id < all[2].Id);
        Assert.Equal(["1", "3"], Backend.GetEntries(Origin, "v1", "https://example.com/a").Select(entry => System.Text.Encoding.UTF8.GetString(entry.Response.Body!.Value.Span)));

        Backend.ApplyBatch(Origin, "v1", [all[0].Id], [Entry("https://example.com/c", "4")]);
        Assert.Equal(["https://example.com/b", "https://example.com/a#f", "https://example.com/c"], Backend.GetEntries(Origin, "v1", urlWithoutQuery: null).Select(entry => entry.Request.Url));
    }

    [Fact]
    public void Entries_AreKeptByTheStorage()
    {
        Backend.CreateCache(Origin, "v1");
        Backend.ApplyBatch(Origin, "v1", [], [Entry("https://example.com/a", "body", vary: "Accept")]);

        var backend = Reopen();
        var entry = Assert.Single(backend.GetEntries(Origin, "v1", urlWithoutQuery: null));
        Assert.Equal("GET", entry.Request.Method);
        Assert.Equal("no-cors", entry.Request.Mode);
        Assert.Equal("https://example.com/page", entry.Request.Referrer);
        Assert.Equal([new("Accept", "text/plain")], entry.Request.Headers);
        Assert.Equal("basic", entry.Response.Type);
        Assert.Equal(201, entry.Response.Status);
        Assert.Equal("Created", entry.Response.StatusText);
        Assert.Equal([new("Content-Type", "text/plain"), new("Vary", "Accept")], entry.Response.Headers);
        Assert.Equal(["https://example.com/a"], entry.Response.UrlList);
        Assert.Equal("body", System.Text.Encoding.UTF8.GetString(entry.Response.Body!.Value.Span));
    }
}

public sealed class InMemoryCacheStorageBackendTests : CacheStorageBackendTests
{
    private readonly InMemoryCacheStorageBackend _backend = new();

    protected override CacheStorageBackend CreateBackend() => _backend;

    // The caches are kept by the instance
    protected override CacheStorageBackend Reopen() => Backend;
}

public sealed class SqliteCacheStorageBackendTests : CacheStorageBackendTests
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "caches-" + Guid.NewGuid().ToString("N") + ".db");

    protected override CacheStorageBackend CreateBackend() => new SqliteCacheStorageBackend(_path);

    protected override void DisposeBackend(CacheStorageBackend backend) => ((SqliteCacheStorageBackend)backend).Dispose();

    protected override void Dispose(bool disposing)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(_path);

        base.Dispose(disposing);
    }
}
