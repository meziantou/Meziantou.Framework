namespace Meziantou.Framework.VectorGraphics;

/// <summary>The rectangles that contain what the elements draw, to size the regions of the filters.</summary>
internal static class SceneGeometry
{
    /// <summary>Gets a rectangle that contains what an element draws, in the coordinates of its parent.</summary>
    /// <returns><see langword="false"/> when the element has a text, whose size is not known without measuring it; otherwise <see langword="true"/>, with <see langword="null"/> when it draws nothing.</returns>
    public static bool TryGetBounds(SceneElement element, out SceneBounds? bounds)
    {
        bounds = null;
        // A stroke extends by half its width, and a miter join by half the miter limit times the width (4 when the limit is not valid)
        var stroke = element is SceneShape { Stroke: not null } shape && shape.StrokeWidth > 0 && double.IsFinite(shape.StrokeWidth)
            ? shape.StrokeWidth / 2 * Math.Max(shape.LineJoin is SceneLineJoin.Miter ? (shape.MiterLimit >= 1 && double.IsFinite(shape.MiterLimit) ? shape.MiterLimit : 4) : 1, shape.LineCap is SceneLineCap.Square ? Math.Sqrt(2) : 1)
            : 0;
        switch (element)
        {
            case SceneGroup group:
                // A child draws its filter too: its blur and its shadow extend it
                foreach (var child in group.Children)
                {
                    if (!TryGetBounds(child, out var childBounds))
                        return false;

                    bounds = Union(bounds, childBounds is { } childBox ? WithFilter(childBox, child.Filter) : null);
                }

                if (bounds is { } box && group.Transform is { } transform)
                {
                    bounds = Transform(box, transform);
                }

                return true;

            case SceneRectangle rectangle:
                bounds = Inflate(new SceneBounds(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height), stroke);
                return true;

            case SceneCircle circle:
                bounds = Inflate(new SceneBounds(circle.CenterX - circle.Radius, circle.CenterY - circle.Radius, 2 * circle.Radius, 2 * circle.Radius), stroke);
                return true;

            case SceneLine line:
                bounds = Inflate(new SceneBounds(Math.Min(line.X1, line.X2), Math.Min(line.Y1, line.Y2), Math.Abs(line.X2 - line.X1), Math.Abs(line.Y2 - line.Y1)), stroke);
                return true;

            case ScenePath path:
                // A cubic curve stays in the convex hull of its control points
                foreach (var command in path.Data.ToCubicCurves().Commands)
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

                bounds = bounds is { } pathBounds ? Inflate(pathBounds, stroke) : null;
                return true;

            case SceneImage image:
                bounds = new SceneBounds(image.X, image.Y, image.Width, image.Height);
                return true;

            default:
                return false;
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
