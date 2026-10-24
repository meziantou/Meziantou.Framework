using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>
/// A destination: a page and how it is shown (ISO 32000-2, 12.3.2), the target of an outline item or a link. A
/// destination can also target a structure element (<see cref="WithStructureElement"/>), as PDF/UA-2 requires.
/// </summary>
public sealed class PdfDestination
{
    private static readonly PdfName SDName = PdfName.Create("SD");

    private PdfDestination(PdfPage page, PdfDestinationKind kind, double? left = null, double? top = null, double? right = null, double? bottom = null, double? zoom = null)
    {
        Page = page;
        Kind = kind;
        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
        Zoom = zoom;
    }

    /// <summary>Gets the page.</summary>
    public PdfPage Page { get; }

    /// <summary>Gets how the page is shown.</summary>
    public PdfDestinationKind Kind { get; }

    /// <summary>Gets the left coordinate, or <see langword="null"/> to keep the current one.</summary>
    public double? Left { get; }

    /// <summary>Gets the top coordinate, or <see langword="null"/> to keep the current one.</summary>
    public double? Top { get; }

    /// <summary>Gets the right coordinate of a <see cref="PdfDestinationKind.FitRectangle"/> destination.</summary>
    public double? Right { get; }

    /// <summary>Gets the bottom coordinate of a <see cref="PdfDestinationKind.FitRectangle"/> destination.</summary>
    public double? Bottom { get; }

    /// <summary>Gets the zoom factor of a <see cref="PdfDestinationKind.Xyz"/> destination (1 is 100%), or <see langword="null"/> to keep the current one.</summary>
    public double? Zoom { get; }

    /// <summary>
    /// Gets the structure element the destination targets (a structure destination, 12.3.2.3), or
    /// <see langword="null"/>. A destination with a structure element is written as a <c>GoTo</c> action whose
    /// <c>/D</c> is the page and whose <c>/SD</c> is the element (12.6.4.2).
    /// </summary>
    public PdfStructureElement? StructureElement { get; private init; }

    /// <summary>Creates a destination that shows the whole page.</summary>
    /// <param name="page">The page.</param>
    /// <returns>The destination.</returns>
    public static PdfDestination Fit(PdfPage page) => new(Check(page), PdfDestinationKind.Fit);

    /// <summary>Creates a destination that shows a point of a page at the upper-left corner of the window.</summary>
    /// <param name="page">The page.</param>
    /// <param name="left">The left coordinate, or <see langword="null"/> to keep the current one.</param>
    /// <param name="top">The top coordinate, or <see langword="null"/> to keep the current one.</param>
    /// <param name="zoom">The zoom factor (1 is 100%), or <see langword="null"/> to keep the current one.</param>
    /// <returns>The destination.</returns>
    public static PdfDestination Xyz(PdfPage page, double? left, double? top, double? zoom = null) => new(Check(page), PdfDestinationKind.Xyz, left: left, top: top, zoom: zoom);

    /// <summary>Creates a destination that fits the width of the page in the window.</summary>
    /// <param name="page">The page.</param>
    /// <param name="top">The coordinate at the top of the window, or <see langword="null"/> to keep the current one.</param>
    /// <returns>The destination.</returns>
    public static PdfDestination FitHorizontally(PdfPage page, double? top) => new(Check(page), PdfDestinationKind.FitHorizontally, top: top);

    /// <summary>Creates a destination that fits the height of the page in the window.</summary>
    /// <param name="page">The page.</param>
    /// <param name="left">The coordinate at the left of the window, or <see langword="null"/> to keep the current one.</param>
    /// <returns>The destination.</returns>
    public static PdfDestination FitVertically(PdfPage page, double? left) => new(Check(page), PdfDestinationKind.FitVertically, left: left);

    /// <summary>Creates a destination that fits a rectangle of the page in the window.</summary>
    /// <param name="page">The page.</param>
    /// <param name="rectangle">The rectangle.</param>
    /// <returns>The destination.</returns>
    public static PdfDestination FitRectangle(PdfPage page, PdfRectangle rectangle) => new(Check(page), PdfDestinationKind.FitRectangle, rectangle.Left, rectangle.Top, rectangle.Right, rectangle.Bottom);

