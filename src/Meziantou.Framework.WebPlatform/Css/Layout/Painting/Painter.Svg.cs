using System.Globalization;
using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Rendering;
using Meziantou.Framework.WebPlatform.Svg;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

// The painting of the SVG content (https://svgwg.org/svg2-draft/render.html): the SVG document fragments of the outermost svg
// elements, in document order (the painter's model). The geometry comes from Svg/ (SvgShapeGeometry, SvgTextLayout, SvgUserSpace);
// the paint servers are in Painter.SvgPaintServers.cs, the markers in Painter.SvgMarkers.cs, the texts in Painter.SvgText.cs and
// the effects (clip paths, masks, filters, opacity, blending) in Painter.Effects.cs.
internal sealed partial class Painter
{
    /// <summary>The maximum nesting of the use elements, the markers and the patterns (a deeper or cyclic reference is not painted).</summary>
    private const int MaxSvgDepth = 16;

    /// <summary>
    /// The transform from the coordinates of the display list builder to the coordinates of the group where the SVG content is
    /// painted (the effects of the SVG elements transform the builder to the user space of the element).
    /// </summary>
    private SvgAffine _svgBuilderTransform = SvgAffine.Identity;

    /// <summary>The state of the painting of an SVG subtree.</summary>
    /// <param name="Viewport">The viewport of the outermost svg element (the percentages of the lengths).</param>
    private sealed record SvgPaintContext(SvgOutermostViewport Viewport)
    {
        /// <summary>
        /// Whether the subtree is the content of a resource or of a use element (a marker, a pattern, a mask, the element referenced
        /// by a use element): its elements are painted even when they are in an element that is never rendered (e.g. defs).
        /// </summary>
        public bool IsResourceContent { get; init; }

        /// <summary>The element referenced by the use element whose shadow tree is painted (the styles are the ones of the element instances), or null.</summary>
        public Element? UseInstanceRoot { get; init; }

        /// <summary>The style the painted element instances inherit from (the style of the use element or of the instance of the parent), or null.</summary>
        public ComputedStyle? ParentStyle { get; init; }

        /// <summary>The width and the height of the use element, which override the ones of the svg or symbol element it references.</summary>
        public (double? Width, double? Height) UseSize { get; init; }

        /// <summary>The size of the SVG viewport of the use element, which the percentages of the x, y, width and height of the svg or symbol element it references refer to.</summary>
        public (double Width, double Height)? UseViewportSize { get; init; }

        /// <summary>The element whose painting is recorded as the source of the display items (the outermost use element), or null for the painted element.</summary>
        public Element? Source { get; init; }

        /// <summary>The paints of <c>context-fill</c> and <c>context-stroke</c> (https://svgwg.org/svg2-draft/painting.html#context-paint), or null.</summary>
        public SvgContextPaints? ContextPaints { get; init; }

        /// <summary>The depth of the use elements, the markers and the patterns being painted.</summary>
        public int Depth { get; init; }
    }

    /// <summary>
    /// The paints of the context element (https://svgwg.org/svg2-draft/painting.html#TermContextElement): its fill and its stroke
    /// (without their opacity) in its user space, and the transform of its user space to the coordinates of the SVG painting.
    /// </summary>
    private sealed record SvgContextPaints(DisplayPaint? Fill, DisplayPaint? Stroke, SvgAffine ContextToRoot)
    {
        /// <summary>Whether the fill of the context element is <c>currentcolor</c>: it is the color of the element that uses <c>context-fill</c> (as in Chromium and WebKit).</summary>
        public bool FillIsCurrentColor { get; init; }

        /// <summary>Whether the stroke of the context element is <c>currentcolor</c>.</summary>
        public bool StrokeIsCurrentColor { get; init; }

        public static SvgContextPaints Create(DisplayPaint? fill, DisplayPaint? stroke, SvgAffine contextToRoot, ComputedStyle style)
            => new(fill, stroke, contextToRoot) { FillIsCurrentColor = IsCurrentColor(style, "fill"), StrokeIsCurrentColor = IsCurrentColor(style, "stroke") };

        private static bool IsCurrentColor(ComputedStyle style, string property) => InfraAscii.EqualsIgnoreAsciiCase(style.GetComputedValue(property), "currentcolor");
    }

    /// <summary>
    /// Paints the SVG document fragment of an outermost svg element (https://svgwg.org/svg2-draft/render.html#PaintersModel): its
    /// descendants in document order, in the coordinates of its content box, clipped to its content box when its overflow is not
    /// visible (the user agent style sheet of SVG sets overflow: hidden on the svg elements that are not the root element) or
    /// when it is the root element of the document (as in the browsers, the content of a stand-alone SVG document is always
    /// clipped to its viewport). The foreignObject elements are painted as stacking contexts in that order.
    /// </summary>
    private void PaintSvgContent(BoxFragment fragment, SvgSvgElement svg, PhysicalOffset offset)
    {
        // https://svgwg.org/svg2-draft/coords.html#ViewBoxAttribute: a viewBox of zero width or height disables the rendering
        if (SvgUserSpace.TryGetViewBox(svg, out var viewBox) && (viewBox.Width <= 0 || viewBox.Height <= 0))
            return;

        var viewport = SvgUserSpace.GetOutermostViewport(fragment);
        var content = fragment.ContentBoxRect.Translate(offset);
        var snappedContent = SnapToDevicePixels(content);
        content = content with { X = SnapToDevicePixels(content.X), Y = SnapToDevicePixels(content.Y) };
        var toGroup = SvgUserSpace.GetOutermostContentTransform(svg, viewport).Then(SvgAffine.Translation(content.X, content.Y));
        var style = fragment.Box.Style;
        var saveCount = _builder.SaveCount;
        if (style.IsRoot)
        {
            _builder.Save();
            _builder.Add(new ClipRectItem(snappedContent));
        }
        else if (ClipsSvgViewport(style))
        {
            // The overflow clip edge of the replaced box (its content box extended by overflow-clip-margin)
            _builder.Save();
            _builder.Add(new ClipRectItem(GetSvgViewportClip(style, SnapToDevicePixels(fragment.OverflowClipRect.Translate(offset)))));
        }
        else if (svg.ParentNode is Document)
        {
            // The root element of an SVG document is always clipped to its viewport, as in the browsers (the user agent style
            // sheet only sets overflow: hidden on the other svg elements)
            _builder.Save();
            _builder.Add(new ClipRectItem(snappedContent));
        }

        var previous = _svgBuilderTransform;
        _svgBuilderTransform = SvgAffine.Identity;
        PaintSvgChildren(svg, style, toGroup, new SvgPaintContext(viewport));
        _svgBuilderTransform = previous;
        _builder.RestoreTo(saveCount);
    }

