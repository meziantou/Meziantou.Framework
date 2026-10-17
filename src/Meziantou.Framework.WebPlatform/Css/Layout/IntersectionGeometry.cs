using System.Globalization;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Svg;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>A length of a root margin or of a scroll margin (https://w3c.github.io/IntersectionObserver/#parse-a-margin): CSS pixels or a percentage.</summary>
internal readonly record struct IntersectionMarginValue(double Value, bool IsPercentage)
{
    /// <summary>Returns the length in CSS pixels, a percentage being resolved against <paramref name="basis"/>.</summary>
    public double Resolve(double basis) => IsPercentage ? Value * basis / 100 : Value;

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture) + (IsPercentage ? "%" : "px");
}

/// <summary>
/// The offsets of a root margin or of a scroll margin of an intersection observer
/// (https://w3c.github.io/IntersectionObserver/#dom-intersectionobserver-rootmargin): the amounts by which the top, right,
/// bottom and left edges of a rectangle are moved outwards.
/// </summary>
internal readonly record struct IntersectionMargin(IntersectionMarginValue Top, IntersectionMarginValue Right, IntersectionMarginValue Bottom, IntersectionMarginValue Left)
{
    /// <summary>The margin "0px 0px 0px 0px".</summary>
    public static IntersectionMargin Zero => default;

    /// <summary>A margin whose four offsets are a percentage of the size of the rectangle.</summary>
    public static IntersectionMargin FromPercentage(double percentage)
    {
        var value = new IntersectionMarginValue(percentage, IsPercentage: true);
        return new IntersectionMargin(value, value, value, value);
    }

    /// <summary>A margin whose four offsets are a length in CSS pixels.</summary>
    public static IntersectionMargin FromPixels(double pixels)
    {
        var value = new IntersectionMarginValue(pixels, IsPercentage: false);
        return new IntersectionMargin(value, value, value, value);
    }

    public bool IsZero => Top.Value == 0 && Right.Value == 0 && Bottom.Value == 0 && Left.Value == 0;

    /// <summary>
    /// https://w3c.github.io/IntersectionObserver/#parse-a-margin: 1 to 4 absolute lengths or percentages (none is 0px), or null
    /// for a failure. As in the browsers, the lengths are truncated to integral CSS pixels.
    /// </summary>
    public static IntersectionMargin? Parse(string marginString)
    {
        var tokens = new List<IntersectionMarginValue>(4);
        foreach (var value in CssParser.ParseListOfComponentValues(marginString))
        {
            if (value is not CssPreservedToken { Token: var token })
                return null;

            if (token.Type == CssTokenType.Whitespace)
                continue;

            if (tokens.Count == 4)
                return null;

            if (token.Type == CssTokenType.Percentage)
            {
                tokens.Add(new IntersectionMarginValue(token.NumericValue, IsPercentage: true));
            }
            else if (token.Type == CssTokenType.Dimension && CssCalc.TryConvertToCanonicalUnit(token.NumericValue, Infra.InfraAscii.ToAsciiLowercase(token.Value), out var pixels, out var unit) && unit == "px")
            {
                tokens.Add(new IntersectionMarginValue(Math.Truncate(pixels) + 0d, IsPercentage: false));
            }
            else
            {
                return null;
            }
        }

        return tokens.Count switch
        {
            0 => Zero,
            1 => new IntersectionMargin(tokens[0], tokens[0], tokens[0], tokens[0]),
            2 => new IntersectionMargin(tokens[0], tokens[1], tokens[0], tokens[1]),
            3 => new IntersectionMargin(tokens[0], tokens[1], tokens[2], tokens[1]),
            _ => new IntersectionMargin(tokens[0], tokens[1], tokens[2], tokens[3]),
        };
    }

    /// <summary>
    /// Returns the rectangle with its edges moved outwards by the offsets "in a manner similar to CSS's margin property". As in
    /// the browsers, the percentages of the left and right offsets are relative to the width of the rectangle, the ones of the
    /// top and bottom offsets to its height.
    /// </summary>
    public PhysicalRect Apply(PhysicalRect rect)
    {
        if (IsZero)
            return rect;

        var top = Top.Resolve(rect.Height);
        var right = Right.Resolve(rect.Width);
        var bottom = Bottom.Resolve(rect.Height);
        var left = Left.Resolve(rect.Width);
        return new PhysicalRect(rect.X - left, rect.Y - top, Math.Max(0, rect.Width + left + right), Math.Max(0, rect.Height + top + bottom));
    }

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Top} {Right} {Bottom} {Left}");
}

