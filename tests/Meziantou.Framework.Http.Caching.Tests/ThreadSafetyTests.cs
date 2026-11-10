using System.Collections.Concurrent;
using System.Net;
using Meziantou.Framework.Http.Caching.InMemory;

namespace Meziantou.Framework.Http.Caching.Tests;

public class ThreadSafetyTests
{
    [Fact]
    public async Task RequestMessageIsSet()
    {
        var firstRequest = true;
        using var handler = new MockResponseHandler(req =>
        {
            if (!firstRequest)
            {
                throw new InvalidOperationException("This handler should be called only once.");
            }

            firstRequest = false;
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Content = new StringContent("default-content");
            response.Headers.TryAddWithoutValidation("Cache-Control", "max-age=600");
            return response;
        });

        using var cache = new HttpCachingDelegateHandler(handler, new InMemoryHttpCacheStore());
        using var httpClient = new HttpClient(cache);

        using var request1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/test");
        using var response1 = await httpClient.SendAsync(request1, XunitCancellationToken);
        Assert.Equal(request1, response1.RequestMessage);

        using var request2 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/test");
        using var response2 = await httpClient.SendAsync(request2, XunitCancellationToken);
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

        using var cache = new HttpCachingDelegateHandler(handler, new InMemoryHttpCacheStore());
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

        using var cache = new HttpCachingDelegateHandler(handler, new InMemoryHttpCacheStore());
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

        using var cache = new HttpCachingDelegateHandler(handler, new InMemoryHttpCacheStore());
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

        using var cache = new HttpCachingDelegateHandler(handler, new InMemoryHttpCacheStore());
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

        using var cache = new HttpCachingDelegateHandler(handler, new InMemoryHttpCacheStore());
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

        using var cache = new HttpCachingDelegateHandler(handler, new InMemoryHttpCacheStore());
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

    [Fact]
    public async Task Documentation_CoalesceRequests()
    {
        var requestCount = 0;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var originHandler = new GatedResponseHandler(gate.Task, request =>
        {
            Interlocked.Increment(ref requestCount);
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("content") };
            response.Headers.TryAddWithoutValidation("Cache-Control", "max-age=600");
            return response;
        });

        #region HttpCaching_CoalesceRequests
        var options = new HttpCachingOptions
        {
            // Concurrent requests for the same URL share a single request to the server
            CoalesceRequests = true,
        };

        using var cachingHandler = new HttpCachingDelegateHandler(originHandler, new InMemoryHttpCacheStore(), options);
        using var httpClient = new HttpClient(cachingHandler);
        #endregion

        var tasks = Enumerable.Range(0, 10)
            .Select(_ => httpClient.GetStringAsync("http://example.com/test", XunitCancellationToken))
            .ToArray();

        await Task.Delay(100, XunitCancellationToken);
        gate.SetResult();

        var bodies = await Task.WhenAll(tasks);
        Assert.All(bodies, body => Assert.Equal("content", body));
        Assert.Equal(1, requestCount);
    }

    [Fact]
    public async Task WhenCoalescingAndStoredResponseIsStaleThenConcurrentRequestsShareTheRevalidation()
    {
        var requestCount = 0;
        var conditionalRequestCount = 0;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        gate.SetResult();
        var timeProvider = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        var revalidationGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new GatedResponseHandler(gate.Task, request =>
        {
            Interlocked.Increment(ref requestCount);
            if (request.Headers.IfNoneMatch.Count > 0)
            {
                Interlocked.Increment(ref conditionalRequestCount);
                var notModified = new HttpResponseMessage(HttpStatusCode.NotModified);
                notModified.Headers.TryAddWithoutValidation("Cache-Control", "max-age=600");
                notModified.Headers.TryAddWithoutValidation("ETag", "\"v1\"");
                return notModified;
            }

            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("content") };
            response.Headers.TryAddWithoutValidation("Cache-Control", "max-age=1");
            response.Headers.TryAddWithoutValidation("ETag", "\"v1\"");
            return response;
        });

        using var cache = new HttpCachingDelegateHandler(handler, new InMemoryHttpCacheStore(), new HttpCachingOptions { CoalesceRequests = true, TimeProvider = timeProvider });
        using var httpClient = new HttpClient(cache);
        Assert.Equal("content", await httpClient.GetStringAsync("http://example.com/test", XunitCancellationToken));

