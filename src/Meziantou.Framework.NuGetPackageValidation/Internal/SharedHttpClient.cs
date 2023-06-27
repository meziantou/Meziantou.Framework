namespace Meziantou.Framework.NuGetPackageValidation.Internal;
internal static class SharedHttpClient
{
    public static HttpClient Instance { get; } = CreateHttpClient();

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "False-positive")]
    private static HttpClient CreateHttpClient()
    {
        var socketHandler = new SocketsHttpHandler()
        {
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
            PooledConnectionLifetime = TimeSpan.FromMinutes(1),
        };

        return new HttpClient(new HttpRetryMessageHandler(socketHandler), disposeHandler: true);
    }
    private sealed class HttpRetryMessageHandler : DelegatingHandler
    {
        public HttpRetryMessageHandler(HttpMessageHandler handler)
            : base(handler)
        {
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            const int MaxRetries = 5;
            for (var i = 1; ; i++)
            {
                TimeSpan? delay = null;
                HttpResponseMessage? result = null;

                try
                {
                    result = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
                    if (!IsLastAttempt(i) && ((int)result.StatusCode >= 500 || result.StatusCode is System.Net.HttpStatusCode.RequestTimeout or System.Net.HttpStatusCode.TooManyRequests))
                    {
                        result.Dispose();

                        // Use "Retry-After" value, if available. Typically, this is sent with
                        // either a 503 (Service Unavailable) or 429 (Too Many Requests):
                        // https://developer.mozilla.org/en-US/docs/Web/HTTP/Headers/Retry-After

                        delay = result.Headers.RetryAfter switch
                        {
                            { Date : { } date  } => date - DateTimeOffset.UtcNow,
                            { Delta: { } delta } => delta,
                            _ => null,
                        };
                    }
                    else
                    {
                        return result;
                    }
                }
                catch (HttpRequestException)
                {
                    result?.Dispose();
                    if (IsLastAttempt(i))
                        throw;
                }

                await Task.Delay(delay is { } someDelay && someDelay > TimeSpan.Zero ? someDelay : TimeSpan.FromMicroseconds(100 * i), cancellationToken).ConfigureAwait(false);

                static bool IsLastAttempt(int i) => i >= MaxRetries;
            }
        }
    }

}