    /// <summary>Returns the clip of an SVG viewport: its rectangle in the axes whose overflow is not visible (https://drafts.csswg.org/css-overflow-3/#valdef-overflow-clip).</summary>
    private static PhysicalRect GetSvgViewportClip(ComputedStyle style, PhysicalRect viewport)
    {
        var clipsX = style.Layout.OverflowX != OverflowKind.Visible;
        var clipsY = style.Layout.OverflowY != OverflowKind.Visible;
        return new PhysicalRect(clipsX ? viewport.X : -UnclippedExtent, clipsY ? viewport.Y : -UnclippedExtent, clipsX ? viewport.Width : 2 * UnclippedExtent, clipsY ? viewport.Height : 2 * UnclippedExtent);
    }

    /// <summary>
    /// Whether an element that establishes an SVG viewport clips its content (https://svgwg.org/svg2-draft/render.html#OverflowAndClipProperties):
    /// its overflow is not visible (auto clips the svg elements, https://github.com/w3c/svgwg/issues/1072, but not the markers).
    /// </summary>
    private static bool ClipsSvgViewport(ComputedStyle style, bool autoIsVisible = false)
        => !IsVisibleOverflow(style.Layout.OverflowX, autoIsVisible) || !IsVisibleOverflow(style.Layout.OverflowY, autoIsVisible);

    private static bool IsVisibleOverflow(OverflowKind overflow, bool autoIsVisible) => overflow == OverflowKind.Visible || (autoIsVisible && overflow == OverflowKind.Auto);

    /// <summary>Returns the style of an element of an SVG subtree: the style of its instance in a use-element shadow tree, or its computed style.</summary>
    private static ComputedStyle GetSvgStyle(Element element, SvgPaintContext context)
    {
        var engine = element.NodeDocument.GetStyleEngine();
        return context is { ParentStyle: { } parent, UseInstanceRoot: { } root } ? engine.GetUseInstanceStyle(element, parent, root) : engine.GetStyle(element);
    }

    private void PaintSvgChildren(Element parent, ComputedStyle parentStyle, SvgAffine parentToGroup, SvgPaintContext context)
    {
        if (context.UseInstanceRoot is not null)
        {
            context = context with { ParentStyle = parentStyle, UseSize = default, UseViewportSize = null };
        }

        // https://svgwg.org/svg2-draft/struct.html#SwitchElement: only the first direct child that is rendered and whose conditional
        // processing attributes evaluate to true
        if (parent.LocalName == "switch")
        {
            for (var child = parent.FirstElementChild; child is not null; child = child.NextElementSibling)
            {
                if (child is SvgElement && !BoxGeneration.IsNeverRenderedSvgElement(child) && child.LocalName is not ("animate" or "animateMotion" or "animateTransform" or "set" or "mpath")
                    && GetSvgStyle(child, context).Layout.Display.Box != DisplayBox.None && SvgConditionalProcessing.Evaluate(child))
                {
                    PaintSvgElement(child, parentToGroup, context);
                    return;
                }
            }

            return;
        }

        for (var child = parent.FirstElementChild; child is not null; child = child.NextElementSibling)
        {
            PaintSvgElement(child, parentToGroup, context);
        }
    }

