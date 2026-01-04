using System.Collections.Concurrent;
using System.Net;

namespace HttpCaching.Tests;

public class ThreadSafetyTests
{
    [Fact]
    public async Task RequestMessageIsSet()
    {
        using var handler = new MockResponseHandler(req =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Content = new StringContent("default-content");
            response.Headers.TryAddWithoutValidation("Cache-Control", "max-age=600");
            return response;
        });

        using var cache = new CachingDelegateHandler(handler);
        using var httpClient = new HttpClient(cache);

        using var request1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/test");
        using var response1 = await httpClient.GetAsync("http://example.com/test", XunitCancellationToken);
        Assert.Equal(request1, response1.RequestMessage);

        using var request2 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/test");
        using var response2 = await httpClient.GetAsync("http://example.com/test", XunitCancellationToken);
        Assert.Equal(request2, response2.RequestMessage);
    }

    [Fact]
    public async Task WhenMultipleThreadsRequestSameUrlThenOnlyOneRequestIsSentToOrigin()
    {
        var requestCount = 0;
        using var handler = new MockResponseHandler(req =>
        {
            Interlocked.Increment(ref requestCount);
            Thread.Sleep(50);
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Content = new StringContent("default-content");
            response.Headers.TryAddWithoutValidation("Cache-Control", "max-age=600");
            return response;
        });

        using var cache = new CachingDelegateHandler(handler);
        using var httpClient = new HttpClient(cache);

        var tasks = Enumerable.Range(0, 10)
            .Select(_ => httpClient.GetAsync("http://example.com/test", XunitCancellationToken))
            .ToArray();

        var responses = await Task.WhenAll(tasks);

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.True(requestCount < 10, $"Expected fewer than 10 requests, but got {requestCount}");

        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task WhenConcurrentRequestsToSameUrlWithDifferentVaryHeadersThenCorrectResponsesAreReturned()
    {
        using var handler = new MockResponseHandler(req =>
        {
            var lang = req.Headers.TryGetValues("Accept-Language", out var values) ? values.FirstOrDefault() : "en";
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Content = new StringContent("default-content");
            response.Headers.TryAddWithoutValidation("Cache-Control", "max-age=600");
            response.Headers.TryAddWithoutValidation("Vary", "Accept-Language");
            response.Content.Headers.TryAddWithoutValidation("Content-Language", lang);
            return response;
        });

        using var cache = new CachingDelegateHandler(handler);
        using var httpClient = new HttpClient(cache);

        var tasks = new List<Task<HttpResponseMessage>>();
        for (var i = 0; i < 20; i++)
        {
            async Task<HttpResponseMessage> SendRequest()
            {
                var lang = i % 2 == 0 ? "en" : "fr";
                using var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/test");
                request.Headers.TryAddWithoutValidation("Accept-Language", lang);
                return await httpClient.SendAsync(request, XunitCancellationToken);
            }

            tasks.Add(SendRequest());
        }

        var responses = await Task.WhenAll(tasks);

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));

        for (var i = 0; i < responses.Length; i++)
        {
            var expectedLang = i % 2 == 0 ? "en" : "fr";
            var actualLang = responses[i].Content.Headers.TryGetValues("Content-Language", out var values)
                ? values.FirstOrDefault()
                : null;
            Assert.Equal(expectedLang, actualLang);
        }

        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task WhenConcurrentRequestsToMultipleUrlsThenAllAreCachedCorrectly()
    {
        var urls = Enumerable.Range(1, 5).Select(i => $"http://example.com/test{i}").ToArray();
        var requestCounts = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);

        using var handler = new MockResponseHandler(req =>
        {
            var url = req.RequestUri?.ToString() ?? string.Empty;
            requestCounts.AddOrUpdate(url, 1, (_, count) => count + 1);
            Thread.Sleep(10);
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Content = new StringContent("default-content");
            response.Headers.TryAddWithoutValidation("Cache-Control", "max-age=600");
            return response;
        });

        using var cache = new CachingDelegateHandler(handler);
        using var httpClient = new HttpClient(cache);

        var tasks = new List<Task<HttpResponseMessage>>();
        for (var i = 0; i < 50; i++)
        {
            var url = urls[i % urls.Length];
            tasks.Add(httpClient.GetAsync(url, XunitCancellationToken));
        }

        var responses = await Task.WhenAll(tasks);

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));

        foreach (var url in urls)
        {
            var count = requestCounts.GetValueOrDefault(url, 0);
            Assert.True(count < 10, $"URL {url} was requested {count} times, expected fewer than 10");
        }

        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task WhenConcurrentRequestsWithConditionalValidationThenNoDataRaces()
    {
        var requestCount = 0;
        using var handler = new MockResponseHandler(req =>
        {
            var count = Interlocked.Increment(ref requestCount);

            if (count == 1)
            {
                var response1 = new HttpResponseMessage(HttpStatusCode.OK);
                response1.Content = new StringContent("default-content");
                response1.Headers.TryAddWithoutValidation("Cache-Control", "max-age=0");
                response1.Headers.TryAddWithoutValidation("ETag", "\"v1\"");
                return response1;
            }

            if (req.Headers.IfNoneMatch.Count != 0)
            {
                return new HttpResponseMessage(HttpStatusCode.NotModified);
            }

            var response2 = new HttpResponseMessage(HttpStatusCode.OK);
            response2.Content = new StringContent("default-content");
            response2.Headers.TryAddWithoutValidation("Cache-Control", "max-age=0");
            response2.Headers.TryAddWithoutValidation("ETag", "\"v2\"");
            return response2;
        });

        using var cache = new CachingDelegateHandler(handler);
        using var httpClient = new HttpClient(cache);

        using (var response = await httpClient.GetAsync("http://example.com/test", XunitCancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var tasks = Enumerable.Range(0, 20)
            .Select(_ => httpClient.GetAsync("http://example.com/test", XunitCancellationToken))
            .ToArray();

        var responses = await Task.WhenAll(tasks);

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.True(requestCount <= 21, $"Expected at most 21 requests, but got {requestCount}");

        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task WhenConcurrentCacheInvalidationsThenCacheRemainsConsistent()
    {
        using var handler = new MockResponseHandler(req =>
        {
            Thread.Sleep(5);
            if (req.Method == HttpMethod.Post)
            {
                return new HttpResponseMessage(HttpStatusCode.Created);
            }

            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Content = new StringContent("default-content");
            response.Headers.TryAddWithoutValidation("Cache-Control", "max-age=600");
            return response;
        });

        using var cache = new CachingDelegateHandler(handler);
        using var httpClient = new HttpClient(cache);

        using (var response = await httpClient.GetAsync("http://example.com/test", XunitCancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var tasks = new List<Task<HttpResponseMessage>>();
        for (var i = 0; i < 30; i++)
        {
            if (i % 5 == 0)
            {
                async Task<HttpResponseMessage> SendRequest()
                {
                    using var postRequest = new HttpRequestMessage(HttpMethod.Post, "http://example.com/test");
                    return await httpClient.SendAsync(postRequest, XunitCancellationToken);
                }

                tasks.Add(SendRequest());
            }
            else
            {
                tasks.Add(httpClient.GetAsync("http://example.com/test", XunitCancellationToken));
            }
        }

        var responses = await Task.WhenAll(tasks);

        Assert.All(responses.Where((r, i) => i % 5 == 0), r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        Assert.All(responses.Where((r, i) => i % 5 != 0), r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));

        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task WhenConcurrentRequestsWithMaxStaleThenStaleResponsesAreReturnedSafely()
    {
        var requestCount = 0;
        using var handler = new MockResponseHandler(req =>
        {
            Interlocked.Increment(ref requestCount);
            Thread.Sleep(20);
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Content = new StringContent("default-content");
            response.Headers.TryAddWithoutValidation("Cache-Control", "max-age=0");
            return response;
        });

        using var cache = new CachingDelegateHandler(handler);
        using var httpClient = new HttpClient(cache);

        using (var response = await httpClient.GetAsync("http://example.com/test"))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var tasks = Enumerable.Range(0, 15)
            .Select(async _ =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/test");
                request.Headers.TryAddWithoutValidation("Cache-Control", "max-stale=3600");
                return await httpClient.SendAsync(request, XunitCancellationToken);
            })
            .ToArray();

        var responses = await Task.WhenAll(tasks);

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(1, requestCount);

        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    private sealed class MockResponseHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFunc) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(responseFunc(request));
        }
    }
}
