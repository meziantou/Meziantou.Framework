namespace Meziantou.Framework.Diagrams.Rendering;

/// <summary>
/// Draws the user journeys as Mermaid: the legend of the actors on the left, the sections above their tasks, a face per
/// task at the height of its score, and an arrow for the time.
/// </summary>
internal static class UserJourneyDiagramRenderer
{
    private const double TaskWidth = 150;
    private const double TaskHeight = 50;
    private const double TaskMargin = 50;
    private const double SectionY = 50;
    private const double TaskY = 110;
    private const double FaceTop = 300;
    private const double FaceStep = 30;
    private const double LineBottom = 450;
    private const double FaceRadius = 15;
    private const double ActorRadius = 7;
    private const double LegendLabelWidth = 360;
    private const double LeftMargin = 150;

    // The colors of the actors and of the faces of Mermaid
    private static readonly SceneColor[] ActorColors = [(SceneColor)"#8FBC8F", (SceneColor)"#7CFC00", (SceneColor)"#00FFFF", (SceneColor)"#20B2AA", (SceneColor)"#B0E0E6", (SceneColor)"#FFFFE0"];
    private static readonly SceneColor FaceColor = (SceneColor)"#FFF8DC";
    private static readonly SceneColor FaceStroke = (SceneColor)"#999999";
    private static readonly SceneColor DetailColor = (SceneColor)"#666666";

    public static SceneBounds Render(UserJourneyDiagram diagram, DiagramRenderContext context, SceneGroup group)
    {
        var theme = context.Theme;
        var bounds = new BoundsBuilder();
        var actors = diagram.GetActors();
        var actorColors = new Dictionary<string, SceneColor>(StringComparer.Ordinal);
        for (var i = 0; i < actors.Count; i++)
        {
            actorColors[actors[i]] = ActorColors[i % ActorColors.Length];
        }

        // The legend: a dot and the name of each actor
        var legend = group.Add(new SceneGroup { CssClass = "legend" });
        var legendWidth = 0.0;
        var y = 60.0;
        foreach (var actor in actors)
        {
            var label = context.Text(actor, maxWidth: LegendLabelWidth);
            legend.Add(new SceneCircle(20, y, ActorRadius) { Fill = actorColors[actor], Stroke = SceneColor.Black, StrokeWidth = 1 });
            label.Draw(legend, 40 + (label.Width / 2), y - 7 + (label.Height / 2), theme.TextColor);
            bounds.Add(new SceneBounds(20 - ActorRadius, y - ActorRadius, 40 + label.Width - (20 - ActorRadius), Math.Max(2 * ActorRadius, label.Height)));
            if (label.Width > 75)
            {
                legendWidth = Math.Max(legendWidth, label.Width);
            }

            y += Math.Max(20, label.Height);
        }

        var left = LeftMargin + legendWidth;
        var fills = GetSectionFills(theme);
        var tasksGroup = group.Add(new SceneGroup { CssClass = "tasks" });
        var index = 0;
        var sectionNumber = 0;
        var lastX = left;
        foreach (var section in diagram.Sections)
        {
            if (section.Tasks.Count is 0)
                continue;

            var fill = fills[sectionNumber % fills.Length];
            var sectionX = left + (index * (TaskMargin + TaskWidth));
            var sectionWidth = (TaskWidth * section.Tasks.Count) + (TaskMargin * (section.Tasks.Count - 1));
            var sectionGroup = tasksGroup.Add(new SceneGroup { CssClass = "journey-section section-type-" + (sectionNumber % fills.Length).ToString(System.Globalization.CultureInfo.InvariantCulture) });
            sectionGroup.Add(new SceneRectangle(sectionX, SectionY, sectionWidth, TaskHeight) { Fill = fill, Stroke = DetailColor, StrokeWidth = 1, CornerRadius = 3 });
            context.Text(section.Name, context.FontSize * 0.875, maxWidth: sectionWidth - 10).Draw(sectionGroup, sectionX + (sectionWidth / 2), SectionY + (TaskHeight / 2), theme.PrimaryTextColor);
            bounds.Add(new SceneBounds(sectionX, SectionY, sectionWidth, TaskHeight));
            foreach (var task in section.Tasks)
            {
                var x = left + (index * (TaskMargin + TaskWidth));
                DrawTask(tasksGroup, context, task, x, fill, actorColors);
                bounds.Add(new SceneBounds(x, TaskY, TaskWidth, LineBottom - TaskY));
                lastX = x;
                index++;
            }

            sectionNumber++;
        }

        // The arrow of the time, under the sections, as far as the last task
        if (index > 0)
        {
            var arrowEnd = lastX + TaskWidth + 46;
            var arrowY = TaskHeight * 4;
            var arrow = group.Add(new SceneGroup { CssClass = "time" });
            arrow.Add(new SceneLine(left, arrowY, arrowEnd - 12, arrowY) { Stroke = theme.TextColor, StrokeWidth = 4 });
            arrow.Add(new ScenePath(Geometry.Polygon((arrowEnd, arrowY), (arrowEnd - 14, arrowY - 7), (arrowEnd - 14, arrowY + 7))) { Fill = theme.TextColor });
            bounds.Add(new SceneBounds(left, arrowY - 7, arrowEnd - left, 14));
        }

        return bounds.ToBounds();
    }

