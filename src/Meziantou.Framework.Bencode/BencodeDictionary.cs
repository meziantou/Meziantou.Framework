using System.Collections;
using System.Text;

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

    public override void WriteTo(BencodeWriter writer, bool canonical)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteStartDictionary();
        foreach (var entry in GetEntries(canonical))
        {
            writer.WriteKey(entry.Key);
            entry.Value.WriteTo(writer, canonical);
        }

        writer.WriteEndDictionary();
    }

    public IEnumerator<KeyValuePair<string, BencodeValue>> GetEnumerator() => _entries.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private IEnumerable<KeyValuePair<string, BencodeValue>> GetEntries(bool canonical)
    {
        if (!canonical)
            return _entries;

        var entries = _entries.ToArray();
        Array.Sort(entries, CompareByUtf8Key);
        return entries;
    }

    private static int CompareByUtf8Key(KeyValuePair<string, BencodeValue> left, KeyValuePair<string, BencodeValue> right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left.Key);
        var rightBytes = Encoding.UTF8.GetBytes(right.Key);
        return leftBytes.AsSpan().SequenceCompareTo(rightBytes);
    }
}
