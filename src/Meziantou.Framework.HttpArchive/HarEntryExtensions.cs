using System.Net;
using System.Net.Http.Headers;

namespace Meziantou.Framework.HttpArchive;

/// <summary>Provides extension methods for converting HAR entries to <see cref="HttpRequestMessage"/> and <see cref="HttpResponseMessage"/>.</summary>
public static class HarEntryExtensions
{
    private const string ContentTypeHeaderName = "Content-Type";

    private static readonly HashSet<string> ContentHeaderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Content-Disposition",
        "Content-Encoding",
        "Content-Language",
        "Content-Length",
        "Content-Location",
        "Content-MD5",
        "Content-Range",
        ContentTypeHeaderName,
        "Expires",
        "Last-Modified",
        "Allow",
    };

    /// <summary>Creates an <see cref="HttpRequestMessage"/> from a HAR entry.</summary>
    /// <param name="entry">The HAR entry to convert.</param>
    /// <returns>An <see cref="HttpRequestMessage"/> representing the HAR request.</returns>
    public static HttpRequestMessage ToHttpRequestMessage(this HarEntry entry)
    {
        return entry.Request.ToHttpRequestMessage();
    }

    /// <summary>Creates an <see cref="HttpResponseMessage"/> from a HAR entry.</summary>
    /// <param name="entry">The HAR entry to convert.</param>
    /// <returns>An <see cref="HttpResponseMessage"/> representing the HAR response.</returns>
    public static HttpResponseMessage ToHttpResponseMessage(this HarEntry entry)
    {
        return entry.Response.ToHttpResponseMessage();
    }

    /// <summary>Creates an <see cref="HttpRequestMessage"/> from a HAR request.</summary>
    /// <param name="request">The HAR request to convert.</param>
    /// <returns>An <see cref="HttpRequestMessage"/> representing the HAR request.</returns>
    public static HttpRequestMessage ToHttpRequestMessage(this HarRequest request)
    {
        var message = new HttpRequestMessage
        {
            Method = new HttpMethod(request.Method),
            RequestUri = new Uri(request.Url),
            Version = ParseHttpVersion(request.HttpVersion),
        };

        HttpContent? content = null;
        if (request.PostData is not null)
        {
            content = request.PostData.Text is not null
                ? new ByteArrayContent(Encoding.UTF8.GetBytes(request.PostData.Text))
                : new ByteArrayContent([]);

            message.Content = content;
        }

        CopyHeaders(request.Headers, message.Headers, content, request.PostData?.MimeType);
        return message;
    }

    /// <summary>Creates an <see cref="HttpResponseMessage"/> from a HAR response.</summary>
    /// <param name="response">The HAR response to convert.</param>
    /// <returns>An <see cref="HttpResponseMessage"/> representing the HAR response.</returns>
    public static HttpResponseMessage ToHttpResponseMessage(this HarResponse response)
    {
        var message = new HttpResponseMessage
        {
            StatusCode = (HttpStatusCode)response.Status,
            ReasonPhrase = response.StatusText,
            Version = ParseHttpVersion(response.HttpVersion),
        };

        var content = CreateContent(response.Content);
        message.Content = content;

        CopyHeaders(response.Headers, message.Headers, content, response.Content.MimeType);
        return message;
    }

    private static ByteArrayContent CreateContent(HarContent harContent)
    {
        if (harContent.Text is null)
            return new ByteArrayContent([]);

        if (string.Equals(harContent.Encoding, "base64", StringComparison.OrdinalIgnoreCase) && TryDecodeBase64(harContent.Text, out var bytes))
            return new ByteArrayContent(bytes);

        return new ByteArrayContent(Encoding.UTF8.GetBytes(harContent.Text));
    }

    private static bool TryDecodeBase64(string text, [NotNullWhen(true)] out byte[]? bytes)
    {
        var buffer = new byte[((text.Length / 4) + 1) * 3];
        if (Convert.TryFromBase64String(text, buffer, out var written))
        {
            bytes = buffer.AsSpan(0, written).ToArray();
            return true;
        }

        bytes = null;
        return false;
    }

    /// <summary>
    /// Copies the archived headers onto the message, routing entity headers to the content.
    /// <paramref name="fallbackMimeType"/> is applied only when the archive contained no Content-Type header,
    /// so the message never ends up with two of them.
    /// </summary>
    private static void CopyHeaders(List<HarHeader> headers, HttpHeaders messageHeaders, HttpContent? content, string? fallbackMimeType)
    {
        var hasContentType = false;
        foreach (var header in headers)
        {
            if (ContentHeaderNames.Contains(header.Name))
            {
                if (content?.Headers.TryAddWithoutValidation(header.Name, header.Value) is true)
                {
                    hasContentType |= string.Equals(header.Name, ContentTypeHeaderName, StringComparison.OrdinalIgnoreCase);
                }
            }
            else
            {
                messageHeaders.TryAddWithoutValidation(header.Name, header.Value);
            }
        }

        if (content is not null && !hasContentType && !string.IsNullOrEmpty(fallbackMimeType))
        {
            content.Headers.TryAddWithoutValidation(ContentTypeHeaderName, fallbackMimeType);
        }
    }

    private static Version ParseHttpVersion(string httpVersion)
    {
        return httpVersion switch
        {
            "HTTP/1.0" or "http/1.0" => new Version(1, 0),
            "HTTP/1.1" or "http/1.1" => new Version(1, 1),
            "HTTP/2" or "HTTP/2.0" or "http/2" or "http/2.0" or "h2" or "h2c" => new Version(2, 0),
            "HTTP/3" or "HTTP/3.0" or "http/3" or "http/3.0" or "h3" => new Version(3, 0),
            _ => new Version(1, 1),
        };
    }
}
