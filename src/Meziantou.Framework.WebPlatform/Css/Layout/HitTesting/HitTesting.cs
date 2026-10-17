using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Rendering;
using Meziantou.Framework.WebPlatform.Svg;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// A fragment hit at a point, and the point in the coordinates of the fragment (its border box origin, before its transform).
/// <see cref="Target"/> is the element hit in a replaced box: an element of the SVG document fragment of an outermost svg
/// element, an area of the image map of an image.
/// </summary>
internal readonly record struct HitTestResult(Fragment Fragment, PhysicalOffset LocalPoint, Element? Target = null)
{
    /// <summary>
    /// The element associated with the hit box (https://drafts.csswg.org/cssom-view/#hit-testing): the element of a box, the
    /// originating element of a pseudo-element box, the parent element of a text, the element hit in a replaced box.
    /// </summary>
    public Element Element => Target ?? Fragment.Box!.Element!;
}

/// <summary>
/// The hit testing (https://drafts.csswg.org/cssom-view/#hit-testing): the box and text fragments at a point of the viewport,
/// from the topmost, in the reverse of the painting order (<see cref="PaintOrder"/>). The point is mapped to the coordinates
/// of each fragment through the scroll positions of the scroll containers and the inverse of the transforms of its ancestors;
/// a fragment is only hit in the overflow clip edge of its ancestors that clip their overflow.
/// </summary>
/// <remarks>
/// A fragment is not hit when its box has <c>visibility</c> other than <c>visible</c>, <c>pointer-events: none</c>, or when its
/// element is inert (https://html.spec.whatwg.org/multipage/interaction.html#inert: "hit-testing must act as if the
/// pointer-events CSS property were set to none"); its descendants can be hit. The hit area of a box is its border box rounded
/// by its border radii, clipped by its <c>clip</c> rectangle and its <c>clip-path</c> (and the ones of its ancestors in its
/// containing block chain; the masks are not taken into account), the one of a text fragment its rectangle. The
/// scrollbars belong to the box of their scroll container.
/// </remarks>
internal static class HitTesting
{
    /// <summary>
    /// Appends the fragments at a point of the viewport (https://drafts.csswg.org/cssom-view/#css-layout-viewport) to
    /// <paramref name="results"/>, the topmost first: all of them, or only the topmost one when <paramref name="all"/> is false.
    /// The layout is computed when it is out of date.
    /// </summary>
    public static void HitTest(LayoutEngine engine, PhysicalOffset point, List<HitTestResult> results, bool all)
    {
        if (engine.EnsureLayout() is null)
            return;

        // https://drafts.csswg.org/css-view-transitions-1/#document-rendering-suppression-for-view-transitions: "all pointer hit
        // testing must target its document element, ignoring all other elements"
        var document = engine.Document;
        if (document.ViewTransitionRenderingSuppressed)
        {
            if (document.DocumentElement is { } root && engine.GetFragments(root) is [var rootFragment, ..])
            {
                results.Add(new HitTestResult(rootFragment, engine.FromViewport(point) - rootFragment.GetOffsetInRoot()));
            }

            return;
        }

        var context = new HitTestContext(engine, engine.FromViewport(point), results, all)
        {
            SkipsCapturedElements = document.IsViewTransitionAnimating && document.HasElementsCapturedInViewTransition,
            SkipsFrozenContents = document.HasFrozenViewTransitionScopes,
        };
        HitTest(context, engine.GetPaintOrder());
    }

