using System.Text;
using Meziantou.Framework.Charts;
using Meziantou.Framework.Mermaid.Internals.Charts;

namespace Meziantou.Framework.Mermaid;

/// <summary>
/// A chart of Mermaid: a pie chart, an XY chart, a quadrant chart, a sankey chart, a treemap or a radar chart. It keeps
/// what the Mermaid text says, so <see cref="MermaidWriter"/> writes it back, and draws it with a <see cref="Plot"/> of
/// Meziantou.Framework.Charts, which <see cref="ToPlot"/> returns to customize the drawing.
/// </summary>
public abstract class MermaidChart : ISceneSource
{
    private DiagramTheme _theme = DiagramTheme.Default;

    private protected MermaidChart()
    {
    }

    /// <summary>Gets or sets the title, drawn above the chart.</summary>
    public string? Title { get; set; }

    /// <summary>Gets or sets the description of the chart for assistive technologies (Mermaid <c>accDescr</c>), which is not drawn.</summary>
    public string? Description { get; set; }

    /// <summary>Gets or sets the accessible name of the chart when it differs from <see cref="Title"/>, which is not drawn (Mermaid <c>accTitle</c>).</summary>
    public string? AccessibleTitle { get; set; }

    /// <summary>Gets or sets the theme of Mermaid whose colors and font the chart uses. The default is <see cref="DiagramTheme.Default"/>.</summary>
    public DiagramTheme Theme
    {
        get => _theme;
        set => _theme = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>Gets or sets the measurer of the texts, or <see langword="null"/> for <see cref="AfmTextMeasurer.Default"/>.</summary>
    public ITextMeasurer? TextMeasurer { get; set; }

    /// <summary>Creates the plot that draws the chart. Each call returns a new plot, which you can change before drawing it.</summary>
    /// <returns>The plot, with the title, the description and the colors of <see cref="Theme"/>.</returns>
    public Plot ToPlot()
    {
        var plot = new Plot
        {
            Title = string.IsNullOrEmpty(Title) ? null : Title,
            Description = Description,
            Theme = MermaidChartTheme.Create(Theme),
            TextMeasurer = TextMeasurer,
        };

        Configure(plot);
        return plot;
    }

    /// <summary>Lays the chart out into drawing primitives.</summary>
    /// <param name="textMeasurer">The measurer of the texts, or <see langword="null"/> for <see cref="TextMeasurer"/>.</param>
    /// <returns>The scene.</returns>
    public Scene CreateScene(ITextMeasurer? textMeasurer = null)
    {
        var scene = ToPlot().CreateScene(textMeasurer);
        if (!string.IsNullOrEmpty(AccessibleTitle))
        {
            scene.Title = AccessibleTitle;
        }

        return scene;
    }

    /// <summary>Returns the chart as SVG.</summary>
    /// <param name="options">The options, or <see langword="null"/> for the defaults, with the identifier prefix <c>chart</c>.</param>
    /// <returns>The SVG document, without an XML declaration.</returns>
    public string ToSvg(SvgRenderOptions? options = null) => SvgSceneRenderer.Render(CreateScene(), options ?? CreateDefaultSvgOptions());

    /// <summary>Writes the chart as SVG.</summary>
    /// <param name="writer">The destination.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults, with the identifier prefix <c>chart</c>.</param>
    public void WriteSvg(TextWriter writer, SvgRenderOptions? options = null) => SvgSceneRenderer.Render(CreateScene(), writer, options ?? CreateDefaultSvgOptions());

    /// <summary>Writes the chart as SVG, encoded in UTF-8 without a byte order mark.</summary>
    /// <param name="stream">The destination. It is left open.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults, with the identifier prefix <c>chart</c>.</param>
    public void WriteSvg(Stream stream, SvgRenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), bufferSize: 4096, leaveOpen: true);
        WriteSvg(writer, options);
    }

    /// <summary>Adds the marks, the scales and the axes of the chart to a plot that already has its title, description and theme.</summary>
    private protected abstract void Configure(Plot plot);

    private static SvgRenderOptions CreateDefaultSvgOptions() => new() { IdPrefix = "chart" };
}
