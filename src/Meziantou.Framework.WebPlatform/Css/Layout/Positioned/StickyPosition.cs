namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The constraints of a sticky positioned box (https://drafts.csswg.org/css-position-3/#stickypos-insets), from which its offset
/// is computed for the scroll positions of its scroll containers. The rectangles are in the coordinates of the root of the
/// fragment tree when the scroll positions are 0 (the coordinates of the scrolled contents of the scroll containers).
/// </summary>
internal sealed class StickyPositionConstraints
{
    public StickyPositionConstraints(BoxFragment? horizontalScrollContainer, BoxFragment? verticalScrollContainer, PhysicalRect scrollport, Sides<double?> insets, PhysicalRect stickyBox, PhysicalRect containingBlock)
    {
        HorizontalScrollContainer = horizontalScrollContainer;
        VerticalScrollContainer = verticalScrollContainer;
        Scrollport = scrollport;
        Insets = insets;
        StickyBox = stickyBox;
        ContainingBlock = containingBlock;
    }

    /// <summary>
    /// The fragment of the nearest scroll container (https://drafts.csswg.org/css-overflow-3/#scroll-container) that can scroll
    /// horizontally ("the nearest scroll container with a matching scrollable axis"), or null for the viewport: the left and
    /// right insets are relative to it.
    /// </summary>
    public BoxFragment? HorizontalScrollContainer { get; }

    /// <summary>The fragment of the nearest scroll container that can scroll vertically, or null for the viewport: the top and bottom insets are relative to it.</summary>
    public BoxFragment? VerticalScrollContainer { get; }

    /// <summary>
    /// The rectangle the insets are relative to when the scroll positions are 0: horizontally, the content box of the horizontal
    /// scroll container (its scrollport without its paddings, as in the browsers) or the viewport, vertically the one of the
    /// vertical scroll container.
    /// </summary>
    public PhysicalRect Scrollport { get; }

    /// <summary>The insets in px (the percentages refer to the size of <see cref="Scrollport"/>), null for <c>auto</c>.</summary>
    public Sides<double?> Insets { get; }

    /// <summary>The border box of the box at its position in the flow.</summary>
    public PhysicalRect StickyBox { get; }

    /// <summary>The rectangle the margin box of the box stays in: the content box of its containing block minus the margins of the box.</summary>
    public PhysicalRect ContainingBlock { get; }

    /// <summary>
    /// The start sides of the containing block in the horizontal and vertical axes (in its writing mode), whose insets win when
    /// both insets of an axis constrain the box (the right side in a <c>direction: rtl</c> containing block).
    /// </summary>
    public (PhysicalSide Horizontal, PhysicalSide Vertical) StartSides { get; init; } = (PhysicalSide.Left, PhysicalSide.Top);

    /// <summary>
    /// Returns the offset of the box for the scroll positions of its scroll containers (the x of
    /// <paramref name="scrollPosition"/> is the one of <see cref="HorizontalScrollContainer"/>, its y the one of
    /// <see cref="VerticalScrollContainer"/>): "For each side of the box, if the corresponding
    /// inset property is not auto, and the corresponding border edge of the box would be outside the corresponding edge of the
    /// sticky view rectangle, then the box must be visually shifted [...] to be inward of that sticky view rectangle edge, insofar
    /// as it can while its position box remains contained within its containing block."
    /// </summary>
    public PhysicalOffset ComputeOffset(PhysicalOffset scrollPosition)
    {
        var view = Scrollport.Translate(scrollPosition);
        var box = StickyBox;
        var dx = 0d;
        var dy = 0d;

        // As in the browsers, the end edges are applied first: the start edges (in the writing mode of the containing block) win
        // when both constrain the box
        if (StartSides.Horizontal == PhysicalSide.Right)
        {
            ApplyLeft(ref dx);
            ApplyRight(ref dx);
        }
        else
        {
            ApplyRight(ref dx);
            ApplyLeft(ref dx);
        }

        if (StartSides.Vertical == PhysicalSide.Bottom)
        {
            ApplyTop(ref dy);
            ApplyBottom(ref dy);
        }
        else
        {
            ApplyBottom(ref dy);
            ApplyTop(ref dy);
        }

        return new PhysicalOffset(dx, dy);

        void ApplyRight(ref double dx)
        {
            if (Insets.Right is { } right)
            {
                var delta = Math.Min(0, view.Right - right - (box.Right + dx));
                dx += Math.Max(delta, Math.Min(0, ContainingBlock.Left - (box.Left + dx)));
            }
        }

        void ApplyLeft(ref double dx)
        {
            if (Insets.Left is { } left)
            {
                var delta = Math.Max(0, view.Left + left - (box.Left + dx));
                dx += Math.Min(delta, Math.Max(0, ContainingBlock.Right - (box.Right + dx)));
            }
        }

        void ApplyBottom(ref double dy)
        {
            if (Insets.Bottom is { } bottom)
            {
                var delta = Math.Min(0, view.Bottom - bottom - (box.Bottom + dy));
                dy += Math.Max(delta, Math.Min(0, ContainingBlock.Top - (box.Top + dy)));
            }
        }

        void ApplyTop(ref double dy)
        {
            if (Insets.Top is { } top)
            {
                var delta = Math.Max(0, view.Top + top - (box.Top + dy));
                dy += Math.Min(delta, Math.Max(0, ContainingBlock.Bottom - (box.Bottom + dy)));
            }
        }
    }
}

/// <summary>
/// The sticky positioning (https://drafts.csswg.org/css-position-3/#sticky-pos), applied after the layout, when the positions
/// of the scroll containers and of the containing blocks are known.
/// </summary>
internal static class StickyPosition
{
    /// <summary>
    /// Computes the constraints of the fragments of the sticky positioned boxes (in tree order: a sticky box inside another one
    /// is constrained at the position of its ancestor) and shifts them by their offset for the current scroll positions of their
    /// scroll containers. Called after each layout pass and each scroll (the previous sticky offsets are removed first).
    /// </summary>
    /// <param name="fragments">The fragments of the sticky positioned boxes, in tree order.</param>
    /// <param name="initialContainingBlock">The size of the initial containing block (the scrollport of the viewport).</param>
    /// <param name="viewportScrollPosition">The scroll position of the viewport.</param>
    public static void Apply(List<BoxFragment> fragments, PhysicalSize initialContainingBlock, PhysicalOffset viewportScrollPosition)
    {
        foreach (var fragment in fragments)
        {
            MoveInlineContainingBlockDescendants(fragment, PhysicalOffset.Zero - fragment.StickyOffset);
            fragment.Offset -= fragment.StickyOffset;
            fragment.StickyOffset = PhysicalOffset.Zero;
        }

        foreach (var fragment in fragments)
        {
            // The containing block of an in-flow box: the nearest block container ancestor; the one of a table part is its table
            // (https://github.com/w3c/csswg-drafts/issues/5020, as in the browsers). The anonymous block boxes around the
            // inline content of a block container that also has block-level children are skipped, as in the browsers
            BoxFragment? containingBlock = null;
            for (var parent = fragment.Parent; parent is not null; parent = parent.Parent)
            {
                if (parent is BoxFragment { Box.IsInlineBox: false } block && block.Box.Display.Internal is not (DisplayInternal.TableRow or DisplayInternal.TableRowGroup or DisplayInternal.TableHeaderGroup or DisplayInternal.TableFooterGroup)
                    && !IsAnonymousBlock(block.Box))
                {
                    containingBlock = block;
                    break;
                }
            }

            // "the nearest scroll container with a matching scrollable axis" (the overflow of the root element applies to the viewport)
            var horizontal = FindScrollContainer(fragment, horizontal: true, out var horizontalIsFixed);
            var vertical = FindScrollContainer(fragment, horizontal: false, out var verticalIsFixed);
            var horizontalPort = GetScrollport(horizontal, initialContainingBlock);
            var verticalPort = GetScrollport(vertical, initialContainingBlock);
            var scrollport = new PhysicalRect(horizontalPort.X, verticalPort.Y, horizontalPort.Width, verticalPort.Height);
            var inset = fragment.Box.Style.Layout.Insets.Inset;
            var insets = new Sides<double?>(
                Resolve(inset.Top, scrollport.Height),
                Resolve(inset.Right, scrollport.Width),
                Resolve(inset.Bottom, scrollport.Height),
                Resolve(inset.Left, scrollport.Width));

            var stickyBox = new PhysicalRect(fragment.GetOffsetInRootWithoutScrolling(), fragment.Size);
            var containingBlockDirection = containingBlock?.WritingDirection ?? fragment.WritingDirection;

            // The margins of the box are its computed margins, as in the browsers: the space that an auto margin takes (the
            // alignment of the box) and the margin of an over-constrained box are not margins, the box moves in them (WPT
            // css/css-position/sticky/position-sticky-margins-002.html, position-sticky-padding-002.html)
            var computedMargin = fragment.Box.Style.Layout.Box.Margin;
            var marginBasis = containingBlock is null ? 0 : containingBlockDirection.IsHorizontal ? containingBlock.ContentBoxRect.Width : containingBlock.ContentBoxRect.Height;
            var margin = new Sides<double>(
                ResolveMargin(computedMargin.Top, marginBasis),
                ResolveMargin(computedMargin.Right, marginBasis),
                ResolveMargin(computedMargin.Bottom, marginBasis),
                ResolveMargin(computedMargin.Left, marginBasis));

            // The containing block of a box whose containing block is a scroll container is its scrollable contents, as in Blink
            var containingBlockRect = containingBlock is null
                ? ExpandBy(stickyBox, margin)
                : (containingBlock.IsScrollContainer ? containingBlock.GetScrollingArea().Contract(containingBlock.Padding) : containingBlock.ContentBoxRect).Translate(containingBlock.GetOffsetInRootWithoutScrolling());
            var constraints = new StickyPositionConstraints(horizontal, vertical, scrollport, insets, stickyBox, ContractBy(containingBlockRect, margin))
            {
                StartSides = containingBlockDirection.IsHorizontal
                    ? (containingBlockDirection.InlineStart, containingBlockDirection.BlockStart)
                    : (containingBlockDirection.BlockStart, containingBlockDirection.InlineStart),
            };

            // A box in a fixed positioned box that is positioned in the viewport does not move when the viewport scrolls: it is
            // constrained by the viewport at its position in it
            var offset = constraints.ComputeOffset(new PhysicalOffset(
                horizontal is null ? (horizontalIsFixed ? 0 : viewportScrollPosition.X) : horizontal.ScrollState?.Offset.X ?? 0,
                vertical is null ? (verticalIsFixed ? 0 : viewportScrollPosition.Y) : vertical.ScrollState?.Offset.Y ?? 0));
            fragment.StickyConstraints = constraints;
            fragment.StickyOffset = offset;
            fragment.Offset += offset;
            MoveInlineContainingBlockDescendants(fragment, offset);

            // https://drafts.csswg.org/cssom/#resolved-values: the used values (the auto insets stay auto, as in the browsers)
            fragment.ResolvedInsets = insets;
        }

        static bool IsAnonymousBlock(ContainerBox box)
            => box is { Kind: ContainerBoxKind.Anonymous, Display.Internal: DisplayInternal.None, IsBlockContainer: true, IsBlockLevel: true, IsInFlow: true, Parent: { IsBlockContainer: true, IsInlineBox: false } parent }
            && FormattingContext.GetKind(parent) == FormattingContextKind.Block;

        static double ResolveMargin(LengthPercentageAuto value, double basis) => value.IsAuto ? 0 : value.Value.Resolve(basis);

        static double? Resolve(LengthPercentageAuto value, double basis) => value.IsAuto ? null : value.Value.Resolve(basis);

        static PhysicalRect GetScrollport(BoxFragment? scrollContainer, PhysicalSize initialContainingBlock)
            => scrollContainer is null ? new PhysicalRect(PhysicalOffset.Zero, initialContainingBlock) : scrollContainer.ContentBoxRect.Translate(scrollContainer.GetOffsetInRootWithoutScrolling());

        static BoxFragment? FindScrollContainer(BoxFragment fragment, bool horizontal, out bool isFixedToViewport)
        {
            isFixedToViewport = false;
            Fragment child = fragment;
            for (var parent = fragment.Parent; parent is not null; child = parent, parent = parent.Parent)
            {
                // (the fragment of a fixed positioned box whose containing block is the viewport is a child of the root fragment)
                if (parent.Parent is null && child is BoxFragment { Box.Style.Layout.Position: PositionKind.Fixed })
                {
                    isFixedToViewport = true;
                }

                if (parent is not BoxFragment { Box: var box, IsScrollContainer: true } scroller || box.Kind == ContainerBoxKind.Viewport)
                    continue;

                var (x, y) = ScrollContainers.GetUsedOverflow(box);
                if (ScrollContainers.IsScrollable(horizontal ? x : y))
                    return scroller;
            }

            return null;
        }

        // The absolutely positioned boxes whose containing block is a sticky positioned inline box move with it: their fragments
        // are children of the fragment of its block container (see PositionedLayout.GetHost), placed from the first fragment of
        // the inline box before it is shifted
        static void MoveInlineContainingBlockDescendants(BoxFragment fragment, PhysicalOffset offset)
        {
            var inline = fragment.Box;
            if (offset == PhysicalOffset.Zero || !inline.IsInlineBox || inline.PreviousContinuation is not null || !ReferenceEquals(inline.FirstFragment, fragment))
                return;

            var hostBox = PositionedLayout.GetHost(inline);
            for (var parent = fragment.Parent; parent is not null; parent = parent.Parent)
            {
                if (parent is not BoxFragment host || !ReferenceEquals(host.Box, hostBox))
                    continue;

                var children = host.ChildrenSpan;
                for (var i = host.InFlowChildCount; i < children.Length; i++)
                {
                    if (children[i] is BoxFragment { Box: var box } child && PositionedLayout.IsAbsolutelyPositioned(box) && ReferenceEquals(PositionedLayout.FindContainingBlock(box), inline))
                    {
                        child.Offset += offset;
                    }
                }

                return;
            }
        }

        static PhysicalRect ExpandBy(PhysicalRect rect, Sides<double> margin) => ContractBy(rect, new Sides<double>(-margin.Top, -margin.Right, -margin.Bottom, -margin.Left));

        // The margins may be negative: the rectangle is not clamped like PhysicalRect.Contract
        static PhysicalRect ContractBy(PhysicalRect rect, Sides<double> margin) => new(rect.X + margin.Left, rect.Y + margin.Top, rect.Width - margin.Left - margin.Right, rect.Height - margin.Top - margin.Bottom);
    }
}
