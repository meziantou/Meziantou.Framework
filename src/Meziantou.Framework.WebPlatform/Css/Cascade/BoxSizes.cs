using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Css.Cascade;

/// <summary>
/// The sizes of the boxes that are known without layout, used when the layout is not available (a document without browsing
/// context, see <see cref="LayoutResolvedValues"/>) and by the size container queries until they use the layout (#350): the size of the content box of a box is known
/// when it is determined by its own properties (an absolute width or height, or a percentage of a known size), when it fills
/// the inline size of its containing block (a block-level box in normal flow whose width is auto), or when it is zero (an
/// auto size in an axis with size containment). Otherwise, the size is unknown.
/// </summary>
/// <remarks>
/// The sizes are computed from the typed used values of <see cref="LayoutStyle"/> (zoomed lengths, https://drafts.csswg.org/css-viewport/#zoom-property),
/// and returned in the coordinate space of the computed values of the element: divided by its effective zoom (e.g. the
/// resolved value of width, or the size of a query container compared with the lengths of a container query).
/// </remarks>
internal static class BoxSizes
{
    /// <summary>Returns whether the writing mode of the element is vertical (its inline axis is the vertical axis).</summary>
    public static bool IsVertical(ComputedStyle style) => style.Layout.IsVerticalWritingMode;

    /// <summary>
    /// Returns the size of the content box of a query container in a physical axis, or null when it is unknown: the
    /// container has no principal box, its principal box is not a layout containment box, or its size depends on the layout.
    /// </summary>
    public static double? GetContainerContentSize(ComputedStyle style, bool horizontal) => IsEligibleSizeContainer(style) ? Unzoom(style, GetContentSize(style, horizontal)) : null;

    /// <summary>Returns whether the element has a principal box that can be a layout containment box.</summary>
    public static bool IsEligibleSizeContainer(ComputedStyle style) => GeneratesBox(style) && CanHaveLayoutContainment(style);

    /// <summary>Returns whether the element may generate a box: neither it nor one of its ancestors has display: none (see <see cref="BoxGeneration"/>).</summary>
    private static bool GeneratesBox(ComputedStyle style) => !BoxGeneration.Get(style).IsDisplayNone;

    /// <summary>
    /// https://drafts.csswg.org/css-contain-2/#containment-layout: layout containment has no effect on the boxes without a
    /// principal box, the internal table boxes, the internal ruby boxes and the non-atomic inline-level boxes. Like the
    /// browsers, the tables are not eligible either (the principal box is the table wrapper box). The SVG elements other than the outermost svg element do not generate CSS boxes.
    /// </summary>
    private static bool CanHaveLayoutContainment(ComputedStyle style)
    {
        if (style.PseudoElement is null && style.Element.NamespaceUri == Namespaces.Svg && style.Element.ParentNode is Element { NamespaceUri: Namespaces.Svg })
            return false;

        // The box of a replaced element whose display is inline is an atomic inline-level box (e.g. a video element)
        var display = style.Layout.Display;
        var isInlineBox = display.IsInlineBox && !(style.PseudoElement is null && display.Inside == DisplayInside.Flow && display.Internal == DisplayInternal.None && BoxGeneration.IsReplaced(style.Element, style));
        return display.GeneratesBox && !isInlineBox && display.Outside != DisplayOutside.RunIn && display.Inside != DisplayInside.Table
            && display.Internal is DisplayInternal.None or DisplayInternal.TableCaption;
    }

    /// <summary>
    /// Returns the size of the border box of the principal box of an element in a physical axis (the reference box of the
    /// transforms), or null when it is unknown: the element has no principal box, it is not a block-level or atomic box (the
    /// same boxes as the size containers), or its size depends on the layout.
    /// </summary>
    public static double? GetBorderBoxSize(ComputedStyle style, bool horizontal)
    {
        if (!IsEligibleSizeContainer(style) || GetContentSize(style, horizontal) is not { } content)
            return null;

        return style.Layout.Box.GetPaddingAndBorder(horizontal, GetContainingBlockSize(style, horizontal: true)) is { } edges ? Unzoom(style, content + edges) : null;
    }