/// <summary>
/// The geometry of an intersection observation (https://w3c.github.io/IntersectionObserver/#update-intersection-observations-algo):
/// the bounding box of the target, its intersection with the root intersection rectangle, and the root intersection rectangle.
/// </summary>
/// <param name="TargetRect">The bounding box of the target, in the coordinates of the viewport of its document.</param>
/// <param name="IntersectionRect">The intersection, in the coordinates of the viewport of the document of the target (a zero rectangle when it does not intersect).</param>
/// <param name="RootBounds">The root intersection rectangle, in the coordinates of the viewport of the document of the root.</param>
/// <param name="IsIntersecting">Whether the target intersects the root, edge-adjacent and zero-area intersections included.</param>
/// <param name="IsVisible">The visibility of the target (https://w3c.github.io/IntersectionObserver/#calculate-visibility-algo).</param>
internal readonly record struct IntersectionObservation(PhysicalRect TargetRect, PhysicalRect IntersectionRect, PhysicalRect RootBounds, bool IsIntersecting, bool IsVisible);

/// <summary>
/// Computes the intersection of a target element with an intersection root (https://w3c.github.io/IntersectionObserver/#calculate-intersection-rect-algo)
/// from the fragments of the layout: the layouts of the documents of the target and of the root (and of the documents between
/// them) are updated when they are out of date.
/// </summary>
/// <remarks>
/// <para>
/// The bounding box of the target is the one of <c>getBoundingClientRect()</c>. Each of its box fragments is clipped by the
/// overflow clip edges of its ancestor fragments (the containing block chain: the out-of-flow boxes are children of the fragment
/// of their containing block) up to the root, in the axes they clip; the scroll margin expands the clip of the scroll
/// containers. The viewport of the document of a nested browsing context (a scroll container too) clips it, then it is mapped
/// to the content box of its container (the transforms included) and clipped by the ancestors of the container. The result is
/// intersected with the root intersection rectangle, and mapped back to the coordinates of the viewport of the document of
/// the target. The intersections are edge-inclusive: two edge-adjacent rectangles, or a rectangle of zero area in another one,
/// intersect.
/// </para>
/// <para>
/// A target that is not rendered (no box, <c>display: none</c>, in the skipped contents of an element), that is not a
/// descendant of an element root in the containing block chain, or that is not in the document of an explicit root, does
/// not intersect, and its rectangles are zero rectangles.
/// </para>
/// </remarks>
internal static class IntersectionGeometry
{
    /// <summary>
    /// Runs the steps 4 to 14 of https://w3c.github.io/IntersectionObserver/#update-intersection-observations-algo for a target:
    /// <paramref name="root"/> is the element or the document of an explicit root observer, or null for the implicit root (the
    /// document of the top-level browsing context). The margins only apply when <paramref name="applyMargins"/> is true (a
    /// same-origin-domain target).
    /// </summary>
    public static IntersectionObservation Compute(Node? root, Document rootDocument, Element target, IntersectionMargin rootMargin, IntersectionMargin scrollMargin, bool applyMargins, bool trackVisibility)
    {
        // As in the browsers, the rectangles of a target that is not rendered or not in the containing block chain of the root are
        // zero rectangles, the root intersection rectangle included (WPT intersection-observer/display-none.html)
        var notIntersecting = default(IntersectionObservation);
        if (GetRootIntersectionRectangle(root, rootDocument, rootMargin, scrollMargin, applyMargins) is not { } rootRect)
            return notIntersecting;

        // Step 5: the target of an explicit root observer must be in the document of the root
        var targetDocument = target.NodeDocument;
        if (root is not null && !ReferenceEquals(targetDocument, rootDocument))
            return notIntersecting;

        if (CssomViewGeometry.GetLayoutEngine(target) is not { } engine || ResizeObservations.IsInSkippedContents(target))
            return notIntersecting;

        var pieces = GetTargetPieces(engine, target);
        if (pieces.Count == 0)
            return notIntersecting;

        var targetRect = pieces[0].Rect;
        for (var i = 1; i < pieces.Count; i++)
        {
            targetRect = targetRect.Union(pieces[i].Rect);
        }

        // Step 6: the target of an element root must be its descendant in the containing block chain
        var rootElement = root as Element;
        PhysicalRect? intersection = null;
        foreach (var (rect, clip, start) in pieces)
        {
            // As in the browsers, the clip path of the target clips it too (WPT intersection-observer/clip-path-animation.html)
            var clipped = rect;
            var clipIntersects = clip is not { } clipRect || TryIntersect(rect, clipRect, out clipped);
            if (!TryComputeIntersection(engine, clipped, start, rootElement, rootDocument, scrollMargin, rootRect, out var pieceIntersection, out var inContainingBlockChain) || !clipIntersects)
            {
                if (!inContainingBlockChain)
                    return notIntersecting;

                continue;
            }

            intersection = intersection is { } previous ? UnionWithEmpty(previous, pieceIntersection) : pieceIntersection;
        }

        if (intersection is not { } intersectionRect)
            return new IntersectionObservation(targetRect, default, rootRect, IsIntersecting: false, IsVisible: false);

        var isVisible = trackVisibility && ComputeVisibility(engine, target, intersectionRect, rootElement, rootDocument);
        return new IntersectionObservation(targetRect, intersectionRect, rootRect, IsIntersecting: true, isVisible);
    }

