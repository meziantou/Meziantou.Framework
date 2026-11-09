using System.Text;
using Meziantou.Framework.Diagrams.Rendering;

namespace Meziantou.Framework.Diagrams;

/// <summary>
/// A diagram: a flowchart, a class diagram, a state diagram... <see cref="CreateScene"/> lays it out into a
/// <see cref="Scene"/>; <see cref="ToSvg"/> writes it as SVG. The output is deterministic: the same diagram always gives
/// the same SVG.
/// </summary>
public abstract class Diagram : ISceneSource
{
    private DiagramTheme _theme = DiagramTheme.Default;
    private double _padding = 8;

    private protected Diagram()
    {
    }

    /// <summary>Gets or sets the title, drawn above the diagram. It is also the accessible name of the SVG.</summary>
    public string? Title { get; set; }

    /// <summary>Gets or sets the accessible description of the diagram (Mermaid <c>accDescr</c>).</summary>
    public string? Description { get; set; }

    /// <summary>Gets or sets the accessible name of the diagram when it differs from <see cref="Title"/>, which is not drawn (Mermaid <c>accTitle</c>).</summary>
    public string? AccessibleTitle { get; set; }

    /// <summary>Gets or sets the colors and the font of the diagram. The default is <see cref="DiagramTheme.Default"/>.</summary>
    public DiagramTheme Theme
    {
        get => _theme;
        set => _theme = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>Gets or sets the measurer of the texts, or <see langword="null"/> for <see cref="AfmTextMeasurer.Default"/>.</summary>
    public ITextMeasurer? TextMeasurer { get; set; }

    /// <summary>Gets or sets the space around the diagram, in pixels. The default is 8.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or not finite.</exception>
    public double Padding
    {
        get => _padding;
        set
        {
            if (!(value >= 0) || !double.IsFinite(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The padding must be a positive finite number.");

            _padding = value;
        }
    }

    /// <summary>Lays the diagram out into drawing primitives.</summary>
    /// <param name="textMeasurer">The measurer of the texts, or <see langword="null"/> for <see cref="TextMeasurer"/>.</param>
    /// <returns>The scene.</returns>
    public Scene CreateScene(ITextMeasurer? textMeasurer = null)
    {
        var measurer = textMeasurer ?? TextMeasurer ?? AfmTextMeasurer.Default;
        var context = new DiagramRenderContext(Theme, measurer);
        var content = new SceneGroup { CssClass = "diagram" };
        var bounds = Render(context, content);
        if (!IsFinite(bounds))
        {
            bounds = default;
        }

        TextBlock? title = string.IsNullOrEmpty(Title) ? null : context.Text(Title, Theme.TitleFontSize);
        const double TitleGap = 10;
        var titleHeight = title is null ? 0 : title.Height + TitleGap;
        var contentWidth = Math.Max(bounds.Width, title?.Width ?? 0);
        var width = contentWidth + (2 * Padding);
        var height = bounds.Height + titleHeight + (2 * Padding);
        var scene = new Scene(Math.Max(width, 1), Math.Max(height, 1), Theme.FontFamily ?? measurer.FontFamily)
        {
            Title = string.IsNullOrEmpty(AccessibleTitle) ? Title : AccessibleTitle,
            Description = Description,
            Background = Theme.Background,
        };

        if (title is not null)
        {
            var titleGroup = scene.Root.Add(new SceneGroup { CssClass = "title" });
            title.Draw(titleGroup, width / 2, Padding + (title.Height / 2), Theme.TitleColor);
        }

        content.Transform = SceneTransform.Translate(Padding + ((contentWidth - bounds.Width) / 2) - bounds.X, Padding + titleHeight - bounds.Y);
        scene.Root.Add(content);
        return scene;
    }

    /// <summary>Returns the diagram as SVG.</summary>
    /// <param name="options">The options, or <see langword="null"/> for the defaults, with the identifier prefix <c>diagram</c>.</param>
    /// <returns>The SVG document, without an XML declaration.</returns>
    public string ToSvg(SvgRenderOptions? options = null) => SvgSceneRenderer.Render(CreateScene(), options ?? CreateDefaultSvgOptions());

    /// <summary>Writes the diagram as SVG.</summary>
    /// <param name="writer">The destination.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults, with the identifier prefix <c>diagram</c>.</param>
    public void WriteSvg(TextWriter writer, SvgRenderOptions? options = null) => SvgSceneRenderer.Render(CreateScene(), writer, options ?? CreateDefaultSvgOptions());

    /// <summary>Writes the diagram as SVG, encoded in UTF-8 without a byte order mark.</summary>
    /// <param name="stream">The destination. It is left open.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults, with the identifier prefix <c>diagram</c>.</param>
    public void WriteSvg(Stream stream, SvgRenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), bufferSize: 4096, leaveOpen: true);
        WriteSvg(writer, options);
    }

    /// <summary>Draws the content of the diagram into a group.</summary>
    /// <returns>The rectangle the content covers.</returns>
    private protected abstract SceneBounds Render(DiagramRenderContext context, SceneGroup group);

    private static SvgRenderOptions CreateDefaultSvgOptions() => new() { IdPrefix = "diagram" };

    private static bool IsFinite(SceneBounds bounds) => double.IsFinite(bounds.X) && double.IsFinite(bounds.Y) && double.IsFinite(bounds.Width) && double.IsFinite(bounds.Height) && bounds.Width >= 0 && bounds.Height >= 0;
}
