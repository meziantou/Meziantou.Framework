using System.Net;
using Meziantou.Framework.Http.Caching.InMemory;

namespace Meziantou.Framework.Http.Caching.Tests;

/// <summary>Tests that the synchronous path cannot silently bypass the cache.</summary>
public sealed class SynchronousSendTests
{
    [Fact]
    public void WhenSendingSynchronouslyThenTheCacheReportsItIsNotSupported()
    {
        using var innerHandler = new StubHandler();
        using var handler = new HttpCachingDelegateHandler(innerHandler, new InMemoryHttpCacheStore());
        using var client = new HttpClient(handler);

        using var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        var exception = Assert.Throws<NotSupportedException>(() => client.Send(request));

        Assert.Contains(nameof(HttpCachingDelegateHandler), exception.Message);

        // The request never reached the origin, so nothing was silently fetched without being cached.
        Assert.Equal(0, innerHandler.SyncCount);
        Assert.Equal(0, innerHandler.AsyncCount);
    }

    [Fact]
    public async Task WhenSendingAsynchronouslyThenTheCacheIsUsed()
    {
        using var innerHandler = new StubHandler();
        using var handler = new HttpCachingDelegateHandler(innerHandler, new InMemoryHttpCacheStore());
        using var client = new HttpClient(handler);

        Assert.Equal("from-origin", await client.GetStringAsync("http://example.com/resource", CancellationToken.None));
        Assert.Equal("from-origin", await client.GetStringAsync("http://example.com/resource", CancellationToken.None));

        Assert.Equal(1, innerHandler.AsyncCount);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public int AsyncCount { get; private set; }
        public int SyncCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            AsyncCount++;
            return Task.FromResult(CreateResponse());
        }

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            SyncCount++;
            return CreateResponse();
        }

        [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Ownership is transferred to the caller")]
        private static HttpResponseMessage CreateResponse()
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("from-origin") };
            response.Headers.TryAddWithoutValidation("Cache-Control", "max-age=3600");
            return response;
        }
    }
}