    /// <summary>
    /// Whether a box is a box of an element captured in the animating view transition or of its contents
    /// (https://drafts.csswg.org/css-view-transitions-1/#view-transition-painting-order): such boxes "do not respond to hit-testing
    /// (as if they had pointer-events: none)"; the pseudo-elements of the view transition, which show their images, do.
    /// </summary>
    private static bool IsCapturedInViewTransition(LayoutBox box)
    {
        if (box.Element is not { } element || ViewTransitionPseudoTree.IsViewTransitionPseudoElement(box.PseudoElement))
            return false;

        var document = element.NodeDocument;
        if (box.PseudoElement is { } pseudoElement && document.IsCapturedInViewTransition(element, pseudoElement))
            return true;

        for (var current = element; current is not null; current = Cascade.StyleEngine.GetInheritanceParent(current))
        {
            if (document.IsCapturedInViewTransition(current))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Whether a box is in the contents of the root element of a scoped view transition whose rendering is suppressed
    /// (https://drafts.csswg.org/css-view-transitions-2/#scoped-view-transitions: "rendering is paused within the view transition
    /// root element"): as the hit testing of a document whose rendering is suppressed targets its document element, the hit
    /// testing in such an element targets the element.
    /// </summary>
    private static bool IsInFrozenViewTransitionScope(LayoutBox box)
    {
        if (box.Element is not { } element)
            return false;

        var document = element.NodeDocument;
        for (var current = box.PseudoElement is null ? Cascade.StyleEngine.GetInheritanceParent(element) : element; current is not null; current = Cascade.StyleEngine.GetInheritanceParent(current))
        {
            if (document.GetScopedViewTransition(current) is { FrozenFrame: not null })
                return true;
        }

        return false;
    }

    /// <summary>Hit tests the items of a painting order, from the topmost. Returns whether the hit testing must stop (a fragment was hit and only the topmost one is needed).</summary>
    private static bool HitTest(HitTestContext context, List<PaintItem> paintOrder)
    {
        var localPoints = context.LocalPoints;
        var results = context.Results;
        var all = context.All;

        // The topmost hit of a 3D rendering context is the nearest one (its painting order is not its order in depth)
        HitTestResult? best3D = null;
        Fragment? best3DContext = null;
        var best3DDepth = double.NegativeInfinity;
        for (var i = paintOrder.Count - 1; i >= 0; i--)
        {
            var (fragment, isSplitInlineBand) = paintOrder[i];
            if (isSplitInlineBand)
            {
                if (HitTestSplitInlineBand(context, (BoxFragment)fragment) && !all)
                    return true;

                continue;
            }

            // A block-level replaced box is hit once, where its content is painted
            if (fragment.Box is null || paintOrder[i].Part == PaintItemPart.Decorations || !IsHitTestable(fragment.Box))
                continue;

            // As in the browsers, an anonymous table cell is hit for the element of its nearest ancestor box that has one (a table
            // row or a row group, which are not hit otherwise: the area of a row group beside the content of its anonymous cell)
            var anonymousCellOwner = fragment.Box.Element is null ? GetAnonymousCellOwner(fragment) : null;
            if (fragment.Box.Element is null && anonymousCellOwner is null)
                continue;

            if (context.SkipsCapturedElements && IsCapturedInViewTransition(fragment.Box))
                continue;

            if (context.SkipsFrozenContents && IsInFrozenViewTransitionScope(fragment.Box))
                continue;

            if (GetLocalPoint(context, fragment) is not { } local || !Contains(fragment, local)
                || (fragment is BoxFragment clipped && (Painter.IsClippedOut(clipped, local) || IsBackFaceHidden(clipped))))
            {
                continue;
            }

            // The elements of an SVG document fragment are above the box of their outermost svg element
            if (fragment is BoxFragment { Box: { Kind: ContainerBoxKind.Replaced, Element: SvgSvgElement svg } } svgFragment)
            {
                var content = svgFragment.ContentBoxRect;
                var svgContext = new SvgHitTestContext(context, svgFragment, SvgUserSpace.GetOutermostViewport(svgFragment), local - content.Offset);
                if (HitTestSvgChildren(svgContext, svg, SvgUserSpace.GetOutermostContentTransform(svg, svgContext.Viewport), useElement: null) && !all)
                    return true;
            }

            if (anonymousCellOwner is not null ? !IsHitTestTarget(anonymousCellOwner.Style, anonymousCellOwner.Element!) : !IsHitTestTarget(fragment.Box.Style, fragment.Box.Element!, fragment.Box.PseudoElement))
                continue;

            // https://html.spec.whatwg.org/multipage/image-maps.html#image-map-processing-model: the area of the image map of an
            // image under the point is hit instead of the image
            var area = fragment is BoxFragment { Box: { Kind: ContainerBoxKind.Replaced, Element: HtmlImageElement image } } imageFragment && image.HasAttributeWithLocalName("usemap")
                ? ImageMaps.HitTest(imageFragment, image, local)
                : null;
            var result = new HitTestResult(fragment, local, area ?? anonymousCellOwner?.Element);
            if (!all)
            {
                var depth = Get3DDepth(fragment, localPoints, out var renderingContext);
                if (best3D is not null && !ReferenceEquals(renderingContext, best3DContext))
                    break;

                if (renderingContext is not null)
                {
                    if (best3D is null || depth > best3DDepth + 1e-6)
                    {
                        (best3D, best3DContext, best3DDepth) = (result, renderingContext, depth);
                    }

                    continue;
                }
            }

            results.Add(result);
            if (!all)
                return true;
        }

        if (best3D is { } hit)
        {
            results.Add(hit);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Returns the depth (z) of the point hit on a fragment that is in a 3D rendering context (in the plane of its nearest box that
    /// participates in the context, or of the root of the context: the box that establishes a 3D rendering context and its
    /// contents that are not transformed are a plane of the context, e.g. in front of a child translated behind it), and the
    /// context (the parent of its root); <see cref="double.NaN"/> and null outside of a 3D rendering context.
    /// </summary>
    private static double Get3DDepth(Fragment fragment, Dictionary<Fragment, PhysicalOffset?> localPoints, out Fragment? renderingContext)
    {
        renderingContext = null;
        for (var current = fragment; current is not null; current = current.Parent)
        {
            if (current is not BoxFragment box)
                continue;

            double[]? m;
            Fragment? ancestor;
            if (LayoutTransforms.ParticipatesIn3DRenderingContext(box))
            {
                m = LayoutEngine.Get3DRenderingContextMatrix(box, out ancestor);
            }
            else if (LayoutTransforms.UsesPreserve3D(box.Box))
            {
                // The root of a 3D rendering context
                m = LayoutEngine.GetTransform(box);
                ancestor = box.Parent;
            }
            else
            {
                continue;
            }

            if (ancestor is null)
                return double.NaN;

            // A plane without 3D transform is in the plane of the parent of the root of the context
            renderingContext = ancestor;
            if (m is null)
                return 0;

            if (!localPoints.TryGetValue(box, out var cached) || cached is not { } point)
            {
                renderingContext = null;
                return double.NaN;
            }

            var z = (m[2] * point.X) + (m[6] * point.Y) + m[14];
            var w = (m[3] * point.X) + (m[7] * point.Y) + m[15];
            return w > 0 ? z / w : z;
        }

        return double.NaN;
    }

    /// <summary>
    /// Hit tests the band of a block split out of inline boxes (see <see cref="PaintItem.IsSplitInlineBand"/>): the margin box
    /// of the block in the block axis, across the content box of the block container in the inline axis. The inline boxes are
    /// hit from the innermost. Returns whether one was hit.
    /// </summary>
    private static bool HitTestSplitInlineBand(HitTestContext context, BoxFragment fragment)
    {
        var results = context.Results;
        var all = context.All;
        if (fragment.Parent is not BoxFragment container || GetLocalPoint(context, container) is not { } point || IsClippedOut(context, container, point))
            return false;

        var content = container.ContentBoxRect;
        var margin = fragment.MarginBoxRect.Translate(fragment.Offset - fragment.RelativeOffset);
        var inBand = container.WritingDirection.IsHorizontal
            ? point.X >= content.Left && point.X < content.Right && point.Y >= margin.Top && point.Y < margin.Bottom
            : point.Y >= content.Top && point.Y < content.Bottom && point.X >= margin.Left && point.X < margin.Right;
        if (!inBand)
            return false;

        var hit = false;
        for (var inline = fragment.Box.SplitInlineParent; inline is { IsInlineBox: true, Element: { } element } && !ReferenceEquals(inline, container.Box); inline = inline.Parent)
        {
            if (inline.PseudoElement is not null || !IsHitTestTarget(inline.Style, element))
                continue;

            results.Add(new HitTestResult(fragment, point, element));
            hit = true;
            if (!all)
                return true;
        }

        return hit;
    }

    /// <summary>
    /// Returns the box of the element that is hit for the fragment of an anonymous table cell: its nearest ancestor box that has
    /// an element (a pseudo-element box is not hit for its anonymous cells), or null for the other anonymous boxes.
    /// </summary>
    private static ContainerBox? GetAnonymousCellOwner(Fragment fragment)
    {
        if (fragment is not BoxFragment { Box: { IsAnonymous: true, Display.Internal: DisplayInternal.TableCell } cell })
            return null;

        for (var parent = cell.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent.Element is not null)
                return parent.PseudoElement is null && parent.Kind != ContainerBoxKind.Viewport ? parent : null;
        }

        return null;
    }

    /// <summary>
    /// Returns whether a box can be hit: as in the browsers, the table rows, the row groups, the columns and the column groups
    /// are not hit (their cells and their table are), and a table is hit by its table wrapper box (its table grid box has the
    /// same element).
    /// </summary>
    private static bool IsHitTestable(LayoutBox box)
        => box is not ContainerBox { Kind: ContainerBoxKind.TableGrid }
        && box.Display.Internal is not (DisplayInternal.TableRow or DisplayInternal.TableRowGroup or DisplayInternal.TableHeaderGroup or DisplayInternal.TableFooterGroup
            or DisplayInternal.TableColumn or DisplayInternal.TableColumnGroup);

    /// <summary>
    /// Whether a box shows its back face with <c>backface-visibility: hidden</c> (https://drafts.csswg.org/css-transforms-2/#backface-visibility-property):
    /// it is not hit (as in the browsers, its descendants can be).
    /// </summary>
    private static bool IsBackFaceHidden(BoxFragment fragment)
        => fragment.Box is { IsAnonymous: false } box && box.Style.GetComputedValue("backface-visibility") == "hidden"
            && (LayoutEngine.Get3DRenderingContextMatrix(fragment, out _) ?? LayoutEngine.GetTransform(fragment)) is { } facing && LayoutTransforms.IsBackFaceVisible(facing);

    /// <summary>Returns the topmost fragment at a point of the viewport, or null.</summary>
    public static HitTestResult? HitTest(LayoutEngine engine, PhysicalOffset point)
    {
        var results = new List<HitTestResult>(1);
        HitTest(engine, point, results, all: false);
        return results.Count > 0 ? results[0] : null;
    }

    /// <summary>Whether a box (or an element of an SVG document fragment) can be the target of the hit testing (its visibility, pointer-events and inertness).</summary>
    private static bool IsHitTestTarget(Cascade.ComputedStyle style, Element element, string? pseudoElement = null)
    {
        if (style.Layout.Visibility != Visibility.Visible || style.GetComputedValue("pointer-events") == "none")
            return false;

        // The inertness of a scroll navigation pseudo-element is the one of its scroll container, not of its originating element
        if (ScrollControls.IsScrollControl(pseudoElement))
            return !ScrollControlActions.IsInert(element.NodeDocument, element, pseudoElement!);

        return !FocusModel.IsInert(element);
    }

    /// <summary>
    /// Hit tests the children of an element of an SVG document fragment (https://svgwg.org/svg2-draft/interact.html#hit-testing),
    /// from the topmost (the last one). <paramref name="parentToContent"/> maps the user space of the children (before their
    /// transform) to the content box of the outermost svg element. Returns whether an element was hit.
    /// </summary>
    /// <param name="useElement">The use element whose referenced content is hit tested (it is the element hit), or null.</param>
    private static bool HitTestSvgChildren(SvgHitTestContext context, Element parent, SvgAffine parentToContent, Element? useElement)
    {
        // https://svgwg.org/svg2-draft/struct.html#SwitchElement: only the first direct child that is rendered
        if (parent.LocalName == "switch")
        {
            for (var child = parent.FirstElementChild; child is not null; child = child.NextElementSibling)
            {
                if (child is SvgElement && BoxGeneration.Get(child.NodeDocument.GetStyleEngine().GetStyle(child)).IsBeingRendered)
                    return HitTestSvgElement(context, child, parentToContent, useElement);
            }

            return false;
        }

        var hit = false;
        for (var child = parent.LastElementChild; child is not null; child = child.PreviousElementSibling)
        {
            if (HitTestSvgElement(context, child, parentToContent, useElement))
            {
                hit = true;
                if (!context.Parent.All)
                    return true;
            }
        }

        return hit;
    }

    /// <summary>
    /// Hit tests an element of an SVG document fragment and its descendants: the shapes with their geometry (the fill and the
    /// stroke, per <c>pointer-events</c>), the glyph cells of the texts (the innermost text content element is hit), the
    /// bounding box of the images and of the containers whose <c>pointer-events</c> is <c>bounding-box</c>, the referenced
    /// content of the use elements, the CSS boxes of the foreignObject elements.
    /// </summary>
    private static bool HitTestSvgElement(SvgHitTestContext context, Element element, SvgAffine parentToContent, Element? useElement)
    {
        if (element is not SvgElement svgElement || !element.IsConnected)
            return false;

        var style = element.NodeDocument.GetStyleEngine().GetStyle(element);
        var generation = BoxGeneration.Get(style);

        // The content referenced by a use element is rendered in its shadow tree (e.g. a symbol or the content of a defs element)
        if (useElement is null ? !generation.IsBeingRendered : generation.IsDisplayNone)
            return false;

        var toContent = SvgUserSpace.GetTransformToParent(element, context.Viewport).Then(parentToContent);
        var pointerEvents = GetPointerEvents(style);
        switch (element.LocalName)
        {
            case "g" or "a" or "svg" or "switch" or "symbol":
            {
                if (!IsInClipPath(context, element, toContent))
                    return false;

                var childHit = HitTestSvgChildren(context, element, toContent, useElement);
                if (childHit && !context.Parent.All)
                    return true;

                // https://svgwg.org/svg2-draft/interact.html#PointerEventsProperty: bounding-box hits the bounding box of a container
                return (pointerEvents == "bounding-box" && IsInBoundingBox(context, svgElement, toContent) && AddSvgResult(context, useElement ?? element)) || childHit;
            }

            case "use":
            {
                if (SvgBoundingBox.GetUseTarget(element) is not { } target || useElement is not null && ReferenceEquals(target, useElement))
                    return false;

                var targetToContent = SvgUserSpace.GetUseTranslation(element, context.Viewport).Then(toContent);
                return HitTestSvgElement(context, target, targetToContent, useElement ?? element);
            }

            case "foreignObject":
                return useElement is null && HitTestForeignObject(context, element);

            case "rect" or "circle" or "ellipse" or "line" or "polyline" or "polygon" or "path":
            {
                if (pointerEvents == "none" || FocusModel.IsInert(element) || toContent.Invert() is not { } inverse)
                    return false;

                var point = inverse.Apply(context.Point.X, context.Point.Y);
                if (!SvgClipping.Contains(element, point, context.Viewport))
                    return false;

                if (pointerEvents == "bounding-box")
                    return IsInBoundingBox(context, svgElement, toContent) && AddSvgResult(context, useElement ?? element);

                var visible = style.Layout.Visibility == Visibility.Visible;
                var (fill, stroke) = GetHitAreas(pointerEvents, visible, style.GetComputedValue("fill") != "none", style.GetComputedValue("stroke") != "none");
                if (!fill && !stroke)
                    return false;

                var geometry = SvgShapeGeometry.Get(element, context.Viewport);
                if (geometry.IsEmpty)
                    return false;

                var isHit = fill && geometry.IsPointInFill(point, style.GetComputedValue("fill-rule") == "evenodd");
                if (!isHit && stroke)
                {
                    var strokeStyle = SvgStrokeStyle.FromElement(element, context.Viewport);
                    isHit = geometry.IsPointInStroke(point, strokeStyle, strokeStyle.NonScaling ? toContent with { E = 0, F = 0 } : SvgAffine.Identity);
                }

                return isHit && AddSvgResult(context, useElement ?? element);
            }

            case "image":
            {
                if (pointerEvents == "none" || FocusModel.IsInert(element))
                    return false;

                var visible = style.Layout.Visibility == Visibility.Visible;
                var (fill, stroke) = GetHitAreas(pointerEvents == "bounding-box" ? "all" : pointerEvents, visible, isFillPainted: true, isStrokePainted: true);
                return (fill || stroke) && IsInBoundingBox(context, svgElement, toContent) && IsInClipPath(context, element, toContent) && AddSvgResult(context, useElement ?? element);
            }

            case "text":
                return HitTestText(context, element, toContent, useElement);

            default:
                return false;
        }
    }

    /// <summary>Hit tests the glyph cells of a text, from the last character: the element hit is the innermost element of the character.</summary>
    private static bool HitTestText(SvgHitTestContext context, Element text, SvgAffine toContent, Element? useElement)
    {
        if (SvgTextLayout.Get(text, context.Viewport) is not { } layout || toContent.Invert() is not { } inverse)
            return false;

        var point = inverse.Apply(context.Point.X, context.Point.Y);
        if (!SvgClipping.Contains(text, point, context.Viewport))
            return false;

        // https://svgwg.org/svg2-draft/interact.html#PointerEventsProperty: bounding-box hits the bounding box of a text content
        // element (the innermost one)
        var styleEngine = text.NodeDocument.GetStyleEngine();
        for (var i = layout.Elements.Count - 1; i >= 0; i--)
        {
            var element = layout.Elements[i];
            var elementStyle = styleEngine.GetStyle(element);
            if (GetPointerEvents(elementStyle) == "bounding-box" && layout.GetBoundingBox(element) is { } box
                && point.X >= box.MinX && point.X <= box.MaxX && point.Y >= box.MinY && point.Y <= box.MaxY)
            {
                return AddSvgResult(context, useElement ?? element);
            }
        }

        var characters = layout.Characters;
        var hit = false;
        Element? lastHit = null;
        for (var i = characters.Length - 1; i >= 0; i--)
        {
            ref readonly var character = ref characters[i];
            if (!character.IsClusterStart || character.IsHidden || !character.Contains(point))
                continue;

            // The pointer-events, the visibility and the paint of the character are the ones of its text
            var style = character.Style;
            var pointerEvents = GetPointerEvents(style);
            if (pointerEvents == "none" || FocusModel.IsInert(character.Element))
                continue;

            var (fill, stroke) = GetHitAreas(pointerEvents == "bounding-box" ? "all" : pointerEvents, style.Layout.Visibility == Visibility.Visible, style.GetComputedValue("fill") != "none", style.GetComputedValue("stroke") != "none");
            if (!fill && !stroke)
                continue;

            var target = useElement ?? character.Element;
            if (ReferenceEquals(target, lastHit))
                continue;

            context.Parent.Results.Add(new HitTestResult(context.Fragment, context.Point + context.Fragment.ContentBoxRect.Offset, target));
            lastHit = target;
            hit = true;
            if (!context.Parent.All)
                return true;
        }

        return hit;
    }

    /// <summary>Hit tests the CSS boxes of a foreignObject element (its fragment is a stacking context).</summary>
    private static bool HitTestForeignObject(SvgHitTestContext context, Element element)
    {
        var engine = context.Parent.Engine;
        if (engine.GetFragments(element) is not [var fragment, ..])
            return false;

        var paintOrder = new List<PaintItem>();
        PaintOrder.CollectStackingContext(fragment, paintOrder);
        var count = context.Parent.Results.Count;
        HitTest(context.Parent, paintOrder);
        return context.Parent.Results.Count > count;
    }

    /// <summary>Whether the point is in the bounding box of an element (mapped to the content box of the outermost svg element).</summary>
    private static bool IsInBoundingBox(SvgHitTestContext context, SvgElement element, SvgAffine toContent)
    {
        var box = SvgBoundingBox.Compute(element, fill: true, stroke: false, context.Viewport);
        if (box.Width <= 0 || box.Height <= 0 || toContent.Invert() is not { } inverse)
            return false;

        var point = inverse.Apply(context.Point.X, context.Point.Y);
        return point.X >= box.X && point.X <= box.X + box.Width && point.Y >= box.Y && point.Y <= box.Y + box.Height;
    }

    /// <summary>Whether the point is in the clipping path of an element (see <see cref="SvgClipping"/>).</summary>
    private static bool IsInClipPath(SvgHitTestContext context, Element element, SvgAffine toContent)
        => toContent.Invert() is { } inverse && SvgClipping.Contains(element, inverse.Apply(context.Point.X, context.Point.Y), context.Viewport);

    /// <summary>Adds an element of an SVG document fragment to the results (an inert element is not hit).</summary>
    private static bool AddSvgResult(SvgHitTestContext context, Element target)
    {
        if (FocusModel.IsInert(target))
            return false;

        context.Parent.Results.Add(new HitTestResult(context.Fragment, context.Point + context.Fragment.ContentBoxRect.Offset, target));
        return true;
    }

    /// <summary>
    /// https://svgwg.org/svg2-draft/interact.html#PointerEventsProperty: whether the interior (the fill) and the stroke of a
    /// graphics element can be hit, for its <c>pointer-events</c>, its visibility and whether its fill and its stroke are painted.
    /// </summary>
    private static (bool Fill, bool Stroke) GetHitAreas(string pointerEvents, bool visible, bool isFillPainted, bool isStrokePainted) => pointerEvents switch
    {
        "visiblefill" => (visible, false),
        "visiblestroke" => (false, visible),
        "visible" => (visible, visible),
        "painted" => (isFillPainted, isStrokePainted),
        "fill" => (true, false),
        "stroke" => (false, true),
        "all" => (true, true),
        "none" => (false, false),
        _ => (visible && isFillPainted, visible && isStrokePainted), // visiblePainted (auto)
    };

    /// <summary>The computed value of <c>pointer-events</c>, in lowercase (the keywords are ASCII case-insensitive).</summary>
    private static string GetPointerEvents(Cascade.ComputedStyle style) => Infra.InfraAscii.ToAsciiLowercase(style.GetComputedValue("pointer-events"));

    /// <summary>The state of a hit testing.</summary>
    private sealed class HitTestContext(LayoutEngine engine, PhysicalOffset rootPoint, List<HitTestResult> results, bool all)
    {
        public LayoutEngine Engine { get; } = engine;

        public PhysicalOffset RootPoint { get; } = rootPoint;

        public List<HitTestResult> Results { get; } = results;

        public bool All { get; } = all;

        /// <summary>Whether the boxes of the elements captured in a view transition are not hit (the transition is animating).</summary>
        public bool SkipsCapturedElements { get; init; }

        /// <summary>Whether the contents of the root elements of the scoped view transitions whose rendering is suppressed are not hit.</summary>
        public bool SkipsFrozenContents { get; init; }

        public Dictionary<Fragment, PhysicalOffset?> LocalPoints { get; } = new(ReferenceEqualityComparer.Instance);

        /// <summary>The last box that clips both axes whose children were tested, and whether the point is outside the shape of its overflow clip edge (see <see cref="IsClippedOut"/>).</summary>
        public BoxFragment? ShapedClipper { get; set; }

        public bool IsOutsideShapedClip { get; set; }
    }

    /// <summary>The state of the hit testing of an SVG document fragment: the point in the coordinates of the content box of the outermost svg element.</summary>
    private sealed class SvgHitTestContext(HitTestContext parent, BoxFragment fragment, SvgOutermostViewport viewport, PhysicalOffset point)
    {
        public HitTestContext Parent { get; } = parent;

        public BoxFragment Fragment { get; } = fragment;

        public SvgOutermostViewport Viewport { get; } = viewport;

        public PhysicalOffset Point { get; } = point;
    }

    /// <summary>
    /// Whether a point (in the coordinates of a fragment) is in the hit area of the fragment: its border box, with the rounded
    /// corners of its <c>border-radius</c> (https://drafts.csswg.org/css-backgrounds/#corner-clipping) for a box, shaped by
    /// <c>corner-shape</c> (the outer contour of the box, https://drafts.csswg.org/css-borders-4/#corner-shape-rendering), or
    /// the outer edge of its border shaped by <c>border-shape</c>.
    /// </summary>
    private static bool Contains(Fragment fragment, PhysicalOffset point)
    {
        // https://drafts.csswg.org/css-borders-4/#border-shape: the box is hit in the outer edge of its shaped border (the shape
        // and its stroke, or the first shape), which can be outside of its border box
        if (fragment is BoxFragment shaped && BorderShapeGeometry.Get(shaped) is { } shape)
            return PathOperations.Contains(shape.OuterPath, point);

        var size = fragment.Size;
        if (!fragment.LocalRect.Contains(point.X, point.Y))
            return false;

        if (fragment is not BoxFragment box || BorderRadii.Get(box) is not { } radii)
            return true;

        var borderRect = new PhysicalRect(0, 0, size.Width, size.Height);
        if (CornerContour.Create(box.Box.Style, borderRect, radii) is { } contour)
            return contour.Contains(borderRect, point);

        return IsInCorners(borderRect, radii, point);
    }

    /// <summary>Whether a point of a rectangle is inside the ellipses of its rounded corners.</summary>
    private static bool IsInCorners(PhysicalRect rect, BorderRadii radii, PhysicalOffset point)
    {
        return IsInCorner(point.X - rect.Left, point.Y - rect.Top, radii.TopLeft)
            && IsInCorner(rect.Right - point.X, point.Y - rect.Top, radii.TopRight)
            && IsInCorner(rect.Right - point.X, rect.Bottom - point.Y, radii.BottomRight)
            && IsInCorner(point.X - rect.Left, rect.Bottom - point.Y, radii.BottomLeft);

        // Whether a point at the given distances from the two sides of a corner is inside the ellipse of the corner
        static bool IsInCorner(double x, double y, (double X, double Y) radius)
        {
            if (radius.X <= 0 || radius.Y <= 0 || x >= radius.X || y >= radius.Y)
                return true;

            var dx = (radius.X - x) / radius.X;
            var dy = (radius.Y - y) / radius.Y;
            return (dx * dx) + (dy * dy) <= 1;
        }
    }

    /// <summary>
    /// Returns a point of the root of the fragment tree (see <see cref="LayoutEngine.MapToRoot(Fragment, PhysicalRect)"/>) in the
    /// coordinates of a fragment, or null when it is clipped out by an ancestor or a transform is not invertible. The points of
    /// the ancestors are cached in the context.
    /// </summary>
    private static PhysicalOffset? GetLocalPoint(HitTestContext context, Fragment fragment)
    {
        var localPoints = context.LocalPoints;
        var rootPoint = context.RootPoint;
        if (localPoints.TryGetValue(fragment, out var cached))
            return cached;

        // The ancestors whose point is not known yet, from the fragment upwards (iterative: the fragment trees can be deep)
        var chain = new Stack<Fragment>();
        PhysicalOffset? point = null;
        var known = false;
        for (var current = fragment; current is not null; current = current.Parent)
        {
            if (localPoints.TryGetValue(current, out point))
            {
                known = true;
                break;
            }

            chain.Push(current);
        }

        while (chain.TryPop(out var current))
        {
            if (!known)
            {
                // The root of the fragment tree
                point = rootPoint - current.Offset;
                known = true;
            }
            else if (point is not { } parentPoint || IsClippedOut(context, current.Parent!, parentPoint))
            {
                point = null;
            }
            else if (LayoutEngine.Get3DRenderingContextMatrix(current, out var ancestor) is { } matrix && ancestor is not null && localPoints.TryGetValue(ancestor, out var ancestorPoint))
            {
                // A fragment of a 3D rendering context: the inverse of its accumulated 3D transform
                point = ancestorPoint is { } basePoint && LayoutTransforms.TryInverseTransformPoint(matrix, basePoint, out var local3D) ? local3D : null;
            }
            else
            {
                point = LayoutEngine.TryMapFromParent(current, parentPoint, out var local) ? local : null;
            }

            localPoints[current] = point;
        }

        return point;
    }

    /// <summary>
    /// Whether the point of the hit testing (<paramref name="point"/>, in the coordinates of a fragment) is outside the overflow
    /// clip edge of the fragment, in the axes it clips (with its rounded corners when it clips both axes), or outside its clip
    /// rectangle or its clip path: its descendants are not hit.
    /// </summary>
    private static bool IsClippedOut(HitTestContext context, Fragment fragment, PhysicalOffset point)
    {
        // The clip rectangle and the clip path of a box clip its descendants (https://drafts.fxtf.org/css-masking/#clipping-paths)
        if (fragment is BoxFragment { Parent: not null } clipper && Painter.IsClippedOut(clipper, point))
            return true;

        // The viewport only clips to itself: the points are in it
        if (fragment is not BoxFragment { ClipsOverflow: true } box || box.Parent is null)
            return false;

        var clip = box.OverflowClipRect;
        if ((box.ClipsOverflowX && (point.X < clip.Left || point.X >= clip.Right)) || (box.ClipsOverflowY && (point.Y < clip.Top || point.Y >= clip.Bottom)))
            return true;

        if (!box.ClipsOverflowX || !box.ClipsOverflowY)
            return false;

        // The shape of the clip of a box that clips both axes is computed once for its children (they follow each other in the
        // painting order, with their descendants, which are clipped by other boxes or not clipped)
        if (!ReferenceEquals(context.ShapedClipper, box))
        {
            context.ShapedClipper = box;
            context.IsOutsideShapedClip = IsOutsideShapedOverflowClip(box, clip, point);
        }

        return context.IsOutsideShapedClip;
    }

    /// <summary>
    /// Whether a point in the overflow clip rectangle of a box that clips both axes is outside of the shape of its overflow clip
    /// edge: the curve of its corners (https://drafts.csswg.org/css-backgrounds/#corner-clipping: the border radii reduced by the
    /// distance from the border edge, as the painting, see <c>Painter.AddOverflowClip</c>), or the inner border shape.
    /// </summary>
    private static bool IsOutsideShapedOverflowClip(BoxFragment box, PhysicalRect clip, PhysicalOffset point)
    {
        // https://drafts.csswg.org/css-borders-4/#border-shape-overflow-interaction: "The inner border-shape clips the overflow
        // content of the element"
        if (BorderShapeGeometry.Get(box) is { } shape)
            return !PathOperations.Contains(shape.GetOverflowClip(box), point);

        if (BorderRadii.Get(box) is null)
            return false;

        var border = new PhysicalRect(0, 0, box.Size.Width, box.Size.Height);
        var rounded = RoundedRect.FromBorderBox(box, border).Outset(new Sides<double>(border.Top - clip.Top, clip.Right - border.Right, clip.Bottom - border.Bottom, border.Left - clip.Left), border.Size);
        if (rounded.IsRect)
            return false;

        return rounded.Contour is null ? !IsInCorners(rounded.Rect, rounded.Radii, point) : !PathOperations.Contains(DisplayPath.FromRoundedRect(rounded), point);
    }
}
