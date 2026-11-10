namespace Meziantou.Framework.Diagrams.Rendering;

/// <summary>
/// The icons of the architecture diagrams that Mermaid has built in, drawn from their SVG in a square of 80 pixels:
/// white line art on a blue square.
/// </summary>
internal static class ArchitectureIcons
{
    public static readonly SceneColor Background = (SceneColor)"#087ebf";

    public static bool IsBuiltIn(string? name) => name is "cloud" or "database" or "disk" or "internet" or "server";

    /// <summary>Draws an icon in a square; an icon that is not built in is a question mark, and no icon an empty square.</summary>
    public static void Draw(SceneGroup target, string? name, double x, double y, double size, ITextMeasurer measurer)
    {
        var scale = size / 80;
        var group = target.Add(new SceneGroup { Transform = new SceneTransform(scale, 0, 0, scale, x, y), CssClass = "icon" });
        group.Add(new SceneRectangle(0, 0, 80, 80) { Fill = Background });
        switch (name)
        {
            case "database":
                Stroke(group, new PathBuilder().MoveTo(20, 57.86).CubicTo(20, 61.8, 28.95, 65, 40, 65).CubicTo(51.05, 65, 60, 61.8, 60, 57.86).Build());
                Stroke(group, new PathBuilder().MoveTo(20, 45.95).CubicTo(20, 49.89, 28.95, 53.09, 40, 53.09).CubicTo(51.05, 53.09, 60, 49.89, 60, 45.95).Build());
                Stroke(group, new PathBuilder().MoveTo(20, 34.05).CubicTo(20, 37.99, 28.95, 41.19, 40, 41.19).CubicTo(51.05, 41.19, 60, 37.99, 60, 34.05).Build());
                Stroke(group, Geometry.Ellipse(40, 22.14, 20, 7.14));
                Stroke(group, new PathBuilder().MoveTo(20, 57.86).LineTo(20, 22.14).Build());
                Stroke(group, new PathBuilder().MoveTo(60, 57.86).LineTo(60, 22.14).Build());
                break;
            case "server":
                Stroke(group, Rectangle(17.5, 17.5, 45, 45));
                Stroke(group, new PathBuilder().MoveTo(17.5, 32.5).LineTo(62.5, 32.5).Build());
                Stroke(group, new PathBuilder().MoveTo(17.5, 47.5).LineTo(62.5, 47.5).Build());
                Fill(group, new PathBuilder().MoveTo(56.25, 25).CubicTo(56.25, 25.27, 55.8, 25.5, 55.25, 25.5).LineTo(44.75, 25.5).CubicTo(44.2, 25.5, 43.75, 25.27, 43.75, 25).CubicTo(43.75, 24.73, 44.2, 24.5, 44.75, 24.5).LineTo(55.25, 24.5).CubicTo(55.8, 24.5, 56.25, 24.73, 56.25, 25).Close().Build());
                Stroke(group, new PathBuilder().MoveTo(56.25, 25).CubicTo(56.25, 25.27, 55.8, 25.5, 55.25, 25.5).LineTo(44.75, 25.5).CubicTo(44.2, 25.5, 43.75, 25.27, 43.75, 25).CubicTo(43.75, 24.73, 44.2, 24.5, 44.75, 24.5).LineTo(55.25, 24.5).CubicTo(55.8, 24.5, 56.25, 24.73, 56.25, 25).Close().Build());
                Fill(group, new PathBuilder().MoveTo(56.25, 40).CubicTo(56.25, 40.27, 55.8, 40.5, 55.25, 40.5).LineTo(44.75, 40.5).CubicTo(44.2, 40.5, 43.75, 40.27, 43.75, 40).CubicTo(43.75, 39.73, 44.2, 39.5, 44.75, 39.5).LineTo(55.25, 39.5).CubicTo(55.8, 39.5, 56.25, 39.73, 56.25, 40).Close().Build());
                Stroke(group, new PathBuilder().MoveTo(56.25, 40).CubicTo(56.25, 40.27, 55.8, 40.5, 55.25, 40.5).LineTo(44.75, 40.5).CubicTo(44.2, 40.5, 43.75, 40.27, 43.75, 40).CubicTo(43.75, 39.73, 44.2, 39.5, 44.75, 39.5).LineTo(55.25, 39.5).CubicTo(55.8, 39.5, 56.25, 39.73, 56.25, 40).Close().Build());
                Fill(group, new PathBuilder().MoveTo(56.25, 55).CubicTo(56.25, 55.27, 55.8, 55.5, 55.25, 55.5).LineTo(44.75, 55.5).CubicTo(44.2, 55.5, 43.75, 55.27, 43.75, 55).CubicTo(43.75, 54.73, 44.2, 54.5, 44.75, 54.5).LineTo(55.25, 54.5).CubicTo(55.8, 54.5, 56.25, 54.73, 56.25, 55).Close().Build());
                Stroke(group, new PathBuilder().MoveTo(56.25, 55).CubicTo(56.25, 55.27, 55.8, 55.5, 55.25, 55.5).LineTo(44.75, 55.5).CubicTo(44.2, 55.5, 43.75, 55.27, 43.75, 55).CubicTo(43.75, 54.73, 44.2, 54.5, 44.75, 54.5).LineTo(55.25, 54.5).CubicTo(55.8, 54.5, 56.25, 54.73, 56.25, 55).Close().Build());
                Fill(group, Geometry.Ellipse(32.5, 25, 0.75, 0.75));
                Fill(group, Geometry.Ellipse(27.5, 25, 0.75, 0.75));
                Fill(group, Geometry.Ellipse(22.5, 25, 0.75, 0.75));
                Fill(group, Geometry.Ellipse(32.5, 40, 0.75, 0.75));
                Fill(group, Geometry.Ellipse(27.5, 40, 0.75, 0.75));
                Fill(group, Geometry.Ellipse(22.5, 40, 0.75, 0.75));
                Fill(group, Geometry.Ellipse(32.5, 55, 0.75, 0.75));
                Fill(group, Geometry.Ellipse(27.5, 55, 0.75, 0.75));
                Fill(group, Geometry.Ellipse(22.5, 55, 0.75, 0.75));
                break;
            case "disk":
                Stroke(group, Rectangle(20, 15, 40, 50));
                Stroke(group, Geometry.Ellipse(24, 19.17, 0.8, 0.83));
                Stroke(group, Geometry.Ellipse(56, 19.17, 0.8, 0.83));
                Stroke(group, Geometry.Ellipse(24, 60.83, 0.8, 0.83));
                Stroke(group, Geometry.Ellipse(56, 60.83, 0.8, 0.83));
                Stroke(group, Geometry.Ellipse(40, 33.75, 14, 14.58));
                Fill(group, Geometry.Ellipse(40, 33.75, 4, 4.17));
                Fill(group, new PathBuilder().MoveTo(37.51, 42.52).LineTo(32.68, 55.74).CubicTo(32.42, 56.45, 31.58, 56.76, 30.92, 56.38).LineTo(26.74, 53.96).CubicTo(26.08, 53.58, 25.93, 52.7, 26.41, 52.12).LineTo(35.42, 41.32).CubicTo(36.3, 40.27, 37.98, 41.24, 37.51, 42.52).Close().Build());
                break;
            case "internet":
                Stroke(group, Geometry.Ellipse(40, 40, 22.5, 22.5));
                Stroke(group, new PathBuilder().MoveTo(40, 17.5).LineTo(40, 62.5).Build());
                Stroke(group, new PathBuilder().MoveTo(17.5, 40).LineTo(62.5, 40).Build());
                Stroke(group, new PathBuilder().MoveTo(39.99, 17.51).CubicTo(24.71, 28.61, 24.71, 51.39, 39.99, 62.49).Build());
                Stroke(group, new PathBuilder().MoveTo(40.01, 17.51).CubicTo(55.29, 28.61, 55.29, 51.39, 40.01, 62.49).Build());
                Stroke(group, new PathBuilder().MoveTo(19.75, 30.1).LineTo(60.25, 30.1).Build());
                Stroke(group, new PathBuilder().MoveTo(19.75, 49.9).LineTo(60.25, 49.9).Build());
                break;
            case "cloud":
                Stroke(group, new PathBuilder().MoveTo(65, 47.5).CubicTo(65, 50.26, 62.76, 52.5, 60, 52.5).LineTo(20, 52.5).CubicTo(17.24, 52.5, 15, 50.26, 15, 47.5).CubicTo(15, 45.63, 16.03, 43.99, 17.56, 43.14).CubicTo(17.52, 42.93, 17.5, 42.72, 17.5, 42.5).CubicTo(17.5, 39.9, 19.98, 37.76, 23.15, 37.53).CubicTo(24.8, 33.02, 29.49, 29.77, 35, 29.77).CubicTo(35.86, 29.77, 36.69, 29.85, 37.5, 30).CubicTo(39.59, 28.43, 42.19, 27.5, 45, 27.5).CubicTo(51.1, 27.5, 56.19, 31.88, 57.28, 37.67).CubicTo(59.42, 38.23, 61, 40.18, 61, 42.5).CubicTo(61, 42.53, 61, 42.57, 60.99, 42.6).CubicTo(63.28, 43.06, 65, 45.08, 65, 47.5).Close().Build());
                break;
            case null:
                break;
            default:
                // An icon of an icon pack of Mermaid, which is not available here
                group.Add(new SceneText(40, 40 + (measurer.Measure("?", new SceneFont(48, Bold: true)).Ascent / 2) - 4, "?", new SceneFont(48, Bold: true), SceneColor.White) { Anchor = SceneTextAnchor.Middle });
                break;
        }
    }

    private static void Fill(SceneGroup group, PathData path) => group.Add(new ScenePath(path) { Fill = SceneColor.White });

    private static void Stroke(SceneGroup group, PathData path) => group.Add(new ScenePath(path) { Stroke = SceneColor.White, StrokeWidth = 2 });

    private static PathData Rectangle(double x, double y, double width, double height) => new PathBuilder().AddRectangle(x, y, width, height).Build();
}
