using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.Pdf.Rendering.Graphics;
using Meziantou.Framework.Pdf.Text;

namespace Meziantou.Framework.Pdf;

/// <summary>An annotation of a page (ISO 32000-2, 12.5): a link, a note, a form field widget, ...</summary>
/// <remarks>
/// The class cannot be derived outside the library. Each type of annotation the specification defines has its class:
/// <see cref="PdfLinkAnnotation"/>, <see cref="PdfWidgetAnnotation"/>, <see cref="PdfPopupAnnotation"/>,
/// <see cref="PdfFileAttachmentAnnotation"/>, and the markup annotations, which derive from
/// <see cref="PdfMarkupAnnotation"/> (<see cref="PdfTextAnnotation"/>, <see cref="PdfFreeTextAnnotation"/>,
/// <see cref="PdfLineAnnotation"/>, <see cref="PdfInkAnnotation"/>, ...). The annotations of other types are instances
/// of this class. The properties read and write the annotation dictionary directly.
/// </remarks>
public class PdfAnnotation
{
    // The points of the geometry of an annotation: /Vertices, /InkList, /QuadPoints, /CL
    internal const int MaxPoints = 65536;

    private PdfAnnotationBorder? _border;

    private protected PdfAnnotation(PdfPage page, PdfDictionary dictionary, PdfReference? reference)
    {
        Page = page;
        Dictionary = dictionary;
        Reference = reference;
    }

    /// <summary>Gets the page of the annotation.</summary>
    public PdfPage Page { get; }

    /// <summary>Gets the annotation dictionary.</summary>
    public PdfDictionary Dictionary { get; }

    /// <summary>Gets the reference to the annotation, or <see langword="null"/> for a direct object (which the specification does not allow, but some files have).</summary>
    public PdfReference? Reference { get; }

    /// <summary>Gets the type of the annotation, such as <c>Link</c>, <c>Text</c> or <c>Widget</c>.</summary>
    public string Subtype => Dictionary.GetName(PdfNames.Subtype)?.Value ?? "";

    /// <summary>Gets or sets the location of the annotation on the page, in default user space.</summary>
    public PdfRectangle Rectangle
    {
        get => PdfRectangle.TryRead(Dictionary.Get<PdfArray>(PdfNames.Rect), out var rectangle) ? rectangle : default;
        set => Dictionary[PdfNames.Rect] = value.ToArray();
    }

    /// <summary>Gets or sets the text of the annotation, or an alternate description of its content.</summary>
    public string? Contents
    {
        get => Dictionary.GetText(PdfNames.Contents);
        set => Dictionary.SetText(PdfNames.Contents, value);
    }

    /// <summary>Gets or sets the name of the annotation (<c>/NM</c>, 12.5.2): a text that identifies it among the annotations of its page.</summary>
    public string? UniqueName
    {
        get => Dictionary.GetText(AnnotationNames.NM);
        set => Dictionary.SetText(AnnotationNames.NM, value);
    }

    /// <summary>Gets or sets the date the annotation was last modified (<c>/M</c>, 12.5.2), or <see langword="null"/> when it has none or it is not a valid date.</summary>
    public DateTimeOffset? ModificationDate
    {
        get => PdfDate.TryParse(Dictionary.GetText(AnnotationNames.M), out var value) ? value : null;
        set => Dictionary[AnnotationNames.M] = value is { } date ? PdfString.FromText(PdfDate.Format(date)) : null;
    }

    /// <summary>Gets or sets the flags of the annotation (<c>/F</c>, 12.5.3): whether it is printed, hidden, locked... The bits the enumeration does not name are kept.</summary>
    public PdfAnnotationFlags Flags
    {
        get => (PdfAnnotationFlags)unchecked((int)Dictionary.GetInteger(PdfNames.F, 0));
        set => Dictionary[PdfNames.F] = value == PdfAnnotationFlags.None ? null : PdfInteger.Get((int)value);
    }

