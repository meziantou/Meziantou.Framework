using System.Globalization;
using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Rendering;
using Meziantou.Framework.WebPlatform.Svg;
using static Meziantou.Framework.WebPlatform.Svg.SvgPathData;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

// The markers (https://svgwg.org/svg2-draft/painting.html#Markers): the marker elements referenced by marker-start, marker-mid and
// marker-end, painted at the vertices of the paths, lines, polylines and polygons.
internal sealed partial class Painter
{
    /// <summary>A vertex of a path (https://svgwg.org/svg2-draft/paths.html#PathDirectionality): its position and its angle (the bisector of its incoming and outgoing directions), in degrees.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    private readonly record struct SvgMarkerVertex(SvgPoint Point, double Angle, double StartAngle);

    /// <summary>
    /// Paints the markers of a shape (https://svgwg.org/svg2-draft/painting.html#MarkerProperties): marker-start at its first
    /// vertex, marker-mid at the other vertices, marker-end at its last vertex.
    /// </summary>
    private void PaintSvgMarkers(Element element, ComputedStyle style, List<SvgPathCommand> commands, SvgAffine toGroup, SvgPaintContext context, DisplayPaint? fill, DisplayPaint? stroke, double strokeWidth)
    {
        if (element.LocalName is not ("path" or "line" or "polyline" or "polygon") || context.Depth >= MaxSvgDepth)
            return;

        var start = GetMarker(element, style, "marker-start");
        var mid = GetMarker(element, style, "marker-mid");
        var end = GetMarker(element, style, "marker-end");
        if (start is null && mid is null && end is null)
            return;

        var vertices = GetMarkerVertices(commands);
        if (vertices.Count == 0)
            return;

        var contextPaints = SvgContextPaints.Create(fill, stroke, toGroup.Then(_svgBuilderTransform), style);
        for (var i = 0; i < vertices.Count; i++)
        {
            var marker = i == 0 ? start : i == vertices.Count - 1 ? end : mid;
            if (marker is not null)
            {
                PaintSvgMarker(marker, vertices[i], isStart: i == 0, strokeWidth, toGroup, context, contextPaints);
            }
        }
    }

