using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meziantou.Framework.HttpArchive;

/// <summary>Represents the state of a cache entry before or after a request.</summary>
public sealed class HarCacheEntry
{
    /// <summary>Gets or sets the expiration time of the cache entry.</summary>
    [JsonPropertyName("expires")]
    public DateTimeOffset? Expires { get; set; }

    /// <summary>Gets or sets the last time the cache entry was opened.</summary>
    [JsonPropertyName("lastAccess")]
    public DateTimeOffset LastAccess { get; set; }

    /// <summary>Gets or sets the ETag.</summary>
    [JsonPropertyName("eTag")]
    public string ETag { get; set; } = "";

    /// <summary>Gets or sets the number of times the cache entry has been opened.</summary>
    [JsonPropertyName("hitCount")]
    public int HitCount { get; set; }

    /// <summary>Gets or sets a comment provided by the user or the application.</summary>
    [JsonPropertyName("comment")]
    public string? Comment { get; set; }

    /// <summary>Gets or sets additional vendor-specific fields.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
