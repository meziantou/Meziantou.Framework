using Xunit;

namespace Meziantou.Framework.Http.Recording.Tests;

public sealed class JsonHttpRecordingStoreTests : IDisposable
{
    private readonly string _tempDir;

    public JsonHttpRecordingStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "HttpRecordingTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_FileDoesNotExist_ReturnsEmpty()
    {
        var store = new JsonHttpRecordingStore(Path.Combine(_tempDir, "nonexistent.json"));
        var entries = await store.LoadAsync(CancellationToken.None);
        Assert.Empty(entries);
    }

    [Fact]
    public async Task RoundTrip_SaveAndLoad()
    {
        var filePath = Path.Combine(_tempDir, "test.json");
        var store = new JsonHttpRecordingStore(filePath);

        var entries = new List<HttpRecordingEntry>
        {
            new()
            {
                Method = "GET",
                RequestUri = "https://example.com/api/test",
                StatusCode = 200,
                RequestHeaders = new Dictionary<string, string[]>
                {
                    ["Accept"] = ["application/json"],
                },
                ResponseBody = "hello"u8.ToArray(),
                ResponseHeaders = new Dictionary<string, string[]>
                {
                    ["Content-Type"] = ["text/plain"],
                },
                RecordedAt = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
            },
            new()
            {
                Method = "POST",
                RequestUri = "https://example.com/api/items",
                StatusCode = 201,
                RequestBody = "{\"name\":\"test\"}"u8.ToArray(),
                RecordedAt = new DateTimeOffset(2025, 1, 1, 0, 0, 1, TimeSpan.Zero),
            },
        };

        await store.SaveAsync(entries, CancellationToken.None);

        Assert.True(File.Exists(filePath));

        var loaded = await store.LoadAsync(CancellationToken.None);
        Assert.Equal(2, loaded.Count);

        Assert.Equal("GET", loaded[0].Method);
        Assert.Equal("https://example.com/api/test", loaded[0].RequestUri);
        Assert.Equal(200, loaded[0].StatusCode);
        Assert.NotNull(loaded[0].ResponseBody);
        Assert.Equal("hello", System.Text.Encoding.UTF8.GetString(loaded[0].ResponseBody));

        Assert.Equal("POST", loaded[1].Method);
        Assert.Equal(201, loaded[1].StatusCode);
        Assert.NotNull(loaded[1].RequestBody);
    }

    [Fact]
    public async Task SaveAsync_CreatesDirectoryIfNeeded()
    {
        var filePath = Path.Combine(_tempDir, "subdir", "deep", "test.json");
        var store = new JsonHttpRecordingStore(filePath);

        var entries = new List<HttpRecordingEntry>
        {
            new() { Method = "GET", RequestUri = "https://example.com/", StatusCode = 200 },
        };

        await store.SaveAsync(entries, CancellationToken.None);
        Assert.True(File.Exists(filePath));
    }

    [Fact]
    public async Task SaveAsync_OverwritesPreviousFile()
    {
        var filePath = Path.Combine(_tempDir, "overwrite.json");
        var store = new JsonHttpRecordingStore(filePath);

        await store.SaveAsync(
            [new() { Method = "GET", RequestUri = "https://example.com/old", StatusCode = 200 }],
            CancellationToken.None);

        await store.SaveAsync(
            [new() { Method = "POST", RequestUri = "https://example.com/new", StatusCode = 201 }],
            CancellationToken.None);

        var loaded = await store.LoadAsync(CancellationToken.None);
        Assert.Single(loaded);
        Assert.Equal("POST", loaded[0].Method);
    }
}