    /// <summary>
    /// Returns whether the border boxes of the fragments of a box intersect the viewport of its document expanded by a margin, after
    /// the clipping by the overflow clip edges of its ancestors (expanded by the same margin for the scroll containers), as an
    /// intersection observer whose root is the document and whose root margin and scroll margin are <paramref name="margin"/> (for
    /// the proximity to the viewport of <c>content-visibility: auto</c>). A box without fragment does not intersect.
    /// With <paramref name="includeOverflowClipEdge"/>, the border boxes are extended to the overflow clip edge of the box (the
    /// edge expanded by <c>overflow-clip-margin</c>).
    /// </summary>
    public static bool IntersectsViewport(LayoutEngine engine, LayoutBox box, IntersectionMargin margin, bool includeOverflowClipEdge = false)
    {
        if (engine.LaidOutRoot is not { } root)
            return false;

        var viewport = margin.Apply(root.PaddingBoxRect);
        for (var fragment = engine.GetLaidOutFirstFragment(box); fragment is not null; fragment = fragment.NextFragmentOfBox)
        {
            var local = includeOverflowClipEdge && fragment is BoxFragment boxFragment ? fragment.LocalRect.Union(boxFragment.OverflowClipRect) : fragment.LocalRect;
            var rect = engine.ToViewport(LayoutEngine.MapToRoot(fragment, local)).Bounds;
            if (ClipByAncestors(engine, fragment.Parent, rootElement: null, margin, ref rect, out _) && TryIntersect(rect, viewport, out _))
                return true;
        }

        return false;
    }

