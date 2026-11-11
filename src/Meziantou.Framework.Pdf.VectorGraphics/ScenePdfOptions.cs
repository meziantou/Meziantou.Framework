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
}
