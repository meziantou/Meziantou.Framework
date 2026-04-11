using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meziantou.Framework.HttpArchive;

/// <summary>Represents an exported page.</summary>
public sealed class HarPage
{
    /// <summary>Gets or sets the date and time stamp for the beginning of the page load.</summary>
    [JsonPropertyName("startedDateTime")]
    public DateTimeOffset StartedDateTime { get; set; }

    /// <summary>Gets or sets the unique identifier of a page. Entries use it to refer to the parent page.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    /// <summary>Gets or sets the page title.</summary>
    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    /// <summary>Gets or sets detailed timing info about the page load.</summary>
    [JsonPropertyName("pageTimings")]
    public HarPageTimings PageTimings { get; set; } = new();

    /// <summary>Gets or sets a comment provided by the user or the application.</summary>
    [JsonPropertyName("comment")]
    public string? Comment { get; set; }

    /// <summary>Gets or sets additional vendor-specific fields.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