    /// <summary>
    /// The root intersection rectangle (https://w3c.github.io/IntersectionObserver/#intersectionobserver-root-intersection-rectangle),
    /// in the coordinates of the viewport of the document of the root: the viewport of a document root (without its scrollbars),
    /// the padding box of an element root that has a content clip, else the bounding box of the element root; expanded by the root
    /// margin and, for a scroll container (the viewport included), by the scroll margin. Null when the root is not rendered or
    /// its document is not fully active.
    /// </summary>
    public static PhysicalRect? GetRootIntersectionRectangle(Node? root, Document rootDocument, IntersectionMargin rootMargin, IntersectionMargin scrollMargin, bool applyMargins)
    {
        PhysicalRect rect;
        if (root is Element element)
        {
            if (CssomViewGeometry.GetLayoutEngine(element) is not { } engine || ResizeObservations.IsInSkippedContents(element) || CssomViewGeometry.GetFirstBox(engine, element) is not { } fragment)
                return null;

            if (fragment.ClipsOverflow && element is not SvgElement)
            {
                rect = engine.ToViewport(LayoutEngine.MapToRoot(fragment, fragment.PaddingBoxRect)).Bounds;
                if (fragment.IsScrollContainer && applyMargins)
                {
                    rect = scrollMargin.Apply(rect);
                }
            }
            else
            {
                var quads = CssomViewGeometry.GetClientQuads(element);
                if (quads.Count == 0)
                    return null;

                rect = quads[0].Bounds;
                for (var i = 1; i < quads.Count; i++)
                {
                    rect = rect.Union(quads[i].Bounds);
                }
            }
        }
        else
        {
            if (!rootDocument.IsFullyActive || rootDocument.BrowsingContext is null || rootDocument.GetLayoutEngine().EnsureLayout() is not { } viewport)
                return null;

            rect = viewport.PaddingBoxRect;
            if (applyMargins)
            {
                rect = scrollMargin.Apply(rect);
            }
        }

        return applyMargins ? rootMargin.Apply(rect) : rect;
    }

    /// <summary>
    /// The parts of the bounding box of a target, in the coordinates of the viewport: the border box of each box fragment of its
    /// principal box with the bounds of its clip path and the ancestor fragment that clips it first, or the bounding box of an
    /// element of an SVG document fragment, clipped by its outermost svg element.
    /// </summary>
    private static List<(PhysicalRect Rect, PhysicalRect? Clip, Fragment? Start)> GetTargetPieces(LayoutEngine engine, Element target)
    {
        var result = new List<(PhysicalRect, PhysicalRect?, Fragment?)>();
        if (target is SvgElement svg && CssomViewGeometry.GetOutermostSvgElement(svg) is { } outermost)
        {
            // As in the browsers, the rectangle of an SVG target is its visual rectangle (its stroke bounding box), clipped by the
            // bounds of the clip paths of the target and its SVG ancestors (WPT intersection-observer/svg-stroke-change.html,
            // svg-clipped-rect-target.html)
            if (engine.GetFragments(outermost) is { Count: > 0 } outermostFragments && outermostFragments[0] is BoxFragment outermostFragment)
            {
                var viewport = SvgUserSpace.GetOutermostViewport(outermostFragment);
                if (SvgBoundingBox.GetBoundingQuad(svg, viewport, stroke: true) is { } quad)
                {
                    var toViewport = SvgUserSpace.GetContentBoxToViewportTransform(engine, outermostFragment);
                    PhysicalRect? clip = null;
                    for (Node? node = svg; node is SvgElement element && !ReferenceEquals(element, outermost); node = node.ParentNode)
                    {
                        if (SvgClipping.GetBounds(element, viewport) is not { } bounds || SvgUserSpace.GetTransformToOutermostContentBox(element, out _, viewport) is not { } toContent)
                            continue;

                        var clipBounds = Transform(toContent.Then(toViewport), bounds);
                        clip = clip is { } previous ? (TryIntersect(previous, clipBounds, out var both) ? both : new PhysicalRect(clipBounds.X, clipBounds.Y, 0, 0)) : clipBounds;
                    }

                    result.Add((Transform(toViewport, quad.Bounds), clip, outermostFragment));
                }
            }

            return result;
        }

        foreach (var fragment in CssomViewGeometry.GetClientFragments(engine, target))
        {
            var clip = ClipPaths.GetBounds(fragment) is { } clipPath ? engine.ToViewport(LayoutEngine.MapToRoot(fragment, clipPath)).Bounds : (PhysicalRect?)null;
            result.Add((engine.ToViewport(LayoutEngine.MapToRoot(fragment, fragment.BorderBoxRect)).Bounds, clip, fragment.Parent));
        }

        return result;
    }