    /// <summary>
    /// Gets or sets the color of the annotation (<c>/C</c>, 12.5.2): the background of its icon, the title bar of its
    /// pop-up window, the border of a link or of a shape, the color of a text markup; <see langword="null"/> when it is
    /// transparent or has none. Changing it does not change an appearance stream the annotation already has: call
    /// <see cref="UpdateAppearance"/>.
    /// </summary>
    public PdfColor? Color
    {
        get => ReadColor(PdfNames.C);
        set => WriteColor(PdfNames.C, value);
    }

    /// <summary>Gets the border of the annotation (<c>/BS</c>, <c>/Border</c> and <c>/BE</c>, 12.5.4): its width, its style, its dashes and its cloudy effect.</summary>
    public PdfAnnotationBorder Border => _border ??= new PdfAnnotationBorder(Dictionary);

    /// <summary>Gets a value indicating whether the annotation has a normal appearance (<c>/AP /N</c>, 12.5.5), a stream or a dictionary of appearance states.</summary>
    public bool HasAppearance => Dictionary.Get<PdfDictionary>(AnnotationNames.AP)?.GetResolved(PdfNames.N) is PdfStream or PdfDictionary;

    /// <summary>
    /// Replaces the appearance of the annotation (<c>/AP</c>, 12.5.5) with one drawn from its properties: its geometry,
    /// its colors, its border and its text. Call it after changing the properties of an annotation, since the viewers draw
    /// the appearance, not the properties.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the appearance was replaced; <see langword="false"/> when the library does not draw
    /// the annotations of this type (links, file attachments, sounds, ...), or the annotation has nothing to draw (no
    /// color, an empty rectangle): its appearance is left as it is.
    /// </returns>
    public bool UpdateAppearance()
    {
        var stream = AppearanceGenerator.Generate(Page, Dictionary, Rectangle, includeIcons: true);
        if (stream is null)
            return false;

        var reference = Page.Document.Objects.Add(stream);
        Dictionary[AnnotationNames.AP] = new PdfDictionary { [PdfNames.N] = reference };
        return true;
    }

    internal static PdfAnnotation Create(PdfPage page, PdfDictionary dictionary, PdfReference? reference)
    {
        return dictionary.GetName(PdfNames.Subtype)?.Value switch
        {
            "Link" => new PdfLinkAnnotation(page, dictionary, reference),
            "FileAttachment" => new PdfFileAttachmentAnnotation(page, dictionary, reference),
            "Text" => new PdfTextAnnotation(page, dictionary, reference),
            "FreeText" => new PdfFreeTextAnnotation(page, dictionary, reference),
            "Line" => new PdfLineAnnotation(page, dictionary, reference),
            "Square" => new PdfSquareAnnotation(page, dictionary, reference),
            "Circle" => new PdfCircleAnnotation(page, dictionary, reference),
            "Polygon" => new PdfPolygonAnnotation(page, dictionary, reference),
            "PolyLine" => new PdfPolyLineAnnotation(page, dictionary, reference),
            "Highlight" or "Underline" or "Squiggly" or "StrikeOut" => new PdfTextMarkupAnnotation(page, dictionary, reference),
            "Stamp" => new PdfStampAnnotation(page, dictionary, reference),
            "Caret" => new PdfCaretAnnotation(page, dictionary, reference),
            "Ink" => new PdfInkAnnotation(page, dictionary, reference),
            "Redact" => new PdfRedactAnnotation(page, dictionary, reference),
            "Popup" => new PdfPopupAnnotation(page, dictionary, reference),
            "Widget" => new PdfWidgetAnnotation(page, dictionary, reference),
            { } subtype when PdfMarkupAnnotation.Subtypes.Contains(subtype) => new PdfMarkupAnnotation(page, dictionary, reference),
            _ => new PdfAnnotation(page, dictionary, reference),
        };
    }

