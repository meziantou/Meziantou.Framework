using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>Reads and writes name trees (ISO 32000-2, 7.9.6).</summary>
internal static class PdfNameTree
{
    private const int MaxDepth = 32;

    public static PdfObject? Find(PdfDictionary root, string key)
    {
        var visited = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        return Find(root, key, visited, depth: 0);
    }

    /// <summary>Gets the entries of a name tree, in the order of the tree.</summary>
    public static List<(PdfString Key, PdfObject Value)> GetEntries(PdfDictionary root)
    {
        var result = new List<(PdfString Key, PdfObject Value)>();
        var visited = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        AddEntries(root, result, visited, depth: 0);
        return result;
    }

    /// <summary>
    /// Replaces the content of a name tree by one root node: the <c>/Names</c> array of the entries, sorted by the bytes
    /// of their keys (7.9.6: the keys are sorted lexically, as bytes).
    /// </summary>
    public static void SetEntries(PdfDictionary root, IEnumerable<(PdfString Key, PdfObject Value)> entries)
    {
        var names = new PdfArray();
        foreach (var (key, value) in entries.OrderBy(entry => entry.Key.Bytes, ByteOrderComparer.Instance))
        {
            names.Add(key);
            names.Add(value);
        }

        root.Remove(PdfNames.Kids);
        root.Remove(PdfNames.Limits);
        root[PdfNames.Names] = names;
    }

    private static void AddEntries(PdfDictionary node, List<(PdfString Key, PdfObject Value)> result, HashSet<PdfDictionary> visited, int depth)
    {
        if (depth > MaxDepth || !visited.Add(node))
            return;

        if (node.Get<PdfArray>(PdfNames.Names) is { } names)
        {
            for (var i = 0; i + 1 < names.Count; i += 2)
            {
                if (names.TryGetResolved<PdfString>(i, out var name))
                {
                    result.Add((name, names[i + 1]));
                }
            }
        }

        if (node.Get<PdfArray>(PdfNames.Kids) is { } kids)
        {
            foreach (var kid in kids)
            {
                if (PdfReference.ResolveIfReference(kid) is PdfDictionary child)
                {
                    AddEntries(child, result, visited, depth + 1);
                }
            }
        }
    }

    private static PdfObject? Find(PdfDictionary node, string key, HashSet<PdfDictionary> visited, int depth)
    {
        if (depth > MaxDepth || !visited.Add(node))
            return null;

        if (node.Get<PdfArray>(PdfNames.Names) is { } names)
        {
            for (var i = 0; i + 1 < names.Count; i += 2)
            {
                if (names.TryGetResolved<PdfString>(i, out var name) && string.Equals(name.ToText(), key, StringComparison.Ordinal))
                    return names.GetResolved(i + 1);
            }
        }

        if (node.Get<PdfArray>(PdfNames.Kids) is { } kids)
        {
            foreach (var kid in kids)
            {
                if (PdfReference.ResolveIfReference(kid) is PdfDictionary child)
                {
                    // The limits of a node bound its keys; a node without limits is searched
                    if (child.Get<PdfArray>(PdfNames.Limits) is { Count: 2 } limits && limits.TryGetResolved<PdfString>(0, out var low) && limits.TryGetResolved<PdfString>(1, out var high)
                        && (string.CompareOrdinal(key, low.ToText()) < 0 || string.CompareOrdinal(key, high.ToText()) > 0))
                    {
                        continue;
                    }

                    if (Find(child, key, visited, depth + 1) is { } found)
                        return found;
                }
            }
        }

        return null;
    }

    private sealed class ByteOrderComparer : IComparer<ReadOnlyMemory<byte>>
    {
        public static ByteOrderComparer Instance { get; } = new();

        public int Compare(ReadOnlyMemory<byte> x, ReadOnlyMemory<byte> y) => x.Span.SequenceCompareTo(y.Span);
    }
}
