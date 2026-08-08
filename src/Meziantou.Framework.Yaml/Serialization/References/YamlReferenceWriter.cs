namespace Meziantou.Framework.Yaml.Serialization.References;

internal sealed class YamlReferenceWriter
{
    private readonly Dictionary<object, string> _anchors = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, int>? _referenceCounts;
    private int _nextId = 1;

    public YamlReferenceWriter(bool collectReferences = false)
    {
        if (collectReferences)
        {
            _referenceCounts = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
        }
    }

    public bool TryGetAnchor(object value, out string anchor)
    {
        if (_anchors.TryGetValue(value, out anchor!))
        {
            if (_referenceCounts is not null)
            {
                _referenceCounts[value]++;
            }

            return true;
        }

        return false;
    }

    public string? GetOrAddAnchor(object value)
    {
        if (_anchors.TryGetValue(value, out var existing))
        {
            return existing;
        }

        if (_referenceCounts is not null && _referenceCounts.TryGetValue(value, out var count) && count < 2)
        {
            return null;
        }

        var anchor = $"id{_nextId:000}";
        _nextId++;
        _anchors[value] = anchor;
        if (_referenceCounts is not null && !_referenceCounts.ContainsKey(value))
        {
            _referenceCounts[value] = 1;
        }

        return anchor;
    }

    public void CompleteReferenceCollection()
    {
        _anchors.Clear();
        _nextId = 1;
    }
}

