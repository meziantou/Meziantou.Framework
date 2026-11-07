using System.Collections;
using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>The pages of a document, in the order of the page tree (ISO 32000-2, 7.7.3).</summary>
/// <remarks>
/// The page tree of a file is kept: a page is added to the node of the page it is inserted before (or of the last page),
/// and the <c>/Count</c> entries of the nodes above a change are updated. A node that is reached twice, or that is not a
/// dictionary, is skipped. A page that changes of node keeps its attributes: the attributes it inherited (resources,
/// media box, crop box, rotation, 7.7.3.4) are copied to it first.
/// </remarks>
public sealed class PdfPageCollection : IReadOnlyList<PdfPage>
{
    private const int MaxTreeDepth = 256;

    private static readonly PdfName StructParentsName = PdfName.Create("StructParents");
    private static readonly PdfName StructParentName = PdfName.Create("StructParent");
    private static readonly PdfName WidgetName = PdfName.Create("Widget");
    private static readonly PdfName PopupName = PdfName.Create("Popup");
    private static readonly PdfName NMName = PdfName.Create("NM");
    private static readonly PdfName BName = PdfName.Create("B");
    private static readonly PdfName[] InheritableKeys = [PdfNames.Resources, PdfNames.MediaBox, PdfNames.CropBox, PdfNames.Rotate];

    private readonly PdfDocument _document;
    private readonly List<PdfPage> _pages = [];
    private readonly Dictionary<PdfReference, PdfPage> _pagesByReference = [];

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
    /// <returns>The new page. It does not inherit the rotation nor the crop box of the node of the page tree it is added to.</returns>
    public PdfPage Insert(int index, PdfSize size)
    {
        CheckInsertionIndex(index);
        var dictionary = new PdfDictionary
        {
            [PdfNames.Type] = PdfNames.Page,
            [PdfNames.Parent] = PdfNull.Instance,
            [PdfNames.MediaBox] = new PdfRectangle(0, 0, size.Width, size.Height).ToArray(),
            [PdfNames.Resources] = new PdfDictionary(),
        };
        return InsertDictionary(index, dictionary);
    }

    /// <summary>Adds a copy of a page at the end of the document. The page can be a page of another document.</summary>
    /// <param name="page">The page to copy.</param>
    /// <returns>The copy.</returns>
    /// <remarks>See <see cref="Insert(int, PdfPage)"/>.</remarks>
    public PdfPage Add(PdfPage page) => Insert(_pages.Count, page);

    /// <summary>Inserts a copy of a page. The page can be a page of this document (a duplicate) or of another document (an import, to merge documents).</summary>
    /// <param name="index">The index of the copy, from 0 to <see cref="Count"/>.</param>
    /// <param name="page">The page to copy.</param>
    /// <returns>The copy.</returns>
    /// <remarks>
    /// <para>
    /// The copy has the attributes of the page, the ones it inherited included, its content and its annotations, except
    /// the form fields (widgets) and the pop-up windows. It is not tagged: its <c>/StructParents</c> and the structure
    /// of its annotations are left out, since the structure tree belongs to the document of the page.
    /// </para>
    /// <para>
    /// A page of this document shares its content streams and its resources with the copy. A page of another document
    /// is copied with the objects it uses, renumbered: the objects shared by several imported pages of a document, such
    /// as the fonts, are copied once. The document must stay open until the copy is saved. The destinations of the links
    /// go to the copies of their pages: a destination to a page that is not imported yet goes to it when it is, and has
    /// no page (null) otherwise; a named destination is replaced by the destination it names.
    /// </para>
    /// </remarks>
    public PdfPage Insert(int index, PdfPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        CheckInsertionIndex(index);
        return page.Document == _document ? Duplicate(index, page) : Import(index, page);
    }

    /// <summary>Moves a page to another position. Its object does not change: the destinations, links and structure elements that refer to it stay valid.</summary>
    /// <param name="fromIndex">The index of the page.</param>
    /// <param name="toIndex">The index of the page after the move.</param>
    public void Move(int fromIndex, int toIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fromIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(fromIndex, _pages.Count);
        ArgumentOutOfRangeException.ThrowIfNegative(toIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(toIndex, _pages.Count);
        if (fromIndex == toIndex)
            return;

        var page = _pages[fromIndex];
        MaterializeInherited(page);
        Detach(page);
        _pages.RemoveAt(fromIndex);

        var (parentReference, parent, position) = GetInsertionPoint(toIndex);
        Attach(page.Dictionary, page.Reference, parentReference, parent, position);
        _pages.Insert(toIndex, page);
    }

    /// <summary>Inserts a copy of a page of the document after it.</summary>
    /// <param name="index">The index of the page.</param>
    /// <returns>The copy, at <paramref name="index"/> + 1.</returns>
    /// <remarks>See <see cref="Insert(int, PdfPage)"/>.</remarks>
    public PdfPage Duplicate(int index) => Insert(index + 1, this[index]);

    /// <summary>
    /// Removes a page from the page tree. Its objects are not written by a full save anymore: the references of the
    /// document to the page are removed too. The outline items and the links that go to the page lose their destination
    /// (the items and the links stay), the named destinations of the page and the <c>/OpenAction</c> that goes to it are
    /// removed, and so are its marked content and its annotations in the structure tree.
    /// </summary>
    /// <param name="index">The index of the page.</param>
    public void RemoveAt(int index)
    {
        var page = _pages[index];
        Detach(page);
        _pages.RemoveAt(index);
        _pagesByReference.Remove(page.Reference);
        PdfPageReferenceRemover.Remove(_document, page);
    }

    /// <summary>Gets the index of a page.</summary>
    /// <param name="page">The page.</param>
    /// <returns>The index, or -1 when the page is not in the document.</returns>
    public int IndexOf(PdfPage page) => _pages.IndexOf(page);

    /// <summary>Finds the page of a page object.</summary>
    internal PdfPage? Find(PdfReference reference) => _pagesByReference.GetValueOrDefault(reference);

    /// <summary>Returns an enumerator over the pages.</summary>
    /// <returns>The enumerator.</returns>
    public IEnumerator<PdfPage> GetEnumerator() => _pages.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private void CheckInsertionIndex(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index, _pages.Count);
    }

