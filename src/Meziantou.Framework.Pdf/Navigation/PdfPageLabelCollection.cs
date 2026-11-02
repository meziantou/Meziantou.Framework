using System.Collections;
using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>
/// The page labels of a document (ISO 32000-2, 12.4.2): the <c>/PageLabels</c> number tree of the catalog, whose keys
/// are the indexes of the first pages of the ranges. The changes are written to the catalog at once.
/// </summary>
public sealed class PdfPageLabelCollection : IReadOnlyCollection<KeyValuePair<int, PdfPageLabel>>
{
    private const int MaxDepth = 32;

    private static readonly PdfName PageLabelsName = PdfName.Create("PageLabels");
    private static readonly PdfName PageLabelName = PdfName.Create("PageLabel");
    private static readonly PdfName NumsName = PdfName.Create("Nums");
    private static readonly PdfName StyleName = PdfName.Create("S");
    private static readonly PdfName PrefixName = PdfName.Create("P");
    private static readonly PdfName StartName = PdfName.Create("St");

    private readonly PdfDocument _document;

    internal PdfPageLabelCollection(PdfDocument document)
    {
        _document = document;
    }

    /// <summary>Gets the number of ranges.</summary>
    public int Count => Read().Count;

    /// <summary>Gets the label of a page: the label of the range it is in, or <see langword="null"/> when the document has no page labels (viewers then show the page number).</summary>
    /// <param name="pageIndex">The index of the page, from 0.</param>
    /// <returns>The label.</returns>
    public string? GetLabel(int pageIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        var ranges = Read();
        if (ranges.Count == 0)
            return null;

        // A page before the first range is not labelled by the tree (the first key shall be 0): its number
        KeyValuePair<int, PdfPageLabel>? range = null;
        foreach (var entry in ranges)
        {
            if (entry.Key > pageIndex)
                break;

            range = entry;
        }

        return range is { } found ? found.Value.GetLabel(pageIndex - found.Key) : (pageIndex + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Starts a range of pages: the page and the pages after it, until the next range, are labelled by <paramref name="label"/>.</summary>
    /// <param name="pageIndex">The index of the first page of the range, from 0.</param>
    /// <param name="label">The label.</param>
    public void Set(int pageIndex, PdfPageLabel label)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        ArgumentNullException.ThrowIfNull(label);
        var ranges = Read();
        ranges[pageIndex] = label;
        Write(ranges);
    }

    /// <summary>Removes the range that starts at a page.</summary>
    /// <param name="pageIndex">The index of the first page of the range.</param>
    /// <returns><see langword="true"/> when there was a range.</returns>
    public bool Remove(int pageIndex)
    {
        var ranges = Read();
        if (!ranges.Remove(pageIndex))
            return false;

        Write(ranges);
        return true;
    }

    /// <summary>Removes every range: the document has no page labels anymore.</summary>
    public void Clear() => _document.Catalog.Remove(PageLabelsName);

    /// <summary>Returns an enumerator over the ranges, by index of their first page.</summary>
    /// <returns>The enumerator.</returns>
    public IEnumerator<KeyValuePair<int, PdfPageLabel>> GetEnumerator() => Read().GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private SortedDictionary<int, PdfPageLabel> Read()
    {
        var result = new SortedDictionary<int, PdfPageLabel>();
        if (_document.Catalog.Get<PdfDictionary>(PageLabelsName) is { } root)
        {
            ReadNode(root, result, new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance), depth: 0);
        }

        return result;
    }

    private static void ReadNode(PdfDictionary node, SortedDictionary<int, PdfPageLabel> result, HashSet<PdfDictionary> visited, int depth)
    {
        if (depth > MaxDepth || !visited.Add(node))
            return;

        if (node.Get<PdfArray>(NumsName) is { } nums)
        {
            for (var i = 0; i + 1 < nums.Count; i += 2)
            {
                if (nums.TryGetResolved<PdfInteger>(i, out var key) && key.Value is >= 0 and <= int.MaxValue && nums.GetResolved(i + 1) is PdfDictionary label)
                {
                    result[(int)key.Value] = ReadLabel(label);
                }
            }
        }

        if (node.Get<PdfArray>(PdfNames.Kids) is { } kids)
        {
            foreach (var kid in kids)
            {
                if (PdfReference.ResolveIfReference(kid) is PdfDictionary child)
                {
                    ReadNode(child, result, visited, depth + 1);
                }
            }
        }
    }

    private static PdfPageLabel ReadLabel(PdfDictionary dictionary)
    {
        var style = dictionary.GetName(StyleName)?.Value switch
        {
            "D" => PdfPageLabelStyle.Decimal,
            "R" => PdfPageLabelStyle.UppercaseRoman,
            "r" => PdfPageLabelStyle.LowercaseRoman,
            "A" => PdfPageLabelStyle.UppercaseLetters,
            "a" => PdfPageLabelStyle.LowercaseLetters,
            _ => PdfPageLabelStyle.None,
        };
        var start = dictionary.GetInteger(StartName, 1);
        return new PdfPageLabel(style, dictionary.GetText(PrefixName), (int)Math.Clamp(start, 1, int.MaxValue));
    }

    /// <summary>Writes the ranges as one node (7.9.7).</summary>
    private void Write(SortedDictionary<int, PdfPageLabel> ranges)
    {
        if (ranges.Count == 0)
        {
            Clear();
            return;
        }

        var nums = new PdfArray();
        foreach (var (index, label) in ranges)
        {
            var dictionary = new PdfDictionary { [PdfNames.Type] = PageLabelName };
            var style = label.Style switch
            {
                PdfPageLabelStyle.Decimal => "D",
                PdfPageLabelStyle.UppercaseRoman => "R",
                PdfPageLabelStyle.LowercaseRoman => "r",
                PdfPageLabelStyle.UppercaseLetters => "A",
                PdfPageLabelStyle.LowercaseLetters => "a",
                _ => null,
            };
            if (style is not null)
            {
                dictionary[StyleName] = PdfName.Create(style);
            }

            if (label.Prefix is not null)
            {
                dictionary[PrefixName] = PdfString.FromText(label.Prefix);
            }

            if (label.StartNumber != 1)
            {
                dictionary[StartName] = PdfInteger.Get(label.StartNumber);
            }

            nums.Add(PdfInteger.Get(index));
            nums.Add(dictionary);
        }

        if (_document.Catalog[PageLabelsName] is PdfReference reference && reference.Resolve() is PdfDictionary existing)
        {
            existing.Remove(PdfNames.Kids);
            existing.Remove(PdfNames.Limits);
            existing[NumsName] = nums;
        }
        else
        {
            _document.Catalog[PageLabelsName] = _document.Objects.Add(new PdfDictionary { [NumsName] = nums });
        }
    }
}
