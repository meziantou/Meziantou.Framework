using System.Text.Json.Nodes;
using Meziantou.Framework.Json.Internals;

namespace Meziantou.Framework.Json;

/// <summary>Represents a single node matched by a JSONPath evaluation.</summary>
public readonly struct JsonPathMatch
{
    private readonly NormalizedPath _path;

    internal JsonPathMatch(JsonNode? value, NormalizedPath path)
    {
        Value = value;
        _path = path;
    }

    /// <summary>
    /// Gets the JSON value of the matched node. May be <see langword="null"/> when the matched value is JSON <c language="json">null</c>.
    /// </summary>
    public JsonNode? Value { get; }

    /// <summary>
    /// Gets the normalized path of the matched node (e.g. <c>$['store']['book'][0]</c>).
    /// </summary>
    /// <remarks>The path is rendered on first access and cached, so evaluations that never read it do not pay for it.</remarks>
    public string Path => _path.Value;
}
