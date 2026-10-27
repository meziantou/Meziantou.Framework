using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Rendering;
using Meziantou.Framework.WebPlatform.Svg;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

// The effects of the stacking contexts (https://drafts.fxtf.org/filter-effects/#placement): the filters, the clip paths, the
// masks, the opacity, the blend modes and the backdrop filters, and the clip rectangles (https://drafts.fxtf.org/css-masking/#clip-property).
internal sealed partial class Painter
{
    /// <summary>
    /// The masks of a stacking context, painted at its end over its contents (with a destination-in layer): the clipPath element
    /// of its clip path (when it is not a single shape), its mask layers and its mask border.
    /// </summary>
    /// <remarks>The mask of an SVG element (<see cref="Fragment"/> is null) only supports the references to mask elements.</remarks>
    private sealed record PendingMask(BoxFragment? Fragment, Element? Source, PhysicalOffset Offset, SvgResourceSpace Space, SvgClipPathElement? ClipPath, bool HasMaskLayers, bool HasMaskBorder);

    /// <summary>
    /// Whether the box is a <c>::view-transition-image-pair()</c> whose images are not blended: the isolation the dynamic view
    /// transition style sheet gives it ("to provide isolation: isolate for its children", for the plus-lighter cross-fade) then
    /// needs no layer, and its images are composited at once as the element is in the document (a layer would round the
    /// colors of a translucent image twice).
    /// </summary>
    private static bool IsViewTransitionImagePairWithoutBlending(BoxFragment fragment)
    {
        if (fragment.Box.PseudoElement is not { } pseudoElement || !pseudoElement.StartsWith(ViewTransitionPseudoTree.ImagePairPrefix, StringComparison.Ordinal))
            return false;

        for (var child = fragment.Box.FirstChild; child is not null; child = child.NextSibling)
        {
            if (child.Style.GetComputedValue("mix-blend-mode") is not ("normal" or ""))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Applies the effects of a stacking context in its coordinates (https://drafts.fxtf.org/filter-effects/#placement: "first any
    /// filter effect is applied, then any clipping, masking and opacity"): a basic shape of <c>clip-path</c> is a clip of the group
    /// (it clips the result of the filters), the opacity, the blend mode, the isolation and the backdrop filters are a layer, the
    /// filters a layer inside it (the same layer when nothing is painted between them), and the masks are painted between the two
    /// at the end of the stacking context.
    /// </summary>
    private void ApplyEffects(BoxFragment fragment, PhysicalOffset offset, Group group)
    {
        // The other fragments of an inline box are painted in the group of its first fragment, which composites them once (see
        // PaintOrder.IsCompositedAsOneGroup)
        if (group.Parent?.Fragment is { } outer && ReferenceEquals(outer.Box, fragment.Box) && PaintOrder.IsCompositedAsOneGroup(fragment.Box))
            return;

        var style = fragment.Box.Style;
        var opacity = GetOpacity(style);
        var blendMode = ParseBlendMode(style.GetComputedValue("mix-blend-mode"));
        var hasFilter = FilterEffects.HasFilter(style);
        // The root element has no backdrop (https://drafts.fxtf.org/filter-effects-2/#BackdropRoot: it is the backdrop root of the document)
        var hasBackdropFilter = !style.IsRoot && FilterEffects.HasFilter(style, "backdrop-filter");
        var clipPathValue = style.GetComputedValue("clip-path");
        var hasMaskLayers = HasMaskLayers(style);
        var hasMaskBorder = style.GetComputedValue("mask-border-source") is not ("none" or "");
        // https://drafts.fxtf.org/filter-effects-2/#BackdropRoot: isolation: isolate does not create a backdrop root: it is not a
        // layer when a descendant filters the backdrop (the layer would hide the backdrop from it; the stacking contexts whose
        // contents blend are still layers, see _isolatedGroups)
        var isolate = style.GetComputedValue("isolation") == "isolate" && _backdropFilterAncestors?.Contains(fragment) != true && !IsViewTransitionImagePairWithoutBlending(fragment);

        // will-change of a property that creates a backdrop root creates one (an isolated group, as the composited layers of the
        // browsers). It is only a layer when a descendant filters the backdrop: a layer that nothing reads would only round the
        // colors of its translucent contents twice (e.g. a ::view-transition-group(), whose animation has backdrop-filter)
        isolate |= _backdropFilterAncestors?.Contains(fragment) == true && WillChangeBackdropRoot(style);
        if (opacity >= 1 && blendMode == BlendMode.Normal && !isolate && !hasFilter && !hasBackdropFilter && clipPathValue is "none" or "" && !hasMaskLayers && !hasMaskBorder)
            return;

        var borderBox = fragment.BorderBoxRect.Translate(offset);
        var space = SvgResourceSpace.ForBox(borderBox, style.Layout.EffectiveZoom);
        // The reference box of the SVG filters of a box is the bounding box of the box and its descendants (as in the browsers)
        var filterSpace = SvgResourceSpace.ForBox(borderBox, fragment.InkOverflow.Translate(offset), style.Layout.EffectiveZoom);

        // The user space of a foreignObject element is the one of its SVG parent: its box is at its x and y
        if (SvgForeignObjectLayout.GetViewportRect(fragment) is { } svgViewport)
        {
            space = space.InSvgViewport(svgViewport.Translate(offset));
            filterSpace = filterSpace.InSvgViewport(svgViewport.Translate(offset));
        }

        var filters = hasFilter ? FilterEffects.Get(style, filterSpace, renderElement: RenderSvgElementForFilter) : null;
        if (filters is not null && Array.Exists(filters, filter => filter is FilterGraph) && style.Element is { } filtered && ContainsCrossOriginContent(filtered, _document, depth: 0))
        {
            // As in the browsers, the SVG filters (whose duration could reveal the pixels) are not applied to cross-origin content
            filters = Array.FindAll(filters, filter => filter is not FilterGraph);
            if (filters.Length == 0)
            {
                filters = null;
            }
        }

        var backdropFilters = hasBackdropFilter ? FilterEffects.Get(style, space, "backdrop-filter") : null;

        // https://drafts.fxtf.org/css-masking/#the-clip-path
        SvgClipPathElement? clipPathElement = null;
        if (BasicShapePaths.ParseClipPath(clipPathValue) is { } clipPath)
        {
            DisplayPath? path = null;
            if (clipPath.Url is { } url)
            {
                // "If the URI reference is not valid (e.g it points to an object that doesn't exist or the object is not a clipPath
                // element), the clip-path property must be treated as if it hadn't been specified"
                if (style.Element is { } element && SvgResources.GetReferencedElement(element, url) is SvgClipPathElement clip)
                {
                    path = GetClipPathElementAsPath(clip, space);
                    if (path is null)
                    {
                        clipPathElement = clip;
                    }
                }
            }
            else
            {
                var referenceBox = BasicShapePaths.GetReferenceBox(fragment, clipPath.ReferenceBox).Translate(offset);
                if (clipPath.Shape is { } shape)
                {
                    path = BasicShapePaths.CreatePath(shape, referenceBox, style.Layout.EffectiveZoom);
                }
                else
                {
                    // A <geometry-box> alone clips to the box, with the rounded corners of the border box
                    path = DisplayPath.FromRoundedRect(GetGeometryBoxShape(fragment, offset, clipPath.ReferenceBox));
                }
            }

            if (path is not null)
            {
                AddClip(path, style.Element);
            }
        }

        // https://drafts.fxtf.org/filter-effects-2/#BackdropRoot: a clip path makes the stacking context a backdrop root (a layer)
        var hasMask = clipPathElement is not null || hasMaskLayers || hasMaskBorder;
        isolate |= clipPathValue is not ("none" or "");
        if (opacity >= 1 && blendMode == BlendMode.Normal && !isolate && filters is null && backdropFilters is null && !hasMask)
            return;

        // The filters are applied by the same layer when no mask is painted on the result of the filters: they also apply to the
        // filtered backdrop of the element (https://drafts.fxtf.org/filter-effects-2/#BackdropFilterProperty: the filtered backdrop
        // is painted as the bottom of the content of the element)
        var mergeFilters = filters is not null && !hasMask;
        _builder.SaveLayer(new SaveLayerItem(opacity, blendMode, mergeFilters ? filters : null)
        {
            Source = style.Element,
            BackdropFilters = backdropFilters,
            BackdropClip = backdropFilters is null ? null : RoundedRect.FromBorderBox(fragment, borderBox),
        });

        if (hasMask)
        {
            group.MaskSaveCount = _builder.SaveCount;
            group.Mask = new PendingMask(fragment, style.Element, offset, space, clipPathElement, hasMaskLayers, hasMaskBorder);
        }

        if (filters is not null && !mergeFilters)
        {
            _builder.SaveLayer(new SaveLayerItem(1, BlendMode.Normal, filters) { Source = style.Element });
        }
    }

    /// <summary>Whether an element or its descendants display the document of a navigable that is not same origin with a document (or that contains one).</summary>
    private static bool ContainsCrossOriginContent(Element element, Document document, int depth)
    {
        if (depth > MaxResourceDepth)
            return false;

        for (Node? node = element; node is not null; node = node == element ? node.FirstChild : NextInSubtree(node, element))
        {
            if (node is HtmlIFrameElement or HtmlFrameElement or HtmlEmbedElement or HtmlObjectElement
                && NavigableContainers.GetContentNavigable((Element)node)?.Document is { } nested && !ReferenceEquals(nested, document))
            {
                if (!Document.IsSameOrigin(nested, document) || (nested.DocumentElement is { } root && ContainsCrossOriginContent(root, document, depth + 1)))
                    return true;
            }
        }

        return false;

        static Node? NextInSubtree(Node node, Node root)
        {
            if (node.FirstChild is { } child)
                return child;

            for (var current = node; current is not null && current != root; current = current.ParentNode)
            {
                if (current.NextSibling is { } sibling)
                    return sibling;
            }

            return null;
        }
    }

    /// <summary>Clips to a path: a rounded rectangle is clipped as one (antialiased as the borders and the rounded backgrounds).</summary>
    private void AddClip(DisplayPath path, Node? source)
    {
        if (path.RoundedRect is { } rounded)
        {
            _builder.Add(rounded.IsRect ? new ClipRectItem(rounded.Rect, AntiAlias: true) { Source = source } : new ClipRoundedRectItem(rounded) { Source = source });
        }
        else
        {
            _builder.Add(new ClipPathItem(path) { Source = source });
        }
    }

    /// <summary>
    /// Whether <c>will-change</c> (or an animation that is current or in effect, see <see cref="LayoutStyle.WillChangeProperties"/>)
    /// names a property whose non-initial values create a backdrop root (https://drafts.fxtf.org/filter-effects-2/#BackdropRoot).
    /// </summary>
    private static bool WillChangeBackdropRoot(ComputedStyle style)
    {
        foreach (var property in style.Layout.WillChangeProperties)
        {
            if (property is "opacity" or "filter" or "backdrop-filter" or "mask" or "mask-image" or "mask-border" or "mask-border-source" or "clip-path" or "mix-blend-mode")
                return true;
        }

        return false;
    }

    /// <summary>Returns the shape of a &lt;geometry-box&gt; of <c>clip-path</c>: the box with the border radii adjusted to it (https://drafts.csswg.org/css-backgrounds/#corner-shaping).</summary>
    private static RoundedRect GetGeometryBoxShape(BoxFragment fragment, PhysicalOffset offset, string? box)
    {
        // The reference box of an inline box split over several lines is the one of its first fragment (see BasicShapePaths.GetReferenceBox)
        if (fragment.Box.IsInlineBox && fragment.Box.FirstFragment is BoxFragment first && !ReferenceEquals(first, fragment))
            return GetGeometryBoxShape(first, offset + (first.GetOffsetInRoot() - fragment.GetOffsetInRoot()), box);

        var rounded = RoundedRect.FromBorderBox(fragment, fragment.BorderBoxRect.Translate(offset));
        if (box == "view-box" && SvgForeignObjectLayout.GetViewportRect(fragment) is { } viewport)
            return new RoundedRect(viewport.Translate(offset), default);

        return box switch
        {
            // https://drafts.csswg.org/css-borders-4/#corner-shaping: "The margin edge [...] calculates its radius by adding the
            // corresponding margin thickness to each border radius, with the corresponding outset-adjusted border radius applied"
            "margin-box" => rounded.Outset(BasicShapePaths.GetMargins(fragment), rounded.Rect.Size),
            "padding-box" => rounded.Inset(fragment.Border),
            "content-box" or "fill-box" => rounded.Inset(fragment.Border.Add(fragment.Padding)),
            _ => rounded,
        };
    }

    /// <summary>Paints the masks of a stacking context, each one in a destination-in layer.</summary>
    private void PaintMask(PendingMask mask)
    {
        var source = mask.Source;
        if (mask.ClipPath is { } clipPath)
        {
            _builder.SaveLayer(new SaveLayerItem(1, BlendMode.DestinationIn, Filters: null) { Source = source });
            PaintClipPathElement(clipPath, mask.Space, depth: 0);
            _builder.Restore();
        }

        if (mask.HasMaskLayers)
        {
            _builder.SaveLayer(new SaveLayerItem(1, BlendMode.DestinationIn, Filters: null) { Source = source });
            if (mask.Fragment is { } fragment)
            {
                PaintMaskLayers(fragment, mask.Offset, mask.Space);
            }
            else if (source is not null)
            {
                PaintSvgMaskLayers(source, mask.Space);
            }

            _builder.Restore();
        }

        if (mask.HasMaskBorder && mask.Fragment is { } boxFragment)
        {
            // https://drafts.fxtf.org/css-masking/#the-mask-border-mode
            var style = boxFragment.Box.Style;
            var luminance = style.GetComputedValue("mask-border-mode") == "luminance";
            _builder.SaveLayer(new SaveLayerItem(1, BlendMode.DestinationIn, luminance ? [LuminanceToAlphaFilter.Instance] : null) { Source = source });
            PaintBorderImage(boxFragment, style, boxFragment.BorderBoxRect.Translate(mask.Offset), "mask-border");
            _builder.Restore();
        }
    }

    /// <summary>
    /// Starts the effects of an SVG element in its user space (the builder is in the user space of the element): its clip path, its
    /// filters, its opacity, its blend mode and its masks (the mask elements referenced by <c>mask-image</c>). Returns the mask to
    /// paint at the end of the element (<see cref="PaintMask"/>) and the save count to restore to before.
    /// </summary>
    private (PendingMask? Mask, int MaskSaveCount) ApplySvgEffects(Element element, ComputedStyle style, double opacity, SvgOutermostViewport viewport)
    {
        var blendMode = ParseBlendMode(style.GetComputedValue("mix-blend-mode"));
        var isolate = style.GetComputedValue("isolation") == "isolate";
        var hasFilter = FilterEffects.HasFilter(style);
        var clipPathValue = style.GetComputedValue("clip-path");

        // The mask layers of an SVG element that do not reference a mask element are ignored
        var hasMaskLayers = HasMaskLayers(style) && HasSvgMaskLayers(element, style);
        if (opacity >= 1 && blendMode == BlendMode.Normal && !isolate && !hasFilter && clipPathValue is "none" or "" && !hasMaskLayers)
            return (null, -1);

        // The object bounding box (also for the content of a mask element, which is not rendered)
        var bbox = element is SvgElement svgElement ? SvgBoundingBox.ComputeContent(svgElement, viewport) : null;
        var box = bbox is { } b ? new PhysicalRect(b.MinX, b.MinY, b.MaxX - b.MinX, b.MaxY - b.MinY) : PhysicalRect.Empty;
        var viewportSize = SvgUserSpace.GetViewportSize(element, viewport);
        var space = new SvgResourceSpace(box, new PhysicalSize(viewportSize.Width, viewportSize.Height), PhysicalOffset.Zero, 1);
        var filters = hasFilter ? FilterEffects.Get(style, space, renderElement: RenderSvgElementForFilter) : null;

        // https://drafts.fxtf.org/css-masking/#the-clip-path
        SvgClipPathElement? clipPathElement = null;
        if (BasicShapePaths.ParseClipPath(clipPathValue) is { } clipPath)
        {
            DisplayPath? path = null;
            if (clipPath.Url is { } url)
            {
                if (SvgResources.GetReferencedElement(element, url) is SvgClipPathElement clip)
                {
                    path = GetClipPathElementAsPath(clip, space);
                    if (path is null)
                    {
                        clipPathElement = clip;
                    }
                }
            }
            else
            {
                // https://drafts.fxtf.org/css-masking/#typedef-geometry-box: for the SVG elements, the CSS boxes are the fill box
                // (content-box, padding-box) or the stroke box (border-box, margin-box), view-box is the nearest SVG viewport
                var referenceBox = clipPath.ReferenceBox switch
                {
                    "fill-box" or "content-box" or "padding-box" => box,
                    "view-box" => new PhysicalRect(0, 0, viewportSize.Width, viewportSize.Height),
                    _ => element is SvgElement stroked && SvgBoundingBox.ComputeContent(stroked, viewport, stroke: true) is { } strokeBox ? new PhysicalRect(strokeBox.MinX, strokeBox.MinY, strokeBox.MaxX - strokeBox.MinX, strokeBox.MaxY - strokeBox.MinY) : box,
                };

                path = clipPath.Shape is { } shape ? BasicShapePaths.CreatePath(shape, referenceBox, 1) : null;
                if (path is null && clipPath.Shape is null)
                {
                    path = new DisplayPath();
                    path.AddRect(referenceBox);
                }
            }

            if (path is not null)
            {
                _builder.Add(new ClipPathItem(path) { Source = element });
            }
        }

        var hasMask = clipPathElement is not null || hasMaskLayers;
        if (opacity >= 1 && blendMode == BlendMode.Normal && !isolate && filters is null && !hasMask)
            return (null, -1);

        var mergeFilters = filters is not null && !hasMask;
        _builder.SaveLayer(new SaveLayerItem(opacity, blendMode, mergeFilters ? filters : null) { Source = element });
        var maskSaveCount = _builder.SaveCount;
        if (filters is not null && !mergeFilters)
        {
            _builder.SaveLayer(new SaveLayerItem(1, BlendMode.Normal, filters) { Source = element });
        }

        return (hasMask ? new PendingMask(Fragment: null, element, PhysicalOffset.Zero, space, clipPathElement, hasMaskLayers, HasMaskBorder: false) : null, maskSaveCount);
    }

    /// <summary>
    /// Whether an SVG element has a mask layer that applies: a reference to a mask element, a gradient, or an image that can be
    /// painted (the layers that reference nothing, e.g. an image that cannot be loaded, are ignored).
    /// </summary>
    private bool HasSvgMaskLayers(Element element, ComputedStyle style)
    {
        foreach (var layer in GetMaskLayers(style))
        {
            if (layer.Layer.Image is { } image && (SvgResources.GetReferencedElement(element, image) is SvgMaskElement || PaintedImage.Resolve(_document, style, image, this) is not null))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Paints the mask layers of an SVG element (https://drafts.fxtf.org/css-masking/#the-mask-image) in its user space: the mask
    /// elements, and the images positioned in the SVG boxes of <c>mask-origin</c> and clipped to the ones of <c>mask-clip</c>
    /// (https://drafts.fxtf.org/css-masking/#typedef-geometry-box).
    /// </summary>
    private void PaintSvgMaskLayers(Element element, SvgResourceSpace space)
    {
        var style = element.NodeDocument.GetStyleEngine().GetStyle(element);
        if (style is null)
            return;

        var layers = GetMaskLayers(style);
        var bottom = true;
        for (var i = layers.Length - 1; i >= 0; i--)
        {
            var mask = layers[i];
            if (mask.Layer.Image is not { } image)
            {
                bottom = false;
                continue;
            }

            var maskElement = SvgResources.GetReferencedElement(element, image) as SvgMaskElement;
            if (maskElement is null && PaintedImage.Resolve(_document, style, image, this) is null)
                continue;

            var luminance = mask.Mode switch
            {
                "luminance" => true,
                "alpha" => false,
                _ => maskElement is not null && SvgUserSpace.GetComputedValue(maskElement, "mask-type") != "alpha",
            };

            _builder.SaveLayer(new SaveLayerItem(1, bottom ? BlendMode.Normal : mask.Composite, luminance ? [LuminanceToAlphaFilter.Instance] : null) { Source = element });
            if (maskElement is not null)
            {
                PaintMaskElement(maskElement, space, depth: 0);
            }
            else
            {
                var positioningArea = GetSvgBox(element, mask.OriginBox, space);
                var paintingArea = mask.NoClip ? new PhysicalRect(-CanvasExtent, -CanvasExtent, 2 * CanvasExtent, 2 * CanvasExtent) : GetSvgBox(element, mask.ClipBox, space);

                // The lengths of the image (the stops of a gradient, the natural size of an image, mask-size, mask-position) are
                // multiplied by the zoom of the element, and the user space is zoomed too: the layer is painted in zoomed CSS px
                var zoom = style.Layout.EffectiveZoom;
                if (zoom != 1 && zoom > 0)
                {
                    _builder.Transform(DisplayMatrix.Scale(1 / zoom, 1 / zoom));
                    positioningArea = new PhysicalRect(positioningArea.X * zoom, positioningArea.Y * zoom, positioningArea.Width * zoom, positioningArea.Height * zoom);
                    paintingArea = new PhysicalRect(paintingArea.X * zoom, paintingArea.Y * zoom, paintingArea.Width * zoom, paintingArea.Height * zoom);
                }

                PaintBackgroundLayer(fragment: null, style, mask.Layer with { Origin = BackgroundBox.BorderBox }, positioningArea, default, default, new RoundedRect(paintingArea, default), blends: false);
            }

            _builder.Restore();
            bottom = false;
        }
    }

    /// <summary>
    /// Returns a box of an SVG element in its user space (https://drafts.fxtf.org/css-masking/#typedef-geometry-box): the fill box for
    /// content-box, padding-box and fill-box, the nearest SVG viewport for view-box, else the stroke box.
    /// </summary>
    private static PhysicalRect GetSvgBox(Element element, string box, SvgResourceSpace space)
    {
        switch (box)
        {
            case "content-box" or "padding-box" or "fill-box":
                return space.BoundingBox;

            case "view-box":
                return new PhysicalRect(PhysicalOffset.Zero, space.Viewport);

            default:
                // (also for the content of a mask element, which is not rendered)
                return element is SvgElement svgElement && SvgBoundingBox.ComputeContent(svgElement, stroke: true) is { } stroke && stroke.MaxX > stroke.MinX
                    ? new PhysicalRect(stroke.MinX, stroke.MinY, stroke.MaxX - stroke.MinX, stroke.MaxY - stroke.MinY)
                    : space.BoundingBox;
        }
    }

    // ----------------------------------------------------------------------------------------------------------------
    // The clip paths (https://drafts.fxtf.org/css-masking/#ClipPathElement)
    // ----------------------------------------------------------------------------------------------------------------

    private const int MaxResourceDepth = 8;

    /// <summary>Returns the transform from the coordinates of the contents of a clipPath element to the coordinates of the painting.</summary>
    private static SvgAffine GetClipPathContentTransform(SvgClipPathElement clip, SvgResourceSpace space)
    {
        // https://drafts.fxtf.org/css-masking/#element-attrdef-clippath-clippathunits: the bounding box units are the coordinates of
        // the contents; the transform of the clipPath element is in the user space of the clipped element (in user units)
        var transform = SvgUserSpace.GetTransform(clip);
        if (SvgResourceSpace.IsObjectBoundingBox(clip, "clipPathUnits", defaultIsObjectBoundingBox: false))
        {
            var box = space.BoundingBox;
            transform = new SvgAffine(box.Width, 0, 0, box.Height, box.X, box.Y).Then(transform);
        }

        return transform.Then(space.UserToPainting);
    }

    /// <summary>
    /// Returns the clipping path of a clipPath element as a single path (in the coordinates of the painting) when it is the fill
    /// area of a single shape without clip path (a clip of the display list); null when the clip needs a mask (several shapes,
    /// texts, nested clip paths). An empty clipPath element clips everything (an empty path).
    /// </summary>
    private static DisplayPath? GetClipPathElementAsPath(SvgClipPathElement clip, SvgResourceSpace space)
    {
        if (SvgUserSpace.GetComputedValue(clip, "clip-path") is { } nested && nested != "none")
            return null;

        Element? shape = null;
        foreach (var child in clip.Children)
        {
            if (!IsRenderedClipPathChild(child))
                continue;

            if (shape is not null || child is not SvgGeometryElement || SvgUserSpace.GetComputedValue(child, "clip-path") is { } childClip && childClip != "none")
                return null;

            shape = child;
        }

        if (shape is null)
            return new DisplayPath();

        var path = DisplayPath.FromSvg(SvgShapeGeometry.GetPath(shape), GetClipRule(shape));
        return BasicShapePaths.Transform(path, SvgUserSpace.GetTransform(shape).Then(GetClipPathContentTransform(clip, space)));
    }

    private static bool IsRenderedClipPathChild(Element child)
        => child is SvgElement && SvgUserSpace.GetComputedValue(child, "display") != "none" && SvgUserSpace.GetComputedValue(child, "visibility") is not ("hidden" or "collapse");

    private static FillRule GetClipRule(Element element) => SvgUserSpace.GetComputedValue(element, "clip-rule") == "evenodd" ? FillRule.EvenOdd : FillRule.NonZero;

    /// <summary>
    /// Paints the clipping path of a clipPath element as an opaque mask (https://drafts.fxtf.org/css-masking/#ClipPathElement): the
    /// union of the fill areas of its shapes (with <c>clip-rule</c>), each one clipped by its own clip path, the whole clipped by the
    /// clip path of the clipPath element.
    /// </summary>
    private void PaintClipPathElement(SvgClipPathElement clip, SvgResourceSpace space, int depth)
    {
        if (depth > MaxResourceDepth)
            return;

        // A reference to a clipPath element being painted (a reference cycle) is invalid: it is ignored
        var active = _activeClipPaths ??= [];
        active.Add(clip);
        var saveCount = _builder.SaveCount;
        var nested = GetNestedClipPath(clip);
        if (nested is not null && active.Contains(nested))
        {
            nested = null;
        }

        if (nested is not null)
        {
            _builder.SaveLayer(new SaveLayerItem(1, BlendMode.Normal, Filters: null));
        }

        var toPainting = GetClipPathContentTransform(clip, space);
        var paint = new DisplayPaint(DisplayColor.Black);
        foreach (var child in clip.Children)
        {
            if (!IsRenderedClipPathChild(child))
                continue;

            Element? geometry = null;
            var childTransform = SvgUserSpace.GetTransform(child);
            var transform = childTransform;
            if (child is SvgGeometryElement)
            {
                geometry = child;
            }
            else if (child is SvgUseElement && SvgBoundingBox.GetUseTarget(child) is SvgGeometryElement target)
            {
                geometry = target;
                transform = SvgUserSpace.GetTransform(target).Then(SvgUserSpace.GetUseTranslation(child)).Then(transform);
            }

            // The texts clip to the outlines of their glyphs
            if (child is SvgTextElement)
            {
                PaintSvgTextGlyphs(child, transform.Then(toPainting), paint);
                continue;
            }

            if (geometry is null)
                continue;

            var path = BasicShapePaths.Transform(DisplayPath.FromSvg(SvgShapeGeometry.GetPath(geometry), GetClipRule(child)), transform.Then(toPainting));
            var childClip = GetNestedClipPath(child);
            if (childClip is not null && !active.Contains(childClip))
            {
                _builder.SaveLayer(new SaveLayerItem(1, BlendMode.Normal, Filters: null));
                _builder.Add(new DrawPathItem(path, paint) { Source = child });
                _builder.SaveLayer(new SaveLayerItem(1, BlendMode.DestinationIn, Filters: null));

                // The clip path of a child applies in the user space of the child (the coordinates of the contents of the clipPath
                // element, with the transform of the child)
                _builder.Transform(DisplayMatrix.FromSvg(childTransform.Then(toPainting)));
                PaintClipPathElement(childClip, GetClipPathChildSpace(child, space), depth + 1);
                _builder.Restore();
                _builder.Restore();
            }
            else
            {
                _builder.Add(new DrawPathItem(path, paint) { Source = child });
            }
        }

        if (nested is not null)
        {
            _builder.SaveLayer(new SaveLayerItem(1, BlendMode.DestinationIn, Filters: null));
            PaintClipPathElement(nested, space, depth + 1);
        }

        _builder.RestoreTo(saveCount);
        active.RemoveAt(active.Count - 1);
    }

    /// <summary>The clipPath elements being painted by <see cref="PaintClipPathElement"/>, from the outermost.</summary>
    private List<SvgClipPathElement>? _activeClipPaths;

    /// <summary>
    /// Returns the resource space of a child of a clipPath element for its own clip path, in the user space of the child (the
    /// coordinates of the painting are the user space): its object bounding box is the bounding box of the child.
    /// </summary>
    private static SvgResourceSpace GetClipPathChildSpace(Element child, SvgResourceSpace space)
    {
        var box = child is SvgElement element && SvgBoundingBox.ComputeContent(element) is { } b ? new PhysicalRect(b.MinX, b.MinY, b.MaxX - b.MinX, b.MaxY - b.MinY) : PhysicalRect.Empty;
        return new SvgResourceSpace(box, space.Viewport, PhysicalOffset.Zero, 1);
    }

    /// <summary>Returns the clipPath element referenced by the <c>clip-path</c> of an SVG element, or null.</summary>
    private static SvgClipPathElement? GetNestedClipPath(Element element)
    {
        var value = SvgUserSpace.GetComputedValue(element, "clip-path");
        if (value is null || BasicShapePaths.ParseClipPath(value) is not { Url: { } url })
            return null;

        return SvgResources.GetReferencedElement(element, url) as SvgClipPathElement;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // The masks (https://drafts.fxtf.org/css-masking/#masking)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>A layer of the mask of a style: a background layer (image, position, size, repetition, origin and clip) with its compositing operator and its mode.</summary>
    /// <param name="OriginBox">The keyword of <c>mask-origin</c> (the SVG elements use the SVG boxes).</param>
    /// <param name="ClipBox">The keyword of <c>mask-clip</c>.</param>
    private sealed record MaskLayer(BackgroundLayer Layer, BlendMode Composite, string Mode, bool NoClip, string OriginBox, string ClipBox);

    private static bool HasMaskLayers(ComputedStyle style)
    {
        var value = style.GetComputedValue("mask-image");
        if (value is "none" or "")
            return false;

        foreach (var layer in GetMaskLayers(style))
        {
            if (layer.Layer.Image is not null)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Whether a mask image references a mask element that is disabled: the element or one of its ancestors has <c>display: none</c>
    /// (https://github.com/w3c/fxtf-drafts/issues/245: the mask is not applied, as in the browsers, where a reference to no element
    /// is an image that cannot be loaded, a transparent black mask).
    /// </summary>
    private static bool IsDisabledMaskElement(Element? element, CssComponentValue image)
    {
        if (element is null || SvgResources.GetReferencedElement(element, image) is not SvgMaskElement mask)
            return false;

        var engine = mask.NodeDocument.GetStyleEngine();
        for (Element? current = mask; current is not null; current = current.ParentNode as Element)
        {
            if (engine.GetStyle(current).Layout.Display.Box == DisplayBox.None)
                return true;
        }

        return false;
    }

    /// <summary>Returns the layers of the mask of a style (the lists of the mask properties are repeated to the number of images, https://drafts.fxtf.org/css-masking/#layering).</summary>
    private static MaskLayer[] GetMaskLayers(ComputedStyle style)
    {
        var images = ValueMatcher.SplitAtCommas(ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(style.GetComputedValue("mask-image"))));
        var positions = CssBackgrounds.SplitList(style.GetComputedValue("mask-position"));
        var sizes = CssBackgrounds.SplitList(style.GetComputedValue("mask-size"));
        var repeats = CssBackgrounds.SplitList(style.GetComputedValue("mask-repeat"));
        var origins = CssBackgrounds.SplitList(style.GetComputedValue("mask-origin"));
        var clips = CssBackgrounds.SplitList(style.GetComputedValue("mask-clip"));
        var composites = CssBackgrounds.SplitList(style.GetComputedValue("mask-composite"));
        var modes = CssBackgrounds.SplitList(style.GetComputedValue("mask-mode"));
        var zoom = style.Layout.EffectiveZoom;
        var layers = new MaskLayer[images.Count];
        for (var i = 0; i < images.Count; i++)
        {
            var image = images[i] is [var single] && !(single is CssPreservedToken { Type: CssTokenType.Ident } ident && InfraAscii.EqualsIgnoreAsciiCase(ident.Token.Value, "none")) ? single : null;
            if (image is not null && IsDisabledMaskElement(style.Element, image))
            {
                image = null;
            }

            var (repeatX, repeatY) = ParseRepeat(Get(repeats, i, "repeat"));
            var position = ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(Get(positions, i, "0% 0%")));
            var (positionX, positionY) = CssBackgrounds.ParsePosition(position) ?? ("0%", "0%");
            var clip = Get(clips, i, "border-box");
            var origin = Get(origins, i, "border-box");
            var background = new BackgroundLayer(
                image,
                ParsePosition(positionX, zoom, "left", "right"),
                ParsePosition(positionY, zoom, "top", "bottom"),
                Get(sizes, i, "auto"),
                repeatX,
                repeatY,
                "scroll",
                ParseMaskBox(origin),
                ParseMaskBox(clip),
                BlendMode.Normal)
            {
                Zoom = zoom,
            };

            // https://drafts.fxtf.org/css-masking/#the-mask-composite
            var composite = Get(composites, i, "add") switch
            {
                "subtract" => BlendMode.SourceOut,
                "intersect" => BlendMode.SourceIn,
                "exclude" => BlendMode.Xor,
                _ => BlendMode.Normal,
            };
            layers[i] = new MaskLayer(background, composite, Get(modes, i, "match-source"), clip == "no-clip", origin, clip);
        }

        return layers;

        static string Get(List<string> values, int index, string defaultValue) => values.Count == 0 ? defaultValue : values[index % values.Count].Trim();

        // "For elements with associated CSS layout box, the used value for fill-box is content-box and for stroke-box and view-box is border-box"
        static BackgroundBox ParseMaskBox(string value) => value switch
        {
            "padding-box" => BackgroundBox.PaddingBox,
            "content-box" or "fill-box" => BackgroundBox.ContentBox,
            _ => BackgroundBox.BorderBox,
        };
    }

    /// <summary>
    /// Paints the mask layers of a box (https://drafts.fxtf.org/css-masking/#the-mask-image): from the bottom layer, each layer in
    /// its own layer composited with the layers below it by its <c>mask-composite</c> operator, its luminance converted to alpha
    /// for the luminance masks (https://drafts.fxtf.org/css-masking/#the-mask-mode).
    /// </summary>
    private void PaintMaskLayers(BoxFragment fragment, PhysicalOffset offset, SvgResourceSpace space)
    {
        var style = fragment.Box.Style;
        var layers = GetMaskLayers(style);
        var borderBox = GetSnappedBorderBox(fragment, offset);

        // https://drafts.fxtf.org/css-masking/#typedef-geometry-box: the view-box of a foreignObject element is its nearest SVG
        // viewport (see BasicShapePaths.GetReferenceBox)
        var viewBox = SvgForeignObjectLayout.GetViewportRect(fragment)?.Translate(offset);
        var bottom = true;
        for (var i = layers.Length - 1; i >= 0; i--)
        {
            var mask = layers[i];
            if (mask.Layer.Image is not { } image)
            {
                bottom = false;
                continue;
            }

            var maskElement = style.Element is { } element ? SvgResources.GetReferencedElement(element, image) as SvgMaskElement : null;
            var luminance = mask.Mode switch
            {
                "luminance" => true,
                "alpha" => false,

                // match-source: the mask-type of a mask element (https://drafts.fxtf.org/css-masking/#the-mask-type), alpha for an image
                _ => maskElement is not null && SvgUserSpace.GetComputedValue(maskElement, "mask-type") != "alpha",
            };

            _builder.SaveLayer(new SaveLayerItem(1, bottom ? BlendMode.Normal : mask.Composite, luminance ? [LuminanceToAlphaFilter.Instance] : null) { Source = style.Element });
            if (maskElement is not null)
            {
                PaintMaskElement(maskElement, space, depth: 0);
            }
            else
            {
                var paintingArea = mask.NoClip
                    ? new RoundedRect(new PhysicalRect(-CanvasExtent, -CanvasExtent, 2 * CanvasExtent, 2 * CanvasExtent), default)
                    : mask.ClipBox == "view-box" && viewBox is { } clipBox ? new RoundedRect(clipBox, default)
                    : GetBackgroundArea(fragment, new RoundedRect(borderBox, default), fragment.Border, fragment.Padding, mask.Layer.Clip);
                if (mask.OriginBox == "view-box" && viewBox is { } originBox)
                {
                    PaintBackgroundLayer(fragment, style, mask.Layer with { Origin = BackgroundBox.BorderBox }, originBox, default, default, paintingArea, blends: false);
                }
                else
                {
                    PaintBackgroundLayer(fragment, style, mask.Layer, borderBox, fragment.Border, fragment.Padding, paintingArea, blends: false);
                }
            }

            _builder.Restore();
            bottom = false;
        }
    }

    /// <summary>
    /// Paints the contents of a mask element (https://drafts.fxtf.org/css-masking/#MaskElement) in the coordinates of the painting:
    /// its children in the coordinates of <c>maskContentUnits</c>, clipped to the mask region of <c>maskUnits</c>.
    /// </summary>
    private void PaintMaskElement(SvgMaskElement mask, SvgResourceSpace space, int depth)
    {
        if (depth > MaxResourceDepth)
            return;

        var region = space.ResolveRegion(mask, SvgResourceSpace.IsObjectBoundingBox(mask, "maskUnits", defaultIsObjectBoundingBox: true), "-10%", "-10%", "120%", "120%");
        if (region.Width <= 0 || region.Height <= 0)
            return;

        var toPainting = space.UserToPainting;
        if (SvgResourceSpace.IsObjectBoundingBox(mask, "maskContentUnits", defaultIsObjectBoundingBox: false))
        {
            var box = space.BoundingBox;
            toPainting = new SvgAffine(box.Width, 0, 0, box.Height, box.X, box.Y).Then(toPainting);
        }

        _builder.Save();
        _builder.Add(new ClipRectItem(space.ToPainting(region)));
        PaintSvgChildren(mask, mask.NodeDocument.GetStyleEngine().GetStyle(mask), toPainting, new SvgPaintContext(GetResourceViewport(mask, space)) { IsResourceContent = true });
        _builder.Restore();
    }

    /// <summary>Returns the SVG viewport of the outermost svg element of a resource element (for the percentages of its contents).</summary>
    private SvgOutermostViewport GetResourceViewport(Element resource, SvgResourceSpace space)
    {
        if (SvgUserSpace.GetOutermostSvgElement(resource) is { } svg && _engine.GetFragments(svg) is [var fragment, ..])
            return SvgUserSpace.GetOutermostViewport(fragment);

        return new SvgOutermostViewport(space.Viewport.Width, space.Viewport.Height, 1);
    }

    // ----------------------------------------------------------------------------------------------------------------
    // clip (https://drafts.fxtf.org/css-masking/#clip-property)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>Whether a box has a clip rectangle: <c>clip</c> is not auto and the box is absolutely positioned.</summary>
    internal static bool HasCssClip(BoxFragment fragment)
    {
        var box = fragment.Box;
        if (box.IsAnonymous || box.Kind is ContainerBoxKind.Viewport or ContainerBoxKind.TableGrid)
            return false;

        var style = box.Style;
        return style.Layout.IsAbsolutelyPositioned && style.GetComputedValue("clip") is not ("auto" or "");
    }

    /// <summary>Clips to the clip rectangle of a box (see <see cref="GetCssClipRect"/>).</summary>
    private void AddCssClip(BoxFragment fragment, PhysicalOffset offset)
    {
        if (GetCssClipRect(fragment) is { } rect)
        {
            _builder.Add(new ClipRectItem(SnapToDevicePixels(rect.Translate(offset))));
        }
    }

    /// <summary>
    /// Returns the clip rectangle of a box in the coordinates of its fragment (<c>rect(top, right, bottom, left)</c>: the offsets of
    /// the edges from the top left corner of the border box, auto being the edge of the border box), or null.
    /// </summary>
    internal static PhysicalRect? GetCssClipRect(BoxFragment fragment)
    {
        if (!HasCssClip(fragment))
            return null;

        var style = fragment.Box.Style;
        var values = ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(style.GetComputedValue("clip")));
        if (values is not [CssFunction function] || !InfraAscii.EqualsIgnoreAsciiCase(function.Name, "rect"))
            return null;

        var arguments = ValueMatcher.RemoveWhitespace(function.Value).FindAll(value => value is not CssPreservedToken { Type: CssTokenType.Comma });
        if (arguments.Count != 4)
            return null;

        var zoom = style.Layout.EffectiveZoom;
        var size = fragment.Size;
        var top = Resolve(arguments[0], 0);
        var right = Resolve(arguments[1], size.Width);
        var bottom = Resolve(arguments[2], size.Height);
        var left = Resolve(arguments[3], 0);
        return new PhysicalRect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));

        double Resolve(CssComponentValue value, double auto)
            => value is CssPreservedToken { Type: CssTokenType.Ident } || !LengthPercentage.TryParse(value, zoom, nonNegative: false, out var length) ? auto : length.Resolve(0);
    }

    /// <summary>
    /// Whether a point in the coordinates of a box fragment is outside of its clip rectangle or of its clip path (a basic shape, a
    /// &lt;geometry-box&gt; or a clipPath element), for the hit testing (https://drafts.fxtf.org/css-masking/#the-clip-path: "the
    /// element does not receive pointer events outside of the clipping region").
    /// </summary>
    internal static bool IsClippedOut(BoxFragment fragment, PhysicalOffset point)
    {
        var box = fragment.Box;
        if (box.IsAnonymous || box.Kind is ContainerBoxKind.Viewport or ContainerBoxKind.TableGrid)
            return false;

        if (GetCssClipRect(fragment) is { } clip && !clip.Contains(point.X, point.Y))
            return true;

        var style = box.Style;
        if (BasicShapePaths.ParseClipPath(style.GetComputedValue("clip-path")) is not { } clipPath)
            return false;

        var zoom = style.Layout.EffectiveZoom;
        if (clipPath.Url is { } url)
        {
            if (style.Element is not { } element || SvgResources.GetReferencedElement(element, url) is not SvgClipPathElement clipElement)
                return false;

            var space = SvgResourceSpace.ForBox(fragment.BorderBoxRect, zoom);
            if (SvgForeignObjectLayout.GetViewportRect(fragment) is { } svgViewport)
            {
                space = space.InSvgViewport(svgViewport);
            }

            return !ClipPathElementContains(clipElement, space, point, depth: 0);
        }

        var path = clipPath.Shape is { } shape
            ? BasicShapePaths.CreatePath(shape, BasicShapePaths.GetReferenceBox(fragment, clipPath.ReferenceBox), zoom)
            : DisplayPath.FromRoundedRect(GetGeometryBoxShape(fragment, PhysicalOffset.Zero, clipPath.ReferenceBox));
        return path is not null && !BasicShapePaths.Contains(path, point);
    }

    /// <summary>Whether a point of the painting coordinates of a resource space is in the clipping path of a clipPath element.</summary>
    internal static bool ClipPathElementContains(SvgClipPathElement clip, SvgResourceSpace space, PhysicalOffset point, int depth)
    {
        if (depth > MaxResourceDepth)
            return true;

        if (GetNestedClipPath(clip) is { } nested && !ClipPathElementContains(nested, space, point, depth + 1))
            return false;

        var toPainting = GetClipPathContentTransform(clip, space);
        foreach (var child in clip.Children)
        {
            if (!IsRenderedClipPathChild(child))
                continue;

            Element? geometry = null;
            var childTransform = SvgUserSpace.GetTransform(child);
            var transform = childTransform;
            if (child is SvgGeometryElement)
            {
                geometry = child;
            }
            else if (child is SvgUseElement && SvgBoundingBox.GetUseTarget(child) is SvgGeometryElement target)
            {
                geometry = target;
                transform = SvgUserSpace.GetTransform(target).Then(SvgUserSpace.GetUseTranslation(child)).Then(transform);
            }

            if (geometry is null)
                continue;

            var path = BasicShapePaths.Transform(DisplayPath.FromSvg(SvgShapeGeometry.GetPath(geometry), GetClipRule(child)), transform.Then(toPainting));
            if (!BasicShapePaths.Contains(path, point))
                continue;

            if (GetNestedClipPath(child) is not { } childClip)
                return true;

            // The clip path of a child applies in the user space of the child (see PaintClipPathElement)
            if (childTransform.Then(toPainting).Invert() is { } toChild)
            {
                var childPoint = toChild.Apply(point.X, point.Y);
                if (ClipPathElementContains(childClip, GetClipPathChildSpace(child, space), new PhysicalOffset(childPoint.X, childPoint.Y), depth + 1))
                    return true;
            }
        }

        return false;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // 3D rendering contexts (https://drafts.csswg.org/css-transforms-2/#3d-rendering-contexts)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>Whether a stacking context with <c>backface-visibility: hidden</c> shows its back face with a transform.</summary>
    private static bool IsBackFaceHidden(BoxFragment fragment, double[]? matrix)
        => matrix is not null && fragment.Box is { IsAnonymous: false } box && box.Kind != ContainerBoxKind.Viewport
            && box.Style.GetComputedValue("backface-visibility") == "hidden" && LayoutTransforms.IsBackFaceVisible(matrix);

    /// <summary>
    /// Whether a stacking context participates in the 3D rendering context of the plane of a box: the elements between them (its
    /// parent, the parent of its parent... up to the box) and the box have the used value preserve-3d of <c>transform-style</c>.
    /// </summary>
    private static bool ParticipatesIn(BoxFragment fragment, ContainerBox owner)
    {
        if (fragment.SvgTransform is not null || !LayoutTransforms.UsesPreserve3D(owner))
            return false;

        for (var parent = LayoutTransforms.GetParentElementBox(fragment.Box); parent is not null; parent = LayoutTransforms.GetParentElementBox(parent))
        {
            if (!LayoutTransforms.UsesPreserve3D(parent))
                return false;

            if (ReferenceEquals(parent, owner) || (parent.Kind == ContainerBoxKind.TableWrapper && ReferenceEquals(parent.Style, owner.Style)))
                return true;
        }

        return false;
    }

    /// <summary>How far the clip of a split plane extends beyond the rectangle of the plane (its ink overflow), in its coordinates.</summary>
    private const double SplitClipMargin = 8;

    /// <summary>A plane of a 3D rendering context: a stacking context that participates in it, with its accumulated transform.</summary>
    private sealed class Plane(BoxFragment fragment, double[] matrix, int order)
    {
        public BoxFragment Fragment { get; } = fragment;

        /// <summary>The transform from the coordinates of the fragment to the coordinates of the root of the 3D rendering context.</summary>
        public double[] Matrix { get; } = matrix;

        public int Order { get; } = order;

        public List<PaintItem> Items { get; } = [];

        public double Depth { get; set; }

        /// <summary>Whether the plane is a segment of the items of a plane after one of its child planes.</summary>
        public bool IsContinuation { get; init; }
    }

    /// <summary>
    /// Paints a 3D rendering context (https://drafts.csswg.org/css-transforms-2/#3d-rendering-contexts): the root (a stacking context
    /// with <c>transform-style: preserve-3d</c>) and the stacking contexts that participate in it are planes with their accumulated
    /// 3D transforms (their 3D transforms are not flattened into the plane of their parent); the planes are painted from the farthest
    /// to the nearest (sorted with a BSP tree, see <see cref="PlaneSorting"/>, which splits the intersecting planes), the planes that
    /// show their back face with <c>backface-visibility: hidden</c> are not painted.
    /// </summary>
    private void Paint3DRenderingContext(List<PaintItem> items, int begin, int end)
    {
        var root = (BoxFragment)items[begin].Fragment;
        BeginStackingContext(root);
        double[] identity = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
        var rootPlane = new Plane(root, identity, 0);
        var planes = new List<Plane> { rootPlane };
        var stack = new Stack<Plane>();
        stack.Push(rootPlane);
        var i = begin + 1;
        while (i < end)
        {
            var item = items[i];
            switch (item.Marker)
            {
                case PaintItemMarker.BeginStackingContext:
                {
                    var fragment = (BoxFragment)item.Fragment;
                    var owner = stack.Peek();
                    if (ParticipatesIn(fragment, owner.Fragment.Box))
                    {
                        var local = fragment.GetOffsetInRoot() - owner.Fragment.GetOffsetInRoot();
                        var matrix = CssTransforms.Multiply(owner.Matrix, Translation(local));
                        if (LayoutTransforms.GetMatrixWithPerspective(fragment) is { } own)
                        {
                            matrix = CssTransforms.Multiply(matrix, own);
                        }

                        var plane = new Plane(fragment, matrix, planes.Count);
                        planes.Add(plane);
                        stack.Push(plane);
                        i++;
                        continue;
                    }

                    // A stacking context that does not participate is flattened in the plane of its parent
                    var endIndex = FindEndOfStackingContext(items, i);
                    for (var j = i; j <= endIndex; j++)
                    {
                        owner.Items.Add(items[j]);
                    }

                    i = endIndex + 1;
                    continue;
                }

                case PaintItemMarker.EndStackingContext:
                    if (stack.Count > 1)
                    {
                        stack.Pop();

                        // The items of the parent plane painted after the plane are a new segment of the parent plane: the planes
                        // at the same depth keep their painting order
                        var parent = stack.Pop();
                        var continuation = new Plane(parent.Fragment, parent.Matrix, planes.Count) { IsContinuation = true };
                        planes.Add(continuation);
                        stack.Push(continuation);
                    }

                    i++;
                    continue;

                default:
                    stack.Peek().Items.Add(item);
                    i++;
                    continue;
            }
        }

        // The polygons of the planes (their ink overflow rectangle) after the transform of the root (the z axis points to the
        // viewer), sorted from the farthest with a BSP tree; the planes behind the viewer fall back to the depth of their center
        var rootMatrix = LayoutTransforms.GetMatrixWithPerspective(root);
        var matrices = new double[planes.Count][];
        var polygons = new List<PlaneSorting.Polygon>(planes.Count);
        for (var index = 0; index < planes.Count; index++)
        {
            var plane = planes[index];
            matrices[index] = rootMatrix is null ? plane.Matrix : CssTransforms.Multiply(rootMatrix, plane.Matrix);

            // As in Blink, a plane without drawable content (e.g. a box without background nor borders whose content is in other
            // planes) is not sorted: it does not split the other planes
            if (polygons is not null && HasDrawableContent(plane))
            {
                if (PlaneSorting.CreatePolygon(index, plane.Fragment.InkOverflow, matrices[index]) is { } polygon)
                {
                    polygons.Add(polygon);
                }
                else
                {
                    polygons = null;
                }
            }
        }

        List<(Plane Plane, PlaneSorting.Polygon? Polygon)> order;
        if (polygons is not null && polygons.Count <= 256)
        {
            var sorted = PlaneSorting.Sort(polygons, (index, x, y) => LayoutTransforms.TryInverseTransformPoint(matrices[index], new PhysicalOffset(x, y), out var local) ? local : null);
            order = sorted.ConvertAll(polygon => (planes[polygon.Plane], (PlaneSorting.Polygon?)polygon));
        }
        else
        {
            for (var index = 0; index < planes.Count; index++)
            {
                var size = planes[index].Fragment.Size;
                var m = matrices[index];
                var x = size.Width / 2;
                var y = size.Height / 2;
                var z = (m[2] * x) + (m[6] * y) + m[14];
                var w = (m[3] * x) + (m[7] * y) + m[15];
                planes[index].Depth = w > 0 ? z / w : z;
            }

            var depthSorted = new List<Plane>(planes);
            depthSorted.Sort((a, b) =>
            {
                var comparison = a.Depth.CompareTo(b.Depth);
                return comparison != 0 ? comparison : a.Order.CompareTo(b.Order);
            });
            order = depthSorted.ConvertAll(plane => (plane, (PlaneSorting.Polygon?)null));
        }

        var rootGroup = _group;
        var rootLocal = root.GetOffsetInRoot() - rootGroup.Origin;
        foreach (var (plane, polygon) in order)
        {
            if (plane.Items.Count == 0 && plane.IsContinuation)
                continue;

            if (ReferenceEquals(plane.Fragment, root))
            {
                // The back face of the plane of the root hides its own contents, not the planes of its 3D rendering context
                if (!IsBackFaceHidden(root, rootMatrix))
                {
                    if (polygon is { IsSplit: true })
                    {
                        // The part of the plane on one side of another plane: the clips of the items are applied inside its clip
                        _builder.RestoreTo(rootGroup.SaveCount);
                        rootGroup.Clips.Clear();
                        var groupSaveCount = rootGroup.SaveCount;
                        _builder.Save();
                        _builder.Add(CreateSplitClip(polygon, plane, rootLocal));
                        rootGroup.SaveCount = _builder.SaveCount;
                        PaintItems(plane.Items);
                        _builder.RestoreTo(groupSaveCount);
                        rootGroup.SaveCount = groupSaveCount;
                        rootGroup.Clips.Clear();
                    }
                    else
                    {
                        PaintItems(plane.Items);
                    }
                }

                continue;
            }

            if (IsBackFaceHidden(plane.Fragment, matrices[plane.Order]))
                continue;

            BeginStackingContext(plane.Fragment, CssTransforms.Multiply(Translation(rootLocal), plane.Matrix));
            if (polygon is { IsSplit: true })
            {
                // The part of the plane on one side of another plane
                _builder.Add(CreateSplitClip(polygon, plane, PhysicalOffset.Zero));
            }

            PaintItems(plane.Items);
            EndStackingContext();
        }

        EndStackingContext();

        // The edges created by the splits are not antialiased: the parts of a plane on both sides of another plane cover the pixels
        // of their common edge (their antialiased coverages would add up to less than 1)
        static ClipPathItem CreateSplitClip(PlaneSorting.Polygon polygon, Plane plane, PhysicalOffset offset)
            => new(CreatePolygonPath(PlaneSorting.GetSplitClip(polygon, plane.Fragment.InkOverflow, SplitClipMargin), offset), AntiAlias: false);

        static DisplayPath CreatePolygonPath(List<PhysicalOffset> points, PhysicalOffset offset)
        {
            var path = new DisplayPath();
            for (var i = 0; i < points.Count; i++)
            {
                if (i == 0)
                {
                    path.MoveTo(points[i].X + offset.X, points[i].Y + offset.Y);
                }
                else
                {
                    path.LineTo(points[i].X + offset.X, points[i].Y + offset.Y);
                }
            }

            path.Close();
            return path;
        }

        static double[] Translation(PhysicalOffset offset) => [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, offset.X, offset.Y, 0, 1];
    }

    /// <summary>
    /// Whether the items of a plane of a 3D rendering context may paint something (conservatively: only the boxes that obviously
    /// paint nothing are ignored, e.g. a block without background, borders, shadows nor outline).
    /// </summary>
    private bool HasDrawableContent(Plane plane)
    {
        if (_caretAnchor is not null || HasFilterEffects(plane.Fragment))
            return true;

        foreach (var item in plane.Items)
        {
            switch (item.Marker)
            {
                case PaintItemMarker.EndStackingContext:
                case PaintItemMarker.Outlines:
                    // The outlines are the ones of the boxes of the plane
                    continue;

                case PaintItemMarker.BeginStackingContext:
                    // A filter may paint without content (e.g. feFlood); the root of the stacking context is the next item
                    if (HasFilterEffects((BoxFragment)item.Fragment))
                        return true;

                    continue;

                default:
                    if (item.IsSplitInlineBand)
                        continue;

                    if (item.Part != PaintItemPart.All || item.Fragment is not BoxFragment fragment || !PaintsNothing(fragment))
                        return true;

                    continue;
            }
        }

        return false;

        static bool HasFilterEffects(BoxFragment fragment)
            => fragment.Box is { IsAnonymous: false } box && box.Kind != ContainerBoxKind.Viewport && (FilterEffects.HasFilter(box.Style) || FilterEffects.HasFilter(box.Style, "backdrop-filter"));

        bool PaintsNothing(BoxFragment fragment)
        {
            var box = fragment.Box;
            if (box.Kind == ContainerBoxKind.Viewport)
                return true;

            if (box.Kind is ContainerBoxKind.Replaced or ContainerBoxKind.TableGrid or ContainerBoxKind.TableWrapper || IsTablePart(box) || box.IsFieldsetContent)
                return false;

            if (box.IsAnonymous)
                return true;

            if (box.Style.Layout.Visibility != Visibility.Visible)
                return true;

            if (box.PseudoElement is not null || box.LayoutState is FramesetState || fragment.MathData is not null || (box.Element is { } element && FormControlContents.HasNativeFrame(element)))
                return false;

            var style = GetDecorationStyle(fragment);
            return GetColor(style, "background-color").IsTransparent
                && style.GetComputedValue("background-image") is ("none" or "")
                && box.Style.GetComputedValue("box-shadow") is ("none" or "")
                && fragment.Border == default
                && box.Style.GetComputedValue("border-image-source") is ("none" or "")
                && BorderShapeGeometry.Get(fragment) is null
                && !HasOutline(box.Style)
                && (!_paintsScrollbars || fragment.ScrollbarsDisplayed == Scrollbars.None)
                && GapDecorations.GetDecorations(fragment) is null;
        }
    }
}