    /// <summary>Gets the annotation of a dictionary the annotation refers to (<c>/Popup</c>, <c>/IRT</c>, <c>/Parent</c>): the instance of the collection of its page when it is on the page of this annotation.</summary>
    internal PdfAnnotation? GetRelated(PdfName key)
    {
        var item = Dictionary[key];
        if ((item is null ? null : PdfReference.ResolveIfReference(item)) is not PdfDictionary target)
            return null;

        foreach (var annotation in Page.Annotations)
        {
            if (ReferenceEquals(annotation.Dictionary, target))
                return annotation;
        }

        var page = target[PdfNames.P] is PdfReference pageReference && Page.Document.Pages.Find(pageReference) is { } other ? other : Page;
        return Create(page, target, item as PdfReference);
    }

    /// <summary>Sets an entry to an annotation of the same document: its reference.</summary>
    internal void SetRelated(PdfName key, PdfAnnotation? value)
    {
        if (value is null)
        {
            Dictionary.Remove(key);
            return;
        }

        if (value.Page.Document != Page.Document)
            throw new ArgumentException("The annotation belongs to another document", nameof(value));

        Dictionary[key] = (PdfObject?)value.Reference ?? value.Dictionary;
    }

    internal PdfColor? ReadColor(PdfName key)
    {
        var components = Dictionary.Get<PdfArray>(key);
        double Get(int index) => Math.Clamp(components.GetNumber(index), 0, 1);
        return components?.Count switch
        {
            1 => PdfColor.FromGray(Get(0)),
            3 => PdfColor.FromRgb(Get(0), Get(1), Get(2)),
            4 => PdfColor.FromCmyk(Get(0), Get(1), Get(2), Get(3)),
            _ => null,
        };
    }

    internal void WriteColor(PdfName key, PdfColor? value) => Dictionary[key] = value is { } color ? PdfArray.FromNumbers(color.GetComponents()) : null;

    /// <summary>Reads an array of numbers as points (x, y): an odd number is ignored, as are the points beyond <see cref="MaxPoints"/>.</summary>
    internal static List<PdfPoint> ReadPoints(PdfArray? array)
    {
        var result = new List<PdfPoint>();
        if (array is null)
            return result;

        for (var i = 0; i + 1 < array.Count && result.Count < MaxPoints; i += 2)
        {
            result.Add(new PdfPoint(array.GetNumber(i), array.GetNumber(i + 1)));
        }

        return result;
    }

    internal static PdfArray WritePoints(IEnumerable<PdfPoint> points)
    {
        var numbers = new List<double>();
        foreach (var point in points)
        {
            numbers.Add(point.X);
            numbers.Add(point.Y);
        }

        return PdfArray.FromNumbers([.. numbers]);
    }

    /// <summary>Reads the bounds of the quadrilaterals of <c>/QuadPoints</c> (12.5.6.10): one rectangle per group of four points.</summary>
    internal static List<PdfRectangle> ReadQuadrilaterals(PdfArray? array)
    {
        var result = new List<PdfRectangle>();
        var points = ReadPoints(array);
        for (var i = 0; i + 3 < points.Count; i += 4)
        {
            var quadrilateral = points.GetRange(i, 4);
            result.Add(new PdfRectangle(quadrilateral.Min(p => p.X), quadrilateral.Min(p => p.Y), quadrilateral.Max(p => p.X), quadrilateral.Max(p => p.Y)));
        }

        return result;
    }

    /// <summary>Writes rectangles as <c>/QuadPoints</c>, in the order viewers expect: upper-left, upper-right, lower-left, lower-right (12.5.6.10, table 182).</summary>
    internal static PdfArray WriteQuadrilaterals(IEnumerable<PdfRectangle> regions)
    {
        var numbers = new List<double>();
        foreach (var region in regions)
        {
            numbers.AddRange([region.Left, region.Top, region.Right, region.Top, region.Left, region.Bottom, region.Right, region.Bottom]);
        }

        return PdfArray.FromNumbers([.. numbers]);
    }
}
