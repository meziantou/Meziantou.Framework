using Meziantou.Framework.HttpArchive;
using Xunit;

namespace Meziantou.Framework.Http.Recording.Tests;

public sealed class HarHttpRecordingStoreTests : IDisposable
{
    private readonly string _tempDir;

    public HarHttpRecordingStoreTests()
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
        var store = new HarHttpRecordingStore(Path.Combine(_tempDir, "nonexistent.har"));
        var entries = await store.LoadAsync(CancellationToken.None);
        Assert.Empty(entries);
    }

    [Fact]
    public async Task RoundTrip_SaveAndLoad()
    {
        var filePath = Path.Combine(_tempDir, "test.har");
        var store = new HarHttpRecordingStore(filePath);

        var entries = new List<HttpRecordingEntry>
        {
            new()
            {
                Method = "GET",
                RequestUri = "https://example.com/api/data",
                StatusCode = 200,
                RequestHeaders = new Dictionary<string, string[]>
                {
                    ["Accept"] = ["application/json"],
                    ["Host"] = ["example.com"],
                },
                ResponseBody = "{\"id\":1}"u8.ToArray(),
                ResponseHeaders = new Dictionary<string, string[]>
                {
                    ["Content-Type"] = ["application/json"],
                },
                RecordedAt = new DateTimeOffset(2025, 6, 15, 10, 30, 0, TimeSpan.Zero),
            },
        };

        await store.SaveAsync(entries, CancellationToken.None);
        Assert.True(File.Exists(filePath));

        // Verify it's valid HAR
        var content = await File.ReadAllTextAsync(filePath);
        Assert.Contains("\"log\"", content);
        Assert.Contains("\"version\"", content);
        Assert.Contains("\"entries\"", content);

        var loaded = await store.LoadAsync(CancellationToken.None);
        Assert.Single(loaded);
        Assert.Equal("GET", loaded[0].Method);
        Assert.Equal("https://example.com/api/data", loaded[0].RequestUri);
        Assert.Equal(200, loaded[0].StatusCode);
        Assert.NotNull(loaded[0].ResponseBody);
        Assert.Equal("{\"id\":1}", System.Text.Encoding.UTF8.GetString(loaded[0].ResponseBody));
    }

    [Fact]
    public async Task RoundTrip_PreservesHeaders()
    {
        var filePath = Path.Combine(_tempDir, "headers.har");
        var store = new HarHttpRecordingStore(filePath);

        var entries = new List<HttpRecordingEntry>
        {
            new()
            {
                Method = "POST",
                RequestUri = "https://example.com/api/items",
                StatusCode = 201,
                RequestHeaders = new Dictionary<string, string[]>
                {
                    ["Content-Type"] = ["application/json"],
                    ["X-Custom"] = ["value1", "value2"],
                },
                RequestBody = "{\"name\":\"test\"}"u8.ToArray(),
                ResponseHeaders = new Dictionary<string, string[]>
                {
                    ["Location"] = ["/api/items/1"],
                },
            },
        };

        await store.SaveAsync(entries, CancellationToken.None);
        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.Single(loaded);
        Assert.Equal("POST", loaded[0].Method);
        Assert.Equal(201, loaded[0].StatusCode);
        Assert.NotNull(loaded[0].RequestHeaders);
        Assert.True(loaded[0].RequestHeaders.ContainsKey("Content-Type"));
        Assert.NotNull(loaded[0].RequestBody);
    }

    [Fact]
    public async Task RoundTrip_BinaryResponseBody_UsesBase64AndPreservesBytes()
    {
        var filePath = Path.Combine(_tempDir, "binary.har");
        var store = new HarHttpRecordingStore(filePath);

        var responseBody = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46 };
        var entries = new List<HttpRecordingEntry>
        {
            new()
            {
                Method = "GET",
                RequestUri = "https://example.com/image.jpg",
                StatusCode = 200,
                ResponseHeaders = new Dictionary<string, string[]>
                {
                    ["Content-Type"] = ["image/jpeg"],
                },
                ResponseBody = responseBody,
            },
        };

        await store.SaveAsync(entries, CancellationToken.None);

        await using (var stream = File.OpenRead(filePath))
        {
            var harDocument = await HarDocument.ParseAsync(stream, CancellationToken.None);
            var harEntry = Assert.Single(harDocument.Log.Entries);
            Assert.Equal("base64", harEntry.Response.Content.Encoding);
        }

        var loaded = await store.LoadAsync(CancellationToken.None);
        var loadedEntry = Assert.Single(loaded);
        Assert.Equal(responseBody, loadedEntry.ResponseBody);
    }

    [Fact]
    public async Task RoundTrip_BinaryRequestBody_UsesVendorBase64AndPreservesBytes()
    {
        var filePath = Path.Combine(_tempDir, "binary-request.har");
        var store = new HarHttpRecordingStore(filePath);

        var requestBody = new byte[] { 0x00, 0x01, 0x02, 0x7F, 0x80, 0xFF };
        var entries = new List<HttpRecordingEntry>
        {
            new()
            {
                Method = "POST",
                RequestUri = "https://example.com/upload",
                StatusCode = 204,
                RequestHeaders = new Dictionary<string, string[]>
                {
                    ["Content-Type"] = ["application/octet-stream"],
                },
                RequestBody = requestBody,
            },
        };

        await store.SaveAsync(entries, CancellationToken.None);

        await using (var stream = File.OpenRead(filePath))
        {
            var harDocument = await HarDocument.ParseAsync(stream, CancellationToken.None);
            var harEntry = Assert.Single(harDocument.Log.Entries);
            var postData = harEntry.Request.PostData;
            Assert.NotNull(postData);
            Assert.Equal(Convert.ToBase64String(requestBody), postData.Text);
            Assert.NotNull(postData.ExtensionData);
            Assert.True(postData.ExtensionData.TryGetValue("x-meziantou-encoding", out var encoding));
            Assert.Equal("base64", encoding.GetString());
        }

        var loaded = await store.LoadAsync(CancellationToken.None);
        var loadedEntry = Assert.Single(loaded);
        Assert.Equal(requestBody, loadedEntry.RequestBody);
    }
}
