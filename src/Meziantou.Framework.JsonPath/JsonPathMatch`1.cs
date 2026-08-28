using Meziantou.Framework.Json.Internals;

namespace Meziantou.Framework.Json;

/// <summary>Represents a single value matched by a JSONPath evaluation.</summary>
/// <typeparam name="TValue">The node type used by the JSONPath navigator.</typeparam>
public readonly struct JsonPathMatch<TValue>
{
    private readonly NormalizedPath _path;

    internal JsonPathMatch(TValue? value, NormalizedPath path)
    {
        Value = value;
        _path = path;
    }

    /// <summary>
    /// Gets the value of the matched node. May be <see langword="null"/> when the navigator represents JSON <c language="json">null</c> as <see langword="null"/>.
    /// </summary>
    public TValue? Value { get; }

    /// <summary>
    /// Gets the normalized path of the matched node (e.g. <c>$['store']['book'][0]</c>).
    /// </summary>
    /// <remarks>The path is rendered on first access and cached, so evaluations that never read it do not pay for it.</remarks>
    public string Path => _path.Value;

    /// <summary>Gets the unrendered path, so adapters can hand it on without forcing it to a string.</summary>
    internal NormalizedPath RawPath => _path;
}
