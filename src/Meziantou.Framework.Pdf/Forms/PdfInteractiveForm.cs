using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>
/// The interactive form of a document (ISO 32000-2, 12.7): the <c>/AcroForm</c> dictionary of the catalog and its field
/// hierarchy. The fields are read from the objects each time: changes made through the object model are seen.
/// </summary>
/// <remarks>
/// Filling a field (<see cref="PdfFormField.Value"/>, <see cref="PdfCheckBoxField.IsChecked"/>,
/// <see cref="PdfChoiceField.SetSelectedValues"/>) changes its value and the appearances of its widgets only: the
/// other fields keep their values, and an incremental save keeps the signatures of the document valid.
/// <see cref="Flatten"/> draws the fields on their pages and removes them.
/// </remarks>
public sealed class PdfInteractiveForm
{
    private const int MaxDepth = 32;
    private const int MaxFields = 100_000;

    private static readonly PdfName AcroFormName = PdfName.Create("AcroForm");
    private static readonly PdfName FieldsName = PdfName.Create("Fields");
    private static readonly PdfName NeedAppearancesName = PdfName.Create("NeedAppearances");

    internal PdfInteractiveForm(PdfDocument document)
    {
        Document = document;
    }

    /// <summary>Gets the document of the form.</summary>
    public PdfDocument Document { get; }

    /// <summary>Gets the interactive form dictionary (<c>/AcroForm</c> of the catalog), or <see langword="null"/> when the document has no form.</summary>
    public PdfDictionary? Dictionary => Document.Catalog.Get<PdfDictionary>(AcroFormName);

    /// <summary>Gets a value indicating whether the document has an interactive form.</summary>
    public bool Exists => Dictionary is not null;

