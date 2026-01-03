using System.Collections.Concurrent;
using System.Net;
using HttpCaching.Tests.Internals;

namespace HttpCaching.Tests;

public class ThreadSafetyTests
{
    [Fact]
    public async Task WhenMultipleThreadsRequestSameUrlThenOnlyOneRequestIsSentToOrigin()
    {
        var requestCount = 0;
        var handler = new MockResponseHandler(async req =>
        {
            Interlocked.Increment(ref requestCount);
            await Task.Delay(50, TestContext.Current.CancellationToken);
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Content = new StringContent("default-content");
            response.Headers.TryAddWithoutValidation("Cache-Control", "max-age=600");
            return response;
        });

        using var cache = new CachingDelegateHandler(handler);
        using var httpClient = new HttpClient(cache);

        var tasks = Enumerable.Range(0, 10)
            .Select(_ => httpClient.GetAsync("http://example.com/test", TestContext.Current.CancellationToken))
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
        var handler = new MockResponseHandler(async req =>
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
        for (int i = 0; i < 20; i++)
        {
            var lang = i % 2 == 0 ? "en" : "fr";
            var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/test");
            request.Headers.TryAddWithoutValidation("Accept-Language", lang);
            tasks.Add(httpClient.SendAsync(request, TestContext.Current.CancellationToken));
        }

        var responses = await Task.WhenAll(tasks);

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));

        for (int i = 0; i < responses.Length; i++)
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
        var requestCounts = new ConcurrentDictionary<string, int>();

        var handler = new MockResponseHandler(async req =>
        {
            var url = req.RequestUri?.ToString() ?? string.Empty;
            requestCounts.AddOrUpdate(url, 1, (_, count) => count + 1);
            await Task.Delay(10, TestContext.Current.CancellationToken);
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Content = new StringContent("default-content");
            response.Headers.TryAddWithoutValidation("Cache-Control", "max-age=600");
            return response;
        });

        using var cache = new CachingDelegateHandler(handler);
        using var httpClient = new HttpClient(cache);

        var tasks = new List<Task<HttpResponseMessage>>();
        for (int i = 0; i < 50; i++)
        {
            var url = urls[i % urls.Length];
            tasks.Add(httpClient.GetAsync(url, TestContext.Current.CancellationToken));
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
        var handler = new MockResponseHandler(async req =>
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

            if (req.Headers.IfNoneMatch.Any())
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

        using (var response = await httpClient.GetAsync("http://example.com/test", TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var tasks = Enumerable.Range(0, 20)
            .Select(_ => httpClient.GetAsync("http://example.com/test", TestContext.Current.CancellationToken))
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
        var handler = new MockResponseHandler(async req =>
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

        using (var response = await httpClient.GetAsync("http://example.com/test", TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var tasks = new List<Task<HttpResponseMessage>>();
        for (int i = 0; i < 30; i++)
        {
            if (i % 5 == 0)
            {
                var postRequest = new HttpRequestMessage(HttpMethod.Post, "http://example.com/test");
                tasks.Add(httpClient.SendAsync(postRequest, TestContext.Current.CancellationToken));
            }
            else
            {
                tasks.Add(httpClient.GetAsync("http://example.com/test", TestContext.Current.CancellationToken));
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
        var handler = new MockResponseHandler(async req =>
        {
            Interlocked.Increment(ref requestCount);
            await Task.Delay(20, TestContext.Current.CancellationToken);
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Content = new StringContent("default-content");
            response.Headers.TryAddWithoutValidation("Cache-Control", "max-age=0");
            return response;
        });

        using var cache = new CachingDelegateHandler(handler);
        using var httpClient = new HttpClient(cache);

        using (var response = await httpClient.GetAsync("http://example.com/test", TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        await Task.Delay(50, TestContext.Current.CancellationToken);

        var tasks = Enumerable.Range(0, 15)
            .Select(_ =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/test");
                request.Headers.TryAddWithoutValidation("Cache-Control", "max-stale=3600");
                return httpClient.SendAsync(request, TestContext.Current.CancellationToken);
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
}
