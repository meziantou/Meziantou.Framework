using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meziantou.Framework.HttpArchive;

/// <summary>Represents an HTTP header.</summary>
public sealed class HarHeader
{
    /// <summary>Gets or sets the header name.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>Gets or sets the header value.</summary>
    [JsonPropertyName("value")]
    public string Value { get; set; } = "";

    /// <summary>Gets or sets a comment provided by the user or the application.</summary>
    [JsonPropertyName("comment")]
    public string? Comment { get; set; }

    /// <summary>Gets or sets additional vendor-specific fields.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
