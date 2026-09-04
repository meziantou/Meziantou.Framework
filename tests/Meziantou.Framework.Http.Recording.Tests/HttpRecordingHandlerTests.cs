using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;

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
        Assert.Contains("/api/missing", ex.RequestUri);
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
            Sanitizers = { new HeaderRemovalSanitizer("Authorization") },
        };

        using var handler = CreateHandler(innerHandler, store, options);
        using var client = CreateClient(handler);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/test");
        request.Headers.Add("Authorization", "Bearer secret");
        using var response = await client.SendAsync(request);

        await handler.SaveAsync();
        var entry = Assert.Single(store.SavedEntries);
        Assert.NotNull(entry.RequestHeaders);
        Assert.DoesNotContain("Authorization", entry.RequestHeaders);
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
        Assert.Contains("test", System.Text.Encoding.UTF8.GetString(entry.RequestBody));
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

    [Fact]
    public async Task AutoMode_MissBehaviorThrow_DoesNotCallInnerHandler()
    {
        using var innerHandler = new FakeHttpHandler(HttpStatusCode.OK, "live");
        var store = new InMemoryRecordingStore();
        var options = new HttpRecordingOptions
        {
            Mode = HttpRecordingMode.Auto,
            MissBehavior = HttpRecordingMissBehavior.Throw,
        };

        using var handler = CreateHandler(innerHandler, store, options);
        using var client = CreateClient(handler);

        await Assert.ThrowsAsync<HttpRecordingMissException>(() => client.GetAsync("/api/data"));
        Assert.Equal(0, innerHandler.CallCount);
    }

    [Fact]
    public async Task AutoMode_MissBehaviorReturnDefault_DoesNotCallInnerHandler()
    {
        using var innerHandler = new FakeHttpHandler(HttpStatusCode.OK, "live");
        var store = new InMemoryRecordingStore();
        var options = new HttpRecordingOptions
        {
            Mode = HttpRecordingMode.Auto,
            MissBehavior = HttpRecordingMissBehavior.ReturnDefault,
        };

        using var handler = CreateHandler(innerHandler, store, options);
        using var client = CreateClient(handler);

        using var response = await client.GetAsync("/api/data");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(0, innerHandler.CallCount);
    }

    [Fact]
    public async Task AutoMode_DefaultMissBehavior_RecordsRealCall()
    {
        using var innerHandler = new FakeHttpHandler(HttpStatusCode.OK, "live");
        var store = new InMemoryRecordingStore();
        var options = new HttpRecordingOptions { Mode = HttpRecordingMode.Auto };

        using var handler = CreateHandler(innerHandler, store, options);
        using var client = CreateClient(handler);

        using var response = await client.GetAsync("/api/data");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, innerHandler.CallCount);

        await handler.SaveAsync();
        Assert.Single(store.SavedEntries);
    }

    [Fact]
    public async Task AutoMode_TwoIdenticalRequests_RecordBothAndReplayLater()
    {
        using var innerHandler = new FakeHttpHandler(HttpStatusCode.OK, "live");
        var store = new InMemoryRecordingStore();
        var options = new HttpRecordingOptions { Mode = HttpRecordingMode.Auto };

        using (var handler = CreateHandler(innerHandler, store, options))
        {
            using var client = CreateClient(handler);
            using var response1 = await client.GetAsync("/api/poll");
            using var response2 = await client.GetAsync("/api/poll");
            await handler.SaveAsync();
        }

        // A recorded entry must not be replayed within the session that produced it, otherwise the second request
        // consumes the first one's recording and the file cannot replay its own scenario.
        Assert.Equal(2, innerHandler.CallCount);
        Assert.HasCount(2, store.SavedEntries);

        var replayStore = new InMemoryRecordingStore(store.SavedEntries);
        using var replayInner = new FakeHttpHandler(HttpStatusCode.InternalServerError);
        using var replayHandler = CreateHandler(replayInner, replayStore, new HttpRecordingOptions { Mode = HttpRecordingMode.Replay });
        using var replayClient = CreateClient(replayHandler);

        using var replayed1 = await replayClient.GetAsync("/api/poll");
        using var replayed2 = await replayClient.GetAsync("/api/poll");
        Assert.Equal(HttpStatusCode.OK, replayed1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replayed2.StatusCode);
        Assert.Equal(0, replayInner.CallCount);
    }

    [Fact]
    public async Task RecordMode_ReplacesExistingEntries_DoesNotAppend()
    {
        var existing = new List<HttpRecordingEntry>
        {
            new()
            {
                Method = "GET",
                RequestUri = "https://example.com/api/data",
                StatusCode = 200,
                ResponseBody = "stale"u8.ToArray(),
            },
        };
        var store = new InMemoryRecordingStore(existing);
        using var innerHandler = new FakeHttpHandler(HttpStatusCode.OK, "fresh");
        var options = new HttpRecordingOptions { Mode = HttpRecordingMode.Record };

        using var handler = CreateHandler(innerHandler, store, options);
        using var client = CreateClient(handler);

        using var response = await client.GetAsync("/api/data");
        await handler.SaveAsync();

        var entry = Assert.Single(store.SavedEntries);
        Assert.Equal("fresh", System.Text.Encoding.UTF8.GetString(entry.ResponseBody!));
    }

    [Fact]
    public async Task ReplayMode_DifferentPostBodies_ReturnTheirOwnResponse()
    {
        var store = new InMemoryRecordingStore();
        using (var recordInner = new EchoHttpHandler())
        using (var recordHandler = CreateHandler(recordInner, store, new HttpRecordingOptions { Mode = HttpRecordingMode.Record }))
        {
            using var recordClient = CreateClient(recordHandler);
            using var recordGetUser = new StringContent("{getUser}");
            using var first = await recordClient.PostAsync("/graphql", recordGetUser);
            using var recordDeleteAll = new StringContent("{deleteAll}");
            using var second = await recordClient.PostAsync("/graphql", recordDeleteAll);
            await recordHandler.SaveAsync();
        }

        var replayStore = new InMemoryRecordingStore(store.SavedEntries);
        using var replayInner = new FakeHttpHandler(HttpStatusCode.InternalServerError);
        using var handler = CreateHandler(replayInner, replayStore, new HttpRecordingOptions { Mode = HttpRecordingMode.Replay });
        using var client = CreateClient(handler);

        // Requested in the opposite order: matching on the body must not hand back the other query's response.
        using var deleteAllContent = new StringContent("{deleteAll}");
        using var deleteAll = await client.PostAsync("/graphql", deleteAllContent);
        using var getUserContent = new StringContent("{getUser}");
        using var getUser = await client.PostAsync("/graphql", getUserContent);

        Assert.Equal("echo:{deleteAll}", await deleteAll.Content.ReadAsStringAsync());
        Assert.Equal("echo:{getUser}", await getUser.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task CustomMatcher_CanMatchOnRequestBody()
    {
        var store = new InMemoryRecordingStore();
        var options = new HttpRecordingOptions
        {
            Mode = HttpRecordingMode.Record,
            RequestMatcher = new RequestBodyMatcher(),
        };

        using (var recordInner = new EchoHttpHandler())
        using (var recordHandler = CreateHandler(recordInner, store, options))
        {
            using var recordClient = CreateClient(recordHandler);
            using var recordContent = new StringContent("{getUser}");
            using var recorded = await recordClient.PostAsync("/graphql", recordContent);
            await recordHandler.SaveAsync();
        }

        var replayStore = new InMemoryRecordingStore(store.SavedEntries);
        using var replayInner = new FakeHttpHandler(HttpStatusCode.InternalServerError);
        using var handler = CreateHandler(replayInner, replayStore, new HttpRecordingOptions
        {
            Mode = HttpRecordingMode.Replay,
            RequestMatcher = new RequestBodyMatcher(),
        });
        using var client = CreateClient(handler);

        using var replayContent = new StringContent("{getUser}");
        using var response = await client.PostAsync("/graphql", replayContent);
        Assert.Equal("echo:{getUser}", await response.Content.ReadAsStringAsync());
        Assert.Equal(0, replayInner.CallCount);
    }

    [Fact]
    public async Task Sanitizer_IsAppliedToLookupEntry_SoCustomMatcherStillMatches()
    {
        var store = new InMemoryRecordingStore();
        var recordOptions = new HttpRecordingOptions
        {
            Mode = HttpRecordingMode.Record,
            RequestMatcher = new AuthorizationHeaderMatcher(),
            Sanitizers = { new HeaderRemovalSanitizer("Authorization") },
        };

        using (var recordInner = new FakeHttpHandler(HttpStatusCode.OK, "recorded"))
        using (var recordHandler = CreateHandler(recordInner, store, recordOptions))
        {
            using var recordClient = CreateClient(recordHandler);
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/data");
            request.Headers.Add("Authorization", "Bearer secret");
            using var recorded = await recordClient.SendAsync(request);
            await recordHandler.SaveAsync();
        }

        var replayStore = new InMemoryRecordingStore(store.SavedEntries);
        using var replayInner = new FakeHttpHandler(HttpStatusCode.InternalServerError);
        using var handler = CreateHandler(replayInner, replayStore, new HttpRecordingOptions
        {
            Mode = HttpRecordingMode.Replay,
            RequestMatcher = new AuthorizationHeaderMatcher(),
            Sanitizers = { new HeaderRemovalSanitizer("Authorization") },
        });
        using var client = CreateClient(handler);

        using var replayRequest = new HttpRequestMessage(HttpMethod.Get, "/api/data");
        replayRequest.Headers.Add("Authorization", "Bearer secret");
        using var response = await client.SendAsync(replayRequest);

        Assert.Equal("recorded", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task UriQueryParameterSanitizer_RedactsSecretAndStillReplays()
    {
        var store = new InMemoryRecordingStore();
        var options = new HttpRecordingOptions
        {
            Mode = HttpRecordingMode.Record,
            Sanitizers = { new UriQueryParameterSanitizer("api_key") },
        };

        using (var recordInner = new FakeHttpHandler(HttpStatusCode.OK, "recorded"))
        using (var recordHandler = CreateHandler(recordInner, store, options))
        {
            using var recordClient = CreateClient(recordHandler);
            using var recorded = await recordClient.GetAsync("/api/data?api_key=SUPERSECRET&page=1");
            await recordHandler.SaveAsync();
        }

        var entry = Assert.Single(store.SavedEntries);
        Assert.DoesNotContain("SUPERSECRET", entry.RequestUri);
        Assert.Contains("page=1", entry.RequestUri);

        var replayStore = new InMemoryRecordingStore(store.SavedEntries);
        using var replayInner = new FakeHttpHandler(HttpStatusCode.InternalServerError);
        using var handler = CreateHandler(replayInner, replayStore, new HttpRecordingOptions
        {
            Mode = HttpRecordingMode.Replay,
            Sanitizers = { new UriQueryParameterSanitizer("api_key") },
        });
        using var client = CreateClient(handler);

        using var response = await client.GetAsync("/api/data?api_key=SUPERSECRET&page=1");
        Assert.Equal("recorded", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task RecordMode_StripsCredentialsFromRequestUri()
    {
        using var innerHandler = new FakeHttpHandler(HttpStatusCode.OK);
        var store = new InMemoryRecordingStore();
        var options = new HttpRecordingOptions { Mode = HttpRecordingMode.Record };

        using var handler = CreateHandler(innerHandler, store, options);
        using var client = new HttpClient(handler);

        using var response = await client.GetAsync("https://user:password@example.com/api/data");
        await handler.SaveAsync();

        var entry = Assert.Single(store.SavedEntries);
        Assert.DoesNotContain("password", entry.RequestUri);
    }

    [Fact]
    public async Task RecordMode_NonSeekableStreamContent_IsRecorded()
    {
        using var innerHandler = new ConsumingHttpHandler();
        var store = new InMemoryRecordingStore();
        var options = new HttpRecordingOptions { Mode = HttpRecordingMode.Record };

        using var handler = CreateHandler(innerHandler, store, options);
        using var client = CreateClient(handler);

        using var content = new StreamContent(new NonSeekableStream("payload-bytes"u8.ToArray()));
        using var response = await client.PostAsync("/api/upload", content);

        await handler.SaveAsync();
        var entry = Assert.Single(store.SavedEntries);
        Assert.Equal("payload-bytes", System.Text.Encoding.UTF8.GetString(entry.RequestBody!));
    }

    [Fact]
    public async Task RecordMode_WhenRecordingFails_DisposesTheResponse()
    {
        using var content = new DisposeTrackingContent("body");
        using var innerHandler = new FakeHttpHandler(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        var store = new InMemoryRecordingStore();
        var options = new HttpRecordingOptions
        {
            Mode = HttpRecordingMode.Record,
            Sanitizers = { new ThrowingSanitizer() },
        };

        using var handler = CreateHandler(innerHandler, store, options);
        using var client = CreateClient(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetAsync("/api/data"));

        // The response never reaches the caller, so the handler must dispose it rather than leak its connection.
        Assert.True(content.IsDisposed);
    }

    [Fact]
    public async Task ReplayMode_ResponseCarriesTheRequestMessage()
    {
        var entries = new List<HttpRecordingEntry>
        {
            new() { Method = "GET", RequestUri = "https://example.com/api/data", StatusCode = 200 },
        };
        var store = new InMemoryRecordingStore(entries);
        using var innerHandler = new FakeHttpHandler(HttpStatusCode.InternalServerError);
        using var handler = CreateHandler(innerHandler, store, new HttpRecordingOptions { Mode = HttpRecordingMode.Replay });
        using var client = CreateClient(handler);

        using var response = await client.GetAsync("/api/data");

        Assert.NotNull(response.RequestMessage);
        Assert.Equal(new Uri("https://example.com/api/data"), response.RequestMessage.RequestUri);
    }

    [Fact]
    public async Task ReplayMode_RoundTripsReasonPhraseAndVersion()
    {
        var entries = new List<HttpRecordingEntry>
        {
            new()
            {
                Method = "GET",
                RequestUri = "https://example.com/api/data",
                StatusCode = 599,
                ReasonPhrase = "Vendor Failure",
                HttpVersion = "2.0",
            },
        };
        var store = new InMemoryRecordingStore(entries);
        using var innerHandler = new FakeHttpHandler(HttpStatusCode.InternalServerError);
        using var handler = CreateHandler(innerHandler, store, new HttpRecordingOptions { Mode = HttpRecordingMode.Replay });
        using var client = CreateClient(handler);

        using var response = await client.GetAsync("/api/data");

        Assert.Equal(599, (int)response.StatusCode);
        Assert.Equal("Vendor Failure", response.ReasonPhrase);
        Assert.Equal(new Version(2, 0), response.Version);
    }

    [Fact]
    public async Task ReplayMode_IgnoresRecordedContentLength()
    {
        var entries = new List<HttpRecordingEntry>
        {
            new()
            {
                Method = "GET",
                RequestUri = "https://example.com/api/data",
                StatusCode = 200,
                ResponseHeaders = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Content-Type"] = ["application/json; charset=utf-8"],
                    ["Content-Length"] = ["99999"],
                },
                ResponseBody = "hello"u8.ToArray(),
            },
        };
        var store = new InMemoryRecordingStore(entries);
        using var innerHandler = new FakeHttpHandler(HttpStatusCode.InternalServerError);
        using var handler = CreateHandler(innerHandler, store, new HttpRecordingOptions { Mode = HttpRecordingMode.Replay });
        using var client = CreateClient(handler);

        using var response = await client.GetAsync("/api/data");

        Assert.Equal(5, response.Content.Headers.ContentLength);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("utf-8", response.Content.Headers.ContentType?.CharSet);
        Assert.Equal("hello", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1000)]
    public async Task ReplayMode_OutOfRangeStatusCode_ThrowsDiagnosableError(int statusCode)
    {
        var entries = new List<HttpRecordingEntry>
        {
            new() { Method = "GET", RequestUri = "https://example.com/api/data", StatusCode = statusCode },
        };
        var store = new InMemoryRecordingStore(entries);
        using var innerHandler = new FakeHttpHandler(HttpStatusCode.InternalServerError);
        using var handler = CreateHandler(innerHandler, store, new HttpRecordingOptions { Mode = HttpRecordingMode.Replay });
        using var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetAsync("/api/data"));
        Assert.Contains("out-of-range status code", exception.Message);
    }

    [Fact]
    public async Task ReplayMode_CanceledToken_Throws()
    {
        var entries = new List<HttpRecordingEntry>
        {
            new() { Method = "GET", RequestUri = "https://example.com/api/data", StatusCode = 200 },
        };
        var store = new InMemoryRecordingStore(entries);
        using var innerHandler = new FakeHttpHandler(HttpStatusCode.InternalServerError);
        using var handler = CreateHandler(innerHandler, store, new HttpRecordingOptions { Mode = HttpRecordingMode.Replay });
        using var client = CreateClient(handler);
        await handler.InitializeAsync();

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetAsync("/api/data", cts.Token));
    }

    [Fact]
    public async Task ReplayMode_Passthrough_CallsInnerHandlerWithoutRecording()
    {
        var store = new InMemoryRecordingStore();
        using var innerHandler = new FakeHttpHandler(HttpStatusCode.OK, "live");
        var options = new HttpRecordingOptions
        {
            Mode = HttpRecordingMode.Replay,
            MissBehavior = HttpRecordingMissBehavior.Passthrough,
        };

        using var handler = CreateHandler(innerHandler, store, options);
        using var client = CreateClient(handler);

        using var response = await client.GetAsync("/api/data");
        Assert.Equal("live", await response.Content.ReadAsStringAsync());
        Assert.Equal(1, innerHandler.CallCount);

        await handler.SaveAsync();
        Assert.Empty(store.SavedEntries);
    }

    [Theory]
    [InlineData(HttpRecordingMode.Record)]
    [InlineData(HttpRecordingMode.Auto)]
    public void Constructor_WithoutInnerHandler_RequiresReplayMode(HttpRecordingMode mode)
    {
        var store = new InMemoryRecordingStore();
        var options = new HttpRecordingOptions { Mode = mode };

        var exception = Assert.Throws<ArgumentException>(() => new HttpRecordingHandler(store, options));
        Assert.Contains(nameof(HttpRecordingMode.Replay), exception.Message);
    }

    [Fact]
    public void Constructor_WithoutInnerHandler_RejectsPassthrough()
    {
        var store = new InMemoryRecordingStore();
        var options = new HttpRecordingOptions
        {
            Mode = HttpRecordingMode.Replay,
            MissBehavior = HttpRecordingMissBehavior.Passthrough,
        };

        Assert.Throws<ArgumentException>(() => new HttpRecordingHandler(store, options));
    }

    [Fact]
    public async Task Constructor_WithoutInnerHandler_ReplaysWithoutTouchingTheNetwork()
    {
        var entries = new List<HttpRecordingEntry>
        {
            new() { Method = "GET", RequestUri = "https://example.com/api/data", StatusCode = 204 },
        };
        var store = new InMemoryRecordingStore(entries);
        var options = new HttpRecordingOptions { Mode = HttpRecordingMode.Replay };

        using var handler = new HttpRecordingHandler(store, options);
        using var client = CreateClient(handler);

        using var response = await client.GetAsync("/api/data");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task AutoSave_SavesOnAsyncDispose()
    {
        using var innerHandler = new FakeHttpHandler(HttpStatusCode.OK, "live");
        var store = new InMemoryRecordingStore();
        var options = new HttpRecordingOptions { Mode = HttpRecordingMode.Auto, AutoSave = true };

        await using (var handler = CreateHandler(innerHandler, store, options))
        {
            using var client = CreateClient(handler);
            using var response = await client.GetAsync("/api/data");
        }

        Assert.Single(store.SavedEntries);
    }

    [Fact]
    public async Task WithoutAutoSave_AsyncDisposeSavesNothing()
    {
        using var innerHandler = new FakeHttpHandler(HttpStatusCode.OK, "live");
        var store = new InMemoryRecordingStore();
        var options = new HttpRecordingOptions { Mode = HttpRecordingMode.Auto };

        await using (var handler = CreateHandler(innerHandler, store, options))
        {
            using var client = CreateClient(handler);
            using var response = await client.GetAsync("/api/data");
        }

        Assert.Empty(store.SavedEntries);
    }

    private sealed class EchoHttpHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("echo:" + body) };
        }
    }

    /// <summary>An inner handler that consumes the request body the way a real transport does, instead of buffering it.</summary>
    private sealed class ConsumingHttpHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            if (request.Content is not null)
            {
                await request.Content.CopyToAsync(Stream.Null, cancellationToken);
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") };
        }
    }

    private sealed class NonSeekableStream(byte[] data) : Stream
    {
        private readonly MemoryStream _inner = new(data);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    private sealed class DisposeTrackingContent : HttpContent
    {
        private readonly byte[] _data;

        public DisposeTrackingContent(string text)
        {
            _data = System.Text.Encoding.UTF8.GetBytes(text);
        }

        public bool IsDisposed { get; private set; }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => stream.WriteAsync(_data, 0, _data.Length);

        protected override bool TryComputeLength(out long length)
        {
            length = _data.Length;
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                IsDisposed = true;
            }

            base.Dispose(disposing);
        }
    }

    private sealed class ThrowingSanitizer : IHttpRecordingSanitizer
    {
        public void Sanitize(HttpRecordingEntry entry) => throw new InvalidOperationException("Sanitizer failed");
    }

    private sealed class RequestBodyMatcher : IHttpRequestMatcher
    {
        public string ComputeFingerprint(HttpRecordingEntry entry)
        {
            var body = entry.RequestBody is null ? "<none>" : Convert.ToBase64String(entry.RequestBody);
            return $"{entry.Method} {entry.RequestUri} body={body}";
        }
    }

    private sealed class AuthorizationHeaderMatcher : IHttpRequestMatcher
    {
        public string ComputeFingerprint(HttpRecordingEntry entry)
        {
            var authorization = entry.RequestHeaders is not null && entry.RequestHeaders.TryGetValue("Authorization", out var values)
                ? string.Join(",", values)
                : "<none>";
            return $"{entry.Method} {entry.RequestUri} auth={authorization}";
        }
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

        public FakeHttpHandler(Func<HttpResponseMessage> responseFactory)
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
