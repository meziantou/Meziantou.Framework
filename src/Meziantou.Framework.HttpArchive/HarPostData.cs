using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meziantou.Framework.HttpArchive;

/// <summary>Represents posted data.</summary>
public sealed class HarPostData
{
    /// <summary>Gets or sets the MIME type of the posted data.</summary>
    [JsonPropertyName("mimeType")]
    public string MimeType { get; set; } = "";

    /// <summary>Gets or sets the list of posted parameters (in case of URL encoded parameters).</summary>
    [JsonPropertyName("params")]
    [SuppressMessage("Design", "CA1002:Do not expose generic lists")]
    public List<HarPostDataParameter>? Params { get; set; }

    /// <summary>Gets or sets the plain text posted data.</summary>
    [JsonPropertyName("text")]
    public string? Text { get; set; }

    /// <summary>Gets or sets a comment provided by the user or the application.</summary>
    [JsonPropertyName("comment")]
    public string? Comment { get; set; }

    /// <summary>Gets or sets additional vendor-specific fields.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
