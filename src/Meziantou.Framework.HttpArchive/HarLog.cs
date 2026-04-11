using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meziantou.Framework.HttpArchive;

/// <summary>Represents the root of the exported data.</summary>
public sealed class HarLog
{
    /// <summary>Gets or sets the version number of the HAR format. Defaults to "1.2".</summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.2";

    /// <summary>Gets or sets information about the application that created the log.</summary>
    [JsonPropertyName("creator")]
    public HarCreator Creator { get; set; } = new();

    /// <summary>Gets or sets information about the browser that created the log.</summary>
    [JsonPropertyName("browser")]
    public HarBrowser? Browser { get; set; }

    /// <summary>Gets or sets the list of exported pages.</summary>
    [JsonPropertyName("pages")]
    [SuppressMessage("Design", "CA1002:Do not expose generic lists")]
    public List<HarPage>? Pages { get; set; }

    /// <summary>Gets or sets the list of exported HTTP requests.</summary>
    [JsonPropertyName("entries")]
    [SuppressMessage("Design", "CA1002:Do not expose generic lists")]
    public List<HarEntry> Entries { get; set; } = [];

    /// <summary>Gets or sets a comment provided by the user or the application.</summary>
    [JsonPropertyName("comment")]
    public string? Comment { get; set; }

    /// <summary>Gets or sets additional vendor-specific fields.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
