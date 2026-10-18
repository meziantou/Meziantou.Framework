using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Css.Selectors;

/// <summary>Caches the parsed selectors of the DOM APIs (querySelector, matches, closest...). Parsed selectors are immutable, so the cache is shared by all threads.</summary>
internal static class SelectorCache
{
    private const int Capacity = 512;

    private static readonly Lock LockObject = new();
    private static readonly Dictionary<string, LinkedListNode<(string Key, SelectorList? Value)>> Entries = new(StringComparer.Ordinal);
    private static readonly LinkedList<(string Key, SelectorList? Value)> Lru = new();

    /// <summary>Parses a selector for the DOM APIs; throws a "SyntaxError" DOMException when it is invalid (https://dom.spec.whatwg.org/#scope-match-a-selectors-string).</summary>
    public static SelectorList Parse(string selectors)
    {
        ArgumentNullException.ThrowIfNull(selectors);
        return TryParse(selectors) ?? throw DomException.Syntax($"'{selectors}' is not a valid selector");
    }

    /// <summary>Parses a selector for the DOM APIs; returns null when it is invalid.</summary>
    public static SelectorList? TryParse(string selectors)
    {
        lock (LockObject)
        {
            if (Entries.TryGetValue(selectors, out var node))
            {
                Lru.Remove(node);
                Lru.AddFirst(node);
                return node.Value.Value;
            }
        }

        var result = SelectorParser.ParseSelector(selectors);
        lock (LockObject)
        {
            if (!Entries.ContainsKey(selectors))
            {
                var node = Lru.AddFirst((selectors, result));
                Entries[selectors] = node;
                if (Entries.Count > Capacity)
                {
                    var last = Lru.Last!;
                    Lru.RemoveLast();
                    Entries.Remove(last.Value.Key);
                }
            }
        }

        return result;
    }
}
