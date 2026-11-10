using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.Pdf.Redaction;

namespace Meziantou.Framework.Pdf;

public sealed partial class PdfPage
{
    private const int MaxStructureDepth = 256;

    /// <summary>
    /// Removes what the page shows in areas: the content is rewritten without the glyphs whose box touches an area (the
    /// invisible text included), the subpaths inside an area (the paths that cross one are clipped), and the pixels of
    /// the images under an area, in the content of the page and of the forms it paints; the annotations that touch an
    /// area are removed, with the values of their form fields. The areas are then painted with
    /// <see cref="PdfRedactionOptions.FillColor"/>.
    /// </summary>
    /// <param name="areas">The areas, in the default user space of the page (points, from the lower-left corner of the media box). The empty areas are ignored.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="PdfRedactionOptions.Default"/>.</param>
    /// <returns>What was removed.</returns>
    /// <remarks>
    /// <para>
    /// The removed content is no longer referenced by the page, so a full save (<see cref="PdfDocument.Save(Stream, PdfSaveOptions?)"/>)
    /// does not write it. The document cannot be saved incrementally anymore: the original bytes an incremental update
    /// keeps contain what was removed.
    /// </para>
    /// <para>
    /// The glyphs kept stay in place: the displacement of a removed glyph is replaced by a number of a <c>TJ</c> array.
    /// A form XObject or an image the areas touch is replaced on this page by a redacted copy, so that the other pages that
    /// paint it are unchanged. A redacted image is stored again in sRGB (as JPEG when it was JPEG), with the erased pixels
    /// transparent. The replacement texts, alternate descriptions and expansions of the marked-content sequences that lose
    /// content are removed. The content of patterns, soft masks, Type 3 glyphs and the appearances of the annotations that
    /// are kept is not changed.
    /// </para>
    /// <para>The page is not changed when the method throws.</para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="areas"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidPdfContentException">A content stream, or an image the areas touch, cannot be decoded.</exception>
    /// <exception cref="UnsupportedPdfFeatureException">An image the areas touch uses a feature the library does not support.</exception>
    /// <exception cref="PdfResourceLimitException">The content runs more operators and glyphs than the limit, or its forms are nested too deep.</exception>
    /// <exception cref="InvalidOperationException">No object number is left for the new content: the file uses the highest numbers (see <see cref="PdfIndirectObjectCollection.Add"/>).</exception>
    public PdfRedactionResult Redact(IEnumerable<PdfRectangle> areas, PdfRedactionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(areas);
        options ??= PdfRedactionOptions.Default;
        var list = areas.Where(area => area.Width > 0 && area.Height > 0 && double.IsFinite(area.Width) && double.IsFinite(area.Height)).ToArray();
        var result = new PdfRedactionResult();
        if (list.Length == 0)
            return result;

        // The rectangle the clipping paths cut the areas out of: everything the page can show
        var world = MediaBox;
        foreach (var area in list)
        {
            world = new PdfRectangle(Math.Min(world.Left, area.Left), Math.Min(world.Bottom, area.Bottom), Math.Max(world.Right, area.Right), Math.Max(world.Top, area.Top));
        }

        world = new PdfRectangle(world.Left - 10, world.Bottom - 10, world.Right + 10, world.Top + 10);

        RedactedContent? content;
        using (var redactor = new ContentRedactor(Document, list, world, result))
        {
            content = redactor.Redact(GetContentData(), Resources, PdfMatrix.Identity, depth: 0);
        }

        var annotations = options.RemoveAnnotations ? FindAnnotations(list) : [];

        // The new content: the redacted content, then the areas painted over it. Its object is added before the page
        // changes, so that the page does not change when no object number is left.
        PdfReference? contentReference = null;
        if (content is not null || options.FillColor is not null)
        {
            var writer = new MemoryStream();
            writer.Write("q\n"u8);
            writer.Write(content is not null ? content.Data : GetContentData());
            writer.Write("\nQ\n"u8);
            if (options.FillColor is { } color)
            {
                var overlay = new PdfStream();
                var builder = new PdfContentBuilder(Document, new PdfDictionary(), overlay).SetFillColor(color);
                foreach (var area in list)
                {
                    builder.Rectangle(area);
                }

                builder.Fill().Complete();
                writer.Write(overlay.EncodedData.Span);
            }

            var stream = new PdfStream();
            stream.SetData(writer.ToArray());
            contentReference = Document.Objects.Add(stream);
        }

        // Nothing failed: the page changes
        if (contentReference is not null)
        {
            if (content is not null)
            {
                content.ApplyTo(GetOrCreateResources());
            }

            Dictionary[PdfNames.Contents] = contentReference;
            _saveStream = null;
        }

        RemoveAnnotations(annotations);
        result.RemovedAnnotationCount = annotations.Count;
        Document.HasRedactions = true;
        return result;
    }

