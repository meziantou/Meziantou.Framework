using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Dom;
using static Meziantou.Framework.WebPlatform.Svg.SvgPathData;

namespace Meziantou.Framework.WebPlatform.Svg;

/// <summary>
/// The intersection and enclosure queries of the svg element (https://svgwg.org/svg2-draft/struct.html#__svg__SVGSVGElement__getIntersectionList):
/// as in the browsers, the rendered graphics elements (the shapes, the texts, the images, the use and foreignObject elements)
/// that can be the target of pointer events are tested with their painted geometry (the fill and the stroke of the shapes, the
/// bounding box of the others) clipped by the bounding box of their clipping path, in the user space of the svg element.
/// </summary>
internal static class SvgIntersection
{
    /// <summary>Returns the graphics elements of the subtree of <paramref name="referenceElement"/> (or of the svg element) that intersect (or are enclosed by) the rectangle, in tree order.</summary>
    public static List<Element> GetElements(SvgSvgElement svg, DomRectReadOnly rect, SvgElement? referenceElement, bool enclosure)
    {
        var result = new List<Element>();
        if (!svg.IsConnected)
            return result;

        var bounds = ToBounds(rect);
        var root = referenceElement ?? svg;
        if (!ReferenceEquals(root, svg) && !svg.IsInclusiveAncestorOf(root))
            return result;

        for (var node = root.FirstChild; node is not null; node = node.GetNextInTreeOrder(root))
        {
            if (node is Element element && IsGraphicsElement(element) && Test(svg, element, bounds, enclosure))
            {
                result.Add(element);
            }
        }

        return result;
    }

    /// <summary>Whether an element intersects (or is enclosed by) the rectangle: a container when all its graphics elements do.</summary>
    public static bool Check(SvgSvgElement svg, SvgElement element, DomRectReadOnly rect, bool enclosure)
    {
        var bounds = ToBounds(rect);
        if (!svg.IsConnected || !svg.IsInclusiveAncestorOf(element))
            return false;

        if (IsGraphicsElement(element))
            return Test(svg, element, bounds, enclosure);

        var any = false;
        for (var node = element.FirstChild; node is not null; node = node.GetNextInTreeOrder(element))
        {
            if (node is Element descendant && IsGraphicsElement(descendant) && IsRendered(descendant))
            {
                if (!Test(svg, descendant, bounds, enclosure))
                    return false;

                any = true;
            }
        }

        return any;
    }

    private static (double MinX, double MinY, double MaxX, double MaxY) ToBounds(DomRectReadOnly rect)
        => (Math.Min(rect.X, rect.X + rect.Width), Math.Min(rect.Y, rect.Y + rect.Height), Math.Max(rect.X, rect.X + rect.Width), Math.Max(rect.Y, rect.Y + rect.Height));

    private static bool IsGraphicsElement(Element element) => element is SvgElement && element.LocalName is "rect" or "circle" or "ellipse" or "line" or "polyline" or "polygon" or "path" or "text" or "image" or "use" or "foreignObject";

    private static bool IsRendered(Element element) => SvgUserSpace.GetStyle(element) is { } style && BoxGeneration.Get(style).IsBeingRendered;

    private static bool Test(SvgSvgElement svg, Element element, (double MinX, double MinY, double MaxX, double MaxY) rect, bool enclosure)
    {
        if (SvgUserSpace.GetStyle(element) is not { } style || !BoxGeneration.Get(style).IsBeingRendered || style.GetComputedValue("pointer-events") == "none")
            return false;

        // The transform from the user space of the element to the user space of the children of the svg element
        var transform = SvgUserSpace.GetTransform(element);
        for (var ancestor = element.ParentNode as Element; ancestor is not null && !ReferenceEquals(ancestor, svg); ancestor = ancestor.ParentNode as Element)
        {
            transform = transform.Then(SvgUserSpace.GetTransformToParent(ancestor));
        }

        if (GetClipBox(element, out var isClipped) is { } clip ? !Intersects(Corners(clip, transform), rect) : isClipped)
            return false;

        if (!enclosure && element is SvgGeometryElement)
        {
            var geometry = SvgShapeGeometry.Get(element);
            if (geometry.IsEmpty)
                return false;

            var fill = style.GetComputedValue("fill") != "none";
            var stroke = style.GetComputedValue("stroke") != "none";
            var strokeHalfWidth = stroke ? SvgStrokeStyle.FromElement(element).Width / 2 : 0;
            return geometry.Intersects(transform, rect, fill || !stroke, style.GetComputedValue("fill-rule") == "evenodd", strokeHalfWidth);
        }

        var box = SvgBoundingBox.Compute((SvgElement)element, fill: true, stroke: enclosure);
        if (box.Width <= 0 && box.Height <= 0)
            return false;

        var corners = Corners((box.X, box.Y, box.X + box.Width, box.Y + box.Height), transform);
        if (!enclosure)
            return Intersects(corners, rect);

        foreach (var corner in corners)
        {
            if (!Contains(rect, corner))
                return false;
        }

        return true;
    }

