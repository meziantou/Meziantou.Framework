using System.Text.Json;
using System.Text.Json.Nodes;

namespace Meziantou.Framework.AtlassianDataFormat;

/// <summary>Converts Atlassian Document Format documents to Markdown.</summary>
public static class AdfToMarkdown
{
    /// <summary>Converts an ADF document to Markdown using the default options.</summary>
    /// <param name="json">The JSON representation of the document.</param>
    /// <exception cref="AdfException">The value is not a valid ADF document.</exception>
    public static string Convert(string json) => Convert(json, new AdfToMarkdownOptions());

    /// <summary>Converts an ADF document to Markdown.</summary>
    /// <param name="json">The JSON representation of the document.</param>
    /// <param name="options">The conversion options.</param>
    /// <exception cref="AdfException">The value is not a valid ADF document.</exception>
    public static string Convert(string json, AdfToMarkdownOptions options) => Convert(AdfDocument.Parse(json), options);

    /// <summary>Converts an ADF document to Markdown using the default options.</summary>
    /// <param name="element">The JSON representation of the document.</param>
    /// <exception cref="AdfException">The value is not a valid ADF document.</exception>
    public static string Convert(JsonElement element) => Convert(element, new AdfToMarkdownOptions());

    /// <summary>Converts an ADF document to Markdown.</summary>
    /// <param name="element">The JSON representation of the document.</param>
    /// <param name="options">The conversion options.</param>
    /// <exception cref="AdfException">The value is not a valid ADF document.</exception>
    public static string Convert(JsonElement element, AdfToMarkdownOptions options) => Convert(AdfDocument.Parse(element), options);

    /// <summary>Converts an ADF document to Markdown using the default options.</summary>
    /// <param name="node">The JSON representation of the document.</param>
    /// <exception cref="AdfException">The value is not a valid ADF document.</exception>
    public static string Convert(JsonNode node) => Convert(node, new AdfToMarkdownOptions());

    /// <summary>Converts an ADF document to Markdown.</summary>
    /// <param name="node">The JSON representation of the document.</param>
    /// <param name="options">The conversion options.</param>
    /// <exception cref="AdfException">The value is not a valid ADF document.</exception>
    public static string Convert(JsonNode node, AdfToMarkdownOptions options) => Convert(AdfDocument.Parse(node), options);

    /// <summary>Converts an ADF document to Markdown using the default options.</summary>
    /// <param name="document">The document to convert.</param>
    public static string Convert(AdfDocument document) => Convert(document, new AdfToMarkdownOptions());

    /// <summary>Converts an ADF document to Markdown.</summary>
    /// <param name="document">The document to convert.</param>
    /// <param name="options">The conversion options.</param>
    public static string Convert(AdfDocument document, AdfToMarkdownOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);

        return new MarkdownConverter(options).Convert(document);
    }
}