    /// <summary>
    /// Returns the used width or height of an element (https://drafts.csswg.org/cssom/#resolved-values): the size of its
    /// content box, or of its border box when box-sizing is border-box, or null when it is unknown (the same boxes as
    /// <see cref="GetBorderBoxSize"/>).
    /// </summary>
    public static double? GetUsedSize(ComputedStyle style, bool horizontal)
    {
        // The contents of the replaced elements and their pseudo-elements do not generate boxes (except the ::before and
        // ::after of the checkboxes and the radio buttons whose appearance is none)
        var generation = BoxGeneration.Get(style);
        if (generation.IsDisplayNone || generation.Kind == BoxGenerationKind.ReplacedContents || !CanHaveLayoutContainment(style))
            return null;

        // The other sizes (e.g. fit-content or calc-size()) need the layout
        var size = style.Layout.Box.GetSize(horizontal);
        if (size.Kind is not (SizeKind.Auto or SizeKind.Stretch) && size.TryResolve(GetContainingBlockSize(style, horizontal)) is null)
            return null;

        if (GetContentSize(style, horizontal) is not { } content)
            return null;

        if (style.Layout.BoxSizing != BoxSizing.BorderBox)
            return Unzoom(style, content);

        return style.Layout.Box.GetPaddingAndBorder(horizontal, GetContainingBlockSize(style, horizontal: true)) is { } edges ? Unzoom(style, content + edges) : null;
    }

    /// <summary>
    /// Returns the size that the percentages of the margins and the paddings refer to (the inline size of the containing
    /// block), or null when the element does not generate a box or the size is unknown.
    /// </summary>
    public static double? GetMarginPercentageBasis(ComputedStyle style) => GeneratesBox(style) && !style.Layout.Display.IsContents ? Unzoom(style, GetContainingBlockSize(style, horizontal: true)) : null;

    /// <summary>Converts a used size (zoomed) to the coordinate space of the computed values of the element.</summary>
    private static double? Unzoom(ComputedStyle style, double? value) => value is { } size && style.Layout.EffectiveZoom is var zoom && zoom != 1 ? size / zoom : value;

    /// <summary>Returns the size of the content box of a box in a physical axis (zoomed), or null when it is not known without layout.</summary>
    private static double? GetContentSize(ComputedStyle style, bool horizontal)
    {
        ref var cache = ref style.Layout.GetContentSizeCache(horizontal);
        if (!cache.Resolved)
        {
            cache = (true, ComputeContentSize(style, horizontal));
        }

        return cache.Value;
    }

    private static double? ComputeContentSize(ComputedStyle style, bool horizontal)
    {
        var layout = style.Layout;
        if (!layout.Display.GeneratesBox)
            return null;

        var box = layout.Box;
        var containingBlock = GetContainingBlockSize(style, horizontal);
        var percentageBasis = horizontal ? containingBlock : GetContainingBlockSize(style, horizontal: true);
        var edges = box.GetPaddingAndBorder(horizontal, percentageBasis);
        var borderBox = layout.BoxSizing == BoxSizing.BorderBox;
        var specified = box.GetSize(horizontal).TryResolve(containingBlock);

        // The size in the box-sizing box
        double size;
        if (specified is { } value)
        {
            size = value;
        }
        else if (GetAutoContentSize(style, horizontal, containingBlock, percentageBasis, edges) is { } content)
        {
            if (borderBox)
            {
                if (edges is not { } autoEdges)
                    return null;

                content += autoEdges;
            }

            size = content;
        }
        else
        {
            return null;
        }

        // https://drafts.csswg.org/css-sizing-3/#min-size-properties: the minimum size wins over the maximum size
        var max = box.GetMaxSize(horizontal).TryResolve(containingBlock) ?? double.PositiveInfinity;
        var min = box.GetMinSize(horizontal).TryResolve(containingBlock) ?? 0;
        size = Math.Max(min, Math.Min(max, size));

        if (!borderBox)
            return size;

        return edges is { } boxEdges ? Math.Max(0, size - boxEdges) : null;
    }

