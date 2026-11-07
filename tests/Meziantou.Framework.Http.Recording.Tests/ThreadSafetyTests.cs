using System.Collections.Concurrent;
using System.Net;

namespace Meziantou.Framework.Http.Recording.Tests;

public sealed class ThreadSafetyTests
{
    [Fact]
    public async Task ConcurrentReplay_AllRequestsGetResponses()
    {
        const int RequestCount = 100;

        var entries = new List<HttpRecordingEntry>();
        for (var i = 0; i < RequestCount; i++)
        {
            entries.Add(new HttpRecordingEntry
            {
                Method = "GET",
                RequestUri = $"https://example.com/api/item/{i}",
                StatusCode = 200,
                ResponseBody = System.Text.Encoding.UTF8.GetBytes($"response-{i}"),
            });
        }

        var store = new InMemoryStore(entries);
        using var innerHandler = new FakeHandler();
        var options = new HttpRecordingOptions { Mode = HttpRecordingMode.Replay };

        using var handler = new HttpRecordingHandler(innerHandler, store, options);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com") };

        var results = new ConcurrentBag<(int Index, string Body)>();
        var tasks = new Task[RequestCount];

        for (var i = 0; i < RequestCount; i++)
        {
            var index = i;
            tasks[i] = Task.Run(async () =>
            {
                using var response = await client.GetAsync($"/api/item/{index}");
                var body = await response.Content.ReadAsStringAsync();
                results.Add((index, body));
            });
        }

        await Task.WhenAll(tasks);

        Assert.HasCount(RequestCount, results);
        Assert.Equal(0, innerHandler.CallCount);

        // Verify all unique responses were returned
        var bodies = results.Select(r => r.Body).OrderBy(b => b, StringComparer.Ordinal).ToList();
        for (var i = 0; i < RequestCount; i++)
        {
            Assert.Contains($"response-{i}", bodies);
        }
    }

