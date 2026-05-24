using System.Collections;

namespace Meziantou.Framework.Bencode;

public sealed class BencodeDictionary : BencodeValue, IReadOnlyDictionary<string, BencodeValue>
{
    private readonly List<KeyValuePair<string, BencodeValue>> _entries = [];
    private readonly Dictionary<string, BencodeValue> _lookup = new(StringComparer.Ordinal);

    public BencodeDictionary()
    {
    }

    public BencodeDictionary(IEnumerable<KeyValuePair<string, BencodeValue>> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        foreach (var entry in entries)
        {
            Add(entry.Key, entry.Value);
        }
    }

    public override BencodeValueKind Kind => BencodeValueKind.Dictionary;

    public int Count => _entries.Count;

    public IEnumerable<string> Keys => _entries.Select(entry => entry.Key);

    public IEnumerable<BencodeValue> Values => _entries.Select(entry => entry.Value);

    public BencodeValue this[string key] => _lookup[key];

    public void Add(string key, BencodeValue value)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);

        if (_lookup.ContainsKey(key))
            throw new ArgumentException($"An entry with key '{key}' already exists.", nameof(key));

        _lookup.Add(key, value);
        _entries.Add(new KeyValuePair<string, BencodeValue>(key, value));
    }

    public bool ContainsKey(string key)
    {
        return _lookup.ContainsKey(key);
    }

    public bool TryGetValue(string key, out BencodeValue value)
    {
        return _lookup.TryGetValue(key, out value!);
    }

    public IEnumerator<KeyValuePair<string, BencodeValue>> GetEnumerator() => _entries.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
