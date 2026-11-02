using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>
/// Removes the references of a document to a page removed from its page tree, so that a full save does not write the
/// page anymore and the document does not go to a page it does not have: the destinations of the outline items, the
/// links and the open action, the named destinations, the structure tree and the widgets of the form fields.
/// </summary>
internal static class PdfPageReferenceRemover
{
    private const int MaxOutlineItems = 100_000;
    private const int MaxDepth = 64;

    private static readonly PdfName OpenActionName = PdfName.Create("OpenAction");

    public static void Remove(PdfDocument document, PdfPage page)
    {
        var reference = page.Reference;
        RemoveFromOutline(document, page);

        foreach (var other in document.Pages)
        {
            if (other.Dictionary.Get<PdfArray>(PdfNames.Annots) is not { } annotations)
                continue;

            for (var i = 0; i < annotations.Count; i++)
            {
                if (annotations.GetResolved(i) is PdfDictionary annotation)
                {
                    RemoveDestination(annotation, reference);
                }
            }
        }

        var catalog = document.Catalog;
        if (TargetsPage(catalog[OpenActionName], reference, depth: 0))
        {
            catalog.Remove(OpenActionName);
        }

        // The named destinations: the /Dests dictionary of PDF 1.1 and the /Dests name tree
        if (catalog.Get<PdfDictionary>(PdfNames.Dests) is { } dests)
        {
            foreach (var key in dests.Keys.ToArray())
            {
                if (TargetsPage(dests[key], reference, depth: 0))
                {
                    dests.Remove(key);
                }
            }
        }

        if (catalog.Get<PdfDictionary>(PdfNames.Names)?.Get<PdfDictionary>(PdfNames.Dests) is { } tree)
        {
            var entries = PdfNameTree.GetEntries(tree);
            var kept = entries.Where(entry => !TargetsPage(entry.Value, reference, depth: 0)).ToList();
            if (kept.Count != entries.Count)
            {
                PdfNameTree.SetEntries(tree, kept);
            }
        }

        if (catalog.ContainsKey(PdfName.Create("StructTreeRoot")))
        {
            document.StructureTree.RemovePage(page);
        }

        PdfInteractiveForm.RemovePage(document, page);
    }

    /// <summary>Indicates whether a destination (an explicit destination, a dictionary with <c>/D</c>, a <c>GoTo</c> action) goes to a page.</summary>
    internal static bool TargetsPage(PdfObject? value, PdfReference page, int depth)
    {
        if (value is null || depth > 4)
            return false;

        return PdfReference.ResolveIfReference(value) switch
        {
            PdfArray { Count: > 0 } array => array[0] is PdfReference target && target.Equals(page),
            PdfDictionary dictionary => (dictionary.GetName(PdfNames.S) is null || dictionary.GetName(PdfNames.S) == PdfNames.GoTo) && TargetsPage(dictionary[PdfNames.D], page, depth + 1),
            _ => false,
        };
    }

    /// <summary>Removes the destination of an outline item or a link when it goes to a page: its <c>/Dest</c>, or its <c>GoTo</c> action.</summary>
    private static void RemoveDestination(PdfDictionary dictionary, PdfReference page)
    {
        if (TargetsPage(dictionary[PdfNames.Dest], page, depth: 0))
        {
            dictionary.Remove(PdfNames.Dest);
        }

        if (dictionary.Get<PdfDictionary>(PdfNames.A) is { } action && action.GetName(PdfNames.S) == PdfNames.GoTo && TargetsPage(action[PdfNames.D], page, depth: 0))
        {
            dictionary.Remove(PdfNames.A);
        }
    }

    private static void RemoveFromOutline(PdfDocument document, PdfPage page)
    {
        // The outline read by the document is written back when it is saved: its items are changed
        if (document.LoadedOutlines is { } outlines)
        {
            RemoveFromOutline(outlines, page, depth: 0);
            return;
        }

        if (document.Catalog.Get<PdfDictionary>(PdfNames.Outlines) is not { } root)
            return;

        var visited = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance) { root };
        var stack = new Stack<(PdfDictionary Node, int Depth)>();
        stack.Push((root, 0));
        while (stack.Count > 0 && visited.Count < MaxOutlineItems)
        {
            var (node, depth) = stack.Pop();
            for (var item = node.Get<PdfDictionary>(PdfNames.First); item is not null && visited.Add(item); item = item.Get<PdfDictionary>(PdfNames.Next))
            {
                RemoveDestination(item, page.Reference);
                if (depth < MaxDepth)
                {
                    stack.Push((item, depth + 1));
                }
            }
        }
    }

    private static void RemoveFromOutline(PdfOutlineCollection items, PdfPage page, int depth)
    {
        if (depth > MaxDepth)
            return;

        foreach (var item in items)
        {
            if (item.Destination?.Page == page)
            {
                item.Destination = null;
            }

            RemoveFromOutline(item.Children, page, depth + 1);
        }
    }
}