    /// <summary>Creates a destination of another kind.</summary>
    /// <param name="page">The page.</param>
    /// <param name="kind">The kind: <see cref="PdfDestinationKind.FitBoundingBox"/>, <see cref="PdfDestinationKind.FitBoundingBoxHorizontally"/> (with <paramref name="coordinate"/> as the top) or <see cref="PdfDestinationKind.FitBoundingBoxVertically"/> (with <paramref name="coordinate"/> as the left).</param>
    /// <param name="coordinate">The coordinate of the kind.</param>
    /// <returns>The destination.</returns>
    public static PdfDestination FitBoundingBox(PdfPage page, PdfDestinationKind kind = PdfDestinationKind.FitBoundingBox, double? coordinate = null)
    {
        return kind switch
        {
            PdfDestinationKind.FitBoundingBox => new(Check(page), kind),
            PdfDestinationKind.FitBoundingBoxHorizontally => new(Check(page), kind, top: coordinate),
            PdfDestinationKind.FitBoundingBoxVertically => new(Check(page), kind, left: coordinate),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "The kind must be one of the FitBoundingBox kinds"),
        };
    }

    /// <summary>Creates a destination that also targets a structure element: the element that holds the content shown at the destination, such as a heading.</summary>
    /// <param name="element">The structure element, of the document of the page.</param>
    /// <returns>A destination with the same page and view, and the structure element.</returns>
    /// <exception cref="ArgumentException">The element is not an element of the document of the page.</exception>
    public PdfDestination WithStructureElement(PdfStructureElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (element.Tree.Document != Page.Document)
            throw new ArgumentException("The structure element is not an element of the document of the page", nameof(element));

        return Copy(element);
    }

    private PdfDestination Copy(PdfStructureElement element) => new(Page, Kind, Left, Top, Right, Bottom, Zoom) { StructureElement = element };

    /// <summary>
    /// Writes the destination in a dictionary that has one (an outline item, a link): <c>/Dest</c>, or, with a structure
    /// element, a <c>GoTo</c> action with the page destination and the structure destination (12.6.4.2, Table 202).
    /// </summary>
    internal void WriteTo(PdfDictionary dictionary)
    {
        if (StructureElement is not { } element)
        {
            dictionary[PdfNames.Dest] = ToArray();
            if (dictionary.Get<PdfDictionary>(PdfNames.A) is { } existing && existing.GetName(PdfNames.S) == PdfNames.GoTo)
            {
                dictionary.Remove(PdfNames.A);
            }

            return;
        }

        dictionary.Remove(PdfNames.Dest);
        dictionary[PdfNames.A] = new PdfDictionary
        {
            [PdfNames.S] = PdfNames.GoTo,
            [PdfNames.D] = ToArray(),
            [SDName] = ToArray(element.Reference),
        };
    }

    /// <summary>Writes the destination as an explicit destination array (12.3.2.2).</summary>
    internal PdfArray ToArray() => ToArray(Page.Reference);

    /// <summary>Writes the destination as an explicit destination array (12.3.2.2), or a structure destination (12.3.2.3) when the target is a structure element.</summary>
    private PdfArray ToArray(PdfReference target)
    {
        var array = new PdfArray(target);
        switch (Kind)
        {
            case PdfDestinationKind.Xyz:
                array.Add(PdfNames.XYZ);
                array.Add(Number(Left));
                array.Add(Number(Top));
                array.Add(Number(Zoom));
                break;
            case PdfDestinationKind.Fit:
                array.Add(PdfNames.Fit);
                break;
            case PdfDestinationKind.FitHorizontally:
                array.Add(PdfNames.FitH);
                array.Add(Number(Top));
                break;
            case PdfDestinationKind.FitVertically:
                array.Add(PdfNames.FitV);
                array.Add(Number(Left));
                break;
            case PdfDestinationKind.FitRectangle:
                array.Add(PdfNames.FitR);
                array.Add(Number(Left));
                array.Add(Number(Bottom));
                array.Add(Number(Right));
                array.Add(Number(Top));
                break;
            case PdfDestinationKind.FitBoundingBox:
                array.Add(PdfNames.FitB);
                break;
            case PdfDestinationKind.FitBoundingBoxHorizontally:
                array.Add(PdfNames.FitBH);
                array.Add(Number(Top));
                break;
            case PdfDestinationKind.FitBoundingBoxVertically:
                array.Add(PdfNames.FitBV);
                array.Add(Number(Left));
                break;
        }

        return array;
    }

    /// <summary>Reads a destination: an explicit destination, a named destination, or the destination of a <c>GoTo</c> action.</summary>
    internal static PdfDestination? Read(PdfDocument document, PdfObject? value, int depth = 0)
    {
        if (depth > 8)
            return null;

        switch (value is null ? null : PdfReference.ResolveIfReference(value))
        {
            case PdfArray array:
                return ReadArray(document, array);
            case PdfName name:
                return Read(document, FindNamedDestination(document, name.Value, nameTree: false), depth + 1);
            case PdfString text:
                return Read(document, FindNamedDestination(document, text.ToText(), nameTree: true), depth + 1);
            case PdfDictionary dictionary:
                // An entry of a /Dests dictionary, or a GoTo action, which can have a structure destination (12.6.4.2)
                var destination = Read(document, dictionary[PdfNames.D], depth + 1);
                if (destination is not null && dictionary.Get<PdfArray>(SDName) is { Count: > 0 } structure && structure[0] is PdfReference reference
                    && reference.Resolve() is PdfDictionary element && element.GetName(PdfNames.S) is not null)
                {
                    return destination.Copy(new PdfStructureElement(document.StructureTree, element, reference));
                }

                return destination;
            default:
                return null;
        }
    }

    private static PdfDestination? ReadArray(PdfDocument document, PdfArray array)
    {
        if (array.Count < 2 || array[0] is not PdfReference pageReference || document.Pages.Find(pageReference) is not { } page)
            return null;

        double? Get(int index) => array.TryGetResolved<PdfNumber>(index, out var number) ? number.DoubleValue : null;
        return array.GetResolved(1) is PdfName kind ? kind.Value switch
        {
            "XYZ" => new PdfDestination(page, PdfDestinationKind.Xyz, left: Get(2), top: Get(3), zoom: Get(4) is 0 ? null : Get(4)),
            "FitH" => new PdfDestination(page, PdfDestinationKind.FitHorizontally, top: Get(2)),
            "FitV" => new PdfDestination(page, PdfDestinationKind.FitVertically, left: Get(2)),
            "FitR" => new PdfDestination(page, PdfDestinationKind.FitRectangle, left: Get(2), bottom: Get(3), right: Get(4), top: Get(5)),
            "FitB" => new PdfDestination(page, PdfDestinationKind.FitBoundingBox),
            "FitBH" => new PdfDestination(page, PdfDestinationKind.FitBoundingBoxHorizontally, top: Get(2)),
            "FitBV" => new PdfDestination(page, PdfDestinationKind.FitBoundingBoxVertically, left: Get(2)),
            _ => new PdfDestination(page, PdfDestinationKind.Fit),
        } : null;
    }

    private static PdfObject? FindNamedDestination(PdfDocument document, string name, bool nameTree)
    {
        // PDF 1.1 names the destinations in the /Dests dictionary of the catalog, PDF 1.2 in the /Dests name tree
        if (!nameTree && document.Catalog.Get<PdfDictionary>(PdfNames.Dests) is { } dests && dests.GetResolved(PdfName.Create(name)) is { } fromDictionary)
            return fromDictionary;

        if (document.Catalog.Get<PdfDictionary>(PdfNames.Names)?.Get<PdfDictionary>(PdfNames.Dests) is { } tree)
            return PdfNameTree.Find(tree, name);

        return null;
    }

    private static PdfObject Number(double? value) => value is { } number ? PdfObjectFactory.Number(number) : PdfNull.Instance;

    private static PdfPage Check(PdfPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        return page;
    }
}