    /// <summary>Paints an element of an SVG document fragment and its descendants, with its effects and its group opacity (https://drafts.csswg.org/css-color-4/#transparency).</summary>
    private void PaintSvgElement(Element element, SvgAffine parentToGroup, SvgPaintContext context)
    {
        if (!StackGuard.HasSufficientStack)
        {
            StackGuard.RunOnNewStack(() =>
            {
                PaintSvgElement(element, parentToGroup, context);
                return true;
            });
            return;
        }

        if (element is not SvgElement || !element.IsConnected || context.Depth > MaxSvgDepth)
            return;

        var style = GetSvgStyle(element, context);
        var generation = BoxGeneration.Get(style);

        // https://drafts.csswg.org/css-display/#unbox-svg: display: contents unboxes the containers (their content is rendered in the
        // user space of their parent, without their effects), the other elements are not rendered
        if (generation.Kind == BoxGenerationKind.Contents)
        {
            if (element.LocalName is "g" or "a" or "svg" or "switch")
            {
                PaintSvgChildren(element, style, parentToGroup, context);
            }
            else if (element.LocalName == "use")
            {
                PaintSvgUse(element, style, parentToGroup, context);
            }

            return;
        }

        // The content of a resource or of a use element is rendered (e.g. a symbol or the content of a defs element), whatever the
        // display of the ancestors of the resource (https://svgwg.org/svg2-draft/painting.html#MarkerElement); only its own display matters
        if (context.IsResourceContent || context.UseInstanceRoot is not null ? style.Layout.Display.Box == DisplayBox.None || generation.Kind == BoxGenerationKind.NoStyle : !generation.IsBeingRendered)
            return;

        // https://svgwg.org/svg2-draft/struct.html#ConditionalProcessing
        if (!SvgConditionalProcessing.Evaluate(element))
            return;

        var localName = element.LocalName;
        var isInstanceRoot = ReferenceEquals(element, context.UseInstanceRoot);

        // A symbol is only rendered as the element referenced by a use element (https://svgwg.org/svg2-draft/struct.html#SymbolElement)
        if (localName == "symbol" && !isInstanceRoot)
            return;

        // The foreignObject elements are stacking contexts: they are painted in the coordinates of the group of the svg element
        if (localName == "foreignObject")
        {
            if (!context.IsResourceContent && context.UseInstanceRoot is null && _engine.GetFragments(element) is [var foreignObject, ..])
            {
                var items = new List<PaintItem>();
                PaintOrder.CollectStackingContext(foreignObject, items, markers: true);
                var inverse = _svgBuilderTransform.Invert();
                if (inverse is null)
                    return;

                var foreignObjectSaveCount = _builder.SaveCount;

                _builder.Save();
                if (!_svgBuilderTransform.IsIdentity)
                {
                    _builder.Transform(DisplayMatrix.FromSvg(inverse.Value));
                }

                // https://svgwg.org/svg2-draft/render.html#EstablishingStackingContex: the foreignObject element is a stacking context,
                // which is an isolated group (https://drafts.fxtf.org/compositing-1/#isolation): its content that blends does not
                // see what is painted below the element
                if (HasBlendingDescendant(element))
                {
                    _builder.SaveLayer(new SaveLayerItem(1, BlendMode.Normal, Filters: null) { Source = element });
                }

                PaintItems(items);
                _builder.RestoreTo(foreignObjectSaveCount);
            }

            return;
        }

        // The SVG viewport established by a nested svg element or by a symbol (https://svgwg.org/svg2-draft/coords.html#EstablishingANewSVGViewport)
        var elementTransform = SvgUserSpace.GetTransform(element, context.Viewport);
        SvgAffine toGroup;
        PhysicalRect? viewportRect = null;
        if (localName is "svg" or "symbol")
        {
            var viewportTransform = GetSvgViewportTransform(element, isInstanceRoot ? context.UseSize : default, isInstanceRoot ? context.UseViewportSize : null, context.Viewport, out var rect);
            if (viewportTransform is null)
                return;

            viewportRect = rect;
            toGroup = viewportTransform.Value.Then(elementTransform).Then(parentToGroup);

            // The percentages of the element instances refer to the size of the viewport of the instance of the svg or symbol
            // element: the size given by the use element, else its own size in the viewport of the use element
            if (isInstanceRoot)
            {
                context = context with { Viewport = context.Viewport with { OverriddenViewportElement = element, OverriddenViewportSize = (rect.Width, rect.Height) } };
            }
        }
        else
        {
            toGroup = elementTransform.Then(parentToGroup);
        }

        var opacity = GetOpacity(style);
        var uniformTextOpacity = false;
        if (localName == "text" && GetUniformSvgTextOpacity(element, style, context) is { } textOpacity and < 1)
        {
            opacity *= textOpacity;
            uniformTextOpacity = true;
        }

        if (opacity <= 0)
            return;

        // https://svgwg.org/svg2-draft/shapes.html: a shape without geometry (e.g. a rectangle of zero width) is not rendered, nor
        // its effects; the effects only apply to the painted elements
        var painted = localName switch
        {
            "g" or "a" or "svg" or "switch" or "symbol" or "use" or "text" => true,
            "image" => element is SvgImageElement image && GetSvgImageViewport(image, style, context.Viewport) is not null,
            "rect" or "circle" or "ellipse" or "line" or "polyline" or "polygon" or "path" => SvgShapeGeometry.GetPath(element, context.Viewport).Count > 0,
            _ => false,
        };
        if (!painted)
            return;

        var saveCount = _builder.SaveCount;
        var previousBuilderTransform = _svgBuilderTransform;
        var frameToRoot = elementTransform.Then(parentToGroup).Then(_svgBuilderTransform);
        PendingMask? mask = null;
        var maskSaveCount = -1;
        if (opacity < 1 || HasSvgEffects(style))
        {
            _builder.Save();
            _builder.Transform(DisplayMatrix.FromSvg(toGroup));
            _svgBuilderTransform = toGroup.Then(_svgBuilderTransform);
            toGroup = SvgAffine.Identity;
            (mask, maskSaveCount) = ApplySvgEffects(element, style, opacity, context.Viewport);
        }

        // The overflow of an SVG viewport is clipped to the viewport (in the coordinate system of the element, before its viewBox)
        if (viewportRect is { } clipRect && ClipsSvgViewport(style))
        {
            _builder.Save();
            AddSvgClipRect(GetSvgViewportClip(style, clipRect), frameToRoot);
        }

        var source = context.Source ?? element;
        switch (localName)
        {
            case "g" or "a" or "svg" or "switch" or "symbol":
                PaintSvgChildren(element, style, toGroup, context);
                break;

            case "use":
                PaintSvgUse(element, style, toGroup, context);
                break;

            case "rect" or "circle" or "ellipse" or "line" or "polyline" or "polygon" or "path":
                PaintSvgShape(element, style, toGroup, context, source);
                break;

            case "text":
                PaintSvgText(element, style, toGroup, context, source, uniformTextOpacity);
                break;

            case "image":
                PaintSvgImage((SvgImageElement)element, style, toGroup, context, source);
                break;
        }

        if (HasOutline(style))
        {
            PaintSvgOutline(element, style, toGroup, context, source);
        }

        if (mask is not null)
        {
            _builder.RestoreTo(maskSaveCount);
            PaintMask(mask);
        }

        _builder.RestoreTo(saveCount);
        _svgBuilderTransform = previousBuilderTransform;
    }

