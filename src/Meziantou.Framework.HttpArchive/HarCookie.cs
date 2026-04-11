using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meziantou.Framework.HttpArchive;

/// <summary>Represents a cookie used in a request or response.</summary>
public sealed class HarCookie
{
    /// <summary>Gets or sets the name of the cookie.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>Gets or sets the cookie value.</summary>
    [JsonPropertyName("value")]
    public string Value { get; set; } = "";

    /// <summary>Gets or sets the path pertaining to the cookie.</summary>
    [JsonPropertyName("path")]
    public string? Path { get; set; }

    /// <summary>Gets or sets the host of the cookie.</summary>
    [JsonPropertyName("domain")]
    public string? Domain { get; set; }

    /// <summary>Gets or sets the cookie expiration time.</summary>
    [JsonPropertyName("expires")]
    public DateTimeOffset? Expires { get; set; }

    /// <summary>Gets or sets whether the cookie is HTTP only.</summary>
    [JsonPropertyName("httpOnly")]
    public bool? HttpOnly { get; set; }

    /// <summary>Gets or sets whether the cookie was transmitted over SSL.</summary>
    [JsonPropertyName("secure")]
    public bool? Secure { get; set; }

    /// <summary>Gets or sets a comment provided by the user or the application.</summary>
    [JsonPropertyName("comment")]
    public string? Comment { get; set; }

    /// <summary>Gets or sets additional vendor-specific fields.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
