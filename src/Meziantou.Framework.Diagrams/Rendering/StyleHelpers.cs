using Meziantou.Framework.Diagrams.Layout;

namespace Meziantou.Framework.Diagrams.Rendering;

internal static class StyleHelpers
{
    public static LayoutDirection ToLayout(this DiagramDirection direction) => direction switch
    {
        DiagramDirection.BottomToTop => LayoutDirection.BottomToTop,
        DiagramDirection.LeftToRight => LayoutDirection.LeftToRight,
        DiagramDirection.RightToLeft => LayoutDirection.RightToLeft,
        _ => LayoutDirection.TopToBottom,
    };

    public static bool IsHorizontal(this DiagramDirection direction) => direction is DiagramDirection.LeftToRight or DiagramDirection.RightToLeft;

    /// <summary>Returns a valid CSS class name, or <see langword="null"/>.</summary>
    public static string? ToCssClass(IEnumerable<string> classes, string prefix)
    {
        var result = prefix;
        foreach (var name in classes)
        {
            if (name.Length > 0 && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
            {
                result += " " + name;
            }
        }

        return result;
    }

    /// <summary>Draws the outline, the underlays and the decorations of a shape centered on a point.</summary>
    public static void DrawShape(SceneGroup group, ShapeDrawing shape, double x, double y, SceneColor fill, SceneColor stroke, double strokeWidth, IReadOnlyList<double>? dashes)
    {
        var transform = SceneTransform.Translate(x, y);
        var shapeGroup = group.Add(new SceneGroup { Transform = transform });
        foreach (var underlay in shape.Underlays)
        {
            shapeGroup.Add(new ScenePath(underlay) { Fill = fill, Stroke = stroke, StrokeWidth = strokeWidth, StrokeDashArray = dashes });
        }

        if (shape.FillWithLineColor)
        {
            shapeGroup.Add(new ScenePath(shape.Outline) { Fill = stroke });
        }
        else if (shape.FillOutline || shape.StrokeOutline)
        {
            shapeGroup.Add(new ScenePath(shape.Outline)
            {
                Fill = shape.FillOutline ? (ScenePaint)fill : null,
                Stroke = shape.StrokeOutline ? (ScenePaint)stroke : null,
                StrokeWidth = strokeWidth,
                StrokeDashArray = dashes,
            });
        }

        foreach (var decoration in shape.FilledDecorations)
        {
            shapeGroup.Add(new ScenePath(decoration) { Fill = stroke });
        }

        foreach (var decoration in shape.Decorations)
        {
            shapeGroup.Add(new ScenePath(decoration) { Stroke = stroke, StrokeWidth = strokeWidth });
        }
    }
}
