using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>
/// A tiling pattern of a document (ISO 32000-2, 8.7.3): a cell, drawn with the builder of
/// <see cref="CreateContentBuilder"/>, repeated at fixed intervals over the areas it paints.
/// </summary>
/// <remarks>
/// A colored pattern sets the colors of its cell itself. An uncolored pattern (<see cref="IsColored"/> is
/// <see langword="false"/>) is a stencil: its cell must not set colors, and the color is given when the pattern is set
/// (<see cref="PdfContentBuilder.SetFillPattern(PdfTilingPattern, PdfColor)"/>).
/// </remarks>
public sealed class PdfTilingPattern : PdfPattern
{
    private readonly PdfStream _stream;
    private readonly PdfReference _reference;

    internal PdfTilingPattern(PdfDocument document, PdfRectangle cell, double xStep, double yStep, bool isColored, PdfMatrix? matrix)
    {
        if (!double.IsFinite(xStep) || xStep == 0)
            throw new ArgumentOutOfRangeException(nameof(xStep), xStep, "The horizontal spacing must be a nonzero number");

        if (!double.IsFinite(yStep) || yStep == 0)
            throw new ArgumentOutOfRangeException(nameof(yStep), yStep, "The vertical spacing must be a nonzero number");

        Document = document;
        IsColored = isColored;
        _stream = new PdfStream();
        var dictionary = _stream.Dictionary;
        dictionary[PdfNames.Type] = PdfNames.Pattern;
        dictionary[PdfName.Create("PatternType")] = PdfInteger.Get(1);
        dictionary[PdfName.Create("PaintType")] = PdfInteger.Get(isColored ? 1 : 2);

        // Constant spacing: the cells may be distorted by at most one device pixel (8.7.3.3)
        dictionary[PdfName.Create("TilingType")] = PdfInteger.Get(1);
        dictionary[PdfNames.BBox] = cell.ToArray();
        dictionary[PdfName.Create("XStep")] = PdfObjectFactory.Number(xStep);
        dictionary[PdfName.Create("YStep")] = PdfObjectFactory.Number(yStep);
        if (matrix is { } value)
        {
            dictionary[PdfNames.Matrix] = ToArray(value);
        }

        Resources = new PdfDictionary();
        dictionary[PdfNames.Resources] = Resources;
        _reference = document.Objects.Add(_stream);
    }

    /// <summary>Gets the document of the pattern.</summary>
    public PdfDocument Document { get; }

    /// <summary>Gets whether the pattern sets its colors itself; an uncolored pattern is painted with the color given when it is set.</summary>
    public bool IsColored { get; }

    /// <summary>Gets the resource dictionary of the cell.</summary>
    public PdfDictionary Resources { get; }

    /// <summary>Creates a builder that appends to the content of the cell.</summary>
    /// <returns>The builder.</returns>
    public PdfContentBuilder CreateContentBuilder() => new(Document, Resources, _stream);

    internal override PdfReference GetReference(PdfDocument document)
    {
        if (document != Document)
            throw new InvalidOperationException("The pattern belongs to another document");

        return _reference;
    }
}
