namespace Meziantou.Framework.Json;

/// <summary>Represents a single value matched by a JSONPath evaluation.</summary>
/// <typeparam name="TValue">The node type used by the JSONPath navigator.</typeparam>
public readonly struct JsonPathMatch<TValue>
    where TValue : class
{
    internal JsonPathMatch(TValue? value, string path)
    {
        Value = value;
        Path = path;
    }

    /// <summary>
    /// Gets the value of the matched node. May be <see langword="null"/> when the matched value is JSON <c>null</c>.
    /// </summary>
    public TValue? Value { get; }

    /// <summary>
    /// Gets the normalized path of the matched node (e.g. <c>$['store']['book'][0]</c>).
    /// </summary>
    public string Path { get; }
}