    /// <summary>
    /// Paints the outline of an SVG element (https://drafts.csswg.org/css-ui-4/#outline-props): as in the browsers, a rectangle
    /// around its object bounding box, at the distance of <c>outline-offset</c>, in its user space.
    /// </summary>
    private void PaintSvgOutline(Element element, ComputedStyle style, SvgAffine toGroup, SvgPaintContext context, Element source)
    {
        if (style.Layout.Visibility != Visibility.Visible || GetSvgObjectBoundingBox(element, context.Viewport) is not { } box)
            return;

        var width = BoxModelStyle.ParseFixedLength(style.GetComputedValue("outline-width"), zoom: 1);
        if (!(width > 0))
            return;

        var outlineOffset = GetOutlineOffset(style, width, zoom: 1);
        var color = GetColor(style, "outline-color");
        var lineStyle = style.GetComputedValue("outline-style") is "auto" ? BorderStyle.Solid : LayoutKeywords.ParseBorderStyle(style.GetComputedValue("outline-style"));
        var outer = new RoundedRect(box, default).Inset(Uniform(-(outlineOffset + width)));
        if (outer.Rect.Width <= 0 || outer.Rect.Height <= 0)
            return;

        _builder.Save();
        _builder.Transform(DisplayMatrix.FromSvg(toGroup));
        BorderPainter.Paint(_builder, outer, Uniform(width), new Sides<BorderStyle>(lineStyle, lineStyle, lineStyle, lineStyle), new Sides<DisplayColor>(color, color, color, color), source, _devicePixelRatio);
        _builder.Restore();
    }

    /// <summary>Whether a descendant of an element has a blend mode (<c>mix-blend-mode</c>).</summary>
    private static bool HasBlendingDescendant(Element element)
    {
        var engine = element.NodeDocument.GetStyleEngine();
        foreach (var descendant in element.Descendants())
        {
            if (descendant is Element child && engine.GetStyle(child).GetComputedValue("mix-blend-mode") is not ("normal" or ""))
                return true;
        }

        return false;
    }

    /// <summary>Clips to a rectangle of a coordinate system given by its transform to the coordinates of the SVG painting.</summary>
    private void AddSvgClipRect(PhysicalRect rect, SvgAffine rectToRoot)
    {
        var transform = _svgBuilderTransform.IsIdentity ? rectToRoot : _svgBuilderTransform.Invert() is { } inverse ? rectToRoot.Then(inverse) : rectToRoot;
        if (transform is { B: 0, C: 0 } && transform.A > 0 && transform.D > 0)
        {
            _builder.Add(new ClipRectItem(new PhysicalRect((rect.X * transform.A) + transform.E, (rect.Y * transform.D) + transform.F, rect.Width * transform.A, rect.Height * transform.D), AntiAlias: true));
            return;
        }

        var path = new DisplayPath();
        path.AddRect(rect);
        _builder.Add(new ClipPathItem(BasicShapePaths.Transform(path, transform)));
    }

    /// <summary>
    /// Returns the transform of the viewport of a nested svg element or of a symbol referenced by a use element
    /// (https://svgwg.org/svg2-draft/coords.html#EstablishingANewSVGViewport): from the user space of its children to its own user
    /// space (its viewBox, then its x and y), and the rectangle of the viewport in its user space; or null when it is not
    /// rendered (a viewBox or a size of zero, a negative size). The width and the height of a use element override the ones of the
    /// element it references (https://svgwg.org/svg2-draft/struct.html#UseLayout).
    /// </summary>
    private static SvgAffine? GetSvgViewportTransform(Element element, (double? Width, double? Height) useSize, (double Width, double Height)? useViewportSize, SvgOutermostViewport viewport, out PhysicalRect rect)
    {
        rect = default;
        var (parentWidth, parentHeight) = useViewportSize ?? SvgUserSpace.GetViewportSize(element, viewport);
        var x = SvgUserSpace.ResolveLength(element, SvgUserSpace.GetComputedValue(element, "x"), parentWidth) ?? 0;
        var y = SvgUserSpace.ResolveLength(element, SvgUserSpace.GetComputedValue(element, "y"), parentHeight) ?? 0;
        // The width and height properties do not apply to the nested svg elements (https://github.com/w3c/svgwg/pull/1059): their attributes do
        var width = useSize.Width ?? SvgUserSpace.ResolveLength(element, element.GetAnimatedAttributeValue("width"), parentWidth) ?? parentWidth;
        var height = useSize.Height ?? SvgUserSpace.ResolveLength(element, element.GetAnimatedAttributeValue("height"), parentHeight) ?? parentHeight;
        if (width <= 0 || height <= 0)
            return null;

        if (SvgUserSpace.TryGetViewBox(element, out var viewBox) && (viewBox.Width <= 0 || viewBox.Height <= 0))
            return null;

        rect = new PhysicalRect(x, y, width, height);
        return SvgUserSpace.GetViewBoxTransform(element, width, height).Then(SvgAffine.Translation(x, y));
    }

