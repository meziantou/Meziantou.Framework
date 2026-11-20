using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>
/// Removes annotations from a page and the references that would keep them in a saved file (ISO 32000-2, 12.5): the
/// widgets from their fields, the pop-ups of the removed annotations, the <c>/IRT</c> and <c>/Popup</c> entries of the
/// other annotations, and the object references of the structure tree (14.7.5.3).
/// </summary>
internal static class AnnotationRemover
{
    private const int MaxStructureDepth = 256;

    /// <summary>Removes annotations, and the pop-up annotations whose parent is removed.</summary>
    public static void Remove(PdfPage page, IEnumerable<(PdfObject Item, PdfDictionary Dictionary)> annotations)
    {
        if (page.Dictionary.Get<PdfArray>(PdfNames.Annots) is not { } array)
            return;

        var removed = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        var list = new List<(PdfObject Item, PdfDictionary Dictionary)>();
        foreach (var annotation in annotations)
        {
            if (removed.Add(annotation.Dictionary))
            {
                list.Add(annotation);
            }
        }

        if (list.Count == 0)
            return;

        // The pop-up annotation of a removed annotation shows its text (12.5.6.14)
        for (var i = 0; i < array.Count; i++)
        {
            if (array.GetResolved(i) is PdfDictionary annotation && !removed.Contains(annotation) && annotation.GetName(PdfNames.Subtype) == AnnotationNames.PopupSubtype
                && annotation.GetResolved(PdfNames.Parent) is PdfDictionary parent && removed.Contains(parent))
            {
                list.Add((array[i], annotation));
                removed.Add(annotation);
            }
        }

        foreach (var (item, annotation) in list)
        {
            if (annotation.GetName(PdfNames.Subtype) == PdfFormField.WidgetName)
            {
                PdfInteractiveForm.RemoveWidget(page.Document, item, annotation);
            }
        }

        for (var i = array.Count - 1; i >= 0; i--)
        {
            if (array.GetResolved(i) is PdfDictionary annotation && removed.Contains(annotation))
            {
                array.RemoveAt(i);
            }
        }

        foreach (var item in array)
        {
            if (PdfReference.ResolveIfReference(item) is not PdfDictionary annotation)
                continue;

            if (annotation.GetResolved(AnnotationNames.IRT) is PdfDictionary target && removed.Contains(target))
            {
                annotation.Remove(AnnotationNames.IRT);
            }

            if (annotation.GetResolved(AnnotationNames.Popup) is PdfDictionary popup && removed.Contains(popup))
            {
                annotation.Remove(AnnotationNames.Popup);
            }
        }

        if (page.Document.Catalog.GetResolved(PdfNames.StructTreeRoot) is PdfDictionary root)
        {
            RemoveObjectReferences(root, removed);
        }
    }

    /// <summary>Removes the object references (14.7.5.3) of the structure tree that point to removed annotations.</summary>
    private static void RemoveObjectReferences(PdfDictionary root, HashSet<PdfDictionary> removed)
    {
        var objectReference = PdfName.Create("OBJR");
        var obj = PdfName.Create("Obj");
        var visited = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<(PdfDictionary Element, int Depth)>();
        pending.Push((root, 0));
        while (pending.Count > 0)
        {
            var (element, depth) = pending.Pop();
            if (depth > MaxStructureDepth || !visited.Add(element))
                continue;

            switch (element.GetResolved(PdfNames.K))
            {
                case PdfDictionary kid when IsRemovedReference(kid):
                    element.Remove(PdfNames.K);
                    break;
                case PdfDictionary kid:
                    pending.Push((kid, depth + 1));
                    break;
                case PdfArray kids:
                    for (var i = kids.Count - 1; i >= 0; i--)
                    {
                        if (kids.GetResolved(i) is not PdfDictionary kid)
                            continue;

                        if (IsRemovedReference(kid))
                        {
                            kids.RemoveAt(i);
                        }
                        else
                        {
                            pending.Push((kid, depth + 1));
                        }
                    }

                    break;
            }
        }

        bool IsRemovedReference(PdfDictionary kid) => kid.GetName(PdfNames.Type) == objectReference && kid.GetResolved(obj) is PdfDictionary target && removed.Contains(target);
    }
}
