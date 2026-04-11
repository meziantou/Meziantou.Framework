using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meziantou.Framework.HttpArchive;

/// <summary>Represents detailed timing info about the page load.</summary>
public sealed class HarPageTimings
{
    /// <summary>Gets or sets the content load time in milliseconds. Use -1 if the timing does not apply.</summary>
    [JsonPropertyName("onContentLoad")]
    public double? OnContentLoad { get; set; }

    /// <summary>Gets or sets the page load time in milliseconds. Use -1 if the timing does not apply.</summary>
    [JsonPropertyName("onLoad")]
    public double? OnLoad { get; set; }

    /// <summary>Gets or sets a comment provided by the user or the application.</summary>
    [JsonPropertyName("comment")]
    public string? Comment { get; set; }

    /// <summary>Gets or sets additional vendor-specific fields.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
