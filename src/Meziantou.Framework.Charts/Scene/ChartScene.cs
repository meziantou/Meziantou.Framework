namespace Meziantou.Framework.Charts;

/// <summary>
/// A chart laid out into drawing primitives, independent of the output format. <see cref="Plot.CreateScene"/> creates
/// it; <see cref="SvgChartRenderer"/> and the renderer of <c>Meziantou.Framework.Imaging.Charts</c> draw it.
/// </summary>
/// <remarks>The coordinates are in pixels, with the origin at the top-left corner and the y axis pointing down. Angles are in degrees, clockwise.</remarks>
public sealed class ChartScene
{
    /// <summary>Initializes a new instance of the <see cref="ChartScene"/> class.</summary>
    /// <param name="width">The width of the chart.</param>
    /// <param name="height">The height of the chart.</param>
    /// <param name="fontFamily">The CSS <c>font-family</c> of the texts.</param>
    public ChartScene(double width, double height, string fontFamily)
    {
        ArgumentNullException.ThrowIfNull(fontFamily);
        Width = width;
        Height = height;
        FontFamily = fontFamily;
    }

    /// <summary>Gets the width of the chart.</summary>
    public double Width { get; }

    /// <summary>Gets the height of the chart.</summary>
    public double Height { get; }

    /// <summary>Gets the CSS <c>font-family</c> of the texts.</summary>
    public string FontFamily { get; }

    /// <summary>Gets or sets the accessible name of the chart, usually its title.</summary>
    public string? Title { get; set; }

    /// <summary>Gets or sets the accessible description of the chart.</summary>
    public string? Description { get; set; }

    /// <summary>Gets or sets the background, or <see langword="null"/> for a transparent background.</summary>
    public ChartColor? Background { get; set; }

    /// <summary>Gets the elements of the chart, drawn in order.</summary>
    public SceneGroup Root { get; } = new();
}