    /// <summary>Gets the fields at the top of the hierarchy (<c>/Fields</c>).</summary>
    public IReadOnlyList<PdfFormField> Fields
    {
        get
        {
            var result = new List<PdfFormField>();
            if (Dictionary?.Get<PdfArray>(FieldsName) is { } fields)
            {
                var visited = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
                foreach (var item in fields)
                {
                    if (PdfReference.ResolveIfReference(item) is PdfDictionary dictionary && visited.Add(dictionary))
                    {
                        result.Add(CreateField(dictionary, item as PdfReference, parent: null));
                    }
                }
            }

            return result;
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether viewers must generate the appearances of the fields (<c>/NeedAppearances</c>,
    /// deprecated in PDF 2.0). The library writes the appearances of the fields it fills, so it is not needed for them.
    /// </summary>
    public bool NeedAppearances
    {
        get => Dictionary?.GetBoolean(NeedAppearancesName) ?? false;
        set
        {
            if (Dictionary is { } dictionary)
            {
                dictionary[NeedAppearancesName] = value ? PdfBoolean.True : null;
            }
            else if (value)
            {
                throw new InvalidOperationException("The document has no interactive form");
            }
        }
    }

    /// <summary>Gets the terminal fields of the form, in the order of the hierarchy: the fields that have a value and widgets.</summary>
    /// <returns>The fields.</returns>
    public IReadOnlyList<PdfFormField> GetFields()
    {
        var result = new List<PdfFormField>();
        var visited = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        void Visit(PdfFormField field, int depth)
        {
            if (depth > MaxDepth || result.Count >= MaxFields || !visited.Add(field.Dictionary))
                return;

            var children = field.Children;
            if (children.Count == 0)
            {
                result.Add(field);
                return;
            }

            foreach (var child in children)
            {
                Visit(child, depth + 1);
            }
        }

        foreach (var field in Fields)
        {
            Visit(field, depth: 0);
        }

        return result;
    }

    /// <summary>Finds a field by its fully qualified name, such as <c>address.city</c>.</summary>
    /// <param name="fullName">The fully qualified name.</param>
    /// <returns>The field, terminal or not, or <see langword="null"/>.</returns>
    public PdfFormField? FindField(string fullName)
    {
        ArgumentNullException.ThrowIfNull(fullName);
        var parts = fullName.Split('.');
        IReadOnlyList<PdfFormField> level = Fields;
        PdfFormField? found = null;
        foreach (var part in parts)
        {
            found = level.FirstOrDefault(field => string.Equals(field.Name, part, StringComparison.Ordinal));
            if (found is null)
                return null;

            level = found.Children;
        }

        return found;
    }

    /// <summary>
    /// Flattens the form: the normal appearance of each widget is drawn in the content of its page, and the fields and
    /// their widgets are removed, so that the values become content that cannot be changed. The signature fields that
    /// are signed are kept, with their widgets, so that the signatures stay.
    /// </summary>
    /// <remarks>
    /// The text fields and the choice fields whose widgets have no appearance, or all of them when
    /// <see cref="NeedAppearances"/> is set, get the appearances of their values first. The hidden widgets are not drawn.
    /// The <c>/AcroForm</c> dictionary is removed when no field is kept.
    /// </remarks>
    public void Flatten()
    {
        if (Dictionary is not { } dictionary)
            return;

        var needAppearances = NeedAppearances;
        var kept = new List<PdfFormField>();
        var builders = new Dictionary<PdfPage, PdfContentBuilder>();
        foreach (var field in GetFields())
        {
            if (field is PdfSignatureField { IsSigned: true })
            {
                kept.Add(field);
                continue;
            }

            if (field is PdfTextField or PdfChoiceField && (needAppearances || field.GetWidgetDictionaries().Any(widget => GetNormalAppearance(widget.Dictionary) is null)))
            {
                PdfFieldAppearance.Update(field);
            }

            foreach (var (widget, reference) in field.GetWidgetDictionaries())
            {
                if (FindPage(widget, reference) is not { } page)
                    continue;

                var flags = (PdfAnnotationFlags)unchecked((int)widget.GetInteger(PdfNames.F, 0));
                if (!flags.HasFlag(PdfAnnotationFlags.Hidden) && GetNormalAppearance(widget) is { } appearance)
                {
                    if (!builders.TryGetValue(page, out var builder))
                    {
                        builder = page.CreateContentBuilder();
                        builders[page] = builder;
                    }

                    Draw(builder, widget, appearance);
                }

                RemoveAnnotation(page, widget, reference);
            }
        }

        foreach (var builder in builders.Values)
        {
            builder.Complete();
        }

        if (kept.Count == 0)
        {
            Document.Catalog.Remove(AcroFormName);
            return;
        }

        // The fields kept, by the field at the top of their hierarchy
        var tops = new List<PdfObject>();
        foreach (var field in kept)
        {
            var top = field;
            while (top.Parent is not null)
            {
                top = top.Parent;
            }

            PdfObject item = top.Reference is not null ? top.Reference : top.Dictionary;
            if (!tops.Contains(item))
            {
                tops.Add(item);
            }
        }

        dictionary[FieldsName] = new PdfArray(tops);
        dictionary.Remove(NeedAppearancesName);
    }

    internal PdfFormField CreateField(PdfDictionary dictionary, PdfReference? reference, PdfFormField? parent) => PdfFormField.Create(this, dictionary, reference, parent);

    /// <summary>Finds the page of a widget: its <c>/P</c> entry, or the page whose annotations have it.</summary>
    internal PdfPage? FindPage(PdfDictionary widget, PdfReference? reference)
    {
        if (widget[PdfNames.P] is PdfReference pageReference && Document.Pages.Find(pageReference) is { } page && Contains(page, widget, reference))
            return page;

        foreach (var candidate in Document.Pages)
        {
            if (Contains(candidate, widget, reference))
                return candidate;
        }

        return null;

        static bool Contains(PdfPage page, PdfDictionary widget, PdfReference? reference)
        {
            if (page.Dictionary.Get<PdfArray>(PdfNames.Annots) is not { } annotations)
                return false;

            foreach (var item in annotations)
            {
                if ((reference is not null && reference.Equals(item)) || ReferenceEquals(PdfReference.ResolveIfReference(item), widget))
                    return true;
            }

            return false;
        }
    }

    /// <summary>Removes the fields of the widgets of a page removed from the document: a widget is removed from its field, and a field without widgets nor children from its parent.</summary>
    internal static void RemovePage(PdfDocument document, PdfPage page)
    {
        if (document.Catalog.Get<PdfDictionary>(AcroFormName) is not { } form || page.Dictionary.Get<PdfArray>(PdfNames.Annots) is not { } annotations)
            return;

        for (var i = 0; i < annotations.Count; i++)
        {
            if (annotations.GetResolved(i) is not PdfDictionary widget || widget.GetName(PdfNames.Subtype) != PdfFormField.WidgetName)
                continue;

            PdfObject item = annotations[i];
            var node = widget;
            for (var depth = 0; depth < MaxDepth; depth++)
            {
                var parent = node.Get<PdfDictionary>(PdfNames.Parent);
                var container = parent?.Get<PdfArray>(PdfNames.Kids) ?? form.Get<PdfArray>(FieldsName);
                if (container is null)
                    break;

                Remove(container, item, node);
                if (parent is null || parent.Get<PdfArray>(PdfNames.Kids) is { Count: > 0 })
                    break;

                item = node[PdfNames.Parent]!;
                node = parent;
            }
        }

        static void Remove(PdfArray array, PdfObject item, PdfDictionary dictionary)
        {
            for (var i = array.Count - 1; i >= 0; i--)
            {
                if (array[i].Equals(item) || ReferenceEquals(PdfReference.ResolveIfReference(array[i]), dictionary))
                {
                    array.RemoveAt(i);
                }
            }
        }
    }

    /// <summary>Gets the normal appearance of a widget: its stream, or the stream of its appearance state (12.5.5).</summary>
    private static PdfObject? GetNormalAppearance(PdfDictionary widget)
    {
        var normal = widget.Get<PdfDictionary>(PdfFormField.APName)?[PdfNames.N];
        switch (normal is null ? null : PdfReference.ResolveIfReference(normal))
        {
            case PdfStream:
                return normal;
            case PdfDictionary states when widget.GetName(PdfFormField.ASName) is { } state && states.GetResolved(state) is PdfStream:
                return states[state];
            default:
                return null;
        }
    }

    /// <summary>Paints an appearance stream as a form XObject in the rectangle of its annotation (12.5.5, the algorithm of the appearance streams).</summary>
    private void Draw(PdfContentBuilder builder, PdfDictionary widget, PdfObject appearance)
    {
        var stream = (PdfStream)PdfReference.ResolveIfReference(appearance);
        if (!PdfRectangle.TryRead(widget.Get<PdfArray>(PdfNames.Rect), out var rectangle) || !PdfRectangle.TryRead(stream.Dictionary.Get<PdfArray>(PdfNames.BBox), out var bbox))
            return;

        var matrix = stream.Dictionary.Get<PdfArray>(PdfNames.Matrix) is { Count: 6 } values
            ? new PdfMatrix(values.GetNumber(0), values.GetNumber(1), values.GetNumber(2), values.GetNumber(3), values.GetNumber(4), values.GetNumber(5))
            : PdfMatrix.Identity;
        var corners = new[] { matrix.Transform(bbox.Left, bbox.Bottom), matrix.Transform(bbox.Right, bbox.Bottom), matrix.Transform(bbox.Right, bbox.Top), matrix.Transform(bbox.Left, bbox.Top) };
        var left = corners.Min(point => point.X);
        var right = corners.Max(point => point.X);
        var bottom = corners.Min(point => point.Y);
        var top = corners.Max(point => point.Y);
        if (right - left <= 0 || top - bottom <= 0)
            return;

        // The form XObject applies its own matrix: the transformation maps its transformed bounding box to the rectangle
        var transform = PdfMatrix.CreateTranslation(-left, -bottom) * PdfMatrix.CreateScale(rectangle.Width / (right - left), rectangle.Height / (top - bottom)) * PdfMatrix.CreateTranslation(rectangle.Left, rectangle.Bottom);
        var reference = appearance as PdfReference ?? Document.Objects.Add(stream);
        stream.Dictionary[PdfNames.Type] = PdfNames.XObject;
        stream.Dictionary[PdfNames.Subtype] = PdfNames.Form;
        var name = builder.AddResource(PdfNames.XObject, "Fm", reference);
        builder.SaveGraphicsState().Transform(transform).PaintXObject(name).RestoreGraphicsState();
    }

    private static void RemoveAnnotation(PdfPage page, PdfDictionary widget, PdfReference? reference)
    {
        if (page.Dictionary.Get<PdfArray>(PdfNames.Annots) is not { } annotations)
            return;

        for (var i = annotations.Count - 1; i >= 0; i--)
        {
            if ((reference is not null && reference.Equals(annotations[i])) || ReferenceEquals(PdfReference.ResolveIfReference(annotations[i]), widget))
            {
                annotations.RemoveAt(i);
            }
        }
    }
}
