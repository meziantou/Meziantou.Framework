namespace Meziantou.Framework.Mermaid.Internals.QuadrantChartDiagrams;

/// <summary>The colors a <see cref="QuadrantChart"/> is drawn with.</summary>
internal sealed record QuadrantChartResolvedColors(
    SceneColor Quadrant1Fill,
    SceneColor Quadrant2Fill,
    SceneColor Quadrant3Fill,
    SceneColor Quadrant4Fill,
    SceneColor Quadrant1TextFill,
    SceneColor Quadrant2TextFill,
    SceneColor Quadrant3TextFill,
    SceneColor Quadrant4TextFill,
    SceneColor PointFill,
    SceneColor PointTextFill,
    SceneColor XAxisTextFill,
    SceneColor YAxisTextFill,
    SceneColor InternalBorderStrokeFill,
    SceneColor ExternalBorderStrokeFill);