    /// <summary>The size of the content box when the size is auto.</summary>
    private static double? GetAutoContentSize(ComputedStyle style, bool horizontal, double? containingBlock, double? percentageBasis, double? edges)
    {
        var layout = style.Layout;
        var inlineAxis = horizontal != layout.IsVerticalWritingMode;
        if (!inlineAxis)
        {
            // https://drafts.csswg.org/css-contain-2/#containment-size: a box with size containment is sized as if it was empty
            return layout.HasSizeContainment(inlineAxis: false) ? 0 : null;
        }

        if (IsBlockLevelInFlow(style))
        {
            // https://drafts.csswg.org/css2/#block-replaced-width: the width of a replaced element (or of a form control)
            // comes from its natural size, which is not known
            if (IsReplacedOrWidget(style))
                return null;

            // https://drafts.csswg.org/css2/#blockwidth: the margins, borders, paddings and width fill the containing block
            if (containingBlock is not { } available || edges is not { } inlineEdges)
                return null;

            // A box that establishes an independent formatting context does not overlap the floats of its block formatting context
            if (EstablishesIndependentFormattingContext(style) && MayHaveIntrudingFloats(style))
                return null;

            var margin = layout.Box.Margin;
            var (start, end) = horizontal ? (margin.Left, margin.Right) : (margin.Top, margin.Bottom);
            if (ResolveMargin(start, percentageBasis) is not { } startMargin || ResolveMargin(end, percentageBasis) is not { } endMargin)
                return null;

            return Math.Max(0, available - startMargin - endMargin - inlineEdges);
        }

        // https://drafts.csswg.org/css2/#shrink-to-fit-float: the shrink-to-fit width of a box with inline-size containment is zero
        var display = layout.Display;
        var atomicInline = display.IsInlineLevel && display.Inside is DisplayInside.FlowRoot or DisplayInside.Flex or DisplayInside.Grid or DisplayInside.GridLanes;
        if ((layout.IsFloating || atomicInline) && !IsFlexOrGridItem(style) && layout.HasSizeContainment(inlineAxis: true))
            return 0;

        return null;
    }

    /// <summary>Returns whether the box is a replaced element (https://html.spec.whatwg.org/multipage/rendering.html#replaced-elements) or a form control.</summary>
    private static bool IsReplacedOrWidget(ComputedStyle style) => style.PseudoElement is null && style.Element is { IsHtmlNamespace: true, TagId: TagId.Img or TagId.Iframe or TagId.Video or TagId.Audio or TagId.Canvas or TagId.Embed or TagId.Object or TagId.Input or TagId.Textarea or TagId.Select or TagId.Button or TagId.Meter or TagId.Progress };

    /// <summary>Returns whether the box is a block-level box in normal flow whose containing block is a block container.</summary>
    private static bool IsBlockLevelInFlow(ComputedStyle style)
    {
        var layout = style.Layout;
        var display = layout.Display;
        if (!display.IsBlockLevel || display.Internal != DisplayInternal.None || display.Inside is not (DisplayInside.Flow or DisplayInside.FlowRoot or DisplayInside.Flex or DisplayInside.Grid or DisplayInside.GridLanes))
            return false;

        if (layout.IsOutOfFlow)
            return false;

        if (style.IsRoot)
            return true;

        if (GetLayoutParent(style) is not { } parent || parent.Layout.IsVerticalWritingMode != layout.IsVerticalWritingMode)
            return false;

        return parent.Layout.Display.IsBlockContainer;
    }

    /// <summary>
    /// Returns whether a block-level box establishes an independent formatting context
    /// (https://drafts.csswg.org/css-display/#establish-an-independent-formatting-context).
    /// </summary>
    private static bool EstablishesIndependentFormattingContext(ComputedStyle style)
    {
        var layout = style.Layout;
        var display = layout.Display;
        if (style.IsRoot || display.Outside != DisplayOutside.Block || display.Inside != DisplayInside.Flow || display.Internal != DisplayInternal.None)
            return true;

        return layout.IsOutOfFlow || layout.IsScrollContainer || layout.IsMultiColumnContainer || layout.HasLayoutContainment || layout.HasPaintContainment
            || (layout.ContainerType & (ContainerType.Size | ContainerType.InlineSize)) != ContainerType.Normal;
    }

    /// <summary>
    /// Returns whether a float may precede the box in its block formatting context: the width of the box then depends on
    /// the position of the float, which is not known without layout.
    /// </summary>
    private static bool MayHaveIntrudingFloats(ComputedStyle style)
    {
        var engine = style.Engine;
        for (var current = style.Element; ;)
        {
            for (var sibling = current.PreviousElementSibling; sibling is not null; sibling = sibling.PreviousElementSibling)
            {
                if (ContainsFloat(engine, sibling))
                    return true;
            }

            if (StyleEngine.GetInheritanceParent(current) is not { } parent)
                return false;

            var parentStyle = engine.GetStyle(parent);
            if (!parentStyle.Layout.Display.IsContents && EstablishesIndependentFormattingContext(parentStyle))
                return false;

            current = parent;
        }
    }

