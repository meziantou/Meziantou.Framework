namespace Meziantou.Framework.Diagrams;

/// <summary>
/// The colors of a <see cref="QuadrantChartDiagram"/> that replace the ones derived from the theme (the <c>quadrant*</c>
/// theme variables of Mermaid). A <see langword="null"/> color keeps the one of the theme.
/// </summary>
public sealed class QuadrantChartColors
{
    /// <summary>Gets or sets the fill of the top right quadrant (Mermaid <c>quadrant1Fill</c>).</summary>
    public SceneColor? Quadrant1Fill { get; set; }

    /// <summary>Gets or sets the fill of the top left quadrant (Mermaid <c>quadrant2Fill</c>).</summary>
    public SceneColor? Quadrant2Fill { get; set; }

    /// <summary>Gets or sets the fill of the bottom left quadrant (Mermaid <c>quadrant3Fill</c>).</summary>
    public SceneColor? Quadrant3Fill { get; set; }

    /// <summary>Gets or sets the fill of the bottom right quadrant (Mermaid <c>quadrant4Fill</c>).</summary>
    public SceneColor? Quadrant4Fill { get; set; }

    /// <summary>Gets or sets the color of the text of the top right quadrant (Mermaid <c>quadrant1TextFill</c>).</summary>
    public SceneColor? Quadrant1TextFill { get; set; }

    /// <summary>Gets or sets the color of the text of the top left quadrant (Mermaid <c>quadrant2TextFill</c>).</summary>
    public SceneColor? Quadrant2TextFill { get; set; }

    /// <summary>Gets or sets the color of the text of the bottom left quadrant (Mermaid <c>quadrant3TextFill</c>).</summary>
    public SceneColor? Quadrant3TextFill { get; set; }

    /// <summary>Gets or sets the color of the text of the bottom right quadrant (Mermaid <c>quadrant4TextFill</c>).</summary>
    public SceneColor? Quadrant4TextFill { get; set; }

    /// <summary>Gets or sets the fill of the points that have no color in their style (Mermaid <c>quadrantPointFill</c>).</summary>
    public SceneColor? PointFill { get; set; }

    /// <summary>Gets or sets the color of the texts of the points (Mermaid <c>quadrantPointTextFill</c>).</summary>
    public SceneColor? PointTextFill { get; set; }

    /// <summary>Gets or sets the color of the texts of the x axis (Mermaid <c>quadrantXAxisTextFill</c>).</summary>
    public SceneColor? XAxisTextFill { get; set; }

    /// <summary>Gets or sets the color of the texts of the y axis (Mermaid <c>quadrantYAxisTextFill</c>).</summary>
    public SceneColor? YAxisTextFill { get; set; }

    /// <summary>Gets or sets the color of the lines between the quadrants (Mermaid <c>quadrantInternalBorderStrokeFill</c>).</summary>
    public SceneColor? InternalBorderStrokeFill { get; set; }

    /// <summary>Gets or sets the color of the border around the quadrants (Mermaid <c>quadrantExternalBorderStrokeFill</c>).</summary>
    public SceneColor? ExternalBorderStrokeFill { get; set; }
}
