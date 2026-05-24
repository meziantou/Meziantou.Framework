using System.Collections;

namespace Meziantou.Framework.Bencode;

public sealed class BencodeDictionary : BencodeValue, IReadOnlyDictionary<BencodeString, BencodeValue>
{
    private readonly List<KeyValuePair<BencodeString, BencodeValue>> _entries = [];
    private readonly Dictionary<BencodeString, BencodeValue> _lookup = [];

    public BencodeDictionary()
    {
    }

    public BencodeDictionary(IEnumerable<KeyValuePair<BencodeString, BencodeValue>> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        foreach (var entry in entries)
        {
            Add(entry.Key, entry.Value);
        }
    }

    public override BencodeValueKind Kind => BencodeValueKind.Dictionary;

    public int Count => _entries.Count;

    public IEnumerable<BencodeString> Keys => _entries.Select(entry => entry.Key);

    public IEnumerable<BencodeValue> Values => _entries.Select(entry => entry.Value);

    public BencodeValue this[BencodeString key]
    {
        get
        {
            ArgumentNullException.ThrowIfNull(key);
            return _lookup[key];
        }
    }

    public void Add(BencodeString key, BencodeValue value)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);

        var normalizedKey = new BencodeString(key.Value.ToArray());

        if (_lookup.ContainsKey(normalizedKey))
            throw new ArgumentException("An entry with the same key already exists.", nameof(key));

        _lookup.Add(normalizedKey, value);
        _entries.Add(new KeyValuePair<BencodeString, BencodeValue>(normalizedKey, value));
    }

    public bool ContainsKey(BencodeString key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _lookup.ContainsKey(key);
    }

    public bool TryGetValue(BencodeString key, out BencodeValue value)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _lookup.TryGetValue(key, out value!);
    }

    public override void WriteTo(BencodeWriter writer, bool canonical)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteStartDictionary();
        foreach (var entry in GetEntries(canonical))
        {
            writer.WriteKey(entry.Key.Value.Span);
            entry.Value.WriteTo(writer, canonical);
        }

        writer.WriteEndDictionary();
    }

    public IEnumerator<KeyValuePair<BencodeString, BencodeValue>> GetEnumerator() => _entries.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private IEnumerable<KeyValuePair<BencodeString, BencodeValue>> GetEntries(bool canonical)
    {
        if (!canonical)
            return _entries;

        var entries = _entries.ToArray();
        Array.Sort(entries, CompareByKeyBytes);
        return entries;
    }

    private static int CompareByKeyBytes(KeyValuePair<BencodeString, BencodeValue> left, KeyValuePair<BencodeString, BencodeValue> right)
    {
        return left.Key.Value.Span.SequenceCompareTo(right.Key.Value.Span);
    }
}
