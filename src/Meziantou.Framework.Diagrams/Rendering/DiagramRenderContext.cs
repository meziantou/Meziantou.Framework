namespace Meziantou.Framework.Diagrams.Rendering;

/// <summary>The state of the drawing of a diagram: its theme and its text measurer.</summary>
internal sealed class DiagramRenderContext(DiagramTheme theme, ITextMeasurer measurer)
{
    public DiagramTheme Theme { get; } = theme;

    public ITextMeasurer Measurer { get; } = measurer;

    public double FontSize => Theme.FontSize;

    /// <summary>Gets the background the hollow markers are filled with.</summary>
    public SceneColor Background => Theme.Background ?? (Theme.IsDark ? SceneColor.Black : SceneColor.White);

    public TextBlock Text(DiagramText? text, double? fontSize = null, bool bold = false, bool italic = false, double? maxWidth = null) => TextBlock.Create(text, Measurer, fontSize ?? FontSize, bold, italic, maxWidth);

    public TextBlock Text(string? text, double? fontSize = null, bool bold = false, bool italic = false, double? maxWidth = null) => TextBlock.Create(text is null ? null : new DiagramText(text), Measurer, fontSize ?? FontSize, bold, italic, maxWidth);
}