    [Fact]
    public async Task ConcurrentReplay_EachRecordingHandedOutExactlyOnce()
    {
        const int RequestCount = 10;

        var entries = new List<HttpRecordingEntry>();
        for (var i = 0; i < RequestCount; i++)
        {
            entries.Add(new HttpRecordingEntry
            {
                Method = "GET",
                RequestUri = "https://example.com/api/same",
                StatusCode = 200,
                ResponseBody = System.Text.Encoding.UTF8.GetBytes($"response-{i}"),
            });
        }

        var store = new InMemoryStore(entries);
        using var innerHandler = new FakeHandler();
        var options = new HttpRecordingOptions { Mode = HttpRecordingMode.Replay };

        using var handler = new HttpRecordingHandler(innerHandler, store, options);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com") };

        // Contend on the single queue that backs this fingerprint: every recording must be handed out exactly once,
        // with none duplicated and none lost.
        var bodies = new ConcurrentBag<string>();
        await Parallel.ForAsync(0, RequestCount, async (_, token) =>
        {
            using var response = await client.GetAsync("/api/same", token);
            bodies.Add(await response.Content.ReadAsStringAsync(token));
        });

        var expected = Enumerable.Range(0, RequestCount).Select(i => $"response-{i}").Order(StringComparer.Ordinal);
        Assert.Equal(expected, bodies.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task ConcurrentReplay_ExhaustedRecordings_ThrowMiss()
    {
        var entries = new List<HttpRecordingEntry>
        {
            new() { Method = "GET", RequestUri = "https://example.com/api/same", StatusCode = 200 },
        };

        var store = new InMemoryStore(entries);
        using var innerHandler = new FakeHandler();
        var options = new HttpRecordingOptions { Mode = HttpRecordingMode.Replay };

        using var handler = new HttpRecordingHandler(innerHandler, store, options);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com") };

        var successes = 0;
        var misses = 0;
        await Parallel.ForAsync(0, 8, async (_, token) =>
        {
            try
            {
                using var response = await client.GetAsync("/api/same", token);
                Interlocked.Increment(ref successes);
            }
            catch (HttpRecordingMissException)
            {
                Interlocked.Increment(ref misses);
            }
        });

        Assert.Equal(1, successes);
        Assert.Equal(7, misses);
    }

    [Fact]
    public async Task SaveAsync_WaitsForInFlightRecording()
    {
        var store = new InMemoryStore([]);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var innerHandler = new BlockingHandler(entered, release);
        var options = new HttpRecordingOptions { Mode = HttpRecordingMode.Record };

        using var handler = new HttpRecordingHandler(innerHandler, store, options);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com") };

        var requestTask = client.GetAsync("/api/slow");
        await entered.Task;

        var saveTask = handler.SaveAsync();
        release.SetResult();

        using var response = await requestTask;
        await saveTask;

        // Saving while a request was still being recorded must not persist a snapshot that omits it, because the
        // store has already replaced whatever it held before.
        Assert.Single(store.SavedEntries);
    }

    [Fact]
    public async Task DisposeDuringInFlightRequest_DoesNotMaskTheResult()
    {
        var entries = new List<HttpRecordingEntry>
        {
            new() { Method = "GET", RequestUri = "https://example.com/api/data", StatusCode = 204 },
        };
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new BlockingLoadStore(entries, entered, release);

        using var innerHandler = new FakeHandler();
        var options = new HttpRecordingOptions { Mode = HttpRecordingMode.Replay };

        var handler = new HttpRecordingHandler(innerHandler, store, options);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com") };

        var requestTask = client.GetAsync("/api/data");
        await entered.Task;

        // Disposing while a request holds the initialization lock must not turn the result into an
        // ObjectDisposedException raised from a finally block.
        handler.Dispose();
        release.SetResult();

        using var response = await requestTask;
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private sealed class BlockingHandler(TaskCompletionSource entered, TaskCompletionSource release) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            entered.TrySetResult();
            await release.Task;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") };
        }
    }

    private sealed class BlockingLoadStore(List<HttpRecordingEntry> entries, TaskCompletionSource entered, TaskCompletionSource release) : IHttpRecordingStore
    {
        public async ValueTask<IReadOnlyList<HttpRecordingEntry>> LoadAsync(CancellationToken cancellationToken)
        {
            entered.TrySetResult();
            await release.Task;
            return entries;
        }

        public ValueTask SaveAsync(IReadOnlyList<HttpRecordingEntry> entries, CancellationToken cancellationToken) => default;
    }

    [Fact]
    public async Task ConcurrentRecording_AllEntriesRecorded()
    {
        const int RequestCount = 50;

        var store = new InMemoryStore();
        using var innerHandler = new FakeHandler();
        var options = new HttpRecordingOptions { Mode = HttpRecordingMode.Record };

        using var handler = new HttpRecordingHandler(innerHandler, store, options);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com") };

        var tasks = new Task[RequestCount];
        for (var i = 0; i < RequestCount; i++)
        {
            var index = i;
            tasks[i] = Task.Run(async () =>
            {
                using var response = await client.GetAsync($"/api/item/{index}");
            });
        }

        await Task.WhenAll(tasks);
        await handler.SaveAsync();

        Assert.Equal(RequestCount, store.SavedEntries.Count);
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private int _callCount;
        public int CallCount => _callCount;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("inner-response"),
            });
        }
    }

    private sealed class InMemoryStore : IHttpRecordingStore
    {
        private readonly List<HttpRecordingEntry> _entries;

        public InMemoryStore() => _entries = [];
        public InMemoryStore(List<HttpRecordingEntry> entries) => _entries = entries;

        public IReadOnlyList<HttpRecordingEntry> SavedEntries { get; private set; } = [];

        public ValueTask<IReadOnlyList<HttpRecordingEntry>> LoadAsync(CancellationToken cancellationToken)
            => new(_entries);

        public ValueTask SaveAsync(IReadOnlyList<HttpRecordingEntry> entries, CancellationToken cancellationToken)
        {
            SavedEntries = entries;
            return default;
        }
    }
}
