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

    /// <summary>
    /// Gets or sets a value that indicates whether the y channels of the mark use the secondary y scale of the plot
    /// (<see cref="Plot.SecondaryYScale"/>), whose axis is on the right, instead of the y scale. Use it to draw two
    /// measures of different units in one chart, such as amounts and a growth rate.
    /// </summary>
    public bool UseSecondaryYScale { get; set; }

    /// <summary>What the mark needs from the plot, once its rows are prepared.</summary>
    internal virtual MarkTraits GetTraits(MarkData data) => MarkTraits.None;

    /// <summary>
    /// The space the mark needs above the first category of an inferred point y scale, in steps between two categories:
    /// ridgelines rise above their categories.
    /// </summary>
    internal virtual double GetYHeadroom() => 0;

    /// <summary>The values the mark maps with a continuous color scale that are known only once the scales are laid out (the counts of hexagons).</summary>
    internal virtual IEnumerable<double> GetLateColorValues(RenderContext context, MarkData data) => [];

    /// <summary>The positions a map fits, in geographic coordinates: the longitudes (x, x1, x2) and the latitudes (y, y1, y2) of the rows, each with each.</summary>
    internal virtual IEnumerable<GeoPosition> GetGeoPositions(MarkData data)
    {
        ChannelValues[] xs = [.. new[] { data.X, data.X1, data.X2 }.Where(channel => channel is { IsContinuous: true })!];
        ChannelValues[] ys = [.. new[] { data.Y, data.Y1, data.Y2 }.Where(channel => channel is { IsContinuous: true })!];
        for (var i = 0; i < data.Count; i++)
        {
            foreach (var x in xs)
            {
                foreach (var y in ys)
                {
                    if (!double.IsNaN(x.Numbers[i]) && !double.IsNaN(y.Numbers[i]))
                        yield return new GeoPosition(x.Numbers[i], y.Numbers[i]);
                }
            }
        }
    }

    /// <summary>Evaluates the channels and applies the transform.</summary>
    internal abstract MarkData Prepare();

    /// <summary>Draws the rows once the scales and the coordinates are known.</summary>
    internal abstract void Render(RenderContext context, MarkData data, SceneGroup group);
}
