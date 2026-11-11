namespace Meziantou.Framework.VectorGraphics;

/// <summary>The rectangles that contain what the elements draw: the filter regions of SVG, the groups and the links of PDF, the filters of the images.</summary>
internal static class SceneGeometry
{
    /// <summary>Gets a rectangle that contains what an element draws, in the coordinates of its parent, or <see langword="null"/> when it draws nothing.</summary>
    /// <remarks>
    /// The strokes extend by half their width, the miter joins by half the miter limit times the width, the square caps by
    /// half the width times the square root of 2. The filters of the children (blurs, shadows) extend their bounds; the
    /// filter of the element does not. The rectangle of a text, before its rotation, is given by <paramref name="measureText"/>.
    /// </remarks>
    public static SceneBounds? GetBounds(SceneElement element, Func<SceneText, SceneBounds?> measureText, bool applyClips = true)
    {
        if (!(element.Opacity > 0))
            return null;

        if (element is SceneGroup || element.Transform is not { } elementTransform)
            return GetUntransformedBounds(element, measureText, applyClips);

        return GetUntransformedBounds(element, measureText, applyClips) is { } untransformed ? Transform(untransformed, elementTransform) : null;
    }

    // The bounds of an element before its transform; a group applies its transform before its clips
    private static SceneBounds? GetUntransformedBounds(SceneElement element, Func<SceneText, SceneBounds?> measureText, bool applyClips)
    {
        var stroke = element is SceneShape { Stroke: not null } shape && shape.StrokeWidth > 0 && double.IsFinite(shape.StrokeWidth) ? StrokeExtent(shape) : 0;
        switch (element)
        {
            case SceneGroup group:
                SceneBounds? bounds = null;
                foreach (var child in group.Children)
                {
                    if (GetBounds(child, measureText) is { } childBounds)
                    {
                        bounds = Union(bounds, WithFilter(childBounds, child.Filter));
                    }
                }

                if (bounds is not { } box)
                    return null;

                if (group.Transform is { } transform)
                {
                    box = Transform(box, transform);
                }

                if (applyClips && group.Clip is { } clip)
                {
                    box = Intersect(box, clip);
                }

                if (applyClips && group.ClipPath is { } clipPath)
                {
                    if (GetPathBounds(clipPath) is not { } clipBounds)
                        return null;

                    box = Intersect(box, clipBounds);
                }

                return box.Width > 0 && box.Height > 0 ? box : null;

            case SceneRectangle rectangle:
                return Inflate(new SceneBounds(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height), stroke);

            case SceneCircle circle:
                return Inflate(new SceneBounds(circle.CenterX - circle.Radius, circle.CenterY - circle.Radius, 2 * circle.Radius, 2 * circle.Radius), stroke);

            case SceneLine line:
                return Inflate(new SceneBounds(Math.Min(line.X1, line.X2), Math.Min(line.Y1, line.Y2), Math.Abs(line.X2 - line.X1), Math.Abs(line.Y2 - line.Y1)), stroke);

            case ScenePath path:
                return GetPathBounds(path.Data) is { } pathBounds ? Inflate(pathBounds, stroke) : null;

            case SceneImage image:
                return new SceneBounds(image.X, image.Y, image.Width, image.Height);

            // A text on a path is within its size of the path: its glyphs are above the path, and its descenders below
            case SceneText { Path: { } textPath } text:
                if (text.Text.Length is 0 || GetPathBounds(textPath) is not { } textPathBounds)
                    return null;

                var size = text.Spans.Count is 0 ? text.Font.Size : text.Spans.Max(span => (span.Font ?? text.Font).Size);
                return Inflate(textPathBounds, double.IsFinite(size) ? Math.Abs(size) : 0);

            case SceneText text:
                if (measureText(text) is not { } textBounds)
                    return null;

                return text.Rotation == 0 ? textBounds : Transform(textBounds, SceneTransform.Rotate(text.Rotation, text.X, text.Y));

            default:
                return null;
        }
    }

