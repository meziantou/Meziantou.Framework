using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meziantou.Framework.HttpArchive;

/// <summary>Represents a posted parameter.</summary>
public sealed class HarPostDataParameter
{
    /// <summary>Gets or sets the name of the posted parameter.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Gets or sets the value of the posted parameter or content of a posted file.</summary>
    [JsonPropertyName("value")]
    public string? Value { get; set; }

    /// <summary>Gets or sets the name of a posted file.</summary>
    [JsonPropertyName("fileName")]
    public string? FileName { get; set; }

    /// <summary>Gets or sets the content type of a posted file.</summary>
    [JsonPropertyName("contentType")]
    public string? ContentType { get; set; }

    /// <summary>Gets or sets a comment provided by the user or the application.</summary>
    [JsonPropertyName("comment")]
    public string? Comment { get; set; }

    /// <summary>Gets or sets additional vendor-specific fields.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