    /// <summary>
    /// Paints a use element (https://svgwg.org/svg2-draft/struct.html#UseElement): the element it references is painted as the
    /// root of its use-element shadow tree, translated by its x and y, its element instances inheriting from the use element. The
    /// use element is the context element of its shadow tree (context-fill, context-stroke).
    /// </summary>
    private void PaintSvgUse(Element use, ComputedStyle style, SvgAffine toGroup, SvgPaintContext context)
    {
        if (SvgBoundingBox.GetUseTarget(use) is not { } target || target is not SvgElement || context.Depth >= MaxSvgDepth)
            return;

        // The width and height attributes of the use element override the size of a referenced svg or symbol element (the CSS
        // properties do not apply, https://github.com/w3c/svgwg/pull/1059)
        var (width, height) = SvgUserSpace.GetViewportSize(use, context.Viewport);
        var useWidth = SvgUserSpace.ResolveLength(use, use.GetAnimatedAttributeValue("width"), width) is >= 0 and var w ? w : (double?)null;
        var useHeight = SvgUserSpace.ResolveLength(use, use.GetAnimatedAttributeValue("height"), height) is >= 0 and var h ? h : (double?)null;
        var useToRoot = toGroup.Then(_svgBuilderTransform);
        PhysicalRect? GetBoundingBox() => GetSvgObjectBoundingBox(use, context.Viewport);
        var childContext = context with
        {
            UseInstanceRoot = target,
            ParentStyle = style,
            UseSize = (useWidth, useHeight),
            UseViewportSize = (width, height),
            Source = context.Source ?? use,
            Depth = context.Depth + 1,
            ContextPaints = SvgContextPaints.Create(GetSvgPaint(use, style, "fill", useToRoot, context, GetBoundingBox), GetSvgPaint(use, style, "stroke", useToRoot, context, GetBoundingBox), useToRoot, style),
        };

        PaintSvgElement(target, SvgUserSpace.GetUseTranslation(use, context.Viewport).Then(toGroup), childContext);
    }

    /// <summary>
    /// Returns the object bounding box of an element in its user space (https://svgwg.org/svg2-draft/coords.html#ObjectBoundingBox),
    /// also when it is painted as the content of a resource or of a use element, or null when it has no geometry.
    /// </summary>
    private static PhysicalRect? GetSvgObjectBoundingBox(Element element, SvgOutermostViewport viewport)
        => element is SvgElement svgElement && SvgBoundingBox.ComputeContent(svgElement, viewport) is { } box ? new PhysicalRect(box.MinX, box.MinY, box.MaxX - box.MinX, box.MaxY - box.MinY) : null;

    /// <summary>Whether an SVG element has a clip path, a mask, a filter, a blend mode or an isolation.</summary>
    private static bool HasSvgEffects(ComputedStyle style)
        => style.GetComputedValue("clip-path") is not ("none" or "") || style.GetComputedValue("mask-image") is not ("none" or "")
            || style.GetComputedValue("filter") is not ("none" or "") || style.GetComputedValue("mix-blend-mode") is not ("normal" or "")
            || style.GetComputedValue("isolation") == "isolate";

    /// <summary>Whether the edges of the shapes of a style are antialiased (https://svgwg.org/svg2-draft/painting.html#ShapeRendering: crispEdges and optimizeSpeed turn it off).</summary>
    private static bool IsSvgAntiAliased(ComputedStyle style) => style.GetComputedValue("shape-rendering") is not ("crispedges" or "crispEdges" or "optimizespeed" or "optimizeSpeed");

    /// <summary>The order of the painting of the fill, the stroke and the markers (https://svgwg.org/svg2-draft/painting.html#PaintOrder).</summary>
    private enum SvgPaintLayer : byte
    {
        Fill,
        Stroke,
        Markers,
    }

    /// <summary>Returns the order of the fill, the stroke and the markers of a value of <c>paint-order</c>: the given keywords, then the other ones in the default order.</summary>
    private static SvgPaintLayer[] GetSvgPaintOrder(ComputedStyle style)
    {
        var value = style.GetComputedValue("paint-order");
        if (value is "normal" or "" or "fill" or "fill stroke" or "fill stroke markers")
            return [SvgPaintLayer.Fill, SvgPaintLayer.Stroke, SvgPaintLayer.Markers];

        var result = new List<SvgPaintLayer>(3);
        foreach (var keyword in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            SvgPaintLayer? layer = keyword switch
            {
                "fill" => SvgPaintLayer.Fill,
                "stroke" => SvgPaintLayer.Stroke,
                "markers" => SvgPaintLayer.Markers,
                _ => null,
            };
            if (layer is { } l && !result.Contains(l))
            {
                result.Add(l);
            }
        }

        foreach (var layer in (ReadOnlySpan<SvgPaintLayer>)[SvgPaintLayer.Fill, SvgPaintLayer.Stroke, SvgPaintLayer.Markers])
        {
            if (!result.Contains(layer))
            {
                result.Add(layer);
            }
        }

        return [.. result];
    }

