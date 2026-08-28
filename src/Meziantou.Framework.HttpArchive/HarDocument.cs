using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Meziantou.Framework.HttpArchive;

/// <summary>Represents the root of a HAR (HTTP Archive) document.</summary>
public sealed class HarDocument
{
    // Derived from the generated contract rather than a second source-generated context, so the serialization
    // metadata for the whole model is emitted once instead of twice.
    private static readonly JsonTypeInfo<HarDocument> IndentedTypeInfo = CreateIndentedTypeInfo();

    /// <summary>Gets or sets the log object containing all HAR data.</summary>
    [JsonPropertyName("log")]
    public HarLog? Log { get; set; } = new();

    /// <summary>Parses a HAR document from a JSON string.</summary>
    /// <param name="json">The JSON string to parse.</param>
    /// <returns>The parsed HAR document.</returns>
    public static HarDocument Parse(string json)
    {
        return JsonSerializer.Deserialize(json, HarSerializerContext.Default.HarDocument)
            ?? throw new InvalidOperationException("Failed to parse HAR document.");
    }

    /// <summary>Parses a HAR document from a UTF-8 stream.</summary>
    /// <param name="stream">The stream containing the HAR JSON data.</param>
    /// <returns>The parsed HAR document.</returns>
    public static HarDocument Parse(Stream stream)
    {
        return JsonSerializer.Deserialize(stream, HarSerializerContext.Default.HarDocument)
            ?? throw new InvalidOperationException("Failed to parse HAR document.");
    }

    /// <summary>Asynchronously parses a HAR document from a UTF-8 stream.</summary>
    /// <param name="stream">The stream containing the HAR JSON data.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>The parsed HAR document.</returns>
    public static async Task<HarDocument> ParseAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        return await JsonSerializer.DeserializeAsync(stream, HarSerializerContext.Default.HarDocument, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Failed to parse HAR document.");
    }

    /// <summary>Serializes the HAR document to a JSON string.</summary>
    /// <param name="indented">Whether to format the JSON output with indentation.</param>
    /// <returns>The JSON string representation of the HAR document.</returns>
    public string ToJsonString(bool indented = false)
    {
        var typeInfo = GetTypeInfo(indented);
        return JsonSerializer.Serialize(this, typeInfo);
    }

    /// <summary>Serializes the HAR document to a UTF-8 stream.</summary>
    /// <param name="stream">The stream to write the JSON data to.</param>
    /// <param name="indented">Whether to format the JSON output with indentation.</param>
    public void WriteTo(Stream stream, bool indented = false)
    {
        var typeInfo = GetTypeInfo(indented);
        JsonSerializer.Serialize(stream, this, typeInfo);
    }

    /// <summary>Asynchronously serializes the HAR document to a UTF-8 stream.</summary>
    /// <param name="stream">The stream to write the JSON data to.</param>
    /// <param name="indented">Whether to format the JSON output with indentation.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    public Task WriteToAsync(Stream stream, bool indented = false, CancellationToken cancellationToken = default)
    {
        var typeInfo = GetTypeInfo(indented);
        return JsonSerializer.SerializeAsync(stream, this, typeInfo, cancellationToken);
    }

    /// <summary>Gets or sets additional vendor-specific fields.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    private static JsonTypeInfo<HarDocument> GetTypeInfo(bool indented)
    {
        return indented ? IndentedTypeInfo : HarSerializerContext.Default.HarDocument;
    }

    private static JsonTypeInfo<HarDocument> CreateIndentedTypeInfo()
    {
        var options = new JsonSerializerOptions(HarSerializerContext.Default.Options)
        {
            WriteIndented = true,
        };

        return (JsonTypeInfo<HarDocument>)options.GetTypeInfo(typeof(HarDocument));
    }
}
