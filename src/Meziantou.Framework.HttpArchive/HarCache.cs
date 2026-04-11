using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meziantou.Framework.HttpArchive;

/// <summary>Represents info about cache usage.</summary>
public sealed class HarCache
{
    /// <summary>Gets or sets the state of a cache entry before the request.</summary>
    [JsonPropertyName("beforeRequest")]
    public HarCacheEntry? BeforeRequest { get; set; }

    /// <summary>Gets or sets the state of a cache entry after the request.</summary>
    [JsonPropertyName("afterRequest")]
    public HarCacheEntry? AfterRequest { get; set; }

    /// <summary>Gets or sets a comment provided by the user or the application.</summary>
    [JsonPropertyName("comment")]
    public string? Comment { get; set; }

    /// <summary>Gets or sets additional vendor-specific fields.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