    /// <summary>The bounding box of the clipping path of an element (https://drafts.fxtf.org/css-masking/#ClipPathElement, in the user space of the element), or null.</summary>
    private static (double MinX, double MinY, double MaxX, double MaxY)? GetClipBox(Element element, out bool isClipped)
    {
        isClipped = false;
        var clipPath = SvgUserSpace.GetComputedValue(element, "clip-path");
        if (clipPath is null || !clipPath.StartsWith("url(", StringComparison.Ordinal))
            return null;

        if (SvgClipping.GetReference(element, clipPath) is not SvgClipPathElement clip || clip.GetAnimatedAttributeValue("clipPathUnits") == "objectBoundingBox")
            return null;

        // An empty clipping path clips everything
        isClipped = true;
        return SvgBoundingBox.ComputeContent(clip);
    }

    private static SvgPoint[] Corners((double MinX, double MinY, double MaxX, double MaxY) box, SvgAffine transform)
        => [transform.Apply(box.MinX, box.MinY), transform.Apply(box.MaxX, box.MinY), transform.Apply(box.MaxX, box.MaxY), transform.Apply(box.MinX, box.MaxY)];

    public static bool Contains((double MinX, double MinY, double MaxX, double MaxY) rect, SvgPoint point)
        => point.X >= rect.MinX && point.X <= rect.MaxX && point.Y >= rect.MinY && point.Y <= rect.MaxY;

    /// <summary>Whether a convex quad intersects a rectangle (the separating axis theorem).</summary>
    private static bool Intersects(SvgPoint[] quad, (double MinX, double MinY, double MaxX, double MaxY) rect)
    {
        for (var i = 0; i < quad.Length; i++)
        {
            if (SegmentIntersects(quad[i], quad[(i + 1) % quad.Length], rect))
                return true;
        }

        // The rectangle inside the quad
        var center = new SvgPoint((rect.MinX + rect.MaxX) / 2, (rect.MinY + rect.MaxY) / 2);
        var sign = 0;
        for (var i = 0; i < quad.Length; i++)
        {
            var a = quad[i];
            var b = quad[(i + 1) % quad.Length];
            var cross = ((b.X - a.X) * (center.Y - a.Y)) - ((b.Y - a.Y) * (center.X - a.X));
            var current = Math.Sign(cross);
            if (current != 0 && sign != 0 && current != sign)
                return false;

            sign = current == 0 ? sign : current;
        }

        return true;
    }

    /// <summary>Whether a segment intersects a rectangle (its edges included): the Liang–Barsky clipping.</summary>
    public static bool SegmentIntersects(SvgPoint a, SvgPoint b, (double MinX, double MinY, double MaxX, double MaxY) rect)
    {
        var t0 = 0d;
        var t1 = 1d;
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        return Clip(-dx, a.X - rect.MinX) && Clip(dx, rect.MaxX - a.X) && Clip(-dy, a.Y - rect.MinY) && Clip(dy, rect.MaxY - a.Y);

        bool Clip(double p, double q)
        {
            if (p == 0)
                return q >= 0;

            var r = q / p;
            if (p < 0)
            {
                if (r > t1)
                    return false;

                t0 = Math.Max(t0, r);
            }
            else
            {
                if (r < t0)
                    return false;

                t1 = Math.Min(t1, r);
            }

            return true;
        }
    }
}
