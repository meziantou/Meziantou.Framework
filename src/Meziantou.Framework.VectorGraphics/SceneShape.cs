namespace Meziantou.Framework.VectorGraphics;

/// <summary>An element that is filled or stroked.</summary>
public abstract class SceneShape : SceneElement
{
    private protected SceneShape()
    {
    }

    /// <summary>Gets or sets how the interior is painted, or <see langword="null"/> to leave it empty.</summary>
    public ScenePaint? Fill { get; set; }

    /// <summary>Gets or sets how the outline is painted, or <see langword="null"/> for no outline.</summary>
    public ScenePaint? Stroke { get; set; }

    /// <summary>Gets or sets the width of the outline.</summary>
    public double StrokeWidth { get; set; } = 1;

    /// <summary>Gets or sets the lengths of the dashes and gaps of the outline, or <see langword="null"/> for a solid outline.</summary>
    public IReadOnlyList<double>? StrokeDashArray { get; set; }

    /// <summary>
    /// Gets or sets the distance into <see cref="StrokeDashArray"/> at which the dashes of the outline start. A negative
    /// distance starts before the pattern, and a value that is not finite is 0.
    /// </summary>
    public double StrokeDashOffset { get; set; }

    /// <summary>
    /// Gets or sets the limit of <see cref="SceneLineJoin.Miter"/> joins: the largest allowed ratio between the length of
    /// the miter and the width of the outline. A sharper corner is beveled. The default is 4, as in SVG; a value less
    /// than 1 or not finite is 4.
    /// </summary>
    public double MiterLimit { get; set; } = 4;

    /// <summary>Gets or sets the shape of the ends of open outlines.</summary>
    public SceneLineCap LineCap { get; set; }

    /// <summary>Gets or sets the shape of the corners of outlines.</summary>
    public SceneLineJoin LineJoin { get; set; }

    /// <summary>
    /// Gets or sets the order in which the fill, the stroke and the markers are painted, as the SVG <c>paint-order</c>
    /// property. The default is the fill, then the stroke, then the markers.
    /// </summary>
    public ScenePaintOrder PaintOrder { get; set; }
}
