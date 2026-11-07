using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.Pdf.Text;

namespace Meziantou.Framework.Pdf;

/// <summary>Reads and writes name trees (ISO 32000-2, 7.9.6).</summary>
internal static class PdfNameTree
{
    private const int MaxDepth = 32;

    /// <summary>
    /// Finds the value of a text key. The keys are byte strings (7.9.6): the text is looked up in each of its encodings as
    /// a text string (7.9.2.2), PDFDocEncoding, UTF-16BE and UTF-8, since the tree is sorted by the bytes of its keys.
    /// </summary>
    public static PdfObject? Find(PdfDictionary root, string key)
    {
        foreach (var candidate in GetEncodings(key))
        {
            if (Find(root, candidate) is { } found)
                return found;
        }

        return null;
    }

    /// <summary>Finds the value of a string key: the key with the same bytes, or, when there is none, the key with the same text in another encoding.</summary>
    public static PdfObject? Find(PdfDictionary root, PdfString key) => Find(root, key.Span) ?? Find(root, key.ToText());

    /// <summary>Finds the value of the key that has these bytes. The limits of the nodes and the keys of the leaves are compared as bytes (7.9.6).</summary>
    public static PdfObject? Find(PdfDictionary root, ReadOnlySpan<byte> key)
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

    private static PdfObject? Find(PdfDictionary node, ReadOnlySpan<byte> key, HashSet<PdfDictionary> visited, int depth)
    {
        if (depth > MaxDepth || !visited.Add(node))
            return null;

        if (node.Get<PdfArray>(PdfNames.Names) is { } names)
        {
            // The keys are sorted (7.9.6): a binary search finds them; a tree that is not sorted is searched linearly
            var low = 0;
            var high = (names.Count / 2) - 1;
            while (low <= high)
            {
                var middle = low + ((high - low) / 2);
                if (!names.TryGetResolved<PdfString>(middle * 2, out var name))
                    break;

                var comparison = name.Span.SequenceCompareTo(key);
                if (comparison == 0)
                    return names.GetResolved((middle * 2) + 1);

                if (comparison < 0)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle - 1;
                }
            }

            for (var i = 0; i + 1 < names.Count; i += 2)
            {
                if (names.TryGetResolved<PdfString>(i, out var name) && name.Span.SequenceEqual(key))
                    return names.GetResolved(i + 1);
            }
        }

        if (node.Get<PdfArray>(PdfNames.Kids) is { } kids)
        {
            foreach (var kid in kids)
            {
                if (PdfReference.ResolveIfReference(kid) is PdfDictionary child)
                {
                    // The limits of a node bound its keys, as bytes; a node without limits is searched
                    if (child.Get<PdfArray>(PdfNames.Limits) is { Count: 2 } limits && limits.TryGetResolved<PdfString>(0, out var lowest) && limits.TryGetResolved<PdfString>(1, out var highest)
                        && (key.SequenceCompareTo(lowest.Span) < 0 || key.SequenceCompareTo(highest.Span) > 0))
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

    /// <summary>Gets the encodings of a text string (7.9.2.2): PDFDocEncoding when the text can be, UTF-16BE and UTF-8 with their byte order marks.</summary>
    private static IEnumerable<byte[]> GetEncodings(string text)
    {
        var pdfDoc = new byte[text.Length];
        var encodable = true;
        for (var i = 0; i < text.Length; i++)
        {
            if (!PdfTextStringEncoding.TryEncodePdfDocEncoding(text[i], out pdfDoc[i]))
            {
                encodable = false;
                break;
            }
        }

        if (encodable)
            yield return pdfDoc;

        var utf16 = new byte[2 + (text.Length * 2)];
        utf16[0] = 0xFE;
        utf16[1] = 0xFF;
        System.Text.Encoding.BigEndianUnicode.GetBytes(text, utf16.AsSpan(2));
        yield return utf16;

        byte[] utf8 = [0xEF, 0xBB, 0xBF, .. System.Text.Encoding.UTF8.GetBytes(text)];
        yield return utf8;
    }

    private sealed class ByteOrderComparer : IComparer<ReadOnlyMemory<byte>>
    {
        public static ByteOrderComparer Instance { get; } = new();

        public int Compare(ReadOnlyMemory<byte> x, ReadOnlyMemory<byte> y) => x.Span.SequenceCompareTo(y.Span);
    }
}
