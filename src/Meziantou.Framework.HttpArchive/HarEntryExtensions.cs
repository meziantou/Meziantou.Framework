using System.Net;
using System.Net.Http.Headers;

namespace Meziantou.Framework.HttpArchive;

/// <summary>Provides extension methods for converting HAR entries to <see cref="HttpRequestMessage"/> and <see cref="HttpResponseMessage"/>.</summary>
public static class HarEntryExtensions
{
    private const string ContentTypeHeaderName = "Content-Type";
    private const string CookieHeaderName = "Cookie";
    private const string SetCookieHeaderName = "Set-Cookie";

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

    /// <summary>
    /// Headers that describe the bytes as they travelled on the wire. The reconstructed content holds the
    /// decoded body from <c>content.text</c>, so replaying these would describe bytes that are no longer there:
    /// a stale Content-Length makes the send fail outright. The recorded values remain available on
    /// <see cref="HarResponse.Content"/>, <see cref="HarRequest.BodySize"/> and the header lists.
    /// </summary>
    private static readonly HashSet<string> WireOnlyHeaderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Content-Length",
        "Content-Encoding",
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
    /// <remarks>
    /// When the archive recorded the body as <c>params</c> instead of <c>text</c>, a
    /// <c>application/x-www-form-urlencoded</c> body is rebuilt from the parameters. Multipart bodies cannot be
    /// rebuilt this way because the original boundary is not part of the archive; they convert to an empty body.
    /// </remarks>
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
            content = CreateRequestContent(request.PostData);
        }
        else if (HasContentHeader(request.Headers))
        {
            // The body was not captured, but the entity headers describing it were: keep somewhere to put them.
            content = new ByteArrayContent([]);
        }

        message.Content = content;

        CopyHeaders(request.Headers, message.Headers, content, request.PostData?.MimeType);

        // Some tools record cookies only in the structured list, without the header that carried them.
        if (request.Cookies.Count > 0 && !message.Headers.Contains(CookieHeaderName))
        {
            message.Headers.TryAddWithoutValidation(CookieHeaderName, BuildCookieHeader(request.Cookies));
        }

        return message;
    }

    private static ByteArrayContent CreateRequestContent(HarPostData postData)
    {
        if (postData.Text is not null)
            return new ByteArrayContent(Encoding.UTF8.GetBytes(postData.Text));

        if (postData.Params is { Count: > 0 } parameters &&
            postData.MimeType.StartsWith("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase))
        {
            return new ByteArrayContent(Encoding.UTF8.GetBytes(BuildFormUrlEncodedBody(parameters)));
        }

        return new ByteArrayContent([]);
    }

    private static string BuildFormUrlEncodedBody(List<HarPostDataParameter> parameters)
    {
        // Archives store the parameters already percent-encoded, exactly as they were split out of the body,
        // so they are joined back as-is rather than re-encoded.
        var builder = new StringBuilder();
        foreach (var parameter in parameters)
        {
            if (builder.Length > 0)
            {
                builder.Append('&');
            }

            builder.Append(parameter.Name).Append('=').Append(parameter.Value);
        }

        return builder.ToString();
    }

    private static bool HasContentHeader(List<HarHeader> headers)
    {
        foreach (var header in headers)
        {
            if (!WireOnlyHeaderNames.Contains(header.Name) && ContentHeaderNames.Contains(header.Name))
                return true;
        }

        return false;
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

        if (response.Cookies.Count > 0 && !message.Headers.Contains(SetCookieHeaderName))
        {
            foreach (var cookie in response.Cookies)
            {
                message.Headers.TryAddWithoutValidation(SetCookieHeaderName, BuildSetCookieHeader(cookie));
            }
        }

        return message;
    }

    private static string BuildCookieHeader(List<HarCookie> cookies)
    {
        var builder = new StringBuilder();
        foreach (var cookie in cookies)
        {
            if (builder.Length > 0)
            {
                builder.Append("; ");
            }

            builder.Append(cookie.Name).Append('=').Append(cookie.Value);
        }

        return builder.ToString();
    }

    private static string BuildSetCookieHeader(HarCookie cookie)
    {
        var builder = new StringBuilder();
        builder.Append(cookie.Name).Append('=').Append(cookie.Value);

        if (!string.IsNullOrEmpty(cookie.Path))
        {
            builder.Append("; Path=").Append(cookie.Path);
        }

        if (!string.IsNullOrEmpty(cookie.Domain))
        {
            builder.Append("; Domain=").Append(cookie.Domain);
        }

        if (cookie.Expires is { } expires)
        {
            builder.Append("; Expires=").Append(expires.UtcDateTime.ToString("R", CultureInfo.InvariantCulture));
        }

        if (cookie.HttpOnly is true)
        {
            builder.Append("; HttpOnly");
        }

        if (cookie.Secure is true)
        {
            builder.Append("; Secure");
        }

        return builder.ToString();
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
            if (WireOnlyHeaderNames.Contains(header.Name))
                continue;

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
