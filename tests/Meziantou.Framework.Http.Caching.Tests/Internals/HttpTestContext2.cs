using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using Meziantou.Framework.InlineSnapshotTesting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Time.Testing;

namespace HttpCaching.Tests.Internals;

internal sealed class HttpTestContext2 : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly HttpClient _httpClient;
    private readonly Queue<ExpectedRequest> _expectedRequests = [];

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope")]
    public HttpTestContext2()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        _app = builder.Build();
        _app.Run(async context =>
        {
            await HandleRequest(context);
        });
        _ = _app.StartAsync();

        var handler = _app.GetTestServer().CreateHandler();
        var cache = new CachingDelegateHandler(handler, TimeProvider);
        _httpClient = new HttpClient(cache, disposeHandler: true);
    }

    public FakeTimeProvider TimeProvider { get; } = new();

    public void AddResponse(Func<HttpContext, Task> responseFactory)
    {
        _expectedRequests.Enqueue(new ExpectedRequest(responseFactory));
    }

    public void AddResponse(HttpStatusCode statusCode, params (string, string)[] headers)
    {
        AddResponse((context) =>
        {
            context.Response.StatusCode = (int)statusCode;
            foreach (var (key, value) in headers)
            {
                context.Response.Headers.Append(key, value);
            }

            return Task.CompletedTask;
        });
    }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope")]
    public void AddResponse(HttpStatusCode statusCode, string content, params (string, string)[] headers)
    {
        AddResponse(async (context) =>
        {
            context.Response.StatusCode = (int)statusCode;
            foreach (var (key, value) in headers)
            {
                context.Response.Headers.Append(key, value);
            }

            context.Response.ContentLength = Encoding.UTF8.GetByteCount(content);
            context.Response.ContentType = "text/plain; charset=utf-8";
            await context.Response.Body.WriteAsync(Encoding.UTF8.GetBytes(content));
        });
    }

    private async Task<HttpResponseMessage> SendRequest(HttpRequestMessage message)
    {
        return await _httpClient.SendAsync(message);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.DisposeAsync();
        if (_expectedRequests.Count != 0)
        {
            // TODO throw new InvalidOperationException("Not all expected requests were made. Remaining requests: " + _expectedRequests.Count);
        }
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


    private async Task HandleRequest(HttpContext context)
    {
        var request = _expectedRequests.Dequeue();
        await request.ResponseFactory(context);
    }

    private sealed record ExpectedRequest(Func<HttpContext, Task> ResponseFactory);
}
