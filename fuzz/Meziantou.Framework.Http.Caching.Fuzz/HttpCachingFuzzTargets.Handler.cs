using System.Net;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.Http.Caching.Fuzz;

public static partial class HttpCachingFuzzTargets
{
    private const int MaxSteps = 12;

    private static readonly Uri BaseUri = new("https://example.com/");
    private static readonly DateTimeOffset StartTime = new(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly HttpMethod[] Methods =
    [
        HttpMethod.Get, HttpMethod.Get, HttpMethod.Get, HttpMethod.Get, HttpMethod.Head, HttpMethod.Head,
        HttpMethod.Post, HttpMethod.Put, HttpMethod.Delete, HttpMethod.Patch, HttpMethod.Options, HttpMethod.Trace,
    ];

    private static readonly string[] Urls = ["/", "/a", "/a?x=1", "/a?x=2", "/a?x=1&y=2", "/a?y=2&x=1", "/a?utm_source=z&x=1", "/b", "/b/c?q"];
    private static readonly string[] RequestHeaderNames = ["Cache-Control", "Pragma", "Accept", "Accept-Language", "Range", "If-None-Match", "If-Modified-Since", "Authorization", "X-Custom", "Cookie"];
    private static readonly string[] ResponseHeaderNames =
    [
        "Cache-Control", "Expires", "Date", "Age", "Vary", "ETag", "Last-Modified", "No-Vary-Search", "Location", "Content-Location", "Content-Type", "Warning", "Pragma", "Set-Cookie",
    ];

    private static readonly int[] StatusCodes = [200, 200, 200, 203, 204, 206, 300, 301, 302, 304, 304, 307, 400, 404, 405, 410, 414, 500, 501, 502, 503, 504];
    private static readonly long?[] MaximumResponseSizes = [5 * 1024 * 1024, null, 64, 400];

    private static readonly string[] HandlerTokens =
    [
        "max-age=", "s-maxage=", "no-cache", "no-store", "must-revalidate", "proxy-revalidate", "public", "private", "immutable", "stale-if-error=", "max-stale", "max-stale=",
        "min-fresh=", "only-if-cached", "no-transform", ", ", "=", "\"", "W/\"", "*", "0", "1", "60", "3600", "2147483647", "2147483648", "-1",
        "Wed, 01 Jan 2025 00:00:00 GMT", "Wed, 01 Jan 2025 01:00:00 GMT", "Thu, 01 Jan 1970 00:00:00 GMT", "Fri, 31 Dec 9999 23:59:59 GMT", "Mon, 01 Jan 0001 00:00:00 GMT",
        "Wednesday, 01-Jan-25 00:00:00 GMT", "Wed Jan  1 00:00:00 2025", "bytes=0-1", "params", "except=(\"x\")", "key-order", "params=(\"utm_source\")", "Accept-Language", "en", "\n", "/a", "?x=1", "https://example.com/",
    ];

    /// <summary>
    /// An <see cref="HttpCachingDelegateHandler"/> over an in-memory store and a fake origin, with a fake clock. The first
    /// byte selects the maximum response size and whether the store corrupts the payloads it returns (two bytes for the
    /// offset, one for the value or a truncation). Then each step is a request (method, URL, headers, time elapsed since
    /// the previous step) and the response the origin gives to it (status, headers among Cache-Control, Expires, Date,
    /// Age, Vary, ETag, Last-Modified, No-Vary-Search, ..., and body); the origin can also be unreachable.
    /// Nothing throws but the origin failure itself, no store error is reported (the store never fails), and a response
    /// that does not come from the origin is either the 504 of an <c>only-if-cached</c> request or a stored response:
    /// it answers a GET or HEAD request, has an Age, and (when the store is not corrupted) has the status and the body of a
    /// response the origin sent for the same method and path, and for the same query unless that response declared a
    /// <c>No-Vary-Search</c>. That response was not marked <c>no-store</c>, nor was its request, and its status was neither
    /// 206 nor 304.
    /// </summary>
    private static void Handler(ReadOnlySpan<byte> data)
    {
        var reader = new InputReader(data);
        var options = reader.ReadByte();
        var corruption = (options & 4) is 0 ? null : new StoreCorruption(reader.ReadByte() | (reader.ReadByte() << 8), reader.ReadByte());

        var time = new ManualTimeProvider(StartTime);
        var store = new MemoryCacheStore(corruption);
        using var origin = new FakeOrigin();
        var storeErrors = new List<Exception>();
        var cachingOptions = new HttpCachingOptions
        {
            TimeProvider = time,
            MaximumResponseSize = MaximumResponseSizes[options & 3],
            OnStoreError = storeErrors.Add,
        };

        using var handler = new HttpCachingDelegateHandler(origin, store, cachingOptions);
        using var invoker = new HttpMessageInvoker(handler, disposeHandler: false);
        for (var step = 0; step < MaxSteps && !reader.IsEmpty; step++)
        {
            var kind = reader.ReadByte();
            var method = Methods[(kind & 0x0F) % Methods.Length];
            time.Advance(TimeSpan.FromSeconds(reader.ReadByte() * ((kind & 0x10) is 0 ? 1 : 3600)));
            var uri = (kind & 0x20) is 0 ? new Uri(BaseUri, Urls[reader.ReadByte() % Urls.Length]) : ParseUri(reader.ReadLine());

            using var request = new HttpRequestMessage(method, uri);
            var requestHeaderCount = reader.ReadByte() % 4;
            for (var i = 0; i < requestHeaderCount; i++)
            {
                request.Headers.TryAddWithoutValidation(FuzzInput.Pick(RequestHeaderNames, reader.ReadByte()), SanitizeHeaderValue(reader.ReadLine()));
            }

            origin.StartStep(ReadResponseSpec(reader));

            HttpResponseMessage response;
            try
            {
                response = Send(invoker, request);
            }
            catch (HttpRequestException) when (origin.IsUnreachable)
            {
                // The origin could not be reached and the cache had nothing it could serve instead
                CheckStoreErrors(storeErrors);
                continue;
            }

            using (response)
            {
                CheckStoreErrors(storeErrors);
                CheckResponse(origin, request, response, corruption is not null);
            }
        }
    }

    private static HttpResponseMessage Send(HttpMessageInvoker invoker, HttpRequestMessage request)
    {
        // Every component is in memory, so the request completes synchronously
        return invoker.SendAsync(request, CancellationToken.None).GetAwaiter().GetResult();
    }

    private static void CheckResponse(FakeOrigin origin, HttpRequestMessage request, HttpResponseMessage response, bool isStoreCorrupted)
    {
        if (origin.HasReturned(response))
            return;

        FuzzAssert.True(request.Method == HttpMethod.Get || request.Method == HttpMethod.Head, $"A response that does not come from the origin answers a {request.Method} request");
        FuzzAssert.True(ReferenceEquals(request, response.RequestMessage), "The RequestMessage of a cached response is not the request");

        var body = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
        if (response.StatusCode is HttpStatusCode.GatewayTimeout && body.Length is 0 && request.Headers.CacheControl?.OnlyIfCached is true)
            return;

        FuzzAssert.True(response.Headers.Age is { } age && age >= TimeSpan.Zero, $"The cached response has no Age, or a negative one: {response.Headers.Age}");
        if (isStoreCorrupted)
            return;

        FuzzAssert.True(origin.TryGetResponse(body, out var stored), $"The cached response has a body the origin never sent: {FuzzAssert.Format(Encoding.UTF8.GetString(body))}");
        var uri = request.RequestUri!;
        FuzzAssert.Equal(stored.Method, request.Method, "The cached response was stored for another method");
        FuzzAssert.Equal(stored.Uri.GetLeftPart(UriPartial.Path), uri.GetLeftPart(UriPartial.Path), "The cached response was stored for another path");
        if (!stored.HasNoVarySearch)
        {
            FuzzAssert.Equal(stored.Uri.GetLeftPart(UriPartial.Query), uri.GetLeftPart(UriPartial.Query), "The cached response was stored for another query, and has no No-Vary-Search header");
        }

        FuzzAssert.Equal(stored.StatusCode, response.StatusCode, "The cached response has another status than the stored one");
        FuzzAssert.False(stored.IsNoStore, "A response marked no-store, or the response to a no-store request, is served from the cache");
        FuzzAssert.False(stored.StatusCode is HttpStatusCode.PartialContent or HttpStatusCode.NotModified, $"A {(int)stored.StatusCode} response is served from the cache");
    }

    private static void CheckStoreErrors(List<Exception> storeErrors)
    {
        if (storeErrors.Count > 0)
            FuzzAssert.Fail($"The handler reported a store error, but the store never fails:{Environment.NewLine}{storeErrors[0]}");
    }

    private static Uri ParseUri(string text)
    {
        if (Uri.TryCreate(BaseUri, text, out var uri) && uri.Scheme is "http" or "https")
            return uri;

        return BaseUri;
    }

    private static string SanitizeHeaderValue(string value)
    {
        // A field value received from the network cannot contain CR, LF or NUL
        return value.Replace("\r", "", StringComparison.Ordinal).Replace("\0", "", StringComparison.Ordinal);
    }

    private static ResponseSpec ReadResponseSpec(InputReader reader)
    {
        var status = reader.ReadByte();
        var headerCount = reader.ReadByte() % 6;
        var headers = new List<(string Name, string Value)>(headerCount);
        for (var i = 0; i < headerCount; i++)
        {
            headers.Add((FuzzInput.Pick(ResponseHeaderNames, reader.ReadByte()), SanitizeHeaderValue(reader.ReadLine())));
        }

        return new ResponseSpec(IsUnreachable: status is 0xFF, (HttpStatusCode)StatusCodes[status % StatusCodes.Length], headers, reader.ReadLine());
    }

    private static IEnumerable<FuzzSeed> GetHandlerSeeds()
    {
        // A step: kind (method, hours, URL as text), seconds, URL index or line, request header count, (name, value) per
        // header, then the response: status (0xFF for an unreachable origin), header count, (name, value) per header, body.
        yield return HandlerSeed("fresh-hit", 0,
            0, 0, 2, 0, 0, 2, 0, "max-age=60", 5, "\"v1\"", "hello",
            0, 10, 2, 0, 0, 0, "unused");
        yield return HandlerSeed("revalidation", 0,
            0, 0, 1, 0, 0, 2, 0, "max-age=1", 5, "\"v1\"", "hello",
            0, 5, 1, 0, 9, 2, 5, "\"v1\"", 0, "max-age=60", "",
            0, 1, 1, 0, 0, 0, "unused");
        yield return HandlerSeed("revalidation-mismatch", 0,
            0, 0, 1, 0, 0, 2, 0, "no-cache", 5, "\"v1\"", "hello",
            0, 5, 1, 0, 9, 1, 5, "\"v2\"", "");
        yield return HandlerSeed("last-modified", 0,
            4, 0, 7, 0, 0, 2, 6, "Tue, 01 Jan 2024 00:00:00 GMT", 2, "Wed, 01 Jan 2025 00:00:00 GMT", "head",
            4, 0x30, 7, 0, 0, 0, "unused",
            0, 0x30, 7, 0, 9, 0, "");
        yield return HandlerSeed("expires", 0,
            0, 0, 3, 0, 0, 2, 1, "Wed, 01 Jan 2025 01:00:00 GMT", 2, "Wed, 01 Jan 2025 00:00:00 GMT", "expires",
            0, 60, 3, 0, 0, 0, "unused",
            0x10, 1, 3, 0, 0, 1, 1, "0", "expired");
        yield return HandlerSeed("no-vary-search", 0,
            0, 0, 4, 0, 0, 2, 0, "max-age=60", 7, "key-order, params=(\"utm_source\")", "nvs",
            0, 1, 5, 0, 0, 0, "unused",
            0, 1, 6, 0, 0, 0, "unused",
            0x20, 1, "/a?x=1&y=2&utm_source=w", 0, 0, 0, "unused");
        yield return HandlerSeed("vary", 0,
            0, 0, 1, 1, 3, "en", 0, 2, 0, "max-age=60", 4, "Accept-Language", "english",
            0, 1, 1, 1, 3, "fr", 0, 2, 0, "max-age=60", 4, "Accept-Language", "french",
            0, 1, 1, 1, 3, "en", 0, 0, "unused",
            0, 1, 1, 0, 0, 1, 4, "*", "star");
        yield return HandlerSeed("invalidation", 0,
            0, 0, 1, 0, 0, 1, 0, "max-age=60", "get",
            6, 1, 1, 0, 0, 2, 8, "/b", 9, "https://example.com/a", "post",
            0, 1, 1, 0, 0, 1, 0, "max-age=60", "get again",
            8, 1, 1, 0, 12, 0, "delete failed");
        yield return HandlerSeed("stale-if-error", 0,
            0, 0, 1, 0, 0, 2, 0, "max-age=1, stale-if-error=3600", 5, "\"v1\"", "hello",
            0, 10, 1, 0, 0xFF, 0, "",
            0, 10, 1, 0, 20, 0, "unavailable",
            0, 10, 0, 0, 0xFF, 0, "");
        yield return HandlerSeed("request-directives", 0,
            0, 0, 1, 0, 0, 1, 0, "max-age=10", "hello",
            0, 20, 1, 1, 0, "max-stale=60", 0, 0, "unused",
            0, 1, 1, 1, 0, "only-if-cached", 0, 0, "unused",
            0, 1, 2, 1, 0, "only-if-cached", 0, 0, "unused",
            0, 1, 1, 1, 0, "no-cache", 0, 0, "fresh",
            0, 1, 1, 1, 0, "max-age=0, min-fresh=5", 0, 0, "fresh",
            0, 1, 1, 1, 1, "no-cache", 0, 0, "pragma",
            0, 1, 1, 1, 0, "no-store", 0, 1, 0, "max-age=60", "no-store");
        yield return HandlerSeed("passthrough", 0,
            0, 0, 1, 1, 4, "bytes=0-1", 5, 1, 0, "max-age=60", "partial",
            0, 0, 1, 1, 5, "\"v1\"", 9, 0, "",
            0, 0, 1, 1, 6, "Wed, 01 Jan 2025 00:00:00 GMT", 0, 0, "unconditional");
        yield return HandlerSeed("immutable", 0,
            0, 0, 1, 0, 0, 2, 0, "max-age=60, immutable, no-cache", 3, "10", "immutable",
            0, 1, 1, 1, 0, "no-cache", 0, 0, "unused");
        yield return HandlerSeed("small-maximum-size", 3,
            0, 0, 1, 0, 0, 1, 0, "max-age=60", new string('x', 100),
            0, 1, 1, 0, 0, 1, 0, "max-age=60", "small",
            0, 1, 1, 0, 0, 0, "unused");
        yield return HandlerSeed("corrupted-store", 4, 30, 0, 0,
            0, 0, 1, 0, 0, 1, 0, "max-age=60", "hello",
            0, 1, 1, 0, 0, 0, "after corruption");
    }

    private static FuzzSeed HandlerSeed(string name, params object[] parts) => new(name, InputReader.Create(parts));

    private sealed record ResponseSpec(bool IsUnreachable, HttpStatusCode StatusCode, List<(string Name, string Value)> Headers, string Body);

    private sealed record OriginResponse(HttpMethod Method, Uri Uri, HttpStatusCode StatusCode, bool HasNoVarySearch, bool IsNoStore);

    /// <summary>Replaces a byte of the payloads the store returns, or truncates them when the value is 0.</summary>
    private sealed record StoreCorruption(int Offset, byte Value)
    {
        public void Apply(HttpCachePersistenceEntry entry)
        {
            var payload = entry.SerializedResponse.ToArray();
            if (payload.Length is 0)
                return;

            var offset = Offset % payload.Length;
            if (Value is 0)
            {
                entry.SerializedResponse = payload.AsMemory(0, offset);
            }
            else
            {
                payload[offset] = Value;
                entry.SerializedResponse = payload;
            }
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan delta) => _now += delta;
    }

    /// <summary>Keeps one entry per secondary key, as <c>InMemoryHttpCacheStore</c> does, and returns copies of the entries.</summary>
    private sealed class MemoryCacheStore(StoreCorruption? corruption) : IHttpCacheStore
    {
        private readonly Dictionary<string, List<HttpCachePersistenceEntry>> _entries = new(StringComparer.Ordinal);

        public ValueTask<IReadOnlyCollection<HttpCachePersistenceEntry>> GetEntriesAsync(string primaryKey, CancellationToken cancellationToken)
        {
            if (!_entries.TryGetValue(primaryKey, out var entries))
                return ValueTask.FromResult<IReadOnlyCollection<HttpCachePersistenceEntry>>([]);

            var result = new List<HttpCachePersistenceEntry>(entries.Count);
            foreach (var entry in entries)
            {
                var copy = entry.Clone();
                corruption?.Apply(copy);
                result.Add(copy);
            }

            return ValueTask.FromResult<IReadOnlyCollection<HttpCachePersistenceEntry>>(result);
        }

        public ValueTask SetEntryAsync(string primaryKey, HttpCachePersistenceEntry entry, CancellationToken cancellationToken)
        {
            if (!_entries.TryGetValue(primaryKey, out var entries))
            {
                entries = [];
                _entries.Add(primaryKey, entries);
            }

            entries.RemoveAll(existing => HttpCachePersistenceEntry.HasSameSecondaryKey(existing, entry));
            entries.Add(entry.Clone());
            return ValueTask.CompletedTask;
        }

        public ValueTask RemoveEntriesAsync(string primaryKey, CancellationToken cancellationToken)
        {
            _entries.Remove(primaryKey);
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Answers each request with the response of the current step, then with a response that cannot be stored. Every body is unique.</summary>
    private sealed class FakeOrigin : HttpMessageHandler
    {
        private static readonly ResponseSpec NotStorable = new(IsUnreachable: false, HttpStatusCode.OK, [("Cache-Control", "no-store")], "not storable");

        private readonly Queue<ResponseSpec> _pending = new();
        private readonly HashSet<HttpResponseMessage> _returned = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<string, OriginResponse> _responses = new(StringComparer.Ordinal);
        private int _counter;

        public bool IsUnreachable { get; private set; }

        public void StartStep(ResponseSpec spec)
        {
            _pending.Clear();
            _pending.Enqueue(spec);
            IsUnreachable = false;
        }

        public bool HasReturned(HttpResponseMessage response) => _returned.Contains(response);

        public bool TryGetResponse(byte[] body, out OriginResponse response) => _responses.TryGetValue(Convert.ToHexString(body), out response!);

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken) => throw new NotSupportedException();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var spec = _pending.TryDequeue(out var pending) ? pending : NotStorable;
            if (spec.IsUnreachable)
            {
                IsUnreachable = true;
                throw new HttpRequestException("The origin is unreachable");
            }

            var body = Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"#{_counter++}:{spec.Body}"));
            var response = new HttpResponseMessage(spec.StatusCode)
            {
                Content = new ByteArrayContent(body),
                RequestMessage = request,
            };

            foreach (var (name, value) in spec.Headers)
            {
                if (!response.Headers.TryAddWithoutValidation(name, value))
                {
                    response.Content.Headers.TryAddWithoutValidation(name, value);
                }
            }

            var isNoStore = response.Headers.CacheControl?.NoStore is true || request.Headers.CacheControl?.NoStore is true;
            _responses[Convert.ToHexString(body)] = new OriginResponse(request.Method, request.RequestUri!, response.StatusCode, response.Headers.Contains("No-Vary-Search"), isNoStore);
            _returned.Add(response);
            return Task.FromResult(response);
        }
    }
}
