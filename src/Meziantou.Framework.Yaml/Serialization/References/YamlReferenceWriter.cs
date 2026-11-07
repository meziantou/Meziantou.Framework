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

    /// <summary>Gets a value indicating whether the references are being collected, before the value is actually written.</summary>
    public bool IsCollecting => _referenceCounts is not null && !IsCollectionComplete;

    /// <summary>Gets a value indicating whether the references were collected, and the value is now actually written.</summary>
    public bool IsCollectionComplete { get; private set; }

    public bool TryGetAnchor(object value, out string anchor)
    {
        if (_anchors.TryGetValue(value, out anchor!))
        {
            if (IsCollecting)
            {
                _referenceCounts![value]++;
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

        if (IsCollectionComplete && _referenceCounts!.TryGetValue(value, out var count) && count < 2)
        {
            return null;
        }

        var anchor = $"id{_nextId:000}";
        _nextId++;
        _anchors[value] = anchor;
        if (IsCollecting)
        {
            _referenceCounts![value] = 1;
        }

        return anchor;
    }

    /// <summary>Determines whether the value was written while the references were collected.</summary>
    /// <remarks>The counts are frozen once the collection is complete, so a value first written afterwards is not reported.</remarks>
    public bool WasCollected(object value) => _referenceCounts is not null && _referenceCounts.ContainsKey(value);

    public void CompleteReferenceCollection()
    {
        _anchors.Clear();
        _nextId = 1;
        IsCollectionComplete = true;
    }
}
