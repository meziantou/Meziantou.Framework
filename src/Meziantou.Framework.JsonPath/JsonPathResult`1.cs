using System.Collections;

namespace Meziantou.Framework.Json;

/// <summary>
/// Represents the result of evaluating a JSONPath expression against a custom object model.
/// Contains the ordered list of matched nodes.
/// </summary>
/// <typeparam name="TValue">The node type used by the JSONPath navigator.</typeparam>
public sealed class JsonPathResult<TValue> : IReadOnlyList<JsonPathMatch<TValue>>
    where TValue : class
{
    internal static readonly JsonPathResult<TValue> Empty = new([]);

    private readonly List<JsonPathMatch<TValue>> _matches;

    internal JsonPathResult(List<JsonPathMatch<TValue>> matches)
    {
        _matches = matches;
    }

    /// <inheritdoc />
    public int Count => _matches.Count;

    /// <inheritdoc />
    public JsonPathMatch<TValue> this[int index] => _matches[index];

    /// <inheritdoc />
    public IEnumerator<JsonPathMatch<TValue>> GetEnumerator() => _matches.GetEnumerator();

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