    private PdfPage InsertDictionary(int index, PdfDictionary dictionary)
    {
        var (parentReference, parent, position) = GetInsertionPoint(index);
        var reference = _document.Objects.Add(dictionary);
        Attach(dictionary, reference, parentReference, parent, position);
        var page = new PdfPage(_document, reference, dictionary);
        _pages.Insert(index, page);
        _pagesByReference[reference] = page;
        return page;
    }

    /// <summary>Gets the node and the position in its kids of a page inserted at an index: before the page at the index, after the last page, or in the root.</summary>
    private (PdfReference ParentReference, PdfDictionary Parent, int Position) GetInsertionPoint(int index)
    {
        if (index < _pages.Count)
        {
            var next = _pages[index];
            var parentReference = next.Dictionary[PdfNames.Parent] as PdfReference ?? throw new InvalidOperationException("The page has no parent node");
            var parent = (PdfDictionary)parentReference.Resolve();
            return (parentReference, parent, Kids(parent).IndexOf(next.Reference));
        }

        if (_pages.Count > 0 && _pages[^1].Dictionary[PdfNames.Parent] is PdfReference lastParent && lastParent.Resolve() is PdfDictionary lastParentDictionary)
            return (lastParent, lastParentDictionary, Kids(lastParentDictionary).IndexOf(_pages[^1].Reference) + 1);

        var root = GetOrCreateRoot();
        var rootDictionary = (PdfDictionary)root.Resolve();
        return (root, rootDictionary, Kids(rootDictionary).Count);
    }

    /// <summary>
    /// Adds a page to the kids of a node. The attributes the page does not have are not inherited from its new node: the
    /// rotation is 0, the crop box is the media box (7.7.3.4).
    /// </summary>
    private static void Attach(PdfDictionary dictionary, PdfReference reference, PdfReference parentReference, PdfDictionary parent, int position)
    {
        dictionary[PdfNames.Parent] = parentReference;
        if (!dictionary.ContainsKey(PdfNames.Rotate) && PdfPage.GetInherited(parent, PdfNames.Rotate) is PdfNumber { DoubleValue: not 0 })
        {
            dictionary[PdfNames.Rotate] = PdfInteger.Get(0);
        }

        if (!dictionary.ContainsKey(PdfNames.CropBox) && PdfPage.GetInherited(parent, PdfNames.CropBox) is not null && PdfRectangle.TryRead(dictionary.Get<PdfArray>(PdfNames.MediaBox), out var mediaBox))
        {
            dictionary[PdfNames.CropBox] = mediaBox.ToArray();
        }

        if (!dictionary.ContainsKey(PdfNames.Resources) && PdfPage.GetInherited(parent, PdfNames.Resources) is not null)
        {
            dictionary[PdfNames.Resources] = new PdfDictionary();
        }

        var kids = Kids(parent);
        kids.Insert(Math.Clamp(position, 0, kids.Count), reference);
        UpdateCounts(parent, delta: 1);
    }

    /// <summary>Removes a page from the kids of its node.</summary>
    private static void Detach(PdfPage page)
    {
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
    }

    /// <summary>Copies the attributes a page inherits to the page itself, so that it keeps them in another node (7.7.3.4).</summary>
    private static void MaterializeInherited(PdfPage page)
    {
        foreach (var key in InheritableKeys)
        {
            if (!page.Dictionary.ContainsKey(key) && page.GetInheritedValue(key) is { } value)
            {
                page.Dictionary[key] = value switch
                {
                    PdfArray array => new PdfArray(array),
                    PdfDictionary dictionary => dictionary.CloneShallow(),
                    _ => value,
                };
            }
        }

        // A page must have a media box (Table 31): the default of the library is US Letter
        if (!page.Dictionary.ContainsKey(PdfNames.MediaBox))
        {
            page.Dictionary[PdfNames.MediaBox] = page.MediaBox.ToArray();
        }
    }

