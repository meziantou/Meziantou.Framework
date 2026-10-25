using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Html;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The scroll containers (https://drafts.csswg.org/css-overflow-3/#scroll-container): the used <c>overflow</c> of the boxes
/// after the propagation to the viewport (https://drafts.csswg.org/css-overflow-3/#overflow-propagation), the classic
/// scrollbars and the scrollbar gutters (https://drafts.csswg.org/css-overflow-3/#scrollbar-gutter-property).
/// </summary>
internal static class ScrollContainers
{
    /// <summary>The thickness of the classic scrollbars with <c>scrollbar-width: thin</c>, as in Chrome on Windows and Linux.</summary>
    private const double ThinScrollbarThickness = 11;

    /// <summary>
    /// Returns whether the <c>overflow</c> of a box is applied to the viewport instead of the box
    /// (https://drafts.csswg.org/css-overflow-3/#overflow-propagation): the root element, and the body element when the root
    /// element is an html element whose <c>overflow</c> is <c>visible</c> in both axes (unless the root or the body element
    /// has containment, https://drafts.csswg.org/css-contain-2/#contain-property). The used value of the box is then <c>visible</c>.
    /// </summary>
    public static bool PropagatesOverflowToViewport(ContainerBox box)
    {
        if (box.PseudoElement is not null || box.Element is not { } element || box.Kind is not (ContainerBoxKind.Normal or ContainerBoxKind.TableWrapper))
            return false;

        if (box.Style.IsRoot)
            return true;

        return element.IsHtml(TagId.Body) && element.ParentNode is Element { ParentNode: Document } root && root.IsHtml(TagId.Html)
            && IsFirstBodyChild(root, element) && GetRootStyle(box) is { } rootLayout && rootLayout.OverflowX == OverflowKind.Visible
            && rootLayout.OverflowY == OverflowKind.Visible && !HasContainment(rootLayout) && !HasContainment(box.Style.Layout);

        static LayoutStyle? GetRootStyle(ContainerBox box)
        {
            for (var parent = box.Parent; parent is not null; parent = parent.Parent)
            {
                if (parent.Kind == ContainerBoxKind.Viewport)
                    return null;

                if (parent.Style.IsRoot && parent.PseudoElement is null)
                    return parent.Style.Layout;
            }

            return null;
        }
    }

    private static bool IsFirstBodyChild(Element root, Element body)
    {
        foreach (var child in root.EnumerateChildNodes())
        {
            if (child is HtmlBodyElement)
                return ReferenceEquals(child, body);
        }

        return false;
    }

    /// <summary>
    /// Whether an html or body element with this style disables the propagation of the properties of the body element to the
    /// initial containing block, the viewport and the canvas (https://drafts.csswg.org/css-contain-2/#contain-property: "when
    /// any containments are active on either the HTML html or body elements"): <c>overflow</c>, <c>writing-mode</c> and
    /// <c>direction</c>, and the background.
    /// </summary>
    internal static bool HasContainment(LayoutStyle style) => style.Contain != Containment.None || style.ContentVisibility != ContentVisibility.Visible;

    /// <summary>
    /// Returns the used <c>overflow-x</c> and <c>overflow-y</c> of a box: <c>visible</c> for the inline boxes, the internal table
    /// boxes that are not cells and the boxes whose <c>overflow</c> is propagated to the viewport (see
    /// <see cref="PropagatesOverflowToViewport"/>); <c>clip</c> for the scroll containers of the replaced elements (their
    /// contents are clipped, they do not scroll, as in the browsers), except a <c>textarea</c>: it is the scroll container of
    /// its inner editor (see <see cref="TextControlLayout"/>), and, as in the browsers, <c>visible</c> behaves as <c>auto</c>;
    /// and a list box with native appearance, the scroll container of its rows (see <see cref="FormControlContents"/>): as in
    /// the browsers, <c>visible</c> scrolls its block axis and hides the overflow of its inline axis.
    /// </summary>
    public static (OverflowKind X, OverflowKind Y) GetUsedOverflow(ContainerBox box)
    {
        // (as in the browsers, a frameset is not a scroll container: its grid fills it, see FramesetFormattingContext)
        if (box.Kind == ContainerBoxKind.Viewport || box.IsInlineBox || FramesetFormattingContext.IsFrameset(box))
            return (OverflowKind.Visible, OverflowKind.Visible);

        var layout = box.Style.Layout;
        var x = layout.OverflowX;
        var y = layout.OverflowY;
        if (x == OverflowKind.Visible && y == OverflowKind.Visible)
        {
            if (FormControlContents.IsNativeListBox(box))
                return layout.IsHorizontalWritingMode ? (OverflowKind.Hidden, OverflowKind.Auto) : (OverflowKind.Auto, OverflowKind.Hidden);

            return TextControlLayout.IsMultiline(box) ? (OverflowKind.Auto, OverflowKind.Auto) : (x, y);
        }

        // https://drafts.csswg.org/css-tables-3/#global-style-overrides: "The overflow property on the table-root and table-wrapper
        // box, when its value is not either visible, clip or hidden, is ignored and treated as if its value was visible"; as in the
        // browsers, a table is not a scroll container (hidden clips like clip). The rows and the cells are clipped by the table
        // grid box, at its padding box (inside the borders of the table); the captions are not clipped
        if (box.Kind == ContainerBoxKind.TableGrid)
            return box.Parent is { Kind: ContainerBoxKind.TableWrapper } wrapper && PropagatesOverflowToViewport(wrapper)
                ? (OverflowKind.Visible, OverflowKind.Visible)
                : (x is OverflowKind.Hidden or OverflowKind.Clip ? OverflowKind.Clip : OverflowKind.Visible, y is OverflowKind.Hidden or OverflowKind.Clip ? OverflowKind.Clip : OverflowKind.Visible);

        if (box.Kind == ContainerBoxKind.TableWrapper)
            return (OverflowKind.Visible, OverflowKind.Visible);

        // https://drafts.csswg.org/css-overflow-3/#overflow-control: "Applies to: block containers, flex containers, grid containers"
        // (and the replaced elements, which clip); the table rows, columns and their groups are not scroll containers
        if (box.Display.Internal is not (DisplayInternal.None or DisplayInternal.TableCell or DisplayInternal.TableCaption))
            return (OverflowKind.Visible, OverflowKind.Visible);

        if (PropagatesOverflowToViewport(box))
            return (OverflowKind.Visible, OverflowKind.Visible);

        if (box.Kind == ContainerBoxKind.Replaced)
            return FormControlContents.IsScrollContainer(box) ? (x, y) : (x == OverflowKind.Visible ? x : OverflowKind.Clip, y == OverflowKind.Visible ? y : OverflowKind.Clip);

        // The foreignObject elements clip their contents as the other SVG viewports (the user agent style sheet of SVG sets
        // overflow: hidden, https://svgwg.org/svg2-draft/render.html#OverflowAndClipProperties): hidden clips without making them
        // scroll containers; as in the browsers, scroll and auto make them scroll containers
        if (SvgForeignObjectLayout.IsForeignObject(box))
            return (x == OverflowKind.Hidden ? OverflowKind.Clip : x, y == OverflowKind.Hidden ? OverflowKind.Clip : y);

        return (x, y);
    }

    /// <summary>Returns whether a box is a scroll container (https://drafts.csswg.org/css-overflow-3/#scroll-container), after the propagation of <c>overflow</c> to the viewport.</summary>
    public static bool IsScrollContainer(ContainerBox box)
    {
        if (!box.Style.Layout.IsScrollContainer && !FormControlContents.IsScrollContainer(box))
            return false;

        var (x, y) = GetUsedOverflow(box);
        return IsScrollable(x) || IsScrollable(y);
    }

    /// <summary>Whether an <c>overflow</c> value lets the box scroll in its axis (hidden, scroll or auto).</summary>
    public static bool IsScrollable(OverflowKind overflow) => overflow is OverflowKind.Hidden or OverflowKind.Scroll or OverflowKind.Auto;

    /// <summary>
    /// Returns the style whose <c>overflow</c>, <c>scrollbar-gutter</c> and <c>scrollbar-width</c> apply to the viewport: the
    /// root element, or the body element when the overflow of the root element is <c>visible</c>
    /// (https://drafts.csswg.org/css-overflow-3/#overflow-propagation; <c>scrollbar-gutter</c> and <c>scrollbar-width</c> always
    /// come from the root element). Null when the root element generates no box.
    /// </summary>
    public static (LayoutStyle? Overflow, LayoutStyle? Root) GetViewportStyles(ContainerBox viewport)
    {
        ContainerBox? rootBox = null;
        foreach (var child in viewport.Children)
        {
            if (child is ContainerBox { PseudoElement: null, IsInTopLayer: false } box && box.Style.IsRoot)
            {
                rootBox = box;
                break;
            }
        }

        if (rootBox is null)
            return (null, null);

        var rootLayout = rootBox.Style.Layout;
        if (rootLayout.OverflowX != OverflowKind.Visible || rootLayout.OverflowY != OverflowKind.Visible || rootBox.Element is not { } root || !root.IsHtml(TagId.Html) || HasContainment(rootLayout))
            return (rootLayout, rootLayout);

        foreach (var child in root.EnumerateChildNodes())
        {
            if (child is HtmlBodyElement body)
            {
                var bodyStyle = root.NodeDocument.GetStyleEngine().GetStyle(body);
                var bodyLayout = bodyStyle.Layout;
                // (https://github.com/w3c/csswg-drafts/pull/4148: a body element that generates no box, also with
                // display: contents, does not propagate its overflow)
                if (bodyLayout.Display.Box != DisplayBox.Normal || HasContainment(bodyLayout))
                    break;

                return (bodyLayout, rootLayout);
            }
        }

        return (rootLayout, rootLayout);
    }

    /// <summary>
    /// Returns the size the viewport-percentage lengths subtract from the viewport (https://drafts.csswg.org/css-values-4/#viewport-relative-lengths):
    /// the scrollbar gutters of the viewport that do not depend on its contents, "if the root element has overflow: scroll or
    /// scrollbar-gutter: stable [...] the viewport-percentage lengths are reduced by the scrollbar gutter" (the scrollbars of
    /// <c>overflow: auto</c> are assumed not to exist, csswg-drafts#6026). It is computed from the styles only (the viewport
    /// units are computed values): the <c>overflow</c> propagated to the viewport (see <see cref="GetViewportStyles"/>), the
    /// <c>scrollbar-gutter</c>, <c>scrollbar-width</c> and <c>writing-mode</c> of the root element. The properties are read one
    /// by one (not the whole typed groups), so that the other properties of the root element can use the viewport units.
    /// </summary>
    /// <returns>The width of the vertical gutters (subtracted from the width) and the height of the horizontal ones.</returns>
    public static (double Width, double Height) GetViewportUnitGutters(StyleEngine engine)
    {
        var document = engine.Document;
        if (document.DocumentElement is not { } root || !ComputedStyleDeclarations.IsBeingRendered(root))
            return default;

        var rootStyle = engine.GetStyle(root);
        var thickness = HidesViewportScrollbars(document) ? 0 : GetScrollbarThickness(document, null);
        thickness = rootStyle.GetComputedValue("scrollbar-width") switch
        {
            "none" => 0,
            "thin" => Math.Min(thickness, ThinScrollbarThickness),
            _ => thickness,
        };

        if (thickness <= 0)
            return default;

        var rootLayout = rootStyle.Layout;
        var overflowLayout = rootLayout;
        if (rootLayout.OverflowX == OverflowKind.Visible && rootLayout.OverflowY == OverflowKind.Visible && root.IsHtml(TagId.Html) && !HasContainment(rootLayout))
        {
            foreach (var child in root.EnumerateChildNodes())
            {
                if (child is HtmlBodyElement body)
                {
                    var bodyLayout = engine.GetStyle(body).Layout;
                    if (bodyLayout.Display.Box == DisplayBox.Normal && !HasContainment(bodyLayout))
                    {
                        overflowLayout = bodyLayout;
                    }

                    break;
                }
            }
        }

        var overflow = GetViewportOverflow(overflowLayout);
        var gutter = rootStyle.GetComputedValue("scrollbar-gutter") switch
        {
            "stable" => ScrollbarGutter.Stable,
            "stable both-edges" or "both-edges stable" => ScrollbarGutter.StableBothEdges,
            _ => ScrollbarGutter.Auto,
        };

        var writingDirection = new WritingDirection(LayoutKeywords.ParseWritingMode(rootStyle.GetComputedValue("writing-mode")), Direction.Ltr);
        var sides = GetScrollbarSides(GetForcedScrollbars(overflow), overflow, writingDirection, gutter, thickness);
        return (sides.HorizontalSum(), sides.VerticalSum());
    }

    /// <summary>
    /// Whether the viewport of a document displays no scrollbar whatever its <c>overflow</c>: the document of an <c>iframe</c> or
    /// <c>frame</c> element whose <c>scrolling</c> attribute is <c>off</c>, <c>noscroll</c> or <c>no</c>
    /// (https://html.spec.whatwg.org/multipage/rendering.html#frames-and-framesets: "the user agent is expected to prevent any
    /// scrollbars from being shown for the viewport of the Document's browsing context, regardless of the 'overflow' property
    /// that applies to that viewport"). The viewport still scrolls.
    /// </summary>
    public static bool HidesViewportScrollbars(Dom.Document document)
        => document.BrowsingContext?.Container is { } container && (container.IsHtml(TagId.Iframe) || container.IsHtml(TagId.Frame))
            && container.GetAttributeValue("scrolling") is { } scrolling
            && (InfraAscii.EqualsIgnoreAsciiCase(scrolling, "no") || InfraAscii.EqualsIgnoreAsciiCase(scrolling, "off") || InfraAscii.EqualsIgnoreAsciiCase(scrolling, "noscroll"));

    /// <summary>
    /// Returns the used <c>overflow</c> of the viewport (https://drafts.csswg.org/css-overflow-3/#overflow-propagation): "If
    /// visible is applied to the viewport, it must be interpreted as auto. If clip is applied to the viewport, it must be
    /// interpreted as hidden."
    /// </summary>
    public static (OverflowKind X, OverflowKind Y) GetViewportOverflow(LayoutStyle? style)
    {
        if (style is null)
            return (OverflowKind.Auto, OverflowKind.Auto);

        return (ForViewport(style.OverflowX), ForViewport(style.OverflowY));

        static OverflowKind ForViewport(OverflowKind value) => value switch
        {
            OverflowKind.Visible => OverflowKind.Auto,
            OverflowKind.Clip => OverflowKind.Hidden,
            _ => value,
        };
    }

    /// <summary>
    /// Returns the thickness of the classic scrollbars of a box (https://drafts.csswg.org/css-scrollbars-1/#scrollbar-width):
    /// <see cref="ViewportOptions.ScrollbarThickness"/> for <c>auto</c> (0 for overlay scrollbars), 11px (as in Chrome) for
    /// <c>thin</c>, 0 for <c>none</c>. The scrollbars do not depend on <c>zoom</c>.
    /// </summary>
    public static double GetScrollbarThickness(LayoutContext context, LayoutStyle? style) => GetScrollbarThickness(context.ScrollbarThickness, style);

    /// <summary>Returns the thickness of the classic scrollbars of a box of a document (see <see cref="GetScrollbarThickness(LayoutContext, LayoutStyle?)"/>).</summary>
    public static double GetScrollbarThickness(Dom.Document document, LayoutStyle? style)
        => GetScrollbarThickness(document.BrowsingContext is { } browsingContext ? browsingContext.Top.Viewport.EffectiveScrollbarThickness : 0, style);

    private static double GetScrollbarThickness(double thickness, LayoutStyle? style)
    {
        if (thickness <= 0 || style is null)
            return thickness <= 0 ? 0 : thickness;

        return style.Overflow.ScrollbarWidth switch
        {
            ScrollbarWidth.None => 0,
            ScrollbarWidth.Thin => Math.Min(thickness, ThinScrollbarThickness),
            _ => thickness,
        };
    }

    /// <summary>
    /// Returns the scrollbar gutters of a scroll container (https://drafts.csswg.org/css-overflow-3/#scrollbar-gutter-property): the
    /// space between its border and its padding that the classic scrollbars take. The vertical scrollbar is on the right, on the
    /// left in a horizontal writing mode with <c>direction: rtl</c> (as in the browsers); the horizontal scrollbar is at the bottom.
    /// <c>scrollbar-gutter: stable</c> reserves the gutter of the scrollbar placed on an inline edge (the vertical scrollbar in a
    /// horizontal writing mode, the horizontal scrollbar in a vertical one) even when there is no scrollbar, and
    /// <c>both-edges</c> on the opposite edge too.
    /// </summary>
    /// <param name="scrollbars">The scrollbars that are displayed.</param>
    /// <param name="overflow">The used overflow of the box.</param>
    /// <param name="writingDirection">The writing mode and the direction of the box.</param>
    /// <param name="gutter">The used <c>scrollbar-gutter</c>.</param>
    /// <param name="thickness">The thickness of the scrollbars.</param>
    public static Sides<double> GetScrollbarSides(Scrollbars scrollbars, (OverflowKind X, OverflowKind Y) overflow, WritingDirection writingDirection, ScrollbarGutter gutter, double thickness)
    {
        if (thickness <= 0)
            return default;

        var vertical = (scrollbars & Scrollbars.Vertical) != Scrollbars.None;
        var horizontal = (scrollbars & Scrollbars.Horizontal) != Scrollbars.None;
        var bothEdgesVertical = false;
        var bothEdgesHorizontal = false;
        if (gutter != ScrollbarGutter.Auto)
        {
            // "stable: The scrollbar gutter is present for classic scrollbars when overflow is hidden, scroll, or auto"
            if (writingDirection.IsHorizontal && IsScrollable(overflow.Y))
            {
                vertical = true;
                bothEdgesVertical = gutter == ScrollbarGutter.StableBothEdges;
            }
            else if (writingDirection.IsVertical && IsScrollable(overflow.X))
            {
                horizontal = true;
                bothEdgesHorizontal = gutter == ScrollbarGutter.StableBothEdges;
            }
        }

        var left = 0d;
        var right = 0d;
        if (vertical)
        {
            if (bothEdgesVertical)
            {
                left = right = thickness;
            }
            else if (writingDirection.IsHorizontal && writingDirection.IsRightToLeft)
            {
                left = thickness;
            }
            else
            {
                right = thickness;
            }
        }

        return new Sides<double>(horizontal && bothEdgesHorizontal ? thickness : 0, right, horizontal ? thickness : 0, left);
    }

    /// <summary>Returns the scrollbars that are always displayed: the axes whose <c>overflow</c> is <c>scroll</c>.</summary>
    public static Scrollbars GetForcedScrollbars((OverflowKind X, OverflowKind Y) overflow)
        => (overflow.X == OverflowKind.Scroll ? Scrollbars.Horizontal : Scrollbars.None) | (overflow.Y == OverflowKind.Scroll ? Scrollbars.Vertical : Scrollbars.None);

    /// <summary>
    /// Returns the scrollbars needed by a scroll container whose scrollable overflow and scrollport are known: the forced ones,
    /// and the ones of the <c>auto</c> axes in which the scrollable overflow is larger than the scrollport. As in Blink ("don't
    /// add auto scrollbars if the box contents aren't visible"), a scrollport without size in the other axis gets no automatic
    /// scrollbar: the scrollbar would give its thickness to a box that displays nothing (WPT css/css-overflow/scrollbar-empty-002.html).
    /// </summary>
    public static Scrollbars GetNeededScrollbars((OverflowKind X, OverflowKind Y) overflow, PhysicalRect scrollingArea, PhysicalRect scrollport)
    {
        var result = GetForcedScrollbars(overflow);

        // A rounding error of the layout must not display a scrollbar
        const double Epsilon = 1d / 64;
        if (overflow.X == OverflowKind.Auto && scrollingArea.Width > scrollport.Width + Epsilon && scrollport.Height > 0)
        {
            result |= Scrollbars.Horizontal;
        }

        if (overflow.Y == OverflowKind.Auto && scrollingArea.Height > scrollport.Height + Epsilon && scrollport.Width > 0)
        {
            result |= Scrollbars.Vertical;
        }

        return result;
    }

    /// <summary>
    /// Returns the physical sides of the scroll origin of a box (https://drafts.csswg.org/css-overflow-3/#scroll-origin): its
    /// block-start and inline-start sides, or the main-start and cross-start sides of a flex container (as in the browsers,
    /// the overflow of a reversed flex container extends from its end edges, e.g. <c>flex-direction: column-reverse</c>), and the
    /// end sides of the reversed axes of a grid lanes container (<c>fill-reverse</c> in the stacking axis, <c>track-reverse</c> in
    /// the grid axis).
    /// </summary>
    public static (PhysicalSide First, PhysicalSide Second) GetScrollOrigin(ContainerBox box, WritingDirection writingDirection)
    {
        if (box.Kind == ContainerBoxKind.Normal && box.Display.Inside == DisplayInside.GridLanes && box.Display.IsGridContainer)
        {
            var (stackReversed, gridReversed) = GridLayoutAlgorithm.GetReversedAxes(box.Style.Layout);
            var blockReversed = GridLayoutAlgorithm.GetLaneAxis(box.Style.Layout) == GridAxis.Rows ? gridReversed : stackReversed;
            var inlineReversed = GridLayoutAlgorithm.GetLaneAxis(box.Style.Layout) == GridAxis.Columns ? gridReversed : stackReversed;
            return (blockReversed ? Opposite(writingDirection.BlockStart) : writingDirection.BlockStart, inlineReversed ? Opposite(writingDirection.InlineStart) : writingDirection.InlineStart);
        }

        if (box.Kind is ContainerBoxKind.Normal or ContainerBoxKind.Anonymous && FormattingContext.GetKind(box) == FormattingContextKind.Flex)
        {
            var properties = FlexContainerProperties.Get(box);
            var (main, cross) = properties.IsRow ? (writingDirection.InlineStart, writingDirection.BlockStart) : (writingDirection.BlockStart, writingDirection.InlineStart);
            return (properties.IsMainReverse ? Opposite(main) : main, properties.IsCrossReverse ? Opposite(cross) : cross);
        }

        return (writingDirection.BlockStart, writingDirection.InlineStart);

        static PhysicalSide Opposite(PhysicalSide side) => side switch
        {
            PhysicalSide.Top => PhysicalSide.Bottom,
            PhysicalSide.Bottom => PhysicalSide.Top,
            PhysicalSide.Left => PhysicalSide.Right,
            _ => PhysicalSide.Left,
        };
    }

    /// <summary>
    /// Clamps a rectangle to the reachable part of the scrollable overflow of a scroll container: the parts beyond the padding
    /// edges of the sides of the scroll origin (<see cref="GetScrollOrigin"/>) are unreachable
    /// (https://drafts.csswg.org/css-overflow-3/#unreachable-scrollable-overflow-region).
    /// </summary>
    public static PhysicalRect ClampToScrollOrigin(PhysicalRect rect, PhysicalRect paddingBox, (PhysicalSide First, PhysicalSide Second) scrollOrigin)
    {
        var left = rect.Left;
        var top = rect.Top;
        var right = rect.Right;
        var bottom = rect.Bottom;
        Clamp(scrollOrigin.First);
        Clamp(scrollOrigin.Second);
        return new PhysicalRect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));

        void Clamp(PhysicalSide side)
        {
            switch (side)
            {
                case PhysicalSide.Left:
                    left = Math.Max(left, paddingBox.Left);
                    right = Math.Max(right, left);
                    break;
                case PhysicalSide.Top:
                    top = Math.Max(top, paddingBox.Top);
                    bottom = Math.Max(bottom, top);
                    break;
                case PhysicalSide.Right:
                    right = Math.Min(right, paddingBox.Right);
                    left = Math.Min(left, right);
                    break;
                default:
                    bottom = Math.Min(bottom, paddingBox.Bottom);
                    top = Math.Min(top, bottom);
                    break;
            }
        }
    }
}

/// <summary>The classic scrollbars of a scroll container.</summary>
[Flags]
internal enum Scrollbars : byte
{
    None = 0,

    /// <summary>The horizontal scrollbar (it scrolls along the x axis).</summary>
    Horizontal = 1,

    /// <summary>The vertical scrollbar (it scrolls along the y axis).</summary>
    Vertical = 2,
}
