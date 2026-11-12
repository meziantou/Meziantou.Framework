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

        SceneBounds? bounds;
        if (element is SceneGroup || element.Transform is not { } elementTransform)
        {
            bounds = GetUntransformedBounds(element, measureText, applyClips);
        }
        else
        {
            bounds = GetUntransformedBounds(element, measureText, applyClips) is { } untransformed ? Transform(untransformed, elementTransform) : null;
        }

        // The clipping path is in the coordinates of the parent, outside of the transform
        if (applyClips && bounds is { } box && element.ClippingPath is { } clippingPath)
        {
            if (GetClipBounds(clippingPath, measureText) is not { } clipBounds)
                return null;

            box = Intersect(box, clipBounds);
            return box.Width > 0 && box.Height > 0 ? box : null;
        }

        return bounds;
    }

    [ThreadStatic]
    private static HashSet<SceneClipPath>? s_clipPaths;

    /// <summary>Gets a rectangle that contains the geometry of a clipping path, cut by its own clipping path, or <see langword="null"/> when it leaves nothing visible.</summary>
    /// <remarks>The rectangle of a shape of the content is its own, without its stroke. A clipping path that cuts itself leaves nothing visible.</remarks>
    public static SceneBounds? GetClipBounds(SceneClipPath clipPath, Func<SceneText, SceneBounds?> measureText)
    {
        s_clipPaths ??= new(ReferenceEqualityComparer.Instance);
        if (!s_clipPaths.Add(clipPath))
            return null;

        try
        {
            if (GetClipContentBounds(clipPath.Content, measureText) is not { } bounds)
                return null;

            if (clipPath.ClippingPath is { } outer)
            {
                if (GetClipBounds(outer, measureText) is not { } outerBounds)
                    return null;

                bounds = Intersect(bounds, outerBounds);
            }

            return bounds.Width > 0 && bounds.Height > 0 ? bounds : null;
        }
        finally
        {
            s_clipPaths.Remove(clipPath);
        }
    }

    // The geometry an element adds to a clipping path, in the coordinates of its parent: the interiors of the shapes,
    // without their strokes, the glyphs of the texts; not the lines and the images
    private static SceneBounds? GetClipContentBounds(SceneElement element, Func<SceneText, SceneBounds?> measureText)
    {
        SceneBounds? bounds;
        switch (element)
        {
            case SceneGroup group:
                bounds = null;
                foreach (var child in group.Children)
                {
                    bounds = Union(bounds, GetClipContentBounds(child, measureText));
                }

                break;

            case SceneUse use:
                if (use.IsCircular())
                    return null;

                s_uses ??= new(ReferenceEqualityComparer.Instance);
                if (!s_uses.Add(use))
                    return null;

                try
                {
                    bounds = GetClipContentBounds(use.Content, measureText);
                }
                finally
                {
                    s_uses.Remove(use);
                }

                break;

            case SceneRectangle rectangle:
                bounds = rectangle.Width > 0 && rectangle.Height > 0 ? new SceneBounds(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height) : null;
                break;

            case SceneCircle circle:
                bounds = circle.Radius > 0 ? new SceneBounds(circle.CenterX - circle.Radius, circle.CenterY - circle.Radius, 2 * circle.Radius, 2 * circle.Radius) : null;
                break;

            case ScenePath path:
                bounds = GetPathBounds(path.Data);
                break;

            // The opacity of the content does not matter
            case SceneText text:
                bounds = GetUntransformedBounds(text, measureText, applyClips: true);
                break;

            default:
                return null;
        }

        if (bounds is { } box && element.Transform is { } transform)
        {
            bounds = Transform(box, transform);
        }

        if (element is not (SceneGroup or SceneUse) && bounds is { } clipped && element.ClippingPath is { } clippingPath)
        {
            if (GetClipBounds(clippingPath, measureText) is not { } clipBounds)
                return null;

            bounds = Intersect(clipped, clipBounds);
        }

        return bounds;
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

                // A view box maps the children to its viewport, which cuts them, inside the transform of the group
                if (group.ViewBox is { } viewBox)
                {
                    if (viewBox.GetTransform() is not { } viewBoxTransform)
                        return null;

                    box = Transform(box, viewBoxTransform);
                    if (applyClips)
                    {
                        box = Intersect(box, viewBox.Viewport);
                    }
                }

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
                return WithMarkers(Inflate(new SceneBounds(Math.Min(line.X1, line.X2), Math.Min(line.Y1, line.Y2), Math.Abs(line.X2 - line.X1), Math.Abs(line.Y2 - line.Y1)), stroke), line, measureText);

            case ScenePath path:
                return WithMarkers(GetPathBounds(path.Data) is { } pathBounds ? Inflate(pathBounds, stroke) : null, path, measureText);

            case SceneImage image:
                return new SceneBounds(image.X, image.Y, image.Width, image.Height);

            // A use draws its content with its filter; a use in its own content draws nothing
            case SceneUse use:
                if (use.IsCircular())
                    return null;

                s_uses ??= new(ReferenceEqualityComparer.Instance);
                if (!s_uses.Add(use))
                    return null;

                try
                {
                    return GetBounds(use.Content, measureText) is { } contentBounds ? WithFilter(contentBounds, use.Content.Filter) : null;
                }
                finally
                {
                    s_uses.Remove(use);
                }

            // A text on a path is within its size of the path: its glyphs are above the path, and its descenders below
            case SceneText { Path: { } textPath } text:
                if (text.Text.Length is 0 || GetPathBounds(textPath) is not { } textPathBounds)
                    return null;

                // The shifts across the path and the baselines move the glyphs away from it
                var size = text.Spans.Count is 0 ? text.Font.Size : text.Spans.Max(span => (span.Font ?? text.Font).Size);
                var shifts = text.Spans.Sum(span => Math.Abs(Finite(span.Dy))) + text.Spans.Select(span => Math.Abs(Finite(span.BaselineShift))).DefaultIfEmpty().Max();
                var extent = (double.IsFinite(size) ? Math.Abs(size) : 0) + (text.Baseline is SceneTextBaseline.Alphabetic ? 0 : Math.Abs(size)) + shifts;
                return Inflate(textPathBounds, Finite(extent) + TextStrokeExtent(text));

            case SceneText text:
                if (measureText(text) is not { } textBounds)
                    return null;

                textBounds = Inflate(textBounds, TextStrokeExtent(text));
                return text.Rotation == 0 ? textBounds : Transform(textBounds, SceneTransform.Rotate(text.Rotation, text.X, text.Y));

            default:
                return null;
        }
    }

    // The markers of a shape are drawn on its vertices. A marker drawn on a shape of its own content is not drawn again.
    [ThreadStatic]
    private static HashSet<SceneMarker>? s_markers;

    [ThreadStatic]
    private static HashSet<SceneUse>? s_uses;

    private static SceneBounds? WithMarkers(SceneBounds? bounds, SceneShape shape, Func<SceneText, SceneBounds?> measureText)
    {
        foreach (var (marker, vertex, isStart) in GetMarkers(shape))
        {
            s_markers ??= new(ReferenceEqualityComparer.Instance);
            if (!s_markers.Add(marker))
                continue;

            try
            {
                if (GetBounds(marker.Content, measureText) is { } content)
                {
                    bounds = Union(bounds, Transform(content, marker.GetTransform(vertex, shape.StrokeWidth, isStart)));
                }
            }
            finally
            {
                s_markers.Remove(marker);
            }
        }

        return bounds;
    }

    /// <summary>Gets the markers of a path or a line, with the vertex each one is drawn on.</summary>
    public static IEnumerable<(SceneMarker Marker, PathPoint Vertex, bool IsStart)> GetMarkers(SceneShape shape)
    {
        var (start, middle, end) = shape switch
        {
            ScenePath path => (path.MarkerStart, path.MarkerMid, path.MarkerEnd),
            SceneLine line => (line.MarkerStart, null, line.MarkerEnd),
            _ => (null, null, null),
        };

        if (start is null && middle is null && end is null)
            yield break;

        var vertices = shape switch
        {
            ScenePath path => path.Data.GetVertices(),
            SceneLine line => new PathBuilder().MoveTo(line.X1, line.Y1).LineTo(line.X2, line.Y2).Build().GetVertices(),
            _ => [],
        };

        for (var i = 0; i < vertices.Count; i++)
        {
            var vertex = vertices[i];
            if (!double.IsFinite(vertex.X) || !double.IsFinite(vertex.Y))
                continue;

            if (i == 0 && start is not null)
            {
                yield return (start, vertex, true);
            }

            if (i > 0 && i < vertices.Count - 1 && middle is not null)
            {
                yield return (middle, vertex, false);
            }

            if (i == vertices.Count - 1 && end is not null)
            {
                yield return (end, vertex, false);
            }
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

            // A graph draws in its region only: its primitives can fill it (a flood), or move the drawing within it
            case SceneFilterGraph graph:
                return graph.GetRegion(bounds);

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

    // The outline of the glyphs of a text extends as the stroke of a shape
    private static double TextStrokeExtent(SceneText text)
    {
        if (!TextPlacement.HasStroke(text))
            return 0;

        var miterLimit = text.MiterLimit >= 1 && double.IsFinite(text.MiterLimit) ? text.MiterLimit : 4;
        var factor = Math.Max(text.LineJoin is SceneLineJoin.Miter ? miterLimit : 1, text.LineCap is SceneLineCap.Square ? Math.Sqrt(2) : 1);
        return Finite(text.StrokeWidth / 2 * factor);
    }

    private static double Finite(double value) => double.IsFinite(value) ? value : 0;

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
