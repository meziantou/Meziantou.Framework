using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meziantou.Framework.HttpArchive;

/// <summary>Represents detailed info about an HTTP response.</summary>
public sealed class HarResponse
{
    /// <summary>Gets or sets the response status code.</summary>
    [JsonPropertyName("status")]
    public int Status { get; set; }

    /// <summary>Gets or sets the response status description.</summary>
    [JsonPropertyName("statusText")]
    public string StatusText { get; set; } = "";

    /// <summary>Gets or sets the response HTTP version.</summary>
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

    /// <summary>Gets or sets the details about the response body.</summary>
    [JsonPropertyName("content")]
    public HarContent Content { get; set; } = new();

    /// <summary>Gets or sets the redirection target URL from the Location response header.</summary>
    [JsonPropertyName("redirectURL")]
    [SuppressMessage("Design", "CA1056:URI-like properties should not be strings")]
    public string RedirectUrl { get; set; } = "";

    /// <summary>Gets or sets the total number of bytes from the start of the HTTP response message until (and including) the double CRLF before the body. Use -1 if the info is not available.</summary>
    [JsonPropertyName("headersSize")]
    public long HeadersSize { get; set; }

    /// <summary>Gets or sets the size of the received response body in bytes. Use -1 if the info is not available.</summary>
    [JsonPropertyName("bodySize")]
    public long BodySize { get; set; }

    /// <summary>Gets or sets a comment provided by the user or the application.</summary>
    [JsonPropertyName("comment")]
    public string? Comment { get; set; }

    /// <summary>Gets or sets additional vendor-specific fields.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
