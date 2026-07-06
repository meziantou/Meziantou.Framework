namespace Meziantou.Framework.Yaml.Serialization.References;

internal sealed class YamlReferenceReader
{
    private readonly Dictionary<string, object> _anchors = new(StringComparer.Ordinal);

    public void Register(string anchor, object value)
    {
        _anchors[anchor] = value;
    }

    public object Resolve(string alias)
    {
        if (_anchors.TryGetValue(alias, out var value))
        {
            return value;
        }

        throw new InvalidOperationException($"Unknown YAML alias '*{alias}'.");
    }
}
