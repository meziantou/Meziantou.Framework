using System.Net;

namespace HttpCaching.Tests.Internals;

/// <summary>
/// A simple mock HTTP handler that returns responses based on a provided function.
/// Useful for testing scenarios where responses depend on request content.
/// </summary>
internal sealed class MockResponseHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responseFunc) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return await responseFunc(request);
    }
}
