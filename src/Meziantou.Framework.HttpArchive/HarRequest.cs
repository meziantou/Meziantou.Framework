using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meziantou.Framework.HttpArchive;

/// <summary>Represents detailed info about an HTTP request.</summary>
public sealed class HarRequest
{
    /// <summary>Gets or sets the request method (GET, POST, ...).</summary>
    [JsonPropertyName("method")]
    public string Method { get; set; } = "";

    /// <summary>Gets or sets the absolute URL of the request (fragments are not included).</summary>
    [JsonPropertyName("url")]
    [SuppressMessage("Design", "CA1056:URI-like properties should not be strings")]
    public string Url { get; set; } = "";

    /// <summary>Gets or sets the request HTTP version.</summary>
    [JsonPropertyName("httpVersion")]
    public string HttpVersion { get; set; } = "";

    /// <summary>Gets or sets the list of cookie objects.</summary>
    [JsonPropertyName("cookies")]
    [SuppressMessage("Design", "CA1002:Do not expose generic lists")]
    public List<HarCookie> Cookies { get; set; } = [];

    /// <summary>Gets or sets the list of header objects.</summary>
    [JsonPropertyName("headers")]
    [SuppressMessage("Design", "CA1002:Do not expose generic lists")]
    public List<HarHeader> Headers { get; set; } = [];

    /// <summary>Gets or sets the list of query string parameter objects.</summary>
    [JsonPropertyName("queryString")]
    [SuppressMessage("Design", "CA1002:Do not expose generic lists")]
    public List<HarQueryParameter> QueryString { get; set; } = [];

    /// <summary>Gets or sets the posted data info.</summary>
    [JsonPropertyName("postData")]
    public HarPostData? PostData { get; set; }

    /// <summary>Gets or sets the total number of bytes from the start of the HTTP request message until (and including) the double CRLF before the body. Use -1 if the info is not available.</summary>
    [JsonPropertyName("headersSize")]
    public long HeadersSize { get; set; }

    /// <summary>Gets or sets the size of the request body in bytes. Use -1 if the info is not available.</summary>
    [JsonPropertyName("bodySize")]
    public long BodySize { get; set; }

    /// <summary>Gets or sets a comment provided by the user or the application.</summary>
    [JsonPropertyName("comment")]
    public string? Comment { get; set; }

    /// <summary>Gets or sets additional vendor-specific fields.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