    /// <summary>
    /// https://w3c.github.io/IntersectionObserver/#calculate-intersection-rect-algo for a part of the target: returns whether it
    /// intersects the root, and the intersection in the coordinates of the viewport of the document of the target.
    /// <paramref name="inContainingBlockChain"/> is false when the target is not a descendant of the element root in the
    /// containing block chain.
    /// </summary>
    private static bool TryComputeIntersection(LayoutEngine targetEngine, PhysicalRect rect, Fragment? start, Element? rootElement, Document rootDocument, IntersectionMargin scrollMargin, PhysicalRect rootBounds, out PhysicalRect intersection, out bool inContainingBlockChain)
    {
        intersection = default;
        inContainingBlockChain = true;
        var intersects = true;
        var engine = targetEngine;
        var document = targetEngine.Document;
        // The scroll margin applies to the scrollports of the documents that are same origin-domain with the document of the
        // target, up to the first one that is not (as in the browsers, WPT scroll-margin-propagation.html)
        var effectiveScrollMargin = scrollMargin;
        List<(BoxFragment Container, PhysicalOffset ContentOffset, LayoutEngine ParentEngine)>? frames = null;
        while (true)
        {
            intersects &= ClipByAncestors(engine, start, rootElement, effectiveScrollMargin, ref rect, out var reachedRoot);
            if (rootElement is not null)
            {
                if (!reachedRoot)
                {
                    inContainingBlockChain = false;
                    return false;
                }

                break;
            }

            if (ReferenceEquals(document, rootDocument))
                break;

            // "If container is the document of a nested browsing context, update intersectionRect by clipping to the viewport of
            // the document, and update container to be the browsing context container of container"
            if (engine.LaidOutRoot is not { } viewport)
                return false;

            intersects &= TryIntersect(rect, effectiveScrollMargin.Apply(viewport.PaddingBoxRect), out rect);
            if (document.BrowsingContext?.Container is not { } container || CssomViewGeometry.GetLayoutEngine(container) is not { } parentEngine || parentEngine.GetFragments(container) is not [var containerFragment, ..])
                return false;

            var contentOffset = containerFragment.ContentBoxRect.Offset;
            rect = parentEngine.ToViewport(LayoutEngine.MapToRoot(containerFragment, rect.Translate(contentOffset))).Bounds;
            (frames ??= []).Add((containerFragment, contentOffset, parentEngine));
            engine = parentEngine;
            document = container.NodeDocument;
            start = containerFragment;
            if (!effectiveScrollMargin.IsZero && !Document.IsSameOriginDomain(document, targetEngine.Document))
            {
                effectiveScrollMargin = IntersectionMargin.Zero;
            }
        }

        intersects &= TryIntersect(rect, rootBounds, out rect);
        if (!intersects)
            return false;

        // "Map intersectionRect to the coordinate space of the viewport of the document containing target"
        if (frames is not null)
        {
            for (var i = frames.Count - 1; i >= 0; i--)
            {
                var (container, contentOffset, parentEngine) = frames[i];
                var quad = new PhysicalQuad(rect);
                if (!TryMapToFrame(quad.P1, out var p1) || !TryMapToFrame(quad.P2, out var p2) || !TryMapToFrame(quad.P3, out var p3) || !TryMapToFrame(quad.P4, out var p4))
                    return false;

                rect = new PhysicalQuad(p1, p2, p3, p4).Bounds;

                bool TryMapToFrame(PhysicalOffset point, out PhysicalOffset result)
                {
                    var mapped = LayoutEngine.TryMapFromRoot(container, parentEngine.FromViewport(point), out result);
                    result -= contentOffset;
                    return mapped;
                }
            }
        }

        intersection = rect;
        return true;
    }