    private static void DrawTask(SceneGroup target, DiagramRenderContext context, UserJourneyTask task, double x, SceneColor fill, Dictionary<string, SceneColor> actorColors)
    {
        var center = x + (TaskWidth / 2);
        var taskGroup = target.Add(new SceneGroup { CssClass = "task" });
        taskGroup.Add(new SceneLine(center, TaskY, center, LineBottom) { Stroke = DetailColor, StrokeWidth = 1, StrokeDashArray = [4, 2] });

        // The face: the higher the score, the higher the face; it smiles above 3 and is sad below 3
        var score = double.IsFinite(task.Score) ? task.Score : 0;
        var faceY = FaceTop + ((5 - Math.Clamp(score, 0, 5)) * FaceStep);
        var face = taskGroup.Add(new SceneGroup { CssClass = "face" });
        face.Add(new SceneCircle(center, faceY, FaceRadius) { Fill = FaceColor, Stroke = FaceStroke, StrokeWidth = 2 });
        face.Add(new SceneCircle(center - (FaceRadius / 3), faceY - (FaceRadius / 3), 1.5) { Fill = DetailColor, Stroke = DetailColor, StrokeWidth = 2 });
        face.Add(new SceneCircle(center + (FaceRadius / 3), faceY - (FaceRadius / 3), 1.5) { Fill = DetailColor, Stroke = DetailColor, StrokeWidth = 2 });
        var mouth = FaceRadius / 2;
        if (score > 3)
        {
            face.Add(new ScenePath(new PathBuilder().MoveTo(center - mouth, faceY + 2).ArcTo(mouth, mouth, 0, false, false, center + mouth, faceY + 2).Build()) { Stroke = DetailColor, StrokeWidth = 1.5, CssClass = "mouth" });
        }
        else if (score < 3)
        {
            face.Add(new ScenePath(new PathBuilder().MoveTo(center - mouth, faceY + 7).ArcTo(mouth, mouth, 0, false, true, center + mouth, faceY + 7).Build()) { Stroke = DetailColor, StrokeWidth = 1.5, CssClass = "mouth" });
        }
        else
        {
            face.Add(new SceneLine(center - 5, faceY + 7, center + 5, faceY + 7) { Stroke = DetailColor, StrokeWidth = 1, CssClass = "mouth" });
        }

        taskGroup.Add(new SceneRectangle(x, TaskY, TaskWidth, TaskHeight) { Fill = fill, Stroke = DetailColor, StrokeWidth = 1, CornerRadius = 3 });
        var actorX = x + 14;
        foreach (var actor in task.Actors)
        {
            if (actorColors.TryGetValue(actor, out var color))
            {
                taskGroup.Add(new SceneCircle(actorX, TaskY, ActorRadius) { Fill = color, Stroke = SceneColor.Black, StrokeWidth = 1, Tooltip = actor });
                actorX += 10;
            }
        }

        context.Text(task.Name, context.FontSize * 0.875, maxWidth: TaskWidth - 10).Draw(taskGroup, center, TaskY + (TaskHeight / 2), context.Theme.PrimaryTextColor);
    }

    /// <summary>Returns the colors of the sections, as the <c>fillType</c> variables of Mermaid: the primary and the secondary colors, turned by 64 and 128 degrees.</summary>
    internal static SceneColor[] GetSectionFills(DiagramTheme theme) =>
    [
        theme.PrimaryColor,
        theme.SecondaryColor,
        RotateHue(theme.PrimaryColor, 64),
        RotateHue(theme.SecondaryColor, 64),
        RotateHue(theme.PrimaryColor, -64),
        RotateHue(theme.SecondaryColor, -64),
        RotateHue(theme.PrimaryColor, 128),
    ];

    private static SceneColor RotateHue(SceneColor color, double degrees)
    {
        var r = color.R / 255.0;
        var g = color.G / 255.0;
        var b = color.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var lightness = (max + min) / 2;
        if (max == min)
            return color;

        var delta = max - min;
        var saturation = lightness > 0.5 ? delta / (2 - max - min) : delta / (max + min);
        double hue;
        if (max == r)
        {
            hue = ((g - b) / delta) + (g < b ? 6 : 0);
        }
        else if (max == g)
        {
            hue = ((b - r) / delta) + 2;
        }
        else
        {
            hue = ((r - g) / delta) + 4;
        }

        return SceneColor.FromHsl((hue * 60) + degrees, saturation, lightness, color.A / 255.0);
    }
}