    /// <summary>
    /// Paints a basic shape or a path (https://svgwg.org/svg2-draft/painting.html#Painting): its fill, its stroke and its markers in
    /// the order of <c>paint-order</c>, with their paint servers.
    /// </summary>
    private void PaintSvgShape(Element element, ComputedStyle style, SvgAffine toGroup, SvgPaintContext context, Element source)
    {
        var commands = SvgShapeGeometry.GetPath(element, context.Viewport);
        if (commands.Count == 0)
            return;

        var visible = style.Layout.Visibility == Visibility.Visible;
        var fillRule = style.GetComputedValue("fill-rule") == "evenodd" ? FillRule.EvenOdd : FillRule.NonZero;
        var path = DisplayPath.FromSvg(commands, fillRule);
        var antiAlias = IsSvgAntiAliased(style);
        var toRoot = toGroup.Then(_svgBuilderTransform);
        PhysicalRect? boundingBox = null;
        PhysicalRect? GetBoundingBox() => boundingBox ??= GetSvgObjectBoundingBox(element, context.Viewport);

        var fillPaint = GetSvgPaint(element, style, "fill", toRoot, context, GetBoundingBox);
        var strokePaint = GetSvgPaint(element, style, "stroke", toRoot, context, GetBoundingBox);
        var strokeStyle = SvgStrokeStyle.FromElement(element, context.Viewport, style);
        foreach (var layer in GetSvgPaintOrder(style))
        {
            switch (layer)
            {
                case SvgPaintLayer.Fill when visible && fillPaint is not null:
                {
                    var paint = fillPaint with { Color = fillPaint.Color.MultiplyAlpha(GetNumber(style, "fill-opacity")), AntiAlias = antiAlias };
                    if (!paint.Color.IsTransparent)
                    {
                        _builder.Save();
                        _builder.Transform(DisplayMatrix.FromSvg(toGroup));
                        _builder.Add(new DrawPathItem(path, paint) { Source = source });
                        _builder.Restore();
                    }

                    break;
                }

                case SvgPaintLayer.Stroke when visible && strokePaint is not null:
                    PaintSvgStroke(path, commands, strokePaint with { AntiAlias = antiAlias }, strokeStyle, style, toGroup, source);
                    break;

                case SvgPaintLayer.Markers:
                    PaintSvgMarkers(element, style, commands, toGroup, context, fillPaint, strokePaint, GetMarkerStrokeWidth(strokeStyle, style, toRoot));
                    break;
            }
        }
    }

    /// <summary>
    /// Returns the stroke width that scales the markers of <c>markerUnits="strokeWidth"</c>, in the user space of the element: with
    /// <c>vector-effect: non-scaling-stroke</c>, the width is a length of the host coordinate space, which is divided by the scale
    /// of the transform to that space (as in the browsers).
    /// </summary>
    private double GetMarkerStrokeWidth(SvgStrokeStyle strokeStyle, ComputedStyle style, SvgAffine toRoot)
    {
        if (!strokeStyle.NonScaling)
            return strokeStyle.Width;

        var scale = Math.Sqrt(((toRoot.A * toRoot.A) + (toRoot.B * toRoot.B) + (toRoot.C * toRoot.C) + (toRoot.D * toRoot.D)) / 2);
        return scale > 0 ? strokeStyle.Width * GetHostZoom(style) / scale : 0;
    }

    /// <summary>
    /// Returns the zoom of the host coordinate space of an SVG element (https://svgwg.org/svg2-draft/coords.html#VectorEffects):
    /// its effective zoom, multiplied in an SVG image by the zoom of the element that draws the image.
    /// </summary>
    private double GetHostZoom(ComputedStyle style)
    {
        var zoom = style.Layout.EffectiveZoom;
        return (zoom > 0 ? zoom : 1) * (_document.SvgImage?.Zoom ?? 1);
    }

    /// <summary>
    /// Strokes a path (https://svgwg.org/svg2-draft/painting.html#StrokeProperties): its width, caps, joins and miter limit, its dashes
    /// (scaled by the ratio of the length of the path to <c>pathLength</c>, https://svgwg.org/svg2-draft/paths.html#PathLengthAttribute),
    /// in the user space of the element, or in the coordinates of the painting with <c>vector-effect: non-scaling-stroke</c>
    /// (https://svgwg.org/svg2-draft/coords.html#VectorEffects).
    /// </summary>
    private void PaintSvgStroke(DisplayPath path, List<Svg.SvgPathCommand> commands, DisplayPaint basePaint, SvgStrokeStyle strokeStyle, ComputedStyle style, SvgAffine toGroup, Element source)
    {
        if (!(strokeStyle.Width > 0))
            return;

        var color = basePaint.Color.MultiplyAlpha(GetNumber(style, "stroke-opacity"));
        if (color.IsTransparent)
            return;

        var dashes = strokeStyle.DashArray;
        var dashOffset = strokeStyle.DashOffset;
        if (dashes is not null)
        {
            // https://svgwg.org/svg2-draft/painting.html#StrokeDashing: a list whose sum is zero is a solid stroke
            var sum = 0d;
            foreach (var dash in dashes)
            {
                sum += dash;
            }

            if (!(sum > 0))
            {
                dashes = null;
            }
            else if (strokeStyle.PathLength is { } pathLength)
            {
                var length = SvgShapeGeometry.FromCommands(commands).PathGeometry.TotalLength;
                if (pathLength > 0)
                {
                    var scale = length / pathLength;
                    dashes = [.. dashes.Select(dash => dash * scale)];
                    dashOffset *= scale;
                }
                else
                {
                    // A pathLength of zero scales the dashes infinitely: the first dash covers the path
                    dashes = dashes[0] > 0 ? null : dashes;
                }
            }
        }

        var properties = ToStrokeStyle(strokeStyle) with { Dashes = dashes, DashOffset = dashOffset };
        var paint = basePaint with { Color = color, Style = PaintStyle.Stroke, Stroke = properties };
        _builder.Save();
        if (strokeStyle.NonScaling)
        {
            // The stroke is in the coordinates of the painting (the host coordinate space): the path and the paint server are transformed
            var toRoot = toGroup.Then(_svgBuilderTransform);
            if (_svgBuilderTransform.Invert() is { } inverse && !inverse.IsIdentity)
            {
                _builder.Transform(DisplayMatrix.FromSvg(inverse));
            }

            path = BasicShapePaths.Transform(path, toRoot);
            paint = TransformSvgPaint(paint, toRoot);

            // The width and the dashes are lengths of the host coordinate space (CSS px, multiplied by the zoom)
            var zoom = GetHostZoom(style);
            if (zoom != 1)
            {
                paint = paint with { Stroke = properties with { Width = properties.Width * zoom, Dashes = properties.Dashes?.Select(dash => dash * zoom).ToArray(), DashOffset = properties.DashOffset * zoom } };
            }
        }
        else
        {
            _builder.Transform(DisplayMatrix.FromSvg(toGroup));
        }

        _builder.Add(new DrawPathItem(path, paint) { Source = source });
        _builder.Restore();
    }

