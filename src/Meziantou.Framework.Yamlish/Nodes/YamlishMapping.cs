using System.Collections;

namespace Meziantou.Framework.Yamlish.Nodes;

/// <summary>Represents a Yamlish mapping node.</summary>
public sealed class YamlishMapping : YamlishNode, IReadOnlyDictionary<string, YamlishNode>
{
    private readonly List<KeyValuePair<string, YamlishNode>> _entries = [];
    private readonly Dictionary<string, YamlishNode> _lookup = new(StringComparer.Ordinal);

    /// <summary>Initializes a new instance of the <see cref="YamlishMapping" /> class.</summary>
    public YamlishMapping()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="YamlishMapping" /> class with the specified entries.</summary>
    /// <param name="entries">The mapping entries.</param>
    public YamlishMapping(IEnumerable<KeyValuePair<string, YamlishNode>> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        foreach (var entry in entries)
        {
            Add(entry.Key, entry.Value);
        }
    }

    /// <inheritdoc />
    public override YamlishNodeKind Kind => YamlishNodeKind.Mapping;

    /// <inheritdoc />
    public int Count => _entries.Count;

    /// <inheritdoc />
    public IEnumerable<string> Keys => _entries.Select(entry => entry.Key);

    /// <inheritdoc />
    public IEnumerable<YamlishNode> Values => _entries.Select(entry => entry.Value);

    /// <inheritdoc />
    public YamlishNode this[string key] => _lookup[key];

    /// <summary>Adds a mapping entry.</summary>
    /// <param name="key">The mapping key.</param>
    /// <param name="value">The mapping value.</param>
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

    /// <inheritdoc />
    public bool ContainsKey(string key) => _lookup.ContainsKey(key);

    /// <inheritdoc />
    public bool TryGetValue(string key, out YamlishNode value) => _lookup.TryGetValue(key, out value!);

    /// <inheritdoc />
    public IEnumerator<KeyValuePair<string, YamlishNode>> GetEnumerator() => _entries.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
