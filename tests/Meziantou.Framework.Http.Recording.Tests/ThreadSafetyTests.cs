using System.Collections.Concurrent;
using System.Net;
using Xunit;

namespace Meziantou.Framework.Http.Recording.Tests;

public sealed class ThreadSafetyTests
{
    [Fact]
    public async Task ConcurrentReplay_AllRequestsGetResponses()
    {
        const int requestCount = 100;

        var entries = new List<HttpRecordingEntry>();
        for (var i = 0; i < requestCount; i++)
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
        var innerHandler = new FakeHandler();
        var options = new HttpRecordingOptions { Mode = HttpRecordingMode.Replay };

        using var handler = new HttpRecordingHandler(innerHandler, store, options);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com") };

        var results = new ConcurrentBag<(int Index, string Body)>();
        var tasks = new Task[requestCount];

        for (var i = 0; i < requestCount; i++)
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

        Assert.Equal(requestCount, results.Count);
        Assert.Equal(0, innerHandler.CallCount);

        // Verify all unique responses were returned
        var bodies = results.Select(r => r.Body).OrderBy(b => b).ToList();
        for (var i = 0; i < requestCount; i++)
        {
            Assert.Contains($"response-{i}", bodies);
        }
    }

    [Fact]
    public async Task ConcurrentReplay_FIFO_IdenticalRequests()
    {
        const int requestCount = 10;

        var entries = new List<HttpRecordingEntry>();
        for (var i = 0; i < requestCount; i++)
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
        var innerHandler = new FakeHandler();
        var options = new HttpRecordingOptions { Mode = HttpRecordingMode.Replay };

        using var handler = new HttpRecordingHandler(innerHandler, store, options);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com") };

        // Make requests sequentially to verify FIFO order
        var bodies = new List<string>();
        for (var i = 0; i < requestCount; i++)
        {
            using var response = await client.GetAsync("/api/same");
            bodies.Add(await response.Content.ReadAsStringAsync());
        }

        for (var i = 0; i < requestCount; i++)
        {
            Assert.Equal($"response-{i}", bodies[i]);
        }
    }

    [Fact]
    public async Task ConcurrentRecording_AllEntriesRecorded()
    {
        const int requestCount = 50;

        var store = new InMemoryStore();
        var innerHandler = new FakeHandler();
        var options = new HttpRecordingOptions { Mode = HttpRecordingMode.Record };

        using var handler = new HttpRecordingHandler(innerHandler, store, options);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.com") };

        var tasks = new Task[requestCount];
        for (var i = 0; i < requestCount; i++)
        {
            var index = i;
            tasks[i] = Task.Run(async () =>
            {
                using var response = await client.GetAsync($"/api/item/{index}");
            });
        }

        await Task.WhenAll(tasks);
        await handler.SaveAsync();

        Assert.Equal(requestCount, store.SavedEntries.Count);
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
