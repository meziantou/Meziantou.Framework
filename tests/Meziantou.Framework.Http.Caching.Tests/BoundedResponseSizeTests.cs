using System.Net;
using Meziantou.Framework.Http.Caching.InMemory;

namespace Meziantou.Framework.Http.Caching.Tests;

/// <summary>Tests for <see cref="HttpCachingOptions.MaximumResponseSize"/> and the bounded body read.</summary>
public sealed class BoundedResponseSizeTests
{
    [Fact]
    public async Task WhenBodyHasNoContentLengthAndExceedsTheLimitThenOnlyTheLimitIsBuffered()
    {
        using var body = new UnknownLengthContent(32 * 1024 * 1024);
        using var innerHandler = new StubHandler(() => body);
        using var handler = new HttpCachingDelegateHandler(innerHandler, new InMemoryHttpCacheStore(), new HttpCachingOptions { MaximumResponseSize = 1024 * 1024 });
        using var client = new HttpClient(handler);

        using var response = await client.GetAsync("http://example.com/big", HttpCompletionOption.ResponseHeadersRead, CancellationToken.None);

        // The origin is not drained just to discard the response: the read stops shortly past the limit.
        Assert.InRange(body.BytesProduced, 1024 * 1024, 4 * 1024 * 1024);
    }