        timeProvider.Advance(TimeSpan.FromSeconds(10));
        handler.Gate = revalidationGate.Task;

        var tasks = Enumerable.Range(0, 10)
            .Select(_ => httpClient.GetStringAsync("http://example.com/test", XunitCancellationToken))
            .ToArray();

        await Task.Delay(100, XunitCancellationToken);
        revalidationGate.SetResult();

        var bodies = await Task.WhenAll(tasks);
        Assert.All(bodies, body => Assert.Equal("content", body));
        Assert.Equal(1, conditionalRequestCount);
        Assert.Equal(2, requestCount);
    }

    [Fact]
    public async Task WhenCoalescingAndResponseIsNotStorableThenWaitingRequestsGoToTheServer()
    {
        var requestCount = 0;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new GatedResponseHandler(gate.Task, request =>
        {
            var count = Interlocked.Increment(ref requestCount);
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("content " + count.ToString(CultureInfo.InvariantCulture)) };
            response.Headers.TryAddWithoutValidation("Cache-Control", "no-store");
            return response;
        });

        using var cache = new HttpCachingDelegateHandler(handler, new InMemoryHttpCacheStore(), new HttpCachingOptions { CoalesceRequests = true });
        using var httpClient = new HttpClient(cache);

        var tasks = Enumerable.Range(0, 5)
            .Select(_ => httpClient.GetStringAsync("http://example.com/test", XunitCancellationToken))
            .ToArray();

        await Task.Delay(100, XunitCancellationToken);
        gate.SetResult();

        var bodies = await Task.WhenAll(tasks);
        Assert.Equal(5, requestCount);
        Assert.HasCount(5, bodies.Distinct(StringComparer.Ordinal));
    }

    [Fact]
    public async Task WhenCoalescingAndResponseVariesThenWaitingRequestsWithAnotherVariantGoToTheServer()
    {
        var requestCount = 0;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new GatedResponseHandler(gate.Task, request =>
        {
            Interlocked.Increment(ref requestCount);
            var language = request.Headers.AcceptLanguage.ToString();
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(language) };
            response.Headers.TryAddWithoutValidation("Cache-Control", "max-age=600");
            response.Headers.TryAddWithoutValidation("Vary", "Accept-Language");
            return response;
        });

        using var cache = new HttpCachingDelegateHandler(handler, new InMemoryHttpCacheStore(), new HttpCachingOptions { CoalesceRequests = true });
        using var httpClient = new HttpClient(cache);

        async Task<string> Send(string language)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/test");
            request.Headers.TryAddWithoutValidation("Accept-Language", language);
            using var response = await httpClient.SendAsync(request, XunitCancellationToken);
            return await response.Content.ReadAsStringAsync(XunitCancellationToken);
        }

        var tasks = new[] { Send("en"), Send("fr"), Send("en"), Send("fr") };
        await Task.Delay(100, XunitCancellationToken);
        gate.SetResult();

        var bodies = await Task.WhenAll(tasks);
        Assert.Equal(["en", "fr", "en", "fr"], bodies);
        Assert.True(requestCount <= 3, $"Expected at most 3 requests, but got {requestCount}");
    }

    [Fact]
    public async Task WhenCoalescingAndWaitingRequestIsCanceledThenItThrows()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new GatedResponseHandler(gate.Task, request =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("content") };
            response.Headers.TryAddWithoutValidation("Cache-Control", "max-age=600");
            return response;
        });

        using var cache = new HttpCachingDelegateHandler(handler, new InMemoryHttpCacheStore(), new HttpCachingOptions { CoalesceRequests = true });
        using var httpClient = new HttpClient(cache);

        var leader = httpClient.GetStringAsync("http://example.com/test", XunitCancellationToken);
        await Task.Delay(50, XunitCancellationToken);

        using var cts = new CancellationTokenSource();
        var waiter = httpClient.GetStringAsync("http://example.com/test", cts.Token);
        await Task.Delay(50, XunitCancellationToken);
        await cts.CancelAsync();

        await Assert.ThrowsAsync<TaskCanceledException>(() => waiter);

        gate.SetResult();
        Assert.Equal("content", await leader);
    }

    private sealed class GatedResponseHandler(Task gate, Func<HttpRequestMessage, HttpResponseMessage> responseFunc) : HttpMessageHandler
    {
        public Task Gate { get; set; } = gate;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            return responseFunc(request);
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
