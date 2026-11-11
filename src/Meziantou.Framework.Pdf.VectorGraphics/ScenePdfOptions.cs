using Meziantou.Framework.VectorGraphics;

namespace Meziantou.Framework.Pdf.VectorGraphics;

/// <summary>The options of <see cref="ScenePdfRenderer"/>.</summary>
public sealed class ScenePdfOptions
{
    private double _scale = 0.75;

    /// <summary>
    /// Gets or sets the number of points (1/72 inch) per unit of the drawing. The default is 0.75: a unit is a CSS pixel
    /// (1/96 inch), so the PDF has the size a browser gives the SVG of the same drawing.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a positive finite number.</exception>
    public double Scale
    {
        get => _scale;
        set
        {
            if (!(value > 0) || !double.IsFinite(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The scale must be a positive finite number.");

            _scale = value;
        }
    }

    /// <summary>
    /// Gets or sets the fonts of the texts, or <see langword="null"/> to draw them with the standard fonts Helvetica,
    /// Helvetica-Bold, Helvetica-Oblique and Helvetica-BoldOblique, which are not embedded. The layout measures the texts
    /// with them, and the PDF embeds the subsets of the faces that draw them. The fonts of a scene
    /// (<see cref="Scene.Fonts"/>) take precedence over them.
    /// </summary>
    public SceneFontCollection? Fonts { get; set; }

    /// <summary>
    /// Gets or sets a value that indicates whether the drawing is tagged, for the assistive technologies: it is a
    /// <c>Figure</c> element of the structure tree of the document, whose alternate description is the title and the
    /// description of the scene (<see cref="Scene.Title"/>, <see cref="Scene.Description"/>), and its hyperlinks are
    /// <c>Link</c> elements of the figure. The default is <see langword="true"/>. Set it to <see langword="false"/> to draw
    /// on a page of a document that is not tagged.
    /// </summary>
    public bool Tagged { get; set; } = true;

    /// <summary>
    /// Gets or sets the element of the structure tree the figure of the drawing is added to, or <see langword="null"/> to
    /// add it at the root of the tree. It must be an element of the document of the page.
    /// </summary>
    public PdfStructureElement? StructureParent { get; set; }
}
