using System.Net;
using System.Net.Http.Headers;

namespace Meziantou.Framework.Http.Recording;

internal static class HttpMessageConverter
{
    private static readonly HashSet<string> ContentHeaderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Content-Disposition",
        "Content-Encoding",
        "Content-Language",
        "Content-Length",
        "Content-Location",
        "Content-MD5",
        "Content-Range",
        "Content-Type",
        "Expires",
        "Last-Modified",
        "Allow",
    };

    public static async Task<HttpRecordingEntry> CreateFromRequestResponseAsync(
        HttpRequestMessage request, HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var entry = new HttpRecordingEntry
        {
            Method = request.Method.Method,
            RequestUri = request.RequestUri?.AbsoluteUri ?? "",
            StatusCode = (int)response.StatusCode,
            RecordedAt = DateTimeOffset.UtcNow,
        };

        // Capture request headers
        entry.RequestHeaders = CaptureHeaders(request.Headers, request.Content?.Headers);

        // Capture request body
        if (request.Content is not null)
        {
            entry.RequestBody = await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        }

        // Capture response headers
        entry.ResponseHeaders = CaptureHeaders(response.Headers, response.Content.Headers);

        // Capture response body
        entry.ResponseBody = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

        return entry;
    }

    public static HttpRecordingEntry CreateFromRequest(HttpRequestMessage request)
    {
        return new HttpRecordingEntry
        {
            Method = request.Method.Method,
            RequestUri = request.RequestUri?.AbsoluteUri ?? "",
            RequestHeaders = CaptureHeaders(request.Headers, request.Content?.Headers),
            StatusCode = 0,
        };
    }

    public static HttpResponseMessage ToHttpResponseMessage(HttpRecordingEntry entry)
    {
        var response = new HttpResponseMessage((HttpStatusCode)entry.StatusCode);

        HttpContent content;
        if (entry.ResponseBody is { Length: > 0 })
        {
            content = new ByteArrayContent(entry.ResponseBody);
        }
        else
        {
            content = new ByteArrayContent([]);
        }

        response.Content = content;

        if (entry.ResponseHeaders is not null)
        {
            foreach (var (name, values) in entry.ResponseHeaders)
            {
                if (ContentHeaderNames.Contains(name))
                {
                    content.Headers.TryAddWithoutValidation(name, values);
                }
                else
                {
                    response.Headers.TryAddWithoutValidation(name, values);
                }
            }
        }

        return response;
    }

    private static Dictionary<string, string[]> CaptureHeaders(
        HttpHeaders headers, HttpContentHeaders? contentHeaders)
    {
        var result = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, values) in headers)
        {
            result[name] = values.ToArray();
        }

        if (contentHeaders is not null)
        {
            foreach (var (name, values) in contentHeaders)
            {
                result[name] = values.ToArray();
            }
        }

        return result;
    }
}
