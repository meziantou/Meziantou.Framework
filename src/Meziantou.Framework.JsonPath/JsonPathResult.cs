using System.Collections;
using System.Text.Json.Nodes;

namespace Meziantou.Framework.Json;

/// <summary>
/// Represents the result of evaluating a JSONPath expression against a JSON value.
/// Contains the ordered list of matched nodes.
/// </summary>
public sealed class JsonPathResult : IReadOnlyList<JsonPathMatch>
{
    internal static readonly JsonPathResult Empty = new([]);

    private readonly List<JsonPathMatch> _matches;

    internal JsonPathResult(List<JsonPathMatch> matches)
    {
        _matches = matches;
    }

    /// <inheritdoc />
    public int Count => _matches.Count;

    /// <inheritdoc />
    public JsonPathMatch this[int index] => _matches[index];

    /// <inheritdoc />
    public IEnumerator<JsonPathMatch> GetEnumerator() => _matches.GetEnumerator();

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    /// Returns the values of all matched nodes as a <see cref="JsonArray"/>.
    /// </summary>
    public JsonArray ToJsonArray()
    {
        var array = new JsonArray();
        foreach (var match in _matches)
        {
            array.Add(match.Value?.DeepClone());
        }

        return array;
    }
}
