using Meziantou.Framework.WebPlatform.Dom;
using static Meziantou.Framework.WebPlatform.Svg.SvgPathData;

namespace Meziantou.Framework.WebPlatform.Svg;

/// <summary>
/// The clipping paths of the SVG elements (https://drafts.fxtf.org/css-masking/#ClipPathElement) for the hit testing: a
/// <c>clip-path</c> property that references a clipPath element clips the element to the union of the fill areas of the
/// shapes and texts of the clipPath element (with their <c>clip-rule</c> and transforms, in the user space of the element, or
/// in its object bounding box for <c>clipPathUnits="objectBoundingBox"</c>). A missing reference does not clip; the basic
/// shapes of <c>clip-path</c> are not supported.
/// </summary>
internal static class SvgClipping
{
    private const int MaxDepth = 16;

    /// <summary>Whether a point in the user space of an element is inside its clipping path (true when the element is not clipped).</summary>
    public static bool Contains(Element element, SvgPoint point, SvgOutermostViewport? viewport = null) => Contains(element, point, viewport, depth: 0);

    private static bool Contains(Element element, SvgPoint point, SvgOutermostViewport? viewport, int depth)
    {
        var clipPath = SvgUserSpace.GetComputedValue(element, "clip-path");
        if (depth > MaxDepth || clipPath is null || clipPath == "none")
            return true;

        if (!clipPath.StartsWith("url(", StringComparison.Ordinal))
            return ContainsInBasicShape(element, clipPath, point, viewport);

        if (GetReference(element, clipPath) is not SvgClipPathElement clip)
            return true;

        // https://drafts.fxtf.org/css-masking/#element-attrdef-clippath-clippathunits: the bounding box units are the coordinates of
        // the contents; the transform of the clipPath element is in the user space of the clipped element
        var transform = SvgUserSpace.GetTransform(clip, viewport);
        if (clip.GetAnimatedAttributeValue("clipPathUnits") == "objectBoundingBox" && element is SvgElement svgElement)
        {
            var box = SvgBoundingBox.Compute(svgElement, fill: true, stroke: false, viewport);
            transform = new SvgAffine(box.Width, 0, 0, box.Height, box.X, box.Y).Then(transform);
        }

        if (transform.Invert() is not { } inverse)
            return false;

        var local = inverse.Apply(point);

        // The clipPath element can itself be clipped
        if (!Contains(clip, local, viewport, depth + 1))
            return false;

        for (var child = clip.FirstElementChild; child is not null; child = child.NextElementSibling)
        {
            if (SvgUserSpace.GetComputedValue(child, "display") == "none" || SvgUserSpace.GetComputedValue(child, "visibility") is "hidden" or "collapse")
                continue;

            if (SvgUserSpace.GetTransform(child, viewport).Invert() is not { } childInverse)
                continue;

            var childPoint = childInverse.Apply(local);
            if (!Contains(child, childPoint, viewport, depth + 1))
                continue;

            var evenOdd = SvgUserSpace.GetComputedValue(child, "clip-rule") == "evenodd";
            if (child is SvgGeometryElement)
            {
                var geometry = SvgShapeGeometry.Get(child, viewport);
                if (!geometry.IsEmpty && geometry.IsPointInFill(childPoint, evenOdd))
                    return true;
            }
            else if (child is SvgTextElement && SvgTextLayout.Get(child, viewport) is { } layout)
            {
                foreach (var character in layout.Characters)
                {
                    if (character.IsClusterStart && !character.IsHidden && character.Contains(childPoint))
                        return true;
                }
            }
            else if (child is SvgUseElement && SvgBoundingBox.GetUseTarget(child) is SvgGeometryElement target
                && (SvgUserSpace.GetTransform(target, viewport).Then(SvgUserSpace.GetUseTranslation(child, viewport))).Invert() is { } useInverse
                && SvgShapeGeometry.Get(target, viewport) is { IsEmpty: false } targetGeometry
                && targetGeometry.IsPointInFill(useInverse.Apply(childPoint), evenOdd))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether a point in the user space of an element is in its clip path that is a basic shape or a &lt;geometry-box&gt;
    /// (https://drafts.fxtf.org/css-masking/#typedef-geometry-box: the fill box for content-box, padding-box and fill-box, the
    /// stroke box for border-box, margin-box and stroke-box, the nearest SVG viewport for view-box).
    /// </summary>
    private static bool ContainsInBasicShape(Element element, string clipPath, SvgPoint point, SvgOutermostViewport? viewport)
    {
        if (element is not SvgElement svgElement || Css.Layout.BasicShapePaths.ParseClipPath(clipPath) is not { Url: null } value)
            return true;

        Css.Layout.PhysicalRect box;
        if (value.ReferenceBox == "view-box")
        {
            var size = SvgUserSpace.GetViewportSize(element, viewport);
            box = new Css.Layout.PhysicalRect(0, 0, size.Width, size.Height);
        }
        else
        {
            var stroke = value.ReferenceBox is not ("fill-box" or "content-box" or "padding-box");
            var rect = SvgBoundingBox.Compute(svgElement, fill: true, stroke, viewport);
            box = new Css.Layout.PhysicalRect(rect.X, rect.Y, rect.Width, rect.Height);
        }

        if (value.Shape is not { } shape)
            return box.Contains(point.X, point.Y);

        return Css.Layout.BasicShapePaths.CreatePath(shape, box, 1) is not { } path || Css.Layout.BasicShapePaths.Contains(path, new Css.Layout.PhysicalOffset(point.X, point.Y));
    }

    /// <summary>
    /// Returns the bounds of the clipPath element referenced by the <c>clip-path</c> property of an element, in the user space of
    /// the element (the bounding box of the content of the clipPath element, with its transform and its <c>clipPathUnits</c>), or
    /// null when the property does not reference a clipPath element (the basic shapes are not supported).
    /// </summary>
    public static Css.Layout.PhysicalRect? GetBounds(Element element, SvgOutermostViewport? viewport = null)
    {
        var clipPath = SvgUserSpace.GetComputedValue(element, "clip-path");
        if (clipPath is null || !clipPath.StartsWith("url(", StringComparison.Ordinal) || GetReference(element, clipPath) is not SvgClipPathElement clip)
            return null;

        var transform = SvgUserSpace.GetTransform(clip, viewport);
        if (clip.GetAnimatedAttributeValue("clipPathUnits") == "objectBoundingBox" && element is SvgElement svgElement)
        {
            var box = SvgBoundingBox.Compute(svgElement, fill: true, stroke: false, viewport);
            transform = new SvgAffine(box.Width, 0, 0, box.Height, box.X, box.Y).Then(transform);
        }

        // An empty clip path clips everything
        if (SvgBoundingBox.ComputeContent(clip, viewport) is not { } content)
            return new Css.Layout.PhysicalRect(0, 0, 0, 0);

        var p1 = transform.Apply(new SvgPoint(content.MinX, content.MinY));
        var p2 = transform.Apply(new SvgPoint(content.MaxX, content.MinY));
        var p3 = transform.Apply(new SvgPoint(content.MaxX, content.MaxY));
        var p4 = transform.Apply(new SvgPoint(content.MinX, content.MaxY));
        var minX = Math.Min(Math.Min(p1.X, p2.X), Math.Min(p3.X, p4.X));
        var minY = Math.Min(Math.Min(p1.Y, p2.Y), Math.Min(p3.Y, p4.Y));
        var maxX = Math.Max(Math.Max(p1.X, p2.X), Math.Max(p3.X, p4.X));
        var maxY = Math.Max(Math.Max(p1.Y, p2.Y), Math.Max(p3.Y, p4.Y));
        return new Css.Layout.PhysicalRect(minX, minY, maxX - minX, maxY - minY);
    }

    /// <summary>Returns the element referenced by a computed url() of a property of an element (see <see cref="Css.Layout.SvgResources.GetReferencedElement"/>), or null.</summary>
    public static Element? GetReference(Element element, string url)
        => Css.Values.ValueMatcher.RemoveWhitespace(Css.Syntax.CssParser.ParseListOfComponentValues(url)) is [var component] ? Css.Layout.SvgResources.GetReferencedElement(element, component) : null;
}