    /// <summary>Returns whether the element, or one of its descendants in the same block formatting context, is a float.</summary>
    private static bool ContainsFloat(StyleEngine engine, Element element)
    {
        var style = engine.GetStyle(element);
        var display = style.Layout.Display;
        if (display.IsNone)
            return false;

        if (style.Layout.IsFloating)
            return true;

        if (!display.IsContents && EstablishesIndependentFormattingContext(style))
            return false;

        for (var child = element.FirstElementChild; child is not null; child = child.NextElementSibling)
        {
            if (ContainsFloat(engine, child))
                return true;
        }

        return false;
    }

    private static bool IsFlexOrGridItem(ComputedStyle style) => GetLayoutParent(style)?.Layout.Display is { Box: DisplayBox.Normal, Internal: DisplayInternal.None, Inside: DisplayInside.Flex or DisplayInside.Grid or DisplayInside.GridLanes };

    /// <summary>The parent box: the nearest ancestor in the flat tree that generates a box (display: contents does not).</summary>
    private static ComputedStyle? GetLayoutParent(ComputedStyle style)
    {
        var parent = style.Parent;
        while (parent is not null && parent.Layout.Display.IsContents)
        {
            parent = parent.Parent;
        }

        return parent;
    }

    /// <summary>Whether the box participates in an inline formatting context without being a containing block (an inline box or a run-in).</summary>
    private static bool IsInlineBox(ComputedStyle style) => style.Layout.Display is var display && (display.IsInlineBox || display.Outside == DisplayOutside.RunIn);

    /// <summary>
    /// Returns the size of the content box of the containing block in a physical axis
    /// (https://drafts.csswg.org/css-display/#containing-block): the initial containing block (the viewport) for the root
    /// element and the fixed positioned boxes, the nearest block container ancestor otherwise; null when it is unknown.
    /// </summary>
    private static double? GetContainingBlockSize(ComputedStyle style, bool horizontal)
    {
        var position = style.Layout.Position;
        if (style.IsRoot || position == PositionKind.Fixed)
            return horizontal ? style.Environment.Width : style.Environment.Height;

        if (position == PositionKind.Absolute)
            return GetAbsolutePositionContainingBlockSize(style, horizontal);

        var parent = GetLayoutParent(style);
        while (parent is not null && IsInlineBox(parent))
        {
            parent = GetLayoutParent(parent);
        }

        return parent is null ? null : GetContentSize(parent, horizontal);
    }

    /// <summary>
    /// https://drafts.csswg.org/css-position-3/#absolute-cb: the containing block of an absolutely positioned box is the
    /// padding box of its nearest ancestor that establishes an absolute positioning containing block, or the initial
    /// containing block. It is unknown when the ancestor is inline (it depends on its fragments).
    /// </summary>
    private static double? GetAbsolutePositionContainingBlockSize(ComputedStyle style, bool horizontal)
    {
        for (var ancestor = GetLayoutParent(style); ancestor is not null; ancestor = GetLayoutParent(ancestor))
        {
            if (!ancestor.Layout.EstablishesAbsolutePositionContainingBlock)
                continue;

            if (IsInlineBox(ancestor) || GetContentSize(ancestor, horizontal) is not { } content)
                return null;

            var padding = ancestor.Layout.Box.Padding;
            var (start, end) = horizontal ? (padding.Left, padding.Right) : (padding.Top, padding.Bottom);
            var percentageBasis = GetContainingBlockSize(ancestor, horizontal: true);
            if (start.TryResolve(percentageBasis) is not { } paddingStart || end.TryResolve(percentageBasis) is not { } paddingEnd)
                return null;

            return content + paddingStart + paddingEnd;
        }

        return horizontal ? style.Environment.Width : style.Environment.Height;
    }

    private static double? ResolveMargin(LengthPercentageAuto value, double? percentageBasis) => value.IsAuto ? 0 : value.Value.TryResolve(percentageBasis);

    /// <summary>Resolves a computed length-percentage to px; null for the keywords (auto, none...) and the percentages of an unknown size.</summary>
    internal static double? ResolveLength(string value, double? percentageBasis) => LengthPercentage.TryParse(value, zoom: 1, nonNegative: false, out var length) ? length.TryResolve(percentageBasis) : null;
}
