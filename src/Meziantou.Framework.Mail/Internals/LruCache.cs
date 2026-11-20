namespace Meziantou.Framework.Mail.Internals;

/// <summary>A small cache that evicts the least recently used entry.</summary>
internal sealed class LruCache<TKey, TValue>
    where TKey : notnull
{
    private readonly int _capacity;
    private readonly Dictionary<TKey, LinkedListNode<(TKey Key, TValue Value)>> _entries = [];
    private readonly LinkedList<(TKey Key, TValue Value)> _order = new();
    private readonly Lock _lock = new();

    public LruCache(int capacity)
    {
        _capacity = capacity;
    }

    public bool TryGetValue(TKey key, [System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out TValue value)
    {
        lock (_lock)
        {
            if (_entries.TryGetValue(key, out var node))
            {
                _order.Remove(node);
                _order.AddFirst(node);
                value = node.Value.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    public void Add(TKey key, TValue value)
    {
        lock (_lock)
        {
            if (_entries.TryGetValue(key, out var existing))
            {
                _order.Remove(existing);
                _entries.Remove(key);
            }

            var node = _order.AddFirst((key, value));
            _entries[key] = node;
            while (_entries.Count > _capacity && _order.Last is { } last)
            {
                _order.RemoveLast();
                _entries.Remove(last.Value.Key);
            }
        }
    }
}
