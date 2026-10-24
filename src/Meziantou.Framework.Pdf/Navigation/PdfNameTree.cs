using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>Looks up a key in a name tree (ISO 32000-2, 7.9.6).</summary>
internal static class PdfNameTree
{
    private const int MaxDepth = 32;

    public static PdfObject? Find(PdfDictionary root, string key)
    {
        var visited = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        return Find(root, key, visited, depth: 0);
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
}
