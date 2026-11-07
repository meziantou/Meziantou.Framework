using System.Reflection;
using Meziantou.Framework.HttpArchive;

namespace Meziantou.Framework.Http.Recording.Tests;

public sealed class HarHttpRecordingStoreTests
{
    [Fact]
    public async Task LoadAsync_FileDoesNotExist_ReturnsEmpty()
    {
        using var temporaryFile = TemporaryFile.Create("nonexistent.har");
        var filePath = (string)temporaryFile;
        File.Delete(filePath);

        var store = new HarHttpRecordingStore(filePath);
        var entries = await store.LoadAsync(CancellationToken.None);
        Assert.Empty(entries);
    }

    [Fact]
    public async Task RoundTrip_SaveAndLoad()
    {
        using var temporaryFile = TemporaryFile.Create("test.har");
        var filePath = (string)temporaryFile;
        var store = new HarHttpRecordingStore(filePath);

        var entries = new List<HttpRecordingEntry>
        {
            new()
            {
                Method = "GET",
                RequestUri = "https://example.com/api/data",
                StatusCode = 200,
                RequestHeaders = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Accept"] = ["application/json"],
                    ["Host"] = ["example.com"],
                },
                ResponseBody = "{\"id\":1}"u8.ToArray(),
                ResponseHeaders = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
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
        Assert.Equal("{\"id\":1}", System.Text.Encoding.UTF8.GetString(loaded[0].ResponseBody!));
    }

    [Fact]
    public async Task RoundTrip_PreservesHeaders()
    {
        using var temporaryFile = TemporaryFile.Create("headers.har");
        var filePath = (string)temporaryFile;
        var store = new HarHttpRecordingStore(filePath);

        var entries = new List<HttpRecordingEntry>
        {
            new()
            {
                Method = "POST",
                RequestUri = "https://example.com/api/items",
                StatusCode = 201,
                RequestHeaders = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Content-Type"] = ["application/json"],
                    ["X-Custom"] = ["value1", "value2"],
                },
                RequestBody = "{\"name\":\"test\"}"u8.ToArray(),
                ResponseHeaders = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
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
        Assert.Contains("Content-Type", loaded[0].RequestHeaders!);
        Assert.NotNull(loaded[0].RequestBody);
    }

    [Fact]
    public async Task RoundTrip_BinaryResponseBody_UsesBase64AndPreservesBytes()
    {
        using var temporaryFile = TemporaryFile.Create("binary.har");
        var filePath = (string)temporaryFile;
        var store = new HarHttpRecordingStore(filePath);

        var responseBody = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46 };
        var entries = new List<HttpRecordingEntry>
        {
            new()
            {
                Method = "GET",
                RequestUri = "https://example.com/image.jpg",
                StatusCode = 200,
                ResponseHeaders = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
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
            var harEntry = Assert.Single(harDocument.Log!.Entries!);
            Assert.Equal("base64", harEntry.Response!.Content!.Encoding);
        }

        var loaded = await store.LoadAsync(CancellationToken.None);
        var loadedEntry = Assert.Single(loaded);
        Assert.Equal(responseBody, loadedEntry.ResponseBody);
    }

    [Fact]
    public async Task RoundTrip_BinaryRequestBody_UsesVendorBase64AndPreservesBytes()
    {
        using var temporaryFile = TemporaryFile.Create("binary-request.har");
        var filePath = (string)temporaryFile;
        var store = new HarHttpRecordingStore(filePath);

        var requestBody = new byte[] { 0x00, 0x01, 0x02, 0x7F, 0x80, 0xFF };
        var entries = new List<HttpRecordingEntry>
        {
            new()
            {
                Method = "POST",
                RequestUri = "https://example.com/upload",
                StatusCode = 204,
                RequestHeaders = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
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
            var harEntry = Assert.Single(harDocument.Log!.Entries!);
            var postData = harEntry.Request!.PostData;
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

    [Fact]
    public async Task SaveAsync_Canceled_DoesNotDestroyTheExistingFile()
    {
        using var temporaryFile = TemporaryFile.Create("recordings.har");
        var filePath = (string)temporaryFile;
        var store = new HarHttpRecordingStore(filePath);
        var entries = new List<HttpRecordingEntry>
        {
            new() { Method = "GET", RequestUri = "https://example.com/api", StatusCode = 200, ResponseBody = "hi"u8.ToArray() },
        };
        await store.SaveAsync(entries, CancellationToken.None);
        var original = await File.ReadAllTextAsync(filePath);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await store.SaveAsync(entries, cts.Token));

        Assert.Equal(original, await File.ReadAllTextAsync(filePath));
        Assert.Single(await store.LoadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task LoadAsync_NullLog_ThrowsInsteadOfReportingNoRecordings()
    {
        using var temporaryFile = TemporaryFile.Create("no-log.har");
        var filePath = (string)temporaryFile;
        await File.WriteAllTextAsync(filePath, "{\"log\":null}");

        var store = new HarHttpRecordingStore(filePath);
        var exception = await Assert.ThrowsAsync<InvalidDataException>(async () => await store.LoadAsync(CancellationToken.None));
        Assert.Contains("no-log.har", exception.Message);
    }

    [Fact]
    public async Task LoadAsync_EmptyLog_ReturnsNoRecordings()
    {
        using var temporaryFile = TemporaryFile.Create("empty-log.har");
        var filePath = (string)temporaryFile;
        await File.WriteAllTextAsync(filePath, "{}");

        // An empty entries list is a legitimately empty recording, not a damaged file.
        var store = new HarHttpRecordingStore(filePath);
        Assert.Empty(await store.LoadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task LoadAsync_MalformedBase64ResponseBody_ReportsNoBodyInsteadOfThrowing()
    {
        using var temporaryFile = TemporaryFile.Create("bad-base64.har");
        var filePath = (string)temporaryFile;
        await File.WriteAllTextAsync(filePath, """
            {"log":{"version":"1.2","entries":[{"startedDateTime":"2024-01-01T00:00:00Z",
            "request":{"method":"GET","url":"https://example.com/api"},
            "response":{"status":200,"content":{"size":3,"mimeType":"application/octet-stream","encoding":"base64","text":"not valid base64 !!"}}}]}}
            """);

        var store = new HarHttpRecordingStore(filePath);
        var entry = Assert.Single(await store.LoadAsync(CancellationToken.None));
        Assert.Null(entry.ResponseBody);
    }

    [Fact]
    public async Task LoadAsync_UnknownContentEncoding_ReportsNoBodyInsteadOfCorruptingIt()
    {
        using var temporaryFile = TemporaryFile.Create("gzip-encoding.har");
        var filePath = (string)temporaryFile;
        await File.WriteAllTextAsync(filePath, """
            {"log":{"version":"1.2","entries":[{"startedDateTime":"2024-01-01T00:00:00Z",
            "request":{"method":"GET","url":"https://example.com/api"},
            "response":{"status":200,"content":{"size":3,"mimeType":"application/json","encoding":"gzip","text":"H4sIAAAA"}}}]}}
            """);

        var store = new HarHttpRecordingStore(filePath);
        var entry = Assert.Single(await store.LoadAsync(CancellationToken.None));
        Assert.Null(entry.ResponseBody);
    }

    [Fact]
    public async Task RoundTrip_BinaryResponseBody()
    {
        using var temporaryFile = TemporaryFile.Create("binary.har");
        var filePath = (string)temporaryFile;
        var store = new HarHttpRecordingStore(filePath);
        var body = new byte[] { 0x00, 0xFF, 0x10, 0x80, 0x7F };
        var entries = new List<HttpRecordingEntry>
        {
            new()
            {
                Method = "GET",
                RequestUri = "https://example.com/image",
                StatusCode = 200,
                ResponseHeaders = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase) { ["Content-Type"] = ["image/png"] },
                ResponseBody = body,
            },
        };

        await store.SaveAsync(entries, CancellationToken.None);
        var loaded = Assert.Single(await store.LoadAsync(CancellationToken.None));

        Assert.Equal(body, loaded.ResponseBody);
    }

    [Fact]
    public async Task RoundTrip_PreservesReasonPhraseAndHttpVersion()
    {
        using var temporaryFile = TemporaryFile.Create("metadata.har");
        var filePath = (string)temporaryFile;
        var store = new HarHttpRecordingStore(filePath);
        var entries = new List<HttpRecordingEntry>
        {
            new()
            {
                Method = "GET",
                RequestUri = "https://example.com/api",
                StatusCode = 599,
                ReasonPhrase = "Vendor Failure",
                HttpVersion = "2.0",
            },
        };

        await store.SaveAsync(entries, CancellationToken.None);
        var loaded = Assert.Single(await store.LoadAsync(CancellationToken.None));

        Assert.Equal("Vendor Failure", loaded.ReasonPhrase);
        Assert.Equal("2.0", loaded.HttpVersion);
    }

    [Fact]
    public async Task SaveAsync_WritesUnknownSizesAsMinusOne()
    {
        using var temporaryFile = TemporaryFile.Create("sizes.har");
        var filePath = (string)temporaryFile;
        var store = new HarHttpRecordingStore(filePath);
        var entries = new List<HttpRecordingEntry>
        {
            new() { Method = "GET", RequestUri = "https://example.com/api", StatusCode = 200, ResponseBody = "hello"u8.ToArray() },
        };

        await store.SaveAsync(entries, CancellationToken.None);

        await using var stream = File.OpenRead(filePath);
        var doc = await HarDocument.ParseAsync(stream, CancellationToken.None);
        var harEntry = Assert.Single(doc.Log!.Entries!);

        // HAR 1.2 uses -1 for "not available"; 0 would be a factual claim that tooling plots.
        Assert.Equal(-1, harEntry.Request!.HeadersSize);
        Assert.Equal(-1, harEntry.Request.BodySize);
        Assert.Equal(-1, harEntry.Response!.HeadersSize);
        Assert.Equal(5, harEntry.Response.BodySize);
        Assert.Equal(-1, harEntry.Time);
    }

    [Fact]
    public async Task SaveAsync_WritesTheAssemblyVersionAsCreatorVersion()
    {
        using var temporaryFile = TemporaryFile.Create("creator.har");
        var filePath = (string)temporaryFile;
        var store = new HarHttpRecordingStore(filePath);
        await store.SaveAsync([new() { Method = "GET", RequestUri = "https://example.com/api", StatusCode = 200 }], CancellationToken.None);

        await using var stream = File.OpenRead(filePath);
        var doc = await HarDocument.ParseAsync(stream, CancellationToken.None);

        var informationalVersion = typeof(HarHttpRecordingStore).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion;
        var expected = informationalVersion.Split('+')[0];

        Assert.Equal(expected, doc.Log!.Creator!.Version);
    }
}
