using System.Net;
using Xunit;

namespace Meziantou.Framework.Http.Recording.Tests;

public sealed class HttpRecordingHandlerTests
{
    private static HttpRecordingHandler CreateHandler(
        HttpMessageHandler innerHandler,
        IHttpRecordingStore store,
        HttpRecordingOptions? options = null)
    {
        return new HttpRecordingHandler(innerHandler, store, options);
    }

    private static HttpClient CreateClient(HttpRecordingHandler handler)
    {
        return new HttpClient(handler)
        {
            BaseAddress = new Uri("https://example.com"),
        };
    }

    [Fact]
    public async Task RecordMode_CallsInnerHandler_AndRecordsEntry()
    {
        var innerHandler = new FakeHttpHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("hello"),
        });
        var store = new InMemoryRecordingStore();
        var options = new HttpRecordingOptions { Mode = HttpRecordingMode.Record };

        using var handler = CreateHandler(innerHandler, store, options);
        using var client = CreateClient(handler);

        using var response = await client.GetAsync("/api/test");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, innerHandler.CallCount);

        await handler.SaveAsync();
        Assert.Single(store.SavedEntries);
        Assert.Equal("GET", store.SavedEntries[0].Method);
        Assert.Contains("/api/test", store.SavedEntries[0].RequestUri);
    }

    [Fact]
    public async Task ReplayMode_ReturnsRecordedResponse_WithoutCallingInner()
    {
        var entries = new List<HttpRecordingEntry>
        {
            new()
            {
                Method = "GET",
                RequestUri = "https://example.com/api/test",
                StatusCode = 200,
                ResponseBody = "recorded"u8.ToArray(),
                ResponseHeaders = new Dictionary<string, string[]>
                {
                    ["Content-Type"] = ["text/plain; charset=utf-8"],
                },
            },
        };
        var store = new InMemoryRecordingStore(entries);
        var innerHandler = new FakeHttpHandler(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var options = new HttpRecordingOptions { Mode = HttpRecordingMode.Replay };

        using var handler = CreateHandler(innerHandler, store, options);
        using var client = CreateClient(handler);

        using var response = await client.GetAsync("/api/test");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, innerHandler.CallCount);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("recorded", body);
    }

    [Fact]
    public async Task ReplayMode_MissBehaviorThrow_ThrowsException()
    {
        var store = new InMemoryRecordingStore();
        var innerHandler = new FakeHttpHandler(new HttpResponseMessage(HttpStatusCode.OK));
        var options = new HttpRecordingOptions
        {
            Mode = HttpRecordingMode.Replay,
            MissBehavior = HttpRecordingMissBehavior.Throw,
        };

        using var handler = CreateHandler(innerHandler, store, options);
        using var client = CreateClient(handler);

        var ex = await Assert.ThrowsAsync<HttpRecordingMissException>(() => client.GetAsync("/api/missing"));
        Assert.Equal("GET", ex.Method);
        Assert.Contains("/api/missing", ex.RequestUri);
    }

    [Fact]
    public async Task ReplayMode_MissBehaviorReturnDefault_Returns500()
    {
        var store = new InMemoryRecordingStore();
        var innerHandler = new FakeHttpHandler(new HttpResponseMessage(HttpStatusCode.OK));
        var options = new HttpRecordingOptions
        {
            Mode = HttpRecordingMode.Replay,
            MissBehavior = HttpRecordingMissBehavior.ReturnDefault,
        };

        using var handler = CreateHandler(innerHandler, store, options);
        using var client = CreateClient(handler);

        using var response = await client.GetAsync("/api/missing");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("No recorded response found", body);
    }

    [Fact]
    public async Task AutoMode_ReplaysExistingMatch()
    {
        var entries = new List<HttpRecordingEntry>
        {
            new()
            {
                Method = "GET",
                RequestUri = "https://example.com/api/test",
                StatusCode = 200,
                ResponseBody = "cached"u8.ToArray(),
            },
        };
        var store = new InMemoryRecordingStore(entries);
        var innerHandler = new FakeHttpHandler(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var options = new HttpRecordingOptions { Mode = HttpRecordingMode.Auto };

        using var handler = CreateHandler(innerHandler, store, options);
        using var client = CreateClient(handler);

        using var response = await client.GetAsync("/api/test");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, innerHandler.CallCount);
    }

    [Fact]
    public async Task AutoMode_RecordsOnMiss()
    {
        var store = new InMemoryRecordingStore();
        var innerHandler = new FakeHttpHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("new-response"),
        });
        var options = new HttpRecordingOptions { Mode = HttpRecordingMode.Auto };

        using var handler = CreateHandler(innerHandler, store, options);
        using var client = CreateClient(handler);

        using var response = await client.GetAsync("/api/new");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, innerHandler.CallCount);

        await handler.SaveAsync();
        Assert.Single(store.SavedEntries);
    }

    [Fact]
    public async Task RecordMode_AppliesSanitizer()
    {
        var innerHandler = new FakeHttpHandler(new HttpResponseMessage(HttpStatusCode.OK));
        var store = new InMemoryRecordingStore();
        var options = new HttpRecordingOptions
        {
            Mode = HttpRecordingMode.Record,
            Sanitizer = new HeaderRemovalSanitizer("Authorization"),
        };

        using var handler = CreateHandler(innerHandler, store, options);
        using var client = CreateClient(handler);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/test");
        request.Headers.Add("Authorization", "Bearer secret");
        using var response = await client.SendAsync(request);

        await handler.SaveAsync();
        var entry = Assert.Single(store.SavedEntries);
        Assert.NotNull(entry.RequestHeaders);
        Assert.False(entry.RequestHeaders.ContainsKey("Authorization"));
    }

    [Fact]
    public async Task ReplayMode_FIFO_MultipleIdenticalRequests()
    {
        var entries = new List<HttpRecordingEntry>
        {
            new()
            {
                Method = "GET",
                RequestUri = "https://example.com/api/data",
                StatusCode = 200,
                ResponseBody = "first"u8.ToArray(),
            },
            new()
            {
                Method = "GET",
                RequestUri = "https://example.com/api/data",
                StatusCode = 200,
                ResponseBody = "second"u8.ToArray(),
            },
        };
        var store = new InMemoryRecordingStore(entries);
        var innerHandler = new FakeHttpHandler(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var options = new HttpRecordingOptions { Mode = HttpRecordingMode.Replay };

        using var handler = CreateHandler(innerHandler, store, options);
        using var client = CreateClient(handler);

        using var response1 = await client.GetAsync("/api/data");
        var body1 = await response1.Content.ReadAsStringAsync();
        Assert.Equal("first", body1);

        using var response2 = await client.GetAsync("/api/data");
        var body2 = await response2.Content.ReadAsStringAsync();
        Assert.Equal("second", body2);
    }

    [Fact]
    public async Task InitializeAsync_CanBeCalledExplicitly()
    {
        var entries = new List<HttpRecordingEntry>
        {
            new()
            {
                Method = "GET",
                RequestUri = "https://example.com/api/test",
                StatusCode = 204,
            },
        };
        var store = new InMemoryRecordingStore(entries);
        var innerHandler = new FakeHttpHandler(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var options = new HttpRecordingOptions { Mode = HttpRecordingMode.Replay };

        using var handler = CreateHandler(innerHandler, store, options);
        await handler.InitializeAsync();

        using var client = CreateClient(handler);
        using var response = await client.GetAsync("/api/test");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task RecordMode_CapturesPostBody()
    {
        var innerHandler = new FakeHttpHandler(new HttpResponseMessage(HttpStatusCode.Created));
        var store = new InMemoryRecordingStore();
        var options = new HttpRecordingOptions { Mode = HttpRecordingMode.Record };

        using var handler = CreateHandler(innerHandler, store, options);
        using var client = CreateClient(handler);

        using var response = await client.PostAsync("/api/items", new StringContent("{\"name\":\"test\"}", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await handler.SaveAsync();

        var entry = Assert.Single(store.SavedEntries);
        Assert.Equal("POST", entry.Method);
        Assert.NotNull(entry.RequestBody);
        Assert.Contains("test", System.Text.Encoding.UTF8.GetString(entry.RequestBody));
    }

    private sealed class FakeHttpHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;

        public FakeHttpHandler(HttpResponseMessage response)
        {
            _response = response;
        }

        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(_response);
        }
    }

    private sealed class InMemoryRecordingStore : IHttpRecordingStore
    {
        private readonly List<HttpRecordingEntry> _entries;

        public InMemoryRecordingStore()
        {
            _entries = [];
        }

        public InMemoryRecordingStore(List<HttpRecordingEntry> entries)
        {
            _entries = entries;
        }

        public List<HttpRecordingEntry> SavedEntries { get; private set; } = [];

        public ValueTask<IReadOnlyList<HttpRecordingEntry>> LoadAsync(CancellationToken cancellationToken)
        {
            return new ValueTask<IReadOnlyList<HttpRecordingEntry>>(_entries);
        }

        public ValueTask SaveAsync(IReadOnlyList<HttpRecordingEntry> entries, CancellationToken cancellationToken)
        {
            SavedEntries = new List<HttpRecordingEntry>(entries);
            return default;
        }
    }
}
