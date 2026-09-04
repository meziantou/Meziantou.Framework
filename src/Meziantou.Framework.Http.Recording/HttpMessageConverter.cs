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
            RequestUri = GetRequestUri(request),
            StatusCode = (int)response.StatusCode,
            ReasonPhrase = response.ReasonPhrase,
            HttpVersion = response.Version.ToString(),
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

    /// <summary>Builds the entry used to look up a recording. It carries everything a matcher may read: method, URI, headers and body.</summary>
    /// <remarks>The caller must have buffered <see cref="HttpRequestMessage.Content"/> so the body can be read without consuming it.</remarks>
    public static async Task<HttpRecordingEntry> CreateFromRequestAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var entry = new HttpRecordingEntry
        {
            Method = request.Method.Method,
            RequestUri = GetRequestUri(request),
            RequestHeaders = CaptureHeaders(request.Headers, request.Content?.Headers),
            StatusCode = 0,
        };

        if (request.Content is not null)
        {
            entry.RequestBody = await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        }

        return entry;
    }

    public static HttpResponseMessage ToHttpResponseMessage(HttpRecordingEntry entry, HttpRequestMessage request)
    {
        if (entry.StatusCode is < 0 or > 999)
        {
            throw new InvalidOperationException($"The recorded entry for {entry.Method} {HttpRecordingUri.Redact(entry.RequestUri)} has an out-of-range status code ({entry.StatusCode}). Valid values are between 0 and 999.");
        }

        var response = new HttpResponseMessage((HttpStatusCode)entry.StatusCode)
        {
            RequestMessage = request,
            ReasonPhrase = entry.ReasonPhrase,
        };

        if (entry.HttpVersion is { } version && Version.TryParse(version, out var parsedVersion))
        {
            response.Version = parsedVersion;
        }

        HttpContent content = entry.ResponseBody is { Length: > 0 } body
            ? new ByteArrayContent(body)
            : new ByteArrayContent([]);

        response.Content = content;

        if (entry.ResponseHeaders is not null)
        {
            foreach (var (name, values) in entry.ResponseHeaders)
            {
                // Content-Length is recomputed from the body we actually have. A recorded value can disagree with it
                // (a hand-edited recording, a sanitized body, a browser-exported HAR holding a decompressed payload),
                // and a wrong Content-Length makes consumers read past the data or wait for bytes that never arrive.
                if (string.Equals(name, "Content-Length", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

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

    private static string GetRequestUri(HttpRequestMessage request)
    {
        // Credentials in the userinfo component would otherwise be written to a recording file that is meant to be
        // committed. Stripping them on both the record and the lookup path keeps matching symmetric.
        return HttpRecordingUri.RemoveUserInfo(request.RequestUri?.AbsoluteUri ?? "");
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
