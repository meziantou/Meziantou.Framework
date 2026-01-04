
using System.Net;
using System.Runtime.CompilerServices;
using Meziantou.Framework.InlineSnapshotTesting;
using Microsoft.Extensions.Time.Testing;

namespace HttpCaching.Tests.Internals;

internal sealed class HttpTestContext : IDisposable
{
    private readonly List<Func<HttpRequestMessage, HttpResponseMessage>> _responseFactories = [];
    private readonly HttpClient _httpClient;
    private int _responseIndex = -1;

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope")]
    public HttpTestContext()
    {
        var handler = new MockResponseHandler(this);
        var cache = new CachingDelegateHandler(handler, TimeProvider);
        _httpClient = new HttpClient(cache, disposeHandler: true);
    }

    public FakeTimeProvider TimeProvider { get; } = new();

    public void AddResponse(Func<HttpRequestMessage, HttpResponseMessage> factory)
    {
        var factoryWrapper = (HttpRequestMessage request) =>
        {
            var response = factory(request);
            response.RequestMessage = request;
            return response;
        };

        _responseFactories.Add(factoryWrapper);
    }

    public void AddResponse(HttpResponseMessage response)
    {
        AddResponse(_ => response);
    }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope")]
    public void AddResponse(HttpStatusCode statusCode, params (string, string)[] headers)
    {
        var response = new HttpResponseMessage(statusCode);
        response.StatusCode = statusCode;
        foreach (var (key, value) in headers)
        {
            if (!response.Headers.TryAddWithoutValidation(key, value))
            {
                response.Content ??= new ByteArrayContent([]);
                response.Content.Headers.TryAddWithoutValidation(key, value);
            }
        }
        AddResponse(_ => response);
    }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope")]
    public void AddResponse(HttpStatusCode statusCode, string content, params (string, string)[] headers)
    {
        var response = new HttpResponseMessage(statusCode);
        response.StatusCode = statusCode;
        response.Content = new StringContent(content);
        foreach (var (key, value) in headers)
        {
            if (!response.Headers.TryAddWithoutValidation(key, value))
            {
                response.Content.Headers.TryAddWithoutValidation(key, value);
            }
        }
        AddResponse(_ => response);
    }

    private async Task<HttpResponseMessage> SendRequest(HttpRequestMessage request)
    {
        return await _httpClient.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private HttpResponseMessage GetResponse(HttpRequestMessage request)
    {
        _responseIndex++;
        if (_responseIndex >= _responseFactories.Count)
        {
            throw new InvalidOperationException("No more responses available");
        }
        var factory = _responseFactories[_responseIndex];
        return factory(request);
    }

    [InlineSnapshotAssertion(nameof(expected))]
    public async Task SnapshotResponse(HttpRequestMessage request, string expected, [CallerFilePath] string? filePath = null, [CallerLineNumber] int lineNumber = -1)
    {
        using var response = await SendRequest(request);
        InlineSnapshot.Validate(response, expected, filePath, lineNumber);
    }

    [InlineSnapshotAssertion(nameof(expected))]
    public async Task SnapshotResponse(string url, string expected, [CallerFilePath] string? filePath = null, [CallerLineNumber] int lineNumber = -1)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await SendRequest(request);
        InlineSnapshot.Validate(response, expected, filePath, lineNumber);
    }

    [InlineSnapshotAssertion(nameof(expected))]
    public async Task SnapshotResponse(HttpMethod method, string url, string expected, [CallerFilePath] string? filePath = null, [CallerLineNumber] int lineNumber = -1)
    {
        using var request = new HttpRequestMessage(method, url);
        using var response = await SendRequest(request);
        InlineSnapshot.Validate(response, expected, filePath, lineNumber);
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
        if (_responseIndex + 1 != _responseFactories.Count)
        {
            // TODO throw new InvalidOperationException($"Not all responses were used. Used {_responseIndex + 1} of {_responseFactories.Count}.");
        }
    }

    private sealed class MockResponseHandler(HttpTestContext context) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(context.GetResponse(request));
        }
    }
}