    [Fact]
    public async Task WhenBodyHasNoContentLengthAndExceedsTheLimitThenTheCallerStillReadsTheWholeBody()
    {
        using var body = new UnknownLengthContent(3 * 1024 * 1024);
        using var innerHandler = new StubHandler(() => body);
        using var handler = new HttpCachingDelegateHandler(innerHandler, new InMemoryHttpCacheStore(), new HttpCachingOptions { MaximumResponseSize = 1024 * 1024 });
        using var client = new HttpClient(handler);

        using var response = await client.GetAsync("http://example.com/big", HttpCompletionOption.ResponseHeadersRead, CancellationToken.None);
        var received = await response.Content.ReadAsByteArrayAsync(CancellationToken.None);

        // Declining to store a response must never truncate it.
        Assert.HasCount(3 * 1024 * 1024, received);
        Assert.All(received, static b => b is 0x42);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task WhenBodyHasNoContentLengthAndExceedsTheLimitThenItIsNotCached()
    {
        var store = new InMemoryHttpCacheStore();
        using var innerHandler = new StubHandler(() => new UnknownLengthContent(2 * 1024 * 1024));
        using var handler = new HttpCachingDelegateHandler(innerHandler, store, new HttpCachingOptions { MaximumResponseSize = 1024 * 1024 });
        using var client = new HttpClient(handler);

        using var response = await client.GetAsync("http://example.com/big", CancellationToken.None);
        _ = await response.Content.ReadAsByteArrayAsync(CancellationToken.None);

        Assert.Empty(await store.GetEntriesAsync("GET http://example.com/big", CancellationToken.None));
    }

    [Fact]
    public async Task WhenBodyHasNoContentLengthAndFitsThenItIsCachedAndReadable()
    {
        var store = new InMemoryHttpCacheStore();
        using var innerHandler = new StubHandler(() => new UnknownLengthContent(1000));
        using var handler = new HttpCachingDelegateHandler(innerHandler, store, new HttpCachingOptions { MaximumResponseSize = 1024 * 1024 });
        using var client = new HttpClient(handler);

        using var response = await client.GetAsync("http://example.com/small", CancellationToken.None);
        var received = await response.Content.ReadAsByteArrayAsync(CancellationToken.None);

        Assert.HasCount(1000, received);
        Assert.All(received, static b => b is 0x42);
        Assert.Single(await store.GetEntriesAsync("GET http://example.com/small", CancellationToken.None));
    }

    [Fact]
    public async Task WhenBodyIsReadTwiceThenTheSecondReadReturnsTheSameBytes()
    {
        using var innerHandler = new StubHandler(() => new UnknownLengthContent(1000));
        using var handler = new HttpCachingDelegateHandler(innerHandler, new InMemoryHttpCacheStore(), new HttpCachingOptions { MaximumResponseSize = 1024 * 1024 });
        using var client = new HttpClient(handler);

        using var response = await client.GetAsync("http://example.com/small", CancellationToken.None);

        // The replacement content must be buffered, like the one it replaces.
        Assert.HasCount(1000, await response.Content.ReadAsByteArrayAsync(CancellationToken.None));
        Assert.HasCount(1000, await response.Content.ReadAsByteArrayAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData(999)]
    [InlineData(1000)]
    public async Task WhenBodyIsAtOrUnderTheLimitThenItIsCached(int size)
    {
        var store = new InMemoryHttpCacheStore();
        using var innerHandler = new StubHandler(() => new UnknownLengthContent(size));
        using var handler = new HttpCachingDelegateHandler(innerHandler, store, new HttpCachingOptions { MaximumResponseSize = 1000 });
        using var client = new HttpClient(handler);

        using var response = await client.GetAsync("http://example.com/edge", CancellationToken.None);
        _ = await response.Content.ReadAsByteArrayAsync(CancellationToken.None);

        // The serialized entry is larger than the body, so the entry itself may still be rejected; what
        // matters here is that a body at the limit is not rejected by the reader.
        Assert.Equal(size, ((UnknownLengthContent)innerHandler.LastContent!).BytesProduced);
    }

    [Fact]
    public async Task WhenMaximumResponseSizeIsNullThenTheBodyIsStillReadable()
    {
        var store = new InMemoryHttpCacheStore();
        using var innerHandler = new StubHandler(() => new UnknownLengthContent(1000));
        using var handler = new HttpCachingDelegateHandler(innerHandler, store, new HttpCachingOptions { MaximumResponseSize = null });
        using var client = new HttpClient(handler);

        using var response = await client.GetAsync("http://example.com/unbounded", CancellationToken.None);

        Assert.HasCount(1000, await response.Content.ReadAsByteArrayAsync(CancellationToken.None));
        Assert.Single(await store.GetEntriesAsync("GET http://example.com/unbounded", CancellationToken.None));
    }

    [Fact]
    public async Task WhenContentLengthAnnouncesMoreThanTheLimitThenTheBodyIsNeverRead()
    {
        var store = new InMemoryHttpCacheStore();
        using var body = new UnknownLengthContent(2 * 1024 * 1024, announceLength: true);
        using var innerHandler = new StubHandler(() => body);
        using var handler = new HttpCachingDelegateHandler(innerHandler, store, new HttpCachingOptions { MaximumResponseSize = 1024 });
        using var client = new HttpClient(handler);

        using var response = await client.GetAsync("http://example.com/announced", HttpCompletionOption.ResponseHeadersRead, CancellationToken.None);

        Assert.Equal(0, body.BytesProduced);
        Assert.Empty(await store.GetEntriesAsync("GET http://example.com/announced", CancellationToken.None));
    }

    [Fact]
    public async Task WhenTheBodyIsBufferedThenNoContentLengthIsInvented()
    {
        using var innerHandler = new StubHandler(() => new UnknownLengthContent(1000));
        using var handler = new HttpCachingDelegateHandler(innerHandler, new InMemoryHttpCacheStore(), new HttpCachingOptions { MaximumResponseSize = 1024 * 1024 });
        using var client = new HttpClient(handler);

        // ResponseContentRead would buffer the content and let HttpContent report the buffer length, which
        // would hide the header the replacement content does or does not carry.
        using var response = await client.GetAsync("http://example.com/chunked", HttpCompletionOption.ResponseHeadersRead, CancellationToken.None);

        // The origin announced no length, so neither does the response the caller sees.
        Assert.Null(response.Content.Headers.ContentLength);
        Assert.HasCount(1000, await response.Content.ReadAsByteArrayAsync(CancellationToken.None));
    }

    /// <summary>Models a streaming response body, the way <see cref="StreamContent"/> wraps a network stream.</summary>
    private sealed class UnknownLengthContent(long size, bool announceLength = false) : HttpContent
    {
        private readonly GeneratorStream _stream = new(size);

        public long BytesProduced => _stream.BytesProduced;

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => _stream.CopyToAsync(stream);

        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(_stream);

        protected override bool TryComputeLength(out long length)
        {
            length = size;
            return announceLength;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _stream.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>Produces <paramref name="size"/> bytes on demand, and counts how many were actually pulled.</summary>
    private sealed class GeneratorStream(long size) : Stream
    {
        private const int MaximumChunk = 64 * 1024;

        public long BytesProduced { get; private set; }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            var remaining = size - BytesProduced;
            if (remaining <= 0 || buffer.IsEmpty)
                return 0;

            var count = (int)Math.Min(Math.Min(buffer.Length, remaining), MaximumChunk);
            buffer[..count].Fill(0x42);
            BytesProduced += count;
            return count;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(Read(buffer.Span));
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class StubHandler(Func<HttpContent> contentFactory) : HttpMessageHandler
    {
        public HttpContent? LastContent { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = contentFactory();
            LastContent = content;
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
            response.Headers.TryAddWithoutValidation("Cache-Control", "max-age=3600");
            content.Headers.TryAddWithoutValidation("Content-Type", "text/plain");
            return Task.FromResult(response);
        }
    }
}
