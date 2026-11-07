using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>A shading pattern (ISO 32000-2, 8.7.4): a color that paints with a shading. It does not belong to a document; the instances are immutable.</summary>
public sealed class PdfShadingPattern : PdfPattern
{
    /// <summary>Initializes a new instance of the <see cref="PdfShadingPattern"/> class.</summary>
    /// <param name="shading">The shading.</param>
    /// <param name="matrix">The matrix from the pattern space to the default coordinate space; <see langword="null"/> for the identity.</param>
    /// <param name="graphicsState">The graphics state parameters the shading is painted with; <see langword="null"/> for none.</param>
    public PdfShadingPattern(PdfShading shading, PdfMatrix? matrix = null, PdfExtGState? graphicsState = null)
    {
        ArgumentNullException.ThrowIfNull(shading);
        Shading = shading;
        Matrix = matrix;
        GraphicsState = graphicsState;
    }

    /// <summary>Gets the shading.</summary>
    public PdfShading Shading { get; }

    /// <summary>Gets the matrix from the pattern space to the default coordinate space.</summary>
    public PdfMatrix? Matrix { get; }

    /// <summary>Gets the graphics state parameters the shading is painted with.</summary>
    public PdfExtGState? GraphicsState { get; }

    internal override PdfReference GetReference(PdfDocument document)
    {
        if (document.SharedResources.TryGetValue(this, out var reference))
            return reference;

        var dictionary = new PdfDictionary
        {
            [PdfNames.Type] = PdfNames.Pattern,
            [PdfName.Create("PatternType")] = PdfInteger.Get(2),
            [PdfNames.Shading] = Shading.GetReference(document),
        };
        if (Matrix is { } matrix)
        {
            dictionary[PdfNames.Matrix] = ToArray(matrix);
        }

        if (GraphicsState is not null)
        {
            dictionary[PdfNames.ExtGState] = GraphicsState.GetReference(document);
        }

        reference = document.Objects.Add(dictionary);
        document.SharedResources[this] = reference;
        return reference;
    }
}