    /// <summary>
    /// Clips a rectangle (in the coordinates of the viewport) by the overflow clip edges (in the axes they clip) and the clip paths
    /// of a fragment and of its ancestors, up to the fragment of <paramref name="rootElement"/> (excluded) or the root of the
    /// fragment tree (the viewport, excluded); the scroll margin expands the clips of the scroll containers. Returns whether the rectangle intersects all
    /// of them (edge-inclusive); <paramref name="reachedRoot"/> tells whether the fragment of the root element was reached.
    /// </summary>
    private static bool ClipByAncestors(LayoutEngine engine, Fragment? start, Element? rootElement, IntersectionMargin scrollMargin, ref PhysicalRect rect, out bool reachedRoot)
    {
        reachedRoot = false;
        var intersects = true;
        for (var ancestor = start; ancestor is { Parent: not null }; ancestor = ancestor.Parent)
        {
            if (ancestor is not BoxFragment box)
                continue;

            if (rootElement is not null && ReferenceEquals(box.Box.Element, rootElement) && box.Box.PseudoElement is null)
            {
                reachedRoot = true;
                break;
            }

            // As in the browsers, a box whose transform flattens its contents (not invertible on the plane z = 0) hides them
            if (LayoutEngine.GetTransform(box) is { } matrix && !LayoutTransforms.IsInvertibleOnPlane(matrix))
            {
                intersects = false;
            }

            // "If container has a content clip or a css clip-path property, update intersectionRect by applying container's clip"
            // (the bounding box of the clip path, expanded by the scroll margin for a scroll container as in the browsers)
            if (ClipPaths.GetBounds(box) is { } clipPath)
            {
                var clipPathRect = engine.ToViewport(LayoutEngine.MapToRoot(box, box.IsScrollContainer ? scrollMargin.Apply(clipPath) : clipPath)).Bounds;
                intersects &= TryIntersect(rect, clipPathRect, out rect);
            }

            if (!box.ClipsOverflow)
                continue;

            var clip = box.IsScrollContainer ? scrollMargin.Apply(box.OverflowClipRect) : box.OverflowClipRect;
            var clipRect = engine.ToViewport(LayoutEngine.MapToRoot(box, clip)).Bounds;
            if (!box.ClipsOverflowX)
            {
                clipRect = new PhysicalRect(rect.X, clipRect.Y, rect.Width, clipRect.Height);
            }
            else if (!box.ClipsOverflowY)
            {
                clipRect = new PhysicalRect(clipRect.X, rect.Y, clipRect.Width, rect.Height);
            }

            intersects &= TryIntersect(rect, clipRect, out rect);
        }

        return intersects;
    }

    /// <summary>
    /// Intersects two rectangles: the edge-adjacent rectangles intersect, as do a rectangle of zero area and a rectangle that
    /// contains it (https://w3c.github.io/IntersectionObserver/#update-intersection-observations-algo, step 11). The result is
    /// the empty rectangle at the origin when they do not intersect.
    /// </summary>
    public static bool TryIntersect(PhysicalRect a, PhysicalRect b, out PhysicalRect result)
    {
        var left = Math.Max(a.Left, b.Left);
        var top = Math.Max(a.Top, b.Top);
        var right = Math.Min(a.Right, b.Right);
        var bottom = Math.Min(a.Bottom, b.Bottom);
        if (right < left || bottom < top)
        {
            result = default;
            return false;
        }

        result = new PhysicalRect(left, top, right - left, bottom - top);
        return true;
    }

