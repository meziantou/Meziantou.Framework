﻿#if NETFRAMEWORK
using System.Net.Http;
#endif

namespace Meziantou.Framework;
internal static class SharedHttpClient
{
    public static HttpClient Instance { get; } = CreateHttpClient();

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "False-positive")]
    private static HttpClient CreateHttpClient()
    {
#if NET
        var socketHandler = new SocketsHttpHandler()
        {
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
            PooledConnectionLifetime = TimeSpan.FromMinutes(1),
        };
#else
        var socketHandler = new HttpClientHandler();
#endif

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
            var defaultDelay = TimeSpan.FromMilliseconds(200);
            for (var i = 1; ; i++, defaultDelay += defaultDelay) // timespan*2 is not supported on .NET 462
            {
                TimeSpan? delayHint = null;
                HttpResponseMessage? result = null;

                try
                {
                    result = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
                    if (!IsLastAttempt(i) && ((int)result.StatusCode >= 500 || result.StatusCode is System.Net.HttpStatusCode.RequestTimeout or (System.Net.HttpStatusCode)429 /* TooManyRequests */))
                    {
                        // Use "Retry-After" value, if available. Typically, this is sent with
                        // either a 503 (Service Unavailable) or 429 (Too Many Requests):
                        // https://developer.mozilla.org/en-US/docs/Web/HTTP/Headers/Retry-After

                        delayHint = result.Headers.RetryAfter switch
                        {
                            { Date: { } date } => date - DateTimeOffset.UtcNow,
                            { Delta: { } delta } => delta,
                            _ => null,
                        };

                        result.Dispose();
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
                catch (TaskCanceledException ex) when (ex.CancellationToken != cancellationToken) // catch "The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing"
                {
                    result?.Dispose();
                    if (IsLastAttempt(i))
                        throw;
                }

                await Task.Delay(delayHint is { } someDelay && someDelay > TimeSpan.Zero ? someDelay : defaultDelay, cancellationToken).ConfigureAwait(false);

                static bool IsLastAttempt(int i) => i >= MaxRetries;
            }
        }
    }
}
