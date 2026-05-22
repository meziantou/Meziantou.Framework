using System.Collections;

namespace Meziantou.Framework.Collections;

public sealed class Trie<TValue> : IEnumerable<KeyValuePair<string, TValue>>
{
    private readonly bool _ignoreCase;
    private readonly TrieNode _root = new();

    public Trie()
        : this(ignoreCase: false)
    {
    }

    public Trie(bool ignoreCase)
    {
        _ignoreCase = ignoreCase;
    }

    public int Count { get; private set; }
    public bool IgnoreCase => _ignoreCase;

    public void Add(string key, TValue value)
    {
        ArgumentNullException.ThrowIfNull(key);

        var node = GetOrCreateNode(key);
        if (node.HasValue)
            throw new ArgumentException("An item with the same key has already been added.", nameof(key));

        node.SetValue(key, value);
        Count++;
    }

    public bool Remove(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        var nodes = new TrieNode[key.Length + 1];
        var edges = key.Length is 0 ? [] : new char[key.Length];
        nodes[0] = _root;

        var node = _root;
        for (var i = 0; i < key.Length; i++)
        {
            var normalized = NormalizeChar(key[i]);
            if (!node.TryGetChild(normalized, out var child))
                return false;

            edges[i] = normalized;
            node = child;
            nodes[i + 1] = node;
        }

        if (!node.HasValue)
            return false;

        node.ClearValue();
        Count--;

        for (var i = key.Length - 1; i >= 0; i--)
        {
            var child = nodes[i + 1];
            if (child.HasValue || child.HasChildren)
                break;

            nodes[i].RemoveChild(edges[i]);
        }

        return true;
    }

    public bool TryGetValue(string key, [MaybeNullWhen(false)] out TValue value)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (TryGetNode(key, out var node) && node.HasValue)
        {
            value = node.Value;
            return true;
        }

        value = default;
        return false;
    }

    public bool ContainsKey(string key)
    {
        return TryGetValue(key, out _);
    }

    public IEnumerable<KeyValuePair<string, TValue>> StartsWith(string prefix)
    {
        ArgumentNullException.ThrowIfNull(prefix);

        if (!TryGetNode(prefix, out var node))
            return [];

        return EnumerateFrom(node);
    }

    public IEnumerator<KeyValuePair<string, TValue>> GetEnumerator() => StartsWith(string.Empty).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private TrieNode GetOrCreateNode(string key)
    {
        var node = _root;
        foreach (var c in key)
        {
            node = node.GetOrAddChild(NormalizeChar(c));
        }

        return node;
    }

    private bool TryGetNode(string key, out TrieNode node)
    {
        node = _root;
        foreach (var c in key)
        {
            if (!node.TryGetChild(NormalizeChar(c), out var child))
                return false;

            node = child;
        }

        return true;
    }

    private char NormalizeChar(char c)
    {
        return _ignoreCase ? char.ToUpperInvariant(c) : c;
    }

    private static IEnumerable<KeyValuePair<string, TValue>> EnumerateFrom(TrieNode node)
    {
        if (node.HasValue)
            yield return new KeyValuePair<string, TValue>(node.Key!, node.Value);

        if (node.Children is null)
            yield break;

        foreach (var child in node.Children.Values)
        {
            foreach (var item in EnumerateFrom(child))
            {
                yield return item;
            }
        }
    }

    private sealed class TrieNode
    {
        private Dictionary<char, TrieNode>? _children;

        public bool HasValue { get; private set; }
        public TValue Value { get; private set; } = default!;
        public string? Key { get; private set; }
        public bool HasChildren => _children is { Count: > 0 };
        public Dictionary<char, TrieNode>? Children => _children;

        public TrieNode GetOrAddChild(char c)
        {
            _children ??= [];

            if (!_children.TryGetValue(c, out var child))
            {
                child = new TrieNode();
                _children.Add(c, child);
            }

            return child;
        }

        public bool TryGetChild(char c, [NotNullWhen(true)] out TrieNode? child)
        {
            if (_children is not null && _children.TryGetValue(c, out child))
                return child is not null;

            child = null;
            return false;
        }

        public void RemoveChild(char c)
        {
            if (_children is null)
                return;

            _children.Remove(c);
            if (_children.Count is 0)
            {
                _children = null;
            }
        }

        public void SetValue(string key, TValue value)
        {
            HasValue = true;
            Key = key;
            Value = value;
        }

        public void ClearValue()
        {
            HasValue = false;
            Key = null;
            Value = default!;
        }
    }
}