    /// <summary>The smallest rectangle that contains two rectangles, including the rectangles of zero area (e.g. a zero-area intersection).</summary>
    private static PhysicalRect UnionWithEmpty(PhysicalRect a, PhysicalRect b)
    {
        var left = Math.Min(a.Left, b.Left);
        var top = Math.Min(a.Top, b.Top);
        return new PhysicalRect(left, top, Math.Max(a.Right, b.Right) - left, Math.Max(a.Bottom, b.Bottom) - top);
    }

    /// <summary>
    /// https://w3c.github.io/IntersectionObserver/#calculate-visibility-algo for an intersecting target: false when the target or
    /// an ancestor up to the root (the containers of the nested browsing contexts included) has a transform other than a 2D
    /// translation or a proportional upscaling, an opacity other than 1 or a filter, or when the intersection of the target is
    /// occluded by a box painted above it or above one of its containers (a box of another element that paints a background, a
    /// border, an outer shadow, an image, a form control or text, and whose border box, border shaped by <c>border-shape</c> or
    /// shadow intersects it). The rest of the ink overflow (filters, outlines) is not taken into account.
    /// </summary>
    private static bool ComputeVisibility(LayoutEngine engine, Element target, PhysicalRect intersection, Element? rootElement, Document rootDocument)
    {
        if (intersection.IsEmpty || engine.GetFragments(target) is not { Count: > 0 } fragments)
            return false;

        IReadOnlyList<Fragment> subject = fragments;
        var rect = intersection;
        while (true)
        {
            for (Fragment? current = subject[0]; current is { Parent: not null }; current = current.Parent)
            {
                if (current is not BoxFragment box)
                    continue;

                if (rootElement is not null && ReferenceEquals(box.Box.Element, rootElement) && box.Box.PseudoElement is null)
                    break;

                var style = box.Box.Style;
                if (LayoutEngine.GetTransform(box) is { } matrix && !IsTranslationOrUpscale(matrix))
                    return false;

                if (double.TryParse(style.GetComputedValue("opacity"), NumberStyles.Float, CultureInfo.InvariantCulture, out var opacity) && opacity < 1)
                    return false;

                if (style.GetComputedValue("filter") is { } filter && filter != "none")
                    return false;
            }

            if (IsOccluded(engine, subject, rect))
                return false;

            if (rootElement is not null || ReferenceEquals(engine.Document, rootDocument))
                return true;

            // The container of the nested browsing context, and the intersection in the coordinates of its document
            if (engine.Document.BrowsingContext?.Container is not { } container || CssomViewGeometry.GetLayoutEngine(container) is not { } parentEngine || parentEngine.GetFragments(container) is not [var containerFragment, ..])
                return false;

            rect = parentEngine.ToViewport(LayoutEngine.MapToRoot(containerFragment, rect.Translate(containerFragment.ContentBoxRect.Offset))).Bounds;
            subject = [containerFragment];
            engine = parentEngine;
        }
    }

