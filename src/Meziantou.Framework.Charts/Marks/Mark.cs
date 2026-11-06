namespace Meziantou.Framework.Charts;

/// <summary>
/// A layer of a <see cref="Plot"/> that draws its data with shapes: bars, lines, dots, arcs... The positions, colors
/// and sizes of the shapes are channels, mapped by the scales of the plot.
/// </summary>
public abstract class Mark
{
    private protected Mark()
    {
    }

    /// <summary>Gets or sets the CSS class of the group of the mark in the SVG output.</summary>
    public string? CssClass { get; set; }

    /// <summary>Gets or sets the opacity of the mark, from 0 to 1. The default is 1.</summary>
    public double Opacity { get; set; } = 1;

    /// <summary>What the mark needs from the plot, once its rows are prepared.</summary>
    internal virtual MarkTraits GetTraits(MarkData data) => MarkTraits.None;

    /// <summary>Evaluates the channels and applies the transform.</summary>
    internal abstract MarkData Prepare();

    /// <summary>Draws the rows once the scales and the coordinates are known.</summary>
    internal abstract void Render(RenderContext context, MarkData data, SceneGroup group);
}
