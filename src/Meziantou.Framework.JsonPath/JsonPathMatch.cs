using System.Text.Json.Nodes;

namespace Meziantou.Framework.Json;

/// <summary>Represents a single node matched by a JSONPath evaluation.</summary>
public readonly struct JsonPathMatch
{
    internal JsonPathMatch(JsonNode? value, string normalizedPath)
    {
        Value = value;
        NormalizedPath = normalizedPath;
    }

    /// <summary>
    /// Gets the JSON value of the matched node. May be <see langword="null"/> when the matched value is JSON <c>null</c>.
    /// </summary>
    public JsonNode? Value { get; }

    /// <summary>
    /// Gets the normalized path of the matched node (e.g. <c>$['store']['book'][0]</c>).
    /// </summary>
    public string NormalizedPath { get; }
}