    /// <summary>Whether a box painted after the fragments of an element (excluding their descendants) covers a part of a rectangle (in the coordinates of the viewport).</summary>
    private static bool IsOccluded(LayoutEngine engine, IReadOnlyList<Fragment> fragments, PhysicalRect rect)
    {
        var subject = new HashSet<Fragment>(fragments, ReferenceEqualityComparer.Instance);
        var paintOrder = engine.GetPaintOrder();
        var index = paintOrder.FindIndex(item => subject.Contains(item.Fragment));
        if (index < 0)
            return false;

        for (var i = index + 1; i < paintOrder.Count; i++)
        {
            var fragment = paintOrder[i].Fragment;
            if (IsInclusiveDescendantOf(fragment, subject) || !Paints(fragment))
                continue;

            // The area a box paints: its border box (or its border shaped by border-shape) and its outer shadows
            var local = fragment.LocalRect;
            if (fragment is BoxFragment box)
            {
                // The contents of a replaced box are in its border box
                var decorations = Painter.GetDecorationBounds(box);
                local = decorations is null ? box.BorderBoxRect : box.Box.Kind == ContainerBoxKind.Replaced ? UnionWithEmpty(decorations.Value, box.BorderBoxRect) : decorations.Value;
            }

            var bounds = engine.ToViewport(LayoutEngine.MapToRoot(fragment, local)).Bounds;
            if (bounds.Left < rect.Right && rect.Left < bounds.Right && bounds.Top < rect.Bottom && rect.Top < bounds.Bottom)
                return true;
        }

        return false;

        static bool IsInclusiveDescendantOf(Fragment fragment, HashSet<Fragment> ancestors)
        {
            for (Fragment? current = fragment; current is not null; current = current.Parent)
            {
                if (ancestors.Contains(current))
                    return true;
            }

            return false;
        }
    }

    /// <summary>Whether a fragment paints something that occludes the boxes painted before it: text, a replaced element, a background, a border or an outer shadow, unless it is invisible or transparent.</summary>
    private static bool Paints(Fragment fragment)
    {
        var box = fragment is BoxFragment boxFragment ? boxFragment.Box : null;
        var style = fragment is TextFragment text ? text.Box?.Style : box?.Style;
        if (style is null || style.GetComputedValue("visibility") != "visible")
            return false;

        for (Fragment? current = fragment; current is not null; current = current.Parent)
        {
            if (current is BoxFragment { Box.Style: var ancestorStyle } && double.TryParse(ancestorStyle.GetComputedValue("opacity"), NumberStyles.Float, CultureInfo.InvariantCulture, out var opacity) && opacity == 0)
                return false;
        }

        if (fragment is TextFragment)
            return true;

        if (box is null)
            return false;

        if (box.Kind == ContainerBoxKind.Replaced)
            return true;

        return fragment is BoxFragment decorated && Painter.GetDecorationBounds(decorated) is not null;
    }

    /// <summary>Whether a transformation matrix is a 2D translation or a proportional 2D upscaling.</summary>
    private static bool IsTranslationOrUpscale(double[] m)
    {
        // The 2D part (a b c d) must be a uniform scale >= 1, without rotation or skew, and the 3D components the identity
        if (m[1] != 0 || m[4] != 0 || m[0] != m[5] || m[0] < 1)
            return false;

        return m[2] == 0 && m[3] == 0 && m[6] == 0 && m[7] == 0 && m[8] == 0 && m[9] == 0 && m[10] == 1 && m[11] == 0 && m[14] == 0 && m[15] == 1;
    }
    /// <summary>Returns the bounds of a rectangle transformed by an affine transform.</summary>
    private static PhysicalRect Transform(SvgAffine transform, PhysicalRect rect)
    {
        var p1 = transform.Apply(rect.X, rect.Y);
        var p2 = transform.Apply(rect.X + rect.Width, rect.Y);
        var p3 = transform.Apply(rect.X + rect.Width, rect.Y + rect.Height);
        var p4 = transform.Apply(rect.X, rect.Y + rect.Height);
        var minX = Math.Min(Math.Min(p1.X, p2.X), Math.Min(p3.X, p4.X));
        var minY = Math.Min(Math.Min(p1.Y, p2.Y), Math.Min(p3.Y, p4.Y));
        var maxX = Math.Max(Math.Max(p1.X, p2.X), Math.Max(p3.X, p4.X));
        var maxY = Math.Max(Math.Max(p1.Y, p2.Y), Math.Max(p3.Y, p4.Y));
        return new PhysicalRect(minX, minY, maxX - minX, maxY - minY);
    }
}