    /// <summary>Returns the stroke properties of the display list for the stroke properties of an SVG element (miter-clip and arcs are miter joins).</summary>
    private static StrokeStyle ToStrokeStyle(SvgStrokeStyle strokeStyle) => new(
        strokeStyle.Width,
        strokeStyle.LineCap switch { "round" => LineCap.Round, "square" => LineCap.Square, _ => LineCap.Butt },
        strokeStyle.LineJoin switch { "round" => LineJoin.Round, "bevel" => LineJoin.Bevel, _ => LineJoin.Miter },
        Math.Max(1, strokeStyle.MiterLimit),
        strokeStyle.DashArray is { } dashArray && dashArray.Sum() > 0 ? dashArray : null,
        strokeStyle.DashOffset);

    /// <summary>Returns a paint whose shader is transformed (the shader is in the coordinates before <paramref name="transform"/>).</summary>
    private static DisplayPaint TransformSvgPaint(DisplayPaint paint, SvgAffine transform)
    {
        if (paint.Shader is not { } shader || transform.IsIdentity)
            return paint;

        var matrix = DisplayMatrix.FromSvg(transform);
        return paint with { Shader = shader with { LocalTransform = shader.LocalTransform is { } local ? matrix.PreConcat(local) : matrix } };
    }

    /// <summary>
    /// Returns the paint of <c>fill</c> or <c>stroke</c> (https://svgwg.org/svg2-draft/painting.html#SpecifyingPaint) without its
    /// opacity, in the user space of the element: a color, a paint server (its fallback when the reference is invalid), the paint of
    /// the context element, or null for none.
    /// </summary>
    /// <param name="element">The painted element.</param>
    /// <param name="style">The style of the element.</param>
    /// <param name="property"><c>fill</c> or <c>stroke</c>.</param>
    /// <param name="toRoot">The transform of the user space of the element to the coordinates of the SVG painting.</param>
    /// <param name="context">The state of the painting.</param>
    /// <param name="getBoundingBox">Returns the object bounding box of the element (for the objectBoundingBox units of the paint servers), or null.</param>
    private DisplayPaint? GetSvgPaint(Element element, ComputedStyle style, string property, SvgAffine toRoot, SvgPaintContext context, Func<PhysicalRect?> getBoundingBox)
    {
        var value = style.GetUsedValue(property);
        if (value is "none" or "")
            return null;

        if (value is "context-fill" or "context-stroke")
        {
            // https://svgwg.org/svg2-draft/painting.html#context-paint: the paint of the context element, in its user space
            if (context.ContextPaints is not { } contextPaints || (value == "context-fill" ? contextPaints.Fill : contextPaints.Stroke) is not { } contextPaint)
                return null;

            if (value == "context-fill" ? contextPaints.FillIsCurrentColor : contextPaints.StrokeIsCurrentColor)
                return new DisplayPaint(GetColor(style, "color"));

            if (contextPaint.Shader is null)
                return contextPaint;

            return toRoot.Invert() is { } inverse ? TransformSvgPaint(contextPaint, contextPaints.ContextToRoot.Then(inverse)) : null;
        }

        if (!value.StartsWith("url(", StringComparison.Ordinal))
            return DisplayColor.Parse(value) is { } color ? new DisplayPaint(color) : null;

        var components = ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(value));
        if (components.Count > 0 && SvgResources.GetReferencedElement(element, components[0]) is { } server
            && TryCreatePaintServer(server, element, getBoundingBox, context, out var serverPaint))
        {
            return serverPaint;
        }

        // The fallback of an invalid reference (https://svgwg.org/svg2-draft/painting.html#SpecifyingPaint): none without fallback
        if (components.Count < 2)
            return null;

        var fallback = CssSerializer.SerializeValue(components.GetRange(1, components.Count - 1), property: null);
        if (fallback is "none")
            return null;

        if (components[1] is CssPreservedToken { Type: CssTokenType.Ident } ident && InfraAscii.EqualsIgnoreAsciiCase(ident.Token.Value, "currentcolor"))
            return new DisplayPaint(GetColor(style, "color"));

