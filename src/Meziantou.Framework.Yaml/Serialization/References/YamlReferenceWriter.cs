namespace Meziantou.Framework.Yaml.Serialization.References;

internal sealed class YamlReferenceWriter
{
    private readonly Dictionary<object, string> _anchors = new(ReferenceEqualityComparer.Instance);
    private int _nextId = 1;

    public bool TryGetAnchor(object value, out string anchor) => _anchors.TryGetValue(value, out anchor!);

    public string GetOrAddAnchor(object value)
    {
        if (_anchors.TryGetValue(value, out var existing))
        {
            return existing;
        }

        var anchor = $"id{_nextId:000}";
        _nextId++;
        _anchors[value] = anchor;
        return anchor;
    }
}

