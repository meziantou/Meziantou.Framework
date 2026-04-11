using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meziantou.Framework.HttpArchive;

/// <summary>Represents the response body.</summary>
public sealed class HarContent
{
    /// <summary>Gets or sets the length of the returned content in bytes.</summary>
    [JsonPropertyName("size")]
    public long Size { get; set; }

    /// <summary>Gets or sets the number of bytes saved due to compression. Use -1 if the info is not available.</summary>
    [JsonPropertyName("compression")]
    public long? Compression { get; set; }

    /// <summary>Gets or sets the MIME type of the response text.</summary>
    [JsonPropertyName("mimeType")]
    public string MimeType { get; set; } = "";

    /// <summary>Gets or sets the response body sent from the server or loaded from the browser cache.</summary>
    [JsonPropertyName("text")]
    public string? Text { get; set; }

    /// <summary>Gets or sets the encoding used for response text field (e.g., "base64").</summary>
    [JsonPropertyName("encoding")]
    public string? Encoding { get; set; }

    /// <summary>Gets or sets a comment provided by the user or the application.</summary>
    [JsonPropertyName("comment")]
    public string? Comment { get; set; }

    /// <summary>Gets or sets additional vendor-specific fields.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