        return DisplayColor.Parse(fallback) is { } fallbackColor ? new DisplayPaint(fallbackColor) : null;
    }

    /// <summary>
    /// Paints an image element (https://svgwg.org/svg2-draft/embedded.html#ImageElement): the image in the rectangle of its x, y, width
    /// and height (auto is the size of the image), fitted by its <c>preserveAspectRatio</c> for a raster image, clipped to the rectangle
    /// (the user agent style sheet sets overflow: hidden). An SVG image is laid out in the rectangle.
    /// </summary>
    private void PaintSvgImage(SvgImageElement element, ComputedStyle style, SvgAffine toGroup, SvgPaintContext context, Element source)
    {
        if (style.Layout.Visibility != Visibility.Visible || element.Image is not { } image)
            return;

        if (GetSvgImageViewport(element, style, context.Viewport) is not { } viewport)
            return;

        // https://drafts.csswg.org/css-images-3/#the-image-orientation: the EXIF orientation of the image, unless it is none
        image = image.WithOrientation(style.Layout.Replaced.ImageOrientation.FromImage);
        var (x, y, width, height) = (viewport.X, viewport.Y, viewport.Width, viewport.Height);
        _builder.Save();
        _builder.Transform(DisplayMatrix.FromSvg(toGroup));
        if (ClipsSvgViewport(style))
        {
            _builder.Add(new ClipRectItem(viewport, AntiAlias: true));
        }

        var opacity = 1d;
        if (image.IsSvg)
        {
            // https://svgwg.org/svg2-draft/embedded.html#ImageElement: the image is laid out in the viewport, the preserveAspectRatio of
            // the image element replacing the one of the root of the image
            if (SvgImage.Get(image, _document) is { } svgImage)
            {
                new PaintedImage.SvgPaintedImage(svgImage, 1, CssValueComputer.GetUsedColorScheme(style), element.GetAnimatedAttributeValue("preserveAspectRatio")).Draw(_builder, viewport, source);
            }
        }
        else if (image.Width > 0 && image.Height > 0)
        {
            var fit = GetPreserveAspectRatioTransform(element.GetAnimatedAttributeValue("preserveAspectRatio"), image.Width, image.Height, width, height);
            var destination = new PhysicalRect(x + fit.E, y + fit.F, image.Width * fit.A, image.Height * fit.D);
            _builder.Add(new DrawImageItem(image, new PhysicalRect(0, 0, image.Width, image.Height), destination, PaintedImage.GetSampling(style.GetComputedValue("image-rendering")), opacity < 1 ? new DisplayPaint(DisplayColor.Black.MultiplyAlpha(opacity)) : null) { Source = source });
        }

        _builder.Restore();
    }

    /// <summary>
    /// Returns the rectangle of an image element in its user space (its x, y, width and height; auto is the size of the image), or
    /// null when it is not rendered (a size of zero, or an auto size of an image that is not loaded).
    /// </summary>
    private static PhysicalRect? GetSvgImageViewport(SvgImageElement element, ComputedStyle style, SvgOutermostViewport outermost)
    {
        var (viewportWidth, viewportHeight) = SvgUserSpace.GetViewportSize(element, outermost);
        var x = SvgUserSpace.ResolveLength(element, style.GetComputedValue("x"), viewportWidth) ?? 0;
        var y = SvgUserSpace.ResolveLength(element, style.GetComputedValue("y"), viewportHeight) ?? 0;
        var specifiedWidth = SvgUserSpace.ResolveLength(element, style.GetComputedValue("width"), viewportWidth);
        var specifiedHeight = SvgUserSpace.ResolveLength(element, style.GetComputedValue("height"), viewportHeight);
        if (element.GetUsedSize(specifiedWidth is >= 0 ? specifiedWidth : null, specifiedHeight is >= 0 ? specifiedHeight : null, style.Layout.Replaced.ImageOrientation.FromImage) is not var (width, height) || width <= 0 || height <= 0)
            return null;

        return new PhysicalRect(x, y, width, height);
    }

    /// <summary>
    /// Returns the transform that fits a rectangle of a size into a viewport per a value of <c>preserveAspectRatio</c>
    /// (https://svgwg.org/svg2-draft/coords.html#PreserveAspectRatioAttribute): a scale and a translation.
    /// </summary>
    internal static SvgAffine GetPreserveAspectRatioTransform(string? preserveAspectRatio, double width, double height, double viewportWidth, double viewportHeight)
    {
        var scaleX = viewportWidth / width;
        var scaleY = viewportHeight / height;
        var parts = (preserveAspectRatio ?? "xMidYMid meet").Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
        var index = parts.Length > 0 && parts[0] == "defer" ? 1 : 0;
        var align = parts.Length > index ? parts[index] : "xMidYMid";
        if (align == "none")
            return new SvgAffine(scaleX, 0, 0, scaleY, 0, 0);

        var scale = parts.Length > index + 1 && parts[index + 1] == "slice" ? Math.Max(scaleX, scaleY) : Math.Min(scaleX, scaleY);
        var translateX = align.Contains("xMid", StringComparison.Ordinal) ? (viewportWidth - (width * scale)) / 2 : align.Contains("xMax", StringComparison.Ordinal) ? viewportWidth - (width * scale) : 0;
        var translateY = align.Contains("YMid", StringComparison.Ordinal) ? (viewportHeight - (height * scale)) / 2 : align.Contains("YMax", StringComparison.Ordinal) ? viewportHeight - (height * scale) : 0;
        return new SvgAffine(scale, 0, 0, scale, translateX, translateY);
    }

    /// <summary>Returns a number or percentage property from 0 to 1 (e.g. <c>fill-opacity</c>).</summary>
    private static double GetNumber(ComputedStyle style, string property)
    {
        var value = style.GetComputedValue(property);
        if (value.EndsWith('%', StringComparison.Ordinal))
            return double.TryParse(value.AsSpan(0, value.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var percentage) ? Math.Clamp(percentage / 100, 0, 1) : 1;

        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? Math.Clamp(number, 0, 1) : 1;
    }
}
