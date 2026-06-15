using System.Collections;

namespace Meziantou.Framework.Yamlish;

public sealed class YamlishMapping : YamlishNode, IReadOnlyDictionary<string, YamlishNode>
{
    private readonly List<KeyValuePair<string, YamlishNode>> _entries = [];
    private readonly Dictionary<string, YamlishNode> _lookup = new(StringComparer.Ordinal);

    public YamlishMapping()
    {
    }

    public YamlishMapping(IEnumerable<KeyValuePair<string, YamlishNode>> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        foreach (var entry in entries)
        {
            Add(entry.Key, entry.Value);
        }
    }

    public override YamlishNodeKind Kind => YamlishNodeKind.Mapping;

    public int Count => _entries.Count;

    public IEnumerable<string> Keys => _entries.Select(entry => entry.Key);

    public IEnumerable<YamlishNode> Values => _entries.Select(entry => entry.Value);

    public YamlishNode this[string key] => _lookup[key];

    public void Add(string key, YamlishNode value)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(value);
        _lookup.Add(key, value);
        _entries.Add(new KeyValuePair<string, YamlishNode>(key, value));
    }

    internal void AddOrReplace(string key, YamlishNode value)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(value);
        if (_lookup.TryAdd(key, value))
        {
            _entries.Add(new KeyValuePair<string, YamlishNode>(key, value));
            return;
        }

        _lookup[key] = value;
        var index = _entries.FindIndex(entry => StringComparer.Ordinal.Equals(entry.Key, key));
        _entries[index] = new KeyValuePair<string, YamlishNode>(key, value);
    }

    public bool ContainsKey(string key) => _lookup.ContainsKey(key);

    public bool TryGetValue(string key, out YamlishNode value) => _lookup.TryGetValue(key, out value!);

    public IEnumerator<KeyValuePair<string, YamlishNode>> GetEnumerator() => _entries.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
