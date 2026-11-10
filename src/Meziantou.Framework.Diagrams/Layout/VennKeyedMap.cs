namespace Meziantou.Framework.Diagrams.Layout;

/// <summary>
/// A map from the identifiers of the sets to values, enumerated in the order of the keys of a JavaScript object, as the
/// layout of venn.js does: the keys that are array indices first, in increasing order, then the other keys in the order
/// they were added. Replacing a value keeps the position of its key.
/// </summary>
internal sealed class VennKeyedMap<T>
{
    private readonly Dictionary<string, T> _values = new(StringComparer.Ordinal);
    private readonly List<string> _order = [];
    private string[]? _keys;

    public int Count => _values.Count;

    public T this[string key]
    {
        get => _values[key];
        set
        {
            if (!_values.ContainsKey(key))
            {
                _order.Add(key);
                _keys = null;
            }

            _values[key] = value;
        }
    }

    /// <summary>Gets the keys in the order of a JavaScript object.</summary>
    public IReadOnlyList<string> Keys => _keys ??= GetKeys();

    public bool ContainsKey(string key) => _values.ContainsKey(key);

    public bool TryGetValue(string key, out T value) => _values.TryGetValue(key, out value!);

    private string[] GetKeys()
    {
        var indices = new List<(uint Index, string Key)>();
        var others = new List<string>();
        foreach (var key in _order)
        {
            if (TryGetArrayIndex(key, out var index))
            {
                indices.Add((index, key));
            }
            else
            {
                others.Add(key);
            }
        }

        indices.Sort((a, b) => a.Index.CompareTo(b.Index));
        var result = new string[_order.Count];
        var i = 0;
        foreach (var (_, key) in indices)
        {
            result[i++] = key;
        }

        foreach (var key in others)
        {
            result[i++] = key;
        }

        return result;
    }

    /// <summary>Indicates whether a key is an array index for JavaScript: the canonical decimal form of an integer below 2^32 - 1.</summary>
    private static bool TryGetArrayIndex(string key, out uint index)
    {
        index = 0;
        if (key.Length is 0 or > 10 || (key.Length > 1 && key[0] == '0'))
            return false;

        ulong value = 0;
        foreach (var c in key)
        {
            if (!char.IsAsciiDigit(c))
                return false;

            value = (value * 10) + (ulong)(c - '0');
        }

        if (value >= uint.MaxValue)
            return false;

        index = (uint)value;
        return true;
    }
}
