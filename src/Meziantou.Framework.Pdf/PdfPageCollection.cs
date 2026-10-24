using System.Collections;
using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>The pages of a document, in the order of the page tree (ISO 32000-2, 7.7.3).</summary>
/// <remarks>
/// The page tree of a file is kept: a page is added to the last node of the tree, and the <c>/Count</c> entries of the
/// nodes above a change are updated. A node that is reached twice, or that is not a dictionary, is skipped.
/// </remarks>
public sealed class PdfPageCollection : IReadOnlyList<PdfPage>
{
    private const int MaxTreeDepth = 256;

    private readonly PdfDocument _document;
    private readonly List<PdfPage> _pages = [];

    internal PdfPageCollection(PdfDocument document)
    {
        _document = document;
        if (document.Catalog.Get<PdfDictionary>(PdfNames.Pages) is { } root)
        {
            var visited = new HashSet<PdfObject>(ReferenceEqualityComparer.Instance);
            Collect(root, document.Catalog[PdfNames.Pages] as PdfReference, visited, depth: 0);
        }
    }

    /// <summary>Gets the number of pages.</summary>
    public int Count => _pages.Count;

    /// <summary>Gets a page.</summary>
    /// <param name="index">The index of the page, from 0.</param>
    /// <returns>The page.</returns>
    public PdfPage this[int index] => _pages[index];

    /// <summary>Adds a page at the end of the document.</summary>
    /// <param name="size">The size of the page (its media box).</param>
    /// <returns>The new page.</returns>
    public PdfPage Add(PdfSize size) => Insert(_pages.Count, size);

    /// <summary>Inserts a page.</summary>
    /// <param name="index">The index of the new page, from 0 to <see cref="Count"/>.</param>
    /// <param name="size">The size of the page (its media box).</param>
    /// <returns>The new page.</returns>
    public PdfPage Insert(int index, PdfSize size)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index, _pages.Count);

        PdfReference parentReference;
        PdfDictionary parent;
        int position;
        if (index < _pages.Count)
        {
            // Before the page at the index, in its node
            var next = _pages[index];
            parentReference = next.Dictionary[PdfNames.Parent] as PdfReference ?? throw new InvalidOperationException("The page has no parent node");
            parent = (PdfDictionary)parentReference.Resolve();
            position = Kids(parent).IndexOf(next.Reference);
        }
        else if (_pages.Count > 0 && _pages[^1].Dictionary[PdfNames.Parent] is PdfReference lastParent && lastParent.Resolve() is PdfDictionary lastParentDictionary)
        {
            // After the last page, in its node
            parentReference = lastParent;
            parent = lastParentDictionary;
            position = Kids(parent).IndexOf(_pages[^1].Reference) + 1;
        }
        else
        {
            parentReference = GetOrCreateRoot();
            parent = (PdfDictionary)parentReference.Resolve();
            position = Kids(parent).Count;
        }

        var dictionary = new PdfDictionary
        {
            [PdfNames.Type] = PdfNames.Page,
            [PdfNames.Parent] = parentReference,
            [PdfNames.MediaBox] = new PdfRectangle(0, 0, size.Width, size.Height).ToArray(),
            [PdfNames.Resources] = new PdfDictionary(),
        };
        var reference = _document.Objects.Add(dictionary);
        var kids = Kids(parent);
        kids.Insert(Math.Clamp(position, 0, kids.Count), reference);
        UpdateCounts(parent, delta: 1);

        var page = new PdfPage(_document, reference, dictionary);
        _pages.Insert(index, page);
        return page;
    }

    /// <summary>Removes a page from the page tree. Its objects are not written by a full save anymore.</summary>
    /// <param name="index">The index of the page.</param>
    public void RemoveAt(int index)
    {
        var page = _pages[index];
        if (page.Dictionary.Get<PdfDictionary>(PdfNames.Parent) is { } parent)
        {
            var kids = Kids(parent);
            var position = kids.IndexOf(page.Reference);
            if (position >= 0)
            {
                kids.RemoveAt(position);
                UpdateCounts(parent, delta: -1);
            }
        }

        _pages.RemoveAt(index);
    }

    /// <summary>Gets the index of a page.</summary>
    /// <param name="page">The page.</param>
    /// <returns>The index, or -1 when the page is not in the document.</returns>
    public int IndexOf(PdfPage page) => _pages.IndexOf(page);

    /// <summary>Finds the page of a page object.</summary>
    internal PdfPage? Find(PdfReference reference)
    {
        foreach (var page in _pages)
        {
            if (page.Reference.Equals(reference))
                return page;
        }

        return null;
    }

    /// <summary>Returns an enumerator over the pages.</summary>
    /// <returns>The enumerator.</returns>
    public IEnumerator<PdfPage> GetEnumerator() => _pages.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private void Collect(PdfDictionary node, PdfReference? reference, HashSet<PdfObject> visited, int depth)
    {
        if (!visited.Add(node) || depth > MaxTreeDepth)
            return;

        if (node.Get<PdfArray>(PdfNames.Kids) is { } kids && !node.IsType(PdfNames.Page))
        {
            foreach (var kid in kids)
            {
                if (PdfReference.ResolveIfReference(kid) is PdfDictionary child)
                {
                    Collect(child, kid as PdfReference, visited, depth + 1);
                }
            }

            return;
        }

        // A leaf: a page, even without /Type (some producers omit it). A page must be an indirect object.
        if (reference is not null && (node.IsType(PdfNames.Page) || !node.IsType(PdfNames.Pages)))
        {
            _pages.Add(new PdfPage(_document, reference, node));
        }
    }

    private PdfReference GetOrCreateRoot()
    {
        if (_document.Catalog[PdfNames.Pages] is PdfReference existing && existing.Resolve() is PdfDictionary)
            return existing;

        var root = new PdfDictionary
        {
            [PdfNames.Type] = PdfNames.Pages,
            [PdfNames.Kids] = new PdfArray(),
            [PdfNames.Count] = PdfInteger.Get(0),
        };
        var reference = _document.Objects.Add(root);
        _document.Catalog[PdfNames.Pages] = reference;
        return reference;
    }

    private static PdfArray Kids(PdfDictionary node)
    {
        if (node.Get<PdfArray>(PdfNames.Kids) is { } kids)
            return kids;

        kids = new PdfArray();
        node[PdfNames.Kids] = kids;
        return kids;
    }

    private static void UpdateCounts(PdfDictionary node, int delta)
    {
        var visited = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        for (var current = node; current is not null && visited.Add(current) && visited.Count <= MaxTreeDepth; current = current.Get<PdfDictionary>(PdfNames.Parent))
        {
            current[PdfNames.Count] = PdfInteger.Get(Math.Max(0, current.GetInteger(PdfNames.Count, 0) + delta));
        }
    }
}