    /// <summary>Gets a rectangle that contains what an element draws with its filter, from what it draws without it.</summary>
    public static SceneBounds WithFilter(SceneBounds bounds, SceneFilter? filter)
    {
        switch (filter)
        {
            case SceneBlurFilter blur:
                return Inflate(bounds, 3 * Deviation(blur.StandardDeviation));

            case SceneDropShadowFilter shadow:
                var shadowBounds = Inflate(bounds, 3 * Deviation(shadow.StandardDeviation));
                var offsetX = double.IsFinite(shadow.OffsetX) ? shadow.OffsetX : 0;
                var offsetY = double.IsFinite(shadow.OffsetY) ? shadow.OffsetY : 0;
                return Union(bounds, new SceneBounds(shadowBounds.X + offsetX, shadowBounds.Y + offsetY, shadowBounds.Width, shadowBounds.Height))!.Value;

            default:
                return bounds;
        }

        static double Deviation(double deviation) => deviation > 0 && double.IsFinite(deviation) ? deviation : 0;
    }

    public static SceneBounds Inflate(SceneBounds bounds, double amount) => amount > 0 ? new SceneBounds(bounds.X - amount, bounds.Y - amount, bounds.Width + (2 * amount), bounds.Height + (2 * amount)) : bounds;

    // A stroke extends by half its width; a miter join by half the miter limit times the width (4 when the limit is not
    // valid), a square cap by half the width times the square root of 2 (a corner of its square)
    private static double StrokeExtent(SceneShape shape)
    {
        var miterLimit = shape.MiterLimit >= 1 && double.IsFinite(shape.MiterLimit) ? shape.MiterLimit : 4;
        var factor = Math.Max(shape.LineJoin is SceneLineJoin.Miter ? miterLimit : 1, shape.LineCap is SceneLineCap.Square ? Math.Sqrt(2) : 1);
        return shape.StrokeWidth / 2 * factor;
    }

    // A cubic curve stays in the convex hull of its control points
    private static SceneBounds? GetPathBounds(PathData data)
    {
        SceneBounds? bounds = null;
        foreach (var command in data.ToCubicCurves().Commands)
        {
            if (command.Kind is PathCommandKind.Close)
                continue;

            bounds = Union(bounds, new SceneBounds(command.X, command.Y, 0, 0));
            if (command.Kind is PathCommandKind.CubicTo)
            {
                bounds = Union(bounds, new SceneBounds(command.X1, command.Y1, 0, 0));
                bounds = Union(bounds, new SceneBounds(command.X2, command.Y2, 0, 0));
            }
        }

        return bounds;
    }

    private static SceneBounds? Union(SceneBounds? first, SceneBounds? second)
    {
        if (first is not { } a)
            return second;

        if (second is not { } b)
            return a;

        var left = Math.Min(a.X, b.X);
        var top = Math.Min(a.Y, b.Y);
        return new SceneBounds(left, top, Math.Max(a.Right, b.Right) - left, Math.Max(a.Bottom, b.Bottom) - top);
    }

    private static SceneBounds Intersect(SceneBounds a, SceneBounds b)
    {
        var left = Math.Max(a.X, b.X);
        var top = Math.Max(a.Y, b.Y);
        return new SceneBounds(left, top, Math.Min(a.Right, b.Right) - left, Math.Min(a.Bottom, b.Bottom) - top);
    }

    private static SceneBounds Transform(SceneBounds bounds, SceneTransform transform)
    {
        var (x1, y1) = transform.Apply(bounds.X, bounds.Y);
        var (x2, y2) = transform.Apply(bounds.Right, bounds.Y);
        var (x3, y3) = transform.Apply(bounds.X, bounds.Bottom);
        var (x4, y4) = transform.Apply(bounds.Right, bounds.Bottom);
        var left = Math.Min(Math.Min(x1, x2), Math.Min(x3, x4));
        var top = Math.Min(Math.Min(y1, y2), Math.Min(y3, y4));
        return new SceneBounds(left, top, Math.Max(Math.Max(x1, x2), Math.Max(x3, x4)) - left, Math.Max(Math.Max(y1, y2), Math.Max(y3, y4)) - top);
    }
}
