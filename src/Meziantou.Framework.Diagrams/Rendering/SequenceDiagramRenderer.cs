namespace Meziantou.Framework.Diagrams.Rendering;

/// <summary>
/// Draws the sequence diagrams as Mermaid: the participants in a row at the top (and the bottom), their lifelines, and
/// the steps from the top to the bottom. The participants are as far apart as their messages and notes need.
/// </summary>
internal static class SequenceDiagramRenderer
{
    public static SceneBounds Render(SequenceDiagram diagram, DiagramRenderContext context, SceneGroup group) => new SequenceLayout(diagram, context, group).Run();
}
