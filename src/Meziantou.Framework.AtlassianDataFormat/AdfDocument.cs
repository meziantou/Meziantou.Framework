using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Meziantou.Framework.AtlassianDataFormat;

/// <summary>Represents an Atlassian Document Format document.</summary>
public sealed class AdfDocument
{
    /// <summary>Gets the version of the document. The only version defined today is <c>1</c>.</summary>
    public int Version { get; init; } = 1;

    /// <summary>Gets the top-level nodes of the document.</summary>
    public IReadOnlyList<AdfNode> Content { get; init; } = [];

    /// <summary>Parses an ADF document from its JSON representation.</summary>
    /// <param name="json">The JSON representation of the document.</param>
    /// <exception cref="AdfException">The value is not a valid ADF document.</exception>
    public static AdfDocument Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            // A string with an unpaired surrogate is not text, so JsonDocument cannot read it (ArgumentException)
            throw new AdfException("The value is not valid JSON", ex);
        }

        using (document)
        {
            return AdfJsonReader.ReadDocument(document.RootElement);
        }
    }

    /// <summary>Parses an ADF document from an already parsed JSON value.</summary>
    /// <param name="element">The JSON representation of the document.</param>
    /// <exception cref="AdfException">The value is not a valid ADF document.</exception>
    public static AdfDocument Parse(JsonElement element) => AdfJsonReader.ReadDocument(element);

    /// <summary>Parses an ADF document from an already parsed JSON value.</summary>
    /// <param name="node">The JSON representation of the document.</param>
    /// <exception cref="AdfException">The value is not a valid ADF document.</exception>
    public static AdfDocument Parse(JsonNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        // Deserializing to JsonElement goes through the reflection-based serializer, which is not trim safe.
        string json;
        try
        {
            json = node.ToJsonString();
        }
        catch (InvalidOperationException ex)
        {
            // Writing the node unescapes its strings, and a string with an escaped unpaired surrogate ("\ud800") is not text
            throw new AdfException("The document has a string that is not valid UTF-16", ex);
        }

        using var document = JsonDocument.Parse(json);
        return AdfJsonReader.ReadDocument(document.RootElement);
    }

    /// <summary>Tries to parse an ADF document from its JSON representation.</summary>
    /// <param name="json">The JSON representation of the document.</param>
    /// <param name="result">The parsed document, or <see langword="null"/> when the value is not a valid ADF document.</param>
    /// <returns><see langword="true"/> when the value was parsed.</returns>
    public static bool TryParse(string? json, [NotNullWhen(returnValue: true)] out AdfDocument? result)
    {
        if (json is not null)
        {
            try
            {
                result = Parse(json);
                return true;
            }
            catch (AdfException)
            {
            }
        }

        result = null;
        return false;
    }

    /// <summary>Tries to parse an ADF document from an already parsed JSON value.</summary>
    /// <param name="element">The JSON representation of the document.</param>
    /// <param name="result">The parsed document, or <see langword="null"/> when the value is not a valid ADF document.</param>
    /// <returns><see langword="true"/> when the value was parsed.</returns>
    public static bool TryParse(JsonElement element, [NotNullWhen(returnValue: true)] out AdfDocument? result)
    {
        try
        {
            result = Parse(element);
            return true;
        }
        catch (AdfException)
        {
            result = null;
            return false;
        }
    }

    /// <summary>Writes the JSON representation of the document.</summary>
    /// <param name="writer">The writer to write to.</param>
    public void WriteTo(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        AdfJsonWriter.WriteDocument(writer, this);
    }

    /// <summary>Returns the JSON representation of the document.</summary>
    /// <param name="indented">Whether the JSON is indented.</param>
    public string ToJsonString(bool indented = false)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = indented }))
        {
            WriteTo(writer);
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>Converts the document to Markdown using the default options.</summary>
    public string ToMarkdown() => AdfToMarkdown.Convert(this);

    /// <summary>Converts the document to Markdown.</summary>
    /// <param name="options">The conversion options.</param>
    public string ToMarkdown(AdfToMarkdownOptions options) => AdfToMarkdown.Convert(this, options);

    /// <summary>Enumerates every node of the document, depth first.</summary>
    public IEnumerable<AdfNode> Descendants()
    {
        foreach (var node in Content)
        {
            foreach (var descendant in node.DescendantsAndSelf())
            {
                yield return descendant;
            }
        }
    }
}