    /// <summary>Copies a page of the document: the copy shares its content streams and its resources.</summary>
    private PdfPage Duplicate(int index, PdfPage source)
    {
        var dictionary = new PdfDictionary();
        foreach (var (key, value) in source.Dictionary)
        {
            if (!IsExcludedFromCopy(key))
            {
                dictionary[key] = value;
            }
        }

        foreach (var key in InheritableKeys)
        {
            if (!dictionary.ContainsKey(key) && source.GetInheritedValue(key) is { } value)
            {
                dictionary[key] = value;
            }
        }

        // The direct objects are copied: a change of a page (a content builder) does not change the other one
        if (dictionary[PdfNames.Resources] is PdfDictionary resources)
        {
            var copy = new PdfDictionary(resources.Count);
            foreach (var (key, value) in resources)
            {
                copy[key] = value is PdfDictionary category ? category.CloneShallow() : value;
            }

            dictionary[PdfNames.Resources] = copy;
        }

        foreach (var key in dictionary.Keys.ToArray())
        {
            if (dictionary[key] is PdfArray array)
            {
                dictionary[key] = new PdfArray(array);
            }
        }

        var page = InsertDictionary(index, dictionary);
        CopyAnnotations(source, page, annotation => annotation.CloneShallow());
        return page;
    }

    /// <summary>Copies a page of another document, with the objects it uses.</summary>
    private PdfPage Import(int index, PdfPage source)
    {
        var importer = PdfObjectImporter.Get(source.Document, _document);
        var dictionary = new PdfDictionary();
        foreach (var (key, value) in source.Dictionary)
        {
            if (!IsExcludedFromCopy(key))
            {
                dictionary[key] = importer.Copy(value);
            }
        }

        foreach (var key in InheritableKeys)
        {
            if (!dictionary.ContainsKey(key) && source.GetInheritedValue(key) is { } value)
            {
                dictionary[key] = importer.Copy(value);
            }
        }

        if (!dictionary.ContainsKey(PdfNames.MediaBox))
        {
            dictionary[PdfNames.MediaBox] = source.MediaBox.ToArray();
        }

        var page = InsertDictionary(index, dictionary);
        importer.MapPage(source.Reference, page.Reference);
        CopyAnnotations(source, page, annotation =>
        {
            // A named destination is replaced by the destination it names: the names belong to the source document
            var resolved = annotation.CloneShallow();
            if (PdfDestination.ResolveNamed(source.Document, resolved[PdfNames.Dest]) is { } destination)
            {
                resolved[PdfNames.Dest] = destination;
            }

            if (resolved.Get<PdfDictionary>(PdfNames.A) is { } action && action.GetName(PdfNames.S) == PdfNames.GoTo
                && PdfDestination.ResolveNamed(source.Document, action[PdfNames.D]) is { } actionDestination)
            {
                var actionCopy = action.CloneShallow();
                actionCopy[PdfNames.D] = actionDestination;
                resolved[PdfNames.A] = actionCopy;
            }

            return (PdfDictionary)importer.Copy(resolved);
        });
        return page;
    }

    /// <summary>The entries of a page that a copy does not have: its node, its structure, its annotations (copied apart) and its article beads.</summary>
    private static bool IsExcludedFromCopy(PdfName key) => key == PdfNames.Parent || key == StructParentsName || key == PdfNames.Annots || key == BName;

    /// <summary>Copies the annotations of a page to its copy, except the widgets of the form fields and the pop-up windows.</summary>
    private void CopyAnnotations(PdfPage source, PdfPage target, Func<PdfDictionary, PdfDictionary> copy)
    {
        if (source.Dictionary.Get<PdfArray>(PdfNames.Annots) is not { } annotations)
            return;

        var result = new PdfArray();
        for (var i = 0; i < annotations.Count; i++)
        {
            if (annotations.GetResolved(i) is not PdfDictionary annotation)
                continue;

            var subtype = annotation.GetName(PdfNames.Subtype);
            if (subtype == WidgetName || subtype == PopupName)
                continue;

            // The annotation is copied without its page, its structure, its pop-up and its unique name
            var shell = annotation.CloneShallow();
            shell.Remove(PdfNames.P);
            shell.Remove(StructParentName);
            shell.Remove(PopupName);
            shell.Remove(PdfNames.Parent);
            shell.Remove(NMName);
            var annotationCopy = copy(shell);
            annotationCopy[PdfNames.P] = target.Reference;
            result.Add(_document.Objects.Add(annotationCopy));
        }

        if (result.Count > 0)
        {
            target.Dictionary[PdfNames.Annots] = result;
        }
    }

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
        if (reference is not null && (node.IsType(PdfNames.Page) || !node.IsType(PdfNames.Pages)) && !_pagesByReference.ContainsKey(reference))
        {
            var page = new PdfPage(_document, reference, node);
            _pages.Add(page);
            _pagesByReference[reference] = page;
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
