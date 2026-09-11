using Meziantou.Framework.Yaml.Events;

namespace Meziantou.Framework.Yaml.Serialization.References;

internal sealed class YamlReferenceReader
{
    private readonly Dictionary<string, object> _anchors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Scalar> _scalarAnchors = new(StringComparer.Ordinal);

    public void Register(string anchor, object value)
    {
        _anchors[anchor] = value;
        _scalarAnchors.Remove(anchor);
    }

    /// <summary>Registers the scalar node an anchor names.</summary>
    /// <remarks>
    /// A scalar is replayed rather than shared, so an alias to it can be read as whatever type the destination
    /// member declares instead of the type the anchored occurrence happened to produce.
    /// </remarks>
    public void RegisterScalar(string anchor, Scalar scalar)
    {
        _scalarAnchors[anchor] = scalar;
        _anchors.Remove(anchor);
    }

    public bool TryResolveScalar(string alias, [NotNullWhen(true)] out Scalar? scalar)
        => _scalarAnchors.TryGetValue(alias, out scalar);

    public object Resolve(string alias)
    {
        if (_anchors.TryGetValue(alias, out var value))
        {
            return value;
        }

        throw new InvalidOperationException($"Unknown YAML alias '*{alias}'.");
    }
}