    /// <summary>Finds the annotations whose rectangle touches an area, and the pop-up annotations of those.</summary>
    private List<(PdfObject Item, PdfDictionary Dictionary)> FindAnnotations(PdfRectangle[] areas)
    {
        var result = new List<(PdfObject Item, PdfDictionary Dictionary)>();
        if (Dictionary.Get<PdfArray>(PdfNames.Annots) is not { } annotations)
            return result;

        var removed = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        for (var i = 0; i < annotations.Count; i++)
        {
            if (annotations.GetResolved(i) is PdfDictionary annotation && PdfRectangle.TryRead(annotation.Get<PdfArray>(PdfNames.Rect), out var rectangle) && areas.Any(area => ContentRedactor.Intersects(rectangle, area)))
            {
                result.Add((annotations[i], annotation));
                removed.Add(annotation);
            }
        }

        // The pop-up annotation of a removed annotation shows its text
        for (var i = 0; i < annotations.Count; i++)
        {
            if (annotations.GetResolved(i) is PdfDictionary annotation && !removed.Contains(annotation) && annotation.GetResolved(PdfNames.Parent) is PdfDictionary parent && removed.Contains(parent))
            {
                result.Add((annotations[i], annotation));
                removed.Add(annotation);
            }
        }

        return result;
    }

    /// <summary>
    /// Removes annotations from the page, the widgets from their fields, and the references that would keep them in a
    /// saved file: the structure elements that point to them, the replies and pop-ups of the other annotations.
    /// </summary>
    private void RemoveAnnotations(List<(PdfObject Item, PdfDictionary Dictionary)> annotations)
    {
        if (annotations.Count == 0 || Dictionary.Get<PdfArray>(PdfNames.Annots) is not { } array)
            return;

        var removed = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        foreach (var (item, annotation) in annotations)
        {
            removed.Add(annotation);
            if (annotation.GetName(PdfNames.Subtype) == PdfFormField.WidgetName)
            {
                PdfInteractiveForm.RemoveWidget(Document, item, annotation);
            }
        }

        for (var i = array.Count - 1; i >= 0; i--)
        {
            if (array.GetResolved(i) is PdfDictionary annotation && removed.Contains(annotation))
            {
                array.RemoveAt(i);
            }
        }

        var inReplyTo = PdfName.Create("IRT");
        var popup = PdfName.Create("Popup");
        foreach (var item in array)
        {
            if (PdfReference.ResolveIfReference(item) is not PdfDictionary annotation)
                continue;

            if (annotation.GetResolved(inReplyTo) is PdfDictionary target && removed.Contains(target))
            {
                annotation.Remove(inReplyTo);
            }

            if (annotation.GetResolved(popup) is PdfDictionary popupAnnotation && removed.Contains(popupAnnotation))
            {
                annotation.Remove(popup);
            }
        }

        if (Document.Catalog.GetResolved(PdfNames.StructTreeRoot) is PdfDictionary root)
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
