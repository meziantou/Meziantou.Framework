using System.Net;
using System.Net.Http.Headers;

namespace Meziantou.Framework.HttpArchive;

/// <summary>Provides extension methods for converting HAR entries to <see cref="HttpRequestMessage"/> and <see cref="HttpResponseMessage"/>.</summary>
public static class HarEntryExtensions
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
            if (request.PostData.Text is not null)
            {
                content = new StringContent(request.PostData.Text, mediaType: new MediaTypeHeaderValue(request.PostData.MimeType));
            }
            else
            {
                content = new ByteArrayContent([]);
            }

            message.Content = content;
        }

        foreach (var header in request.Headers)
        {
            if (ContentHeaderNames.Contains(header.Name))
            {
                content?.Headers.TryAddWithoutValidation(header.Name, header.Value);
            }
            else
            {
                message.Headers.TryAddWithoutValidation(header.Name, header.Value);
            }
        }

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

        HttpContent content;
        if (string.Equals(response.Content.Encoding, "base64", StringComparison.OrdinalIgnoreCase) && response.Content.Text is not null)
        {
            content = new ByteArrayContent(Convert.FromBase64String(response.Content.Text));
        }
        else if (response.Content.Text is not null)
        {
            content = new StringContent(response.Content.Text, mediaType: new MediaTypeHeaderValue(response.Content.MimeType));
        }
        else
        {
            content = new ByteArrayContent([]);
        }

        message.Content = content;

        foreach (var header in response.Headers)
        {
            if (ContentHeaderNames.Contains(header.Name))
            {
                content.Headers.TryAddWithoutValidation(header.Name, header.Value);
            }
            else
            {
                message.Headers.TryAddWithoutValidation(header.Name, header.Value);
            }
        }

        return message;
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
