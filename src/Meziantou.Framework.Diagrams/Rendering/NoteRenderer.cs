namespace Meziantou.Framework.Diagrams.Rendering;

/// <summary>Draws the notes: a box in the colors of the notes of the theme.</summary>
internal static class NoteRenderer
{
    private const double Padding = 8;

    public static GraphNode CreateNote(GraphModel model, DiagramRenderContext context, DiagramText text, GraphCluster? cluster)
    {
        var block = context.Text(text, maxWidth: 200);
        var width = block.Width + (2 * Padding);
        var height = block.Height + (2 * Padding);
        var node = model.AddNode(width, height, cluster);
        node.Draw = (target, x, y) => Draw(target, context, block, x, y);
        return node;
    }

    public static void Draw(SceneGroup target, DiagramRenderContext context, TextBlock block, double x, double y)
    {
        var theme = context.Theme;
        var width = block.Width + (2 * Padding);
        var height = block.Height + (2 * Padding);
        var noteGroup = target.Add(new SceneGroup { CssClass = "note" });
        noteGroup.Add(new SceneRectangle(x - (width / 2), y - (height / 2), width, height) { Fill = theme.NoteBackground, Stroke = theme.NoteBorderColor, StrokeWidth = theme.StrokeWidth });
        block.Draw(noteGroup, x, y, theme.NoteTextColor);
    }

    public static (double Width, double Height) Measure(TextBlock block) => (block.Width + (2 * Padding), block.Height + (2 * Padding));
}
