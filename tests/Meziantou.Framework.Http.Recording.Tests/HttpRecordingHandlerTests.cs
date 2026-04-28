using System.Net;
using Meziantou.Framework;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
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
        using var innerHandler = new FakeHttpHandler(HttpStatusCode.OK, "hello");
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
        Assert.Contains("/api/test", store.SavedEntries[0].RequestUri, StringComparison.Ordinal);
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
                ResponseHeaders = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Content-Type"] = ["text/plain; charset=utf-8"],
                },
            },
        };
        var store = new InMemoryRecordingStore(entries);
        using var innerHandler = new FakeHttpHandler(HttpStatusCode.InternalServerError);
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
        using var innerHandler = new FakeHttpHandler(HttpStatusCode.OK);
        var options = new HttpRecordingOptions
        {
            Mode = HttpRecordingMode.Replay,
            MissBehavior = HttpRecordingMissBehavior.Throw,
        };

        using var handler = CreateHandler(innerHandler, store, options);
        using var client = CreateClient(handler);

        var ex = await Assert.ThrowsAsync<HttpRecordingMissException>(() => client.GetAsync("/api/missing"));
        Assert.Equal("GET", ex.Method);
        Assert.Contains("/api/missing", ex.RequestUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReplayMode_MissBehaviorReturnDefault_Returns500()
    {
        var store = new InMemoryRecordingStore();
        using var innerHandler = new FakeHttpHandler(HttpStatusCode.OK);
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
        Assert.Contains("No recorded response found", body, StringComparison.Ordinal);
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
        using var innerHandler = new FakeHttpHandler(HttpStatusCode.InternalServerError);
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
        using var innerHandler = new FakeHttpHandler(HttpStatusCode.OK, "new-response");
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
        using var innerHandler = new FakeHttpHandler(HttpStatusCode.OK);
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
        using var innerHandler = new FakeHttpHandler(HttpStatusCode.InternalServerError);
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
        using var innerHandler = new FakeHttpHandler(HttpStatusCode.InternalServerError);
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
        using var innerHandler = new FakeHttpHandler(HttpStatusCode.Created);
        var store = new InMemoryRecordingStore();
        var options = new HttpRecordingOptions { Mode = HttpRecordingMode.Record };

        using var handler = CreateHandler(innerHandler, store, options);
        using var client = CreateClient(handler);

        using var content = new StringContent("{\"name\":\"test\"}", System.Text.Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("/api/items", content);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await handler.SaveAsync();

        var entry = Assert.Single(store.SavedEntries);
        Assert.Equal("POST", entry.Method);
        Assert.NotNull(entry.RequestBody);
        Assert.Contains("test", System.Text.Encoding.UTF8.GetString(entry.RequestBody), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Integration_JsonStore_RecordThenReplay()
    {
        using var recordingsFile = TemporaryFile.Create("recordings.json");
        var recordingsPath = (string)recordingsFile;
        File.Delete(recordingsPath);
        var (app, baseAddress) = await StartTestServerAsync();
        try
        {
            using var recordInnerHandler = new SocketsHttpHandler();
            using var recordHandler = new HttpRecordingHandler(recordInnerHandler, new JsonHttpRecordingStore(recordingsPath), new HttpRecordingOptions
            {
                Mode = HttpRecordingMode.Record,
            });

            using var recordClient = new HttpClient(recordHandler)
            {
                BaseAddress = baseAddress,
            };

            using (var response = await recordClient.GetAsync("/api/text"))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal("hello integration", await response.Content.ReadAsStringAsync());
            }

            using (var response = await recordClient.GetAsync("/api/items/42?b=2&a=1"))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal("{\"id\":42,\"name\":\"item-42\"}", await response.Content.ReadAsStringAsync());
            }

            using var postContent = new StringContent("posted-value");
            using (var response = await recordClient.PostAsync("/api/echo", postContent))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal("echo:posted-value", await response.Content.ReadAsStringAsync());
            }

            await recordHandler.SaveAsync();
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }

        using var replayInnerHandler = new SocketsHttpHandler();
        using var replayHandler = new HttpRecordingHandler(replayInnerHandler, new JsonHttpRecordingStore(recordingsPath), new HttpRecordingOptions
        {
            Mode = HttpRecordingMode.Replay,
        });

        using var replayClient = new HttpClient(replayHandler)
        {
            BaseAddress = baseAddress,
        };

        using (var response = await replayClient.GetAsync("/api/text"))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("hello integration", await response.Content.ReadAsStringAsync());
        }

        using (var response = await replayClient.GetAsync("/api/items/42?a=1&b=2"))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("{\"id\":42,\"name\":\"item-42\"}", await response.Content.ReadAsStringAsync());
        }

        using var replayPostContent = new StringContent("posted-value");
        using (var response = await replayClient.PostAsync("/api/echo", replayPostContent))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("echo:posted-value", await response.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task Integration_HarStore_RecordThenReplay()
    {
        using var recordingsFile = TemporaryFile.Create("recordings.har");
        var recordingsPath = (string)recordingsFile;
        File.Delete(recordingsPath);
        var (app, baseAddress) = await StartTestServerAsync();
        try
        {
            using var recordInnerHandler = new SocketsHttpHandler();
            using var recordHandler = new HttpRecordingHandler(recordInnerHandler, new HarHttpRecordingStore(recordingsPath), new HttpRecordingOptions
            {
                Mode = HttpRecordingMode.Record,
            });

            using var recordClient = new HttpClient(recordHandler)
            {
                BaseAddress = baseAddress,
            };

            using (var response = await recordClient.GetAsync("/api/binary"))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal("AAECAwQF/w==", Convert.ToBase64String(await response.Content.ReadAsByteArrayAsync()));
            }

            using var postContent = new ByteArrayContent(new byte[] { 0x00, 0x01, 0x7F, 0x80, 0xFF });
            using (var response = await recordClient.PostAsync("/api/echo", postContent))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal("echo-bytes:00017F80FF", await response.Content.ReadAsStringAsync());
            }

            await recordHandler.SaveAsync();
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }

        using var replayInnerHandler = new SocketsHttpHandler();
        using var replayHandler = new HttpRecordingHandler(replayInnerHandler, new HarHttpRecordingStore(recordingsPath), new HttpRecordingOptions
        {
            Mode = HttpRecordingMode.Replay,
        });

        using var replayClient = new HttpClient(replayHandler)
        {
            BaseAddress = baseAddress,
        };

        using (var response = await replayClient.GetAsync("/api/binary"))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("AAECAwQF/w==", Convert.ToBase64String(await response.Content.ReadAsByteArrayAsync()));
        }

        using var replayPostContent = new ByteArrayContent(new byte[] { 0x00, 0x01, 0x7F, 0x80, 0xFF });
        using (var response = await replayClient.PostAsync("/api/echo", replayPostContent))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("echo-bytes:00017F80FF", await response.Content.ReadAsStringAsync());
        }
    }

    private static async Task<(WebApplication App, Uri BaseAddress)> StartTestServerAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        var app = builder.Build();
        app.MapGet("/api/text", static () => Results.Text("hello integration"));
        app.MapGet("/api/items/{id:int}", static (int id) => Results.Json(new { id, name = $"item-{id}" }));
        app.MapGet("/api/binary", static () => Results.Bytes(new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0xFF }, "application/octet-stream"));
        app.MapPost("/api/echo", static async (HttpRequest request) =>
        {
            await using var stream = new MemoryStream();
            await request.Body.CopyToAsync(stream);
            var bytes = stream.ToArray();
            if (request.ContentType is not null && request.ContentType.StartsWith("text/plain", StringComparison.Ordinal))
            {
                return Results.Text("echo:" + System.Text.Encoding.UTF8.GetString(bytes));
            }

            return Results.Text("echo-bytes:" + Convert.ToHexString(bytes));
        });

        await app.StartAsync();
        var address = app.Urls.First(static u => u.StartsWith("http://", StringComparison.Ordinal));
        return (app, new Uri(address));
    }

    private sealed class FakeHttpHandler : HttpMessageHandler
    {
        private readonly Func<HttpResponseMessage> _responseFactory;

        public FakeHttpHandler(HttpStatusCode statusCode)
            : this(() => new HttpResponseMessage(statusCode))
        {
        }

        public FakeHttpHandler(HttpStatusCode statusCode, string responseContent)
            : this(() => new HttpResponseMessage(statusCode) { Content = new StringContent(responseContent) })
        {
        }

        private FakeHttpHandler(Func<HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(_responseFactory());
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