    /// <summary>Returns the marker element referenced by a marker property of an element, or null.</summary>
    private static SvgMarkerElement? GetMarker(Element element, ComputedStyle style, string property)
    {
        var value = style.GetComputedValue(property);
        if (value is "none" or "")
            return null;

        var components = ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(value));
        return components.Count == 1 ? SvgResources.GetReferencedElement(element, components[0]) as SvgMarkerElement : null;
    }

    /// <summary>
    /// Paints a marker at a vertex (https://svgwg.org/svg2-draft/painting.html#MarkerElement): its content in the coordinates of its
    /// viewBox mapped to its viewport (markerWidth, markerHeight), the reference point (refX, refY) at the vertex, rotated by
    /// <c>orient</c> and scaled by the stroke width (markerUnits), clipped to its viewport when its overflow is not visible.
    /// </summary>
    private void PaintSvgMarker(SvgMarkerElement marker, SvgMarkerVertex vertex, bool isStart, double strokeWidth, SvgAffine toGroup, SvgPaintContext context, SvgContextPaints contextPaints)
    {
        var markerStyle = marker.NodeDocument.GetStyleEngine().GetStyle(marker);
        var (viewportWidth, viewportHeight) = SvgUserSpace.GetViewportSize(marker, context.Viewport);
        var width = SvgUserSpace.ResolveLength(marker, marker.GetAnimatedAttributeValue("markerWidth"), viewportWidth) ?? 3;
        var height = SvgUserSpace.ResolveLength(marker, marker.GetAnimatedAttributeValue("markerHeight"), viewportHeight) ?? 3;
        if (!(width > 0) || !(height > 0))
            return;

        var hasViewBox = SvgUserSpace.TryGetViewBox(marker, out var viewBox);
        if (hasViewBox && (viewBox.Width <= 0 || viewBox.Height <= 0))
            return;

        var viewBoxTransform = SvgUserSpace.GetViewBoxTransform(marker, width, height);

        // https://svgwg.org/svg2-draft/painting.html#MarkerElementRefXAttribute: a coordinate of the viewBox, or a keyword of its edges
        var referenceWidth = hasViewBox ? viewBox.Width : width;
        var referenceHeight = hasViewBox ? viewBox.Height : height;
        var originX = hasViewBox ? viewBox.X : 0;
        var originY = hasViewBox ? viewBox.Y : 0;
        var refX = ResolveMarkerReference(marker, marker.GetAnimatedAttributeValue("refX"), "left", "right", originX, referenceWidth);
        var refY = ResolveMarkerReference(marker, marker.GetAnimatedAttributeValue("refY"), "top", "bottom", originY, referenceHeight);
        var reference = viewBoxTransform.Apply(refX, refY);

        var angle = GetMarkerOrientation(marker.GetAnimatedAttributeValue("orient"), vertex, isStart);
        var scale = marker.GetAnimatedAttributeValue("markerUnits") == "userSpaceOnUse" ? 1 : strokeWidth;
        var frameToUser = SvgAffine.Translation(-reference.X, -reference.Y).Then(SvgAffine.Scale(scale, scale)).Then(SvgAffine.Rotation(angle)).Then(SvgAffine.Translation(vertex.Point.X, vertex.Point.Y));
        var frameToGroup = frameToUser.Then(toGroup);

        var saveCount = _builder.SaveCount;

        // The content is clipped to the viewport when it overflows it (a clip that does not cut the content would only change the
        // antialiasing of its edges)
        if (ClipsSvgViewport(markerStyle, autoIsVisible: true) && !SvgContentFits(marker, hasViewBox ? new PhysicalRect(viewBox.X, viewBox.Y, viewBox.Width, viewBox.Height) : new PhysicalRect(0, 0, width, height), context.Viewport))
        {
            _builder.Save();
            AddSvgClipRect(new PhysicalRect(0, 0, width, height), frameToGroup.Then(_svgBuilderTransform));
        }

        PaintSvgChildren(marker, markerStyle, viewBoxTransform.Then(frameToGroup), context with
        {
            IsResourceContent = true,
            UseInstanceRoot = null,
            ParentStyle = null,
            UseSize = default,
            Source = null,
            ContextPaints = contextPaints,
            Depth = context.Depth + 1,
        });
        _builder.RestoreTo(saveCount);
    }

    /// <summary>Whether the stroke bounding boxes of the children of an element are inside a rectangle of its user space.</summary>
    private static bool SvgContentFits(Element element, PhysicalRect rect, SvgOutermostViewport viewport)
    {
        for (var child = element.FirstElementChild; child is not null; child = child.NextElementSibling)
        {
            if (child is not SvgElement svgChild || SvgBoundingBox.ComputeContent(svgChild, viewport, stroke: true) is not { } box)
                continue;

            var transform = SvgUserSpace.GetTransformToParent(child, viewport);
            foreach (var (x, y) in (ReadOnlySpan<(double, double)>)[(box.MinX, box.MinY), (box.MaxX, box.MinY), (box.MaxX, box.MaxY), (box.MinX, box.MaxY)])
            {
                var point = transform.Apply(x, y);
                if (point.X < rect.X - 1e-9 || point.Y < rect.Y - 1e-9 || point.X > rect.Right + 1e-9 || point.Y > rect.Bottom + 1e-9)
                    return false;
            }
        }

        return true;
    }

    /// <summary>Resolves refX or refY: a number or a length in the coordinates of the viewBox, or a keyword (https://svgwg.org/svg2-draft/painting.html#MarkerElementRefXAttribute).</summary>
    private static double ResolveMarkerReference(Element marker, string? value, string startKeyword, string endKeyword, double origin, double size)
    {
        if (value is null)
            return 0;

        value = value.Trim();
        if (value == startKeyword)
            return origin;

        if (value == "center")
            return origin + (size / 2);

        if (value == endKeyword)
            return origin + size;

        return SvgUserSpace.ResolveLength(marker, value, size) ?? 0;
    }

    /// <summary>
    /// Returns the rotation of a marker (https://svgwg.org/svg2-draft/painting.html#OrientAttribute): <c>auto</c> is the angle of the
    /// vertex, <c>auto-start-reverse</c> the angle reversed for the start marker, an angle (or a number of degrees) is fixed.
    /// </summary>
    private static double GetMarkerOrientation(string? orient, SvgMarkerVertex vertex, bool isStart)
    {
        orient = orient?.Trim();
        if (orient == "auto")
            return vertex.Angle;

        if (orient == "auto-start-reverse")
            return isStart ? vertex.Angle + 180 : vertex.Angle;

        if (string.IsNullOrEmpty(orient))
            return 0;

        if (double.TryParse(orient, NumberStyles.Float, CultureInfo.InvariantCulture, out var degrees))
            return degrees;

        var unitStart = orient.Length;
        while (unitStart > 0 && char.IsAsciiLetter(orient[unitStart - 1]))
        {
            unitStart--;
        }

        if (!double.TryParse(orient.AsSpan(0, unitStart), NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            return 0;

        return orient[unitStart..] switch
        {
            "deg" => number,
            "rad" => number * 180 / Math.PI,
            "grad" => number * 0.9,
            "turn" => number * 360,
            _ => 0,
        };
    }

    /// <summary>A segment of a path with the directions at its ends (zero when the segment has no direction there).</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    private readonly record struct MarkerSegment(SvgPoint Start, SvgPoint End, SvgPoint StartDirection, SvgPoint EndDirection, bool IsClose);

    /// <summary>
    /// Returns the vertices of a path for its markers (https://svgwg.org/svg2-draft/paths.html#PathDirectionality): the start of each
    /// subpath and the end of each segment, with the angle of the bisector of the incoming and outgoing directions (the outgoing
    /// direction at the start of an open subpath, the incoming one at its end; the closing segment is the incoming direction of the
    /// start of a closed subpath).
    /// </summary>
    private static List<SvgMarkerVertex> GetMarkerVertices(List<SvgPathCommand> commands)
    {
        // The segments of each subpath, from the normalized commands (one segment per command: the arcs are kept)
        var subpaths = new List<(SvgPoint Start, List<MarkerSegment> Segments)>();
        var normalizer = new Normalizer(keepArcs: true);
        var current = default(SvgPoint);
        foreach (var command in commands)
        {
            normalizer.Emit(command, (state, segment) =>
            {
                switch (segment.Type)
                {
                    case 'M':
                        state.Add((segment.Target, []));
                        current = segment.Target;
                        return;

                    case 'L':
                    case 'Z':
                    {
                        if (state.Count == 0)
                        {
                            state.Add((current, []));
                        }

                        var direction = segment.Target - current;
                        if (segment.Type == 'Z' && direction == default && state[^1].Segments is [.., { IsClose: false } last])
                        {
                            // A closepath of zero length after a segment closes the subpath without adding a vertex (as in the browsers);
                            // after a moveto or a closepath, it is a vertex (an implicit subpath)
                            state[^1].Segments[^1] = last with { IsClose = true };
                            break;
                        }

                        state[^1].Segments.Add(new MarkerSegment(current, segment.Target, direction, direction, segment.Type == 'Z'));
                        break;
                    }

                    case 'C':
                    {
                        if (state.Count == 0)
                        {
                            state.Add((current, []));
                        }

                        var startDirection = FirstNonZero(segment.Point1 - current, segment.Point2 - current, segment.Target - current);
                        var endDirection = FirstNonZero(segment.Target - segment.Point2, segment.Target - segment.Point1, segment.Target - current);
                        state[^1].Segments.Add(new MarkerSegment(current, segment.Target, startDirection, endDirection, IsClose: false));
                        break;
                    }

                    case 'A':
                    {
                        if (state.Count == 0)
                        {
                            state.Add((current, []));
                        }

                        var arc = segment.Arc;
                        state[^1].Segments.Add(new MarkerSegment(current, segment.Target, ArcDirection(arc, arc.StartAngle), ArcDirection(arc, arc.StartAngle + arc.SweepAngle), IsClose: false));
                        break;
                    }
                }

                current = segment.Target;
            }, subpaths);
        }

        var vertices = new List<SvgMarkerVertex>();
        foreach (var (start, segments) in subpaths)
        {
            var closed = segments.Count > 0 && segments[^1].IsClose;
            var firstOut = segments.Count > 0 ? FirstDirection(segments, 0, forward: true) : default;
            var lastIn = segments.Count > 0 ? FirstDirection(segments, segments.Count - 1, forward: false) : default;

            // The start of the subpath
            var startIn = closed ? lastIn : firstOut;
            vertices.Add(new SvgMarkerVertex(start, Bisect(startIn, firstOut), Angle(firstOut)));

            for (var i = 0; i < segments.Count; i++)
            {
                var incoming = FirstDirection(segments, i, forward: false);
                SvgPoint outgoing;
                if (i + 1 < segments.Count)
                {
                    outgoing = FirstDirection(segments, i + 1, forward: true);
                }
                else
                {
                    outgoing = closed ? firstOut : incoming;
                }

                vertices.Add(new SvgMarkerVertex(segments[i].End, Bisect(incoming, outgoing), Angle(outgoing)));
            }
        }

        return vertices;

        static SvgPoint FirstNonZero(SvgPoint a, SvgPoint b, SvgPoint c) => a != default ? a : b != default ? b : c;

        static SvgPoint ArcDirection(SvgArc arc, double theta)
        {
            // The derivative of the point of the arc, in the direction of the sweep
            var sign = arc.SweepAngle < 0 ? -1 : 1;
            var x = -arc.RadiusX * Math.Sin(theta) * sign;
            var y = arc.RadiusY * Math.Cos(theta) * sign;
            return new SvgPoint((arc.Cos * x) - (arc.Sin * y), (arc.Sin * x) + (arc.Cos * y));
        }

        // The direction at the start (or the end) of a segment; a segment of zero length takes the direction of the next (or previous) one
        static SvgPoint FirstDirection(List<MarkerSegment> segments, int index, bool forward)
        {
            for (var i = index; i >= 0 && i < segments.Count; i += forward ? 1 : -1)
            {
                var direction = forward ? segments[i].StartDirection : segments[i].EndDirection;
                if (direction != default)
                    return direction;
            }

            for (var i = index; i >= 0 && i < segments.Count; i += forward ? -1 : 1)
            {
                var direction = forward ? segments[i].EndDirection : segments[i].StartDirection;
                if (direction != default)
                    return direction;
            }

            return default;
        }

        static double Angle(SvgPoint direction) => direction == default ? 0 : Math.Atan2(direction.Y, direction.X) * 180 / Math.PI;

        static double Bisect(SvgPoint incoming, SvgPoint outgoing)
        {
            if (incoming == default)
                return Angle(outgoing);

            if (outgoing == default)
                return Angle(incoming);

            var a1 = Angle(incoming);
            var a2 = Angle(outgoing);

            // Opposite directions: the bisector is the incoming direction turned by -90 degrees (as in the browsers)
            var dot = ((incoming.X * outgoing.X) + (incoming.Y * outgoing.Y)) / (incoming.Length * outgoing.Length);
            if (dot <= -1 + 1e-12)
                return a1 - 90;

            if (Math.Abs(a1 - a2) > 180)
            {
                a1 += 360;
            }

            return (a1 + a2) / 2;
        }
    }
}
