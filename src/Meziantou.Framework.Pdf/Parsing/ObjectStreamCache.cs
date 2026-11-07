using System.Diagnostics.CodeAnalysis;

namespace Meziantou.Framework.Pdf.Parsing;

/// <summary>
/// The decoded object streams the reader keeps, bounded by their number and by their decoded bytes. When a stream does not
/// fit, streams are evicted one at a time, chosen at random (with a fixed seed, so that reading a file is deterministic)
/// among those other than the stream just added: accesses that cycle over more streams than the capacity still find most
/// of them, where evicting every stream or the least recently used one would decode each stream again.
/// </summary>
internal sealed class ObjectStreamCache(int maxCount, long maxBytes)
{
    private readonly Dictionary<int, int> _indexes = [];
    private readonly List<(int Number, ObjectStreamData Data)> _entries = [];
    private long _bytes;
    private uint _random = 0x9E3779B9;

    public int Count => _entries.Count;

    public long Bytes => _bytes;

    public bool TryGetValue(int number, [NotNullWhen(true)] out ObjectStreamData? data)
    {
        if (_indexes.TryGetValue(number, out var index))
        {
            data = _entries[index].Data;
            return true;
        }

        data = null;
        return false;
    }

    public void Add(int number, ObjectStreamData data)
    {
        Remove(number);
        while (_entries.Count > 0 && (_entries.Count >= maxCount || _bytes + data.Data.Length > maxBytes))
        {
            RemoveAt((int)(NextRandom() % (uint)_entries.Count));
        }

        _indexes.Add(number, _entries.Count);
        _entries.Add((number, data));
        _bytes += data.Data.Length;
    }

    public void Clear()
    {
        _indexes.Clear();
        _entries.Clear();
        _bytes = 0;
    }

    private void Remove(int number)
    {
        if (_indexes.TryGetValue(number, out var index))
        {
            RemoveAt(index);
        }
    }

    private void RemoveAt(int index)
    {
        var removed = _entries[index];
        _indexes.Remove(removed.Number);
        _bytes -= removed.Data.Data.Length;

        // The last entry takes the place of the removed one
        var last = _entries.Count - 1;
        if (index != last)
        {
            var moved = _entries[last];
            _entries[index] = moved;
            _indexes[moved.Number] = index;
        }

        _entries.RemoveAt(last);
    }

    /// <summary>Gets the next value of a xorshift generator.</summary>
    private uint NextRandom()
    {
        var x = _random;
        x ^= x << 13;
        x ^= x >> 17;
        x ^= x << 5;
        _random = x;
        return x;
    }
}
