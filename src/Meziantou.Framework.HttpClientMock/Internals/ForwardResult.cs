using System.Collections.Frozen;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace Meziantou.Framework.Internals;

internal sealed class ForwardResult(HttpClient? httpClient) : IResult
{
    // Hop-by-hop headers are scoped to a single connection and must not be forwarded (RFC 9110 §7.6.1)
    private static readonly FrozenSet<string> HopByHopHeaders = new[]
    {
        HeaderNames.Connection,
        HeaderNames.KeepAlive,
        HeaderNames.ProxyAuthenticate,
        HeaderNames.ProxyAuthorization,
        "Proxy-Connection",
        HeaderNames.TE,
        HeaderNames.Trailer,
        HeaderNames.TransferEncoding,
        HeaderNames.Upgrade,
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public Task ExecuteAsync(HttpContext context)
    {
        return ExecuteAsyncCore(context, httpClient);
    }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "HttpClient doesn't need to be disposed")]
    public static async Task ExecuteAsyncCore(HttpContext context, HttpClient? httpClient = null)
    {
        var localHttpClient = httpClient ?? context.RequestServices.GetRequiredService<IHttpClientFactory>().CreateClient();
        var request = context.Request;

        var method = HttpMethod.Parse(context.Request.Method);
        var url = $"{request.Scheme}://{request.Host}{request.PathBase}{request.Path}{request.QueryString}";
        using var requestMessage = new HttpRequestMessage(method, url);
        if (HasBody(request))
        {
            requestMessage.Content = new StreamContent(context.Request.Body);
        }

        foreach (var header in request.Headers)
        {
            if (HopByHopHeaders.Contains(header.Key))
                continue;

            var values = header.Value.AsEnumerable();

            // Content headers ("Content-Type", "Content-Length", ...) are rejected by HttpRequestMessage.Headers,
            // they must be set on the content instead
            if (!requestMessage.Headers.TryAddWithoutValidation(header.Key, values))
            {
                requestMessage.Content?.Headers.TryAddWithoutValidation(header.Key, values);
            }
        }

        using var response = await localHttpClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted).ConfigureAwait(false);
        context.Response.StatusCode = (int)response.StatusCode;
        CopyResponseHeaders(response.Headers, context.Response);
        CopyResponseHeaders(response.Content.Headers, context.Response);

        await response.Content.CopyToAsync(context.Response.Body, context.RequestAborted).ConfigureAwait(false);

        static bool HasBody(HttpRequest request)
        {
            return request.ContentLength > 0
                || request.ContentType is not null
                || request.Headers.ContainsKey(HeaderNames.TransferEncoding);
        }

        static void CopyResponseHeaders(HttpHeaders headers, HttpResponse response)
        {
            foreach (var header in headers)
            {
                if (HopByHopHeaders.Contains(header.Key))
                    continue;

                // The length is recomputed by the server for the body it actually writes
                if (string.Equals(header.Key, HeaderNames.ContentLength, StringComparison.OrdinalIgnoreCase))
                    continue;

                response.Headers[header.Key] = header.Value.ToArray();
            }
        }
    }
}
