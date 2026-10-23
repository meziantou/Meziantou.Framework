using System.Runtime.InteropServices;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Html;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>The kinds of formatting contexts (https://drafts.csswg.org/css-display/#formatting-context) a box lays out its contents with.</summary>
internal enum FormattingContextKind : byte
{
    /// <summary>A block container with block-level children (https://drafts.csswg.org/css2/#block-formatting).</summary>
    Block,

    /// <summary>A block container with inline-level children (https://drafts.csswg.org/css2/#inline-formatting).</summary>
    Inline,

    /// <summary>A flex container (https://drafts.csswg.org/css-flexbox-1/), including <c>-webkit-box</c>.</summary>
    Flex,

    /// <summary>A grid container (https://drafts.csswg.org/css-grid-2/), including <c>grid-lanes</c>.</summary>
    Grid,

    /// <summary>A table grid box and the internal table boxes (https://drafts.csswg.org/css-tables-3/), but the cells.</summary>
    Table,

    /// <summary>A block-level ruby container or an internal ruby box (https://drafts.csswg.org/css-ruby-1/).</summary>
    Ruby,

    /// <summary>A MathML box (https://w3c.github.io/mathml-core/#new-display-math-value).</summary>
    Math,

    /// <summary>A replaced box: its content is outside of the CSS formatting model.</summary>
    Replaced,
}

/// <summary>The content box of a box being laid out: the space its formatting context lays out the children in.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct ContentArea(WritingDirection WritingDirection, LogicalOffset Offset, double InlineSize, double? BlockSize)
{
    /// <summary>
    /// The block formatting context the contents participate in: the one of the parent when the box participates in it (see
    /// <see cref="ConstraintSpace.BlockFormattingContext"/>), or null when the box establishes a new formatting context (a block
    /// container then creates the state of its block formatting context).
    /// </summary>
    public BlockFormattingContextState? BlockFormattingContext { get; init; }

    /// <summary>
    /// The position of the content box in the coordinates of the <see cref="BlockFormattingContext"/> (see <see cref="ExclusionSpace"/>):
    /// the inline coordinate is the distance of its line-left edge from the line-left edge of the content box of the root, the
    /// block coordinate the distance of its block-start edge from the block-start edge of the content box of the root (the
    /// margins not collapsed yet at that position included). Zero for a block formatting context root.
    /// </summary>
    public LogicalOffset BlockFormattingContextOffset { get; init; }

    /// <summary>
    /// Whether the block-start margin of the box collapses with the block-start margins of its first in-flow children
    /// (https://drafts.csswg.org/css2/#collapsing-margins): the box participates in the block formatting context of its parent
    /// and has no block-start border or padding. The collapsed margins are then returned in <see cref="ContentLayoutResult.StartMargin"/>.
    /// </summary>
    public bool StartMarginCollapsesWithChildren { get; init; }

    /// <summary>
    /// Whether the box participates in the block formatting context of its parent and has no block-end border or padding: the
    /// block-end margins of its last children may collapse with its own (the stretch-fit block size of its children treats them
    /// as zero, see <see cref="ConstraintSpace.StretchIgnoresBlockEndMargin"/>).
    /// </summary>
    public bool EndMarginMayCollapseWithChildren { get; init; }

    /// <summary>
    /// Whether the margins of the in-flow children that are adjacent to the block-start edge of the content box are trimmed
    /// (https://drafts.csswg.org/css-box-4/#margin-trim): the box is a block container or a multi-column container whose
    /// <c>margin-trim</c> has <c>block-start</c>, or its own block-start margin is trimmed by an ancestor and collapses with the
    /// margins of its first children ("It also truncates any descendant margins collapsed with such a margin").
    /// </summary>
    public bool TrimsBlockStartMargins { get; init; }

    /// <summary>Whether the margins of the in-flow children that are adjacent to the block-end edge of the content box are trimmed (see <see cref="TrimsBlockStartMargins"/>).</summary>
    public bool TrimsBlockEndMargins { get; init; }

    /// <summary>The outside marker of a list item that is placed next to the first line box of the contents (see <see cref="ConstraintSpace.OutsideMarker"/>), or null.</summary>
    public PendingOutsideMarker? OutsideMarker { get; init; }

    /// <summary>
    /// The box model edges of the box whose contents are laid out by <see cref="FormattingContext.Layout"/>: the sums of its
    /// borders, scrollbar gutters and paddings (the <c>LayoutEdges</c> of a layout API container, see <see cref="LayoutApiFormattingContext"/>).
    /// </summary>
    public LogicalSides<double> Edges { get; init; }

    /// <summary>
    /// The multi-column container whose content is laid out in the content area, in a strip of the column width (see
    /// <see cref="MultiColumnFormattingContext"/>): the block formatting context of the content keeps it for its column spanners.
    /// Null for the other boxes.
    /// </summary>
    public MultiColumnFlow? MultiColumnFlow { get; init; }

    /// <summary>
    /// The line clamping of a line-clamp container whose contents are laid out (see <see cref="Layout.LineClamp"/>): the block
    /// formatting context created for the contents keeps it. Null for the other boxes.
    /// </summary>
    public LineClamp? LineClamp { get; init; }

    /// <summary>
    /// The group alignment of the line boxes of a block formatting context root whose contents are laid out (<c>text-group-align</c>,
    /// see <see cref="Layout.TextGroupAlignment"/>): the block formatting context created for the contents keeps it. Null for the other boxes.
    /// </summary>
    public TextGroupAlignment? TextGroupAlignment { get; init; }

    /// <summary>In quirks mode, the basis of the percentage heights of the children when <see cref="BlockSize"/> is indefinite (see <see cref="ConstraintSpace.QuirksPercentageBlockSize"/>).</summary>
    public double? QuirksPercentageBlockSize { get; init; }

    /// <summary>Whether <see cref="QuirksPercentageBlockSize"/> is the basis of the stretch-fit block size of the children (see <see cref="ConstraintSpace.StretchUsesQuirksPercentageBlockSize"/>).</summary>
    public bool StretchUsesQuirksPercentageBlockSize { get; init; }

    /// <summary>
    /// When <see cref="BlockSize"/> is indefinite, the content-box block size its minimum size property resolves to (the used
    /// block size of the box is at least that), or null when it behaves as 0: a formatting context that sizes its contents to
    /// the block size of the box clamps their block size with it (e.g. the main size of a column flex container,
    /// https://drafts.csswg.org/css-flexbox-1/#algo-main-container).
    /// </summary>
    public double? MinBlockSize { get; init; }

    /// <summary>When <see cref="BlockSize"/> is indefinite, the content-box block size its maximum size property resolves to, or null when it behaves as <c>none</c> (see <see cref="MinBlockSize"/>).</summary>
    public double? MaxBlockSize { get; init; }

    /// <summary>
    /// Whether <see cref="BlockSize"/> is the size of the content box but is indefinite for the percentages of the children (see
    /// <see cref="ConstraintSpace.IsFixedBlockSizeIndefinite"/>).
    /// </summary>
    public bool IsBlockSizeIndefinite { get; init; }

    /// <summary>
    /// Whether <see cref="InlineSize"/> is given by the contents of the box (its fit-content size, or an intrinsic size keyword): it is
    /// not a definite size (https://drafts.csswg.org/css-sizing-3/#definite: "a size that can be determined without performing layout").
    /// </summary>
    public bool InlineSizeDependsOnContents { get; init; }

    /// <summary>
    /// Whether <see cref="BlockSize"/> comes from the inline size through the preferred aspect ratio and the box grows to contain
    /// its contents (https://drafts.csswg.org/css-sizing-4/#aspect-ratio-minimum: its automatic minimum size): the contents are
    /// aligned in the larger of the two sizes.
    /// </summary>
    public bool BlockSizeGrowsToContents { get; init; }

    /// <summary>
    /// The basis of the percentage block sizes of the children of an anonymous block box whose block size is automatic: the one
    /// of its parent (https://drafts.csswg.org/css2/#anonymous-block-level: "anonymous block boxes are ignored when resolving
    /// percentage values that would refer to it: the closest non-anonymous ancestor box is used instead"), else null.
    /// </summary>
    public double? AnonymousPercentageBlockSize { get; init; }

    /// <summary>The block size of the content box when it is definite for the percentages of the children, else null.</summary>
    public double? PercentageBlockSize => IsBlockSizeIndefinite ? null : BlockSize ?? AnonymousPercentageBlockSize;

    /// <summary>Whether the last baseline of the contents is the baseline of an inline-block (see <see cref="ConstraintSpace.UsesInlineBlockBaseline"/>).</summary>
    public bool UsesInlineBlockBaseline { get; init; }

    /// <summary>
    /// When <see cref="BlockSize"/> is indefinite, the block size of the content box when it is known before the contents are laid
    /// out (size containment: the contents do not change it, https://drafts.csswg.org/css-contain-2/#containment-size), else null:
    /// the grid layout resolves the percentages of its rows against it.
    /// </summary>
    public double? ContainedBlockSize { get; init; }

    /// <summary>
    /// Whether the box is laid out to measure its intrinsic block size (see <see cref="ConstraintSpace.MeasuresIntrinsicBlockSize"/>):
    /// only the block size of the contents is needed, a formatting context may skip the creation of their fragments.
    /// </summary>
    public bool MeasuresIntrinsicBlockSize { get; init; }

    /// <summary>Whether the box is laid out to find its automatic block size (see <see cref="ConstraintSpace.MeasuresAutomaticBlockSize"/>).</summary>
    public bool MeasuresAutomaticBlockSize { get; init; }

    /// <summary>Whether the content box is the one of a table cell in its first layout (see <see cref="ConstraintSpace.ScrollContainerPercentageBlockSizeIsZero"/>).</summary>
    public bool IsTableCellMeasure { get; init; }

    /// <summary>With <see cref="IsTableCellMeasure"/>: whether the block size of the cell is restricted (see <see cref="ConstraintSpace.IsRestrictedTableCell"/>).</summary>
    public bool IsRestrictedTableCell { get; init; }

    /// <summary>The basis of the percentage block sizes of the replaced children of a table cell whose height is a length, or of its anonymous block boxes (see <see cref="ConstraintSpace.ReplacedPercentageBlockSize"/>), else null.</summary>
    public double? ReplacedPercentageBlockSize { get; init; }

    /// <summary>The constraint space of the in-flow children: the content box is their containing block.</summary>
    public ConstraintSpace ChildSpace => IsBlockSizeIndefinite || IsTableCellMeasure
        ? ConstraintSpace.ForContentBox(WritingDirection, InlineSize, BlockSize, QuirksPercentageBlockSize) with { PercentageBlockSize = IsBlockSizeIndefinite ? null : BlockSize, ScrollContainerPercentageBlockSizeIsZero = IsTableCellMeasure && IsRestrictedTableCell, UsesInlineBlockBaseline = UsesInlineBlockBaseline, StretchUsesQuirksPercentageBlockSize = StretchUsesQuirksPercentageBlockSize, ReplacedPercentageBlockSize = ReplacedPercentageBlockSize }
        : ConstraintSpace.ForContentBox(WritingDirection, InlineSize, BlockSize, QuirksPercentageBlockSize) with { PercentageBlockSize = PercentageBlockSize, OrthogonalFallbackInlineSize = BlockSize is null ? GetOrthogonalFallbackInlineSize() : null, UsesInlineBlockBaseline = UsesInlineBlockBaseline, StretchUsesQuirksPercentageBlockSize = StretchUsesQuirksPercentageBlockSize, ReplacedPercentageBlockSize = ReplacedPercentageBlockSize };

    /// <summary>
    /// https://drafts.csswg.org/css-writing-modes-4/#orthogonal-auto: the fallback of the available inline space of the orthogonal
    /// children when the block size of the content box is indefinite includes "the size represented by the containing block’s
    /// inner max size (if that is fixed) floored by its inner min size (if that is fixed)" (the initial containing block is the
    /// other limit, see <see cref="ConstraintSpace.OrthogonalFallbackInlineSize"/>).
    /// </summary>
    private double? GetOrthogonalFallbackInlineSize() => MaxBlockSize is { } max ? Math.Max(max, MinBlockSize ?? 0) : null;
}

/// <summary>The result of the layout of the contents of a box.</summary>
/// <param name="BlockSize">
/// The block size of the contents (the <c>auto</c> block size of the content box), without the margins of the last children
/// returned in <see cref="EndMargin"/>.
/// </param>
/// <param name="FirstBaseline">The first baseline, from the block-start edge of the content box, or null.</param>
/// <param name="LastBaseline">The last baseline, from the block-start edge of the content box, or null.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct ContentLayoutResult(double BlockSize, double? FirstBaseline = null, double? LastBaseline = null)
{
    /// <summary>
    /// The block-start margins of the first in-flow children that collapse with the block-start margin of the box (when
    /// <see cref="ContentArea.StartMarginCollapsesWithChildren"/>), and the margins of the children that collapse through
    /// before them: they are outside of the content box.
    /// </summary>
    public MarginStrut StartMargin { get; init; }

    /// <summary>
    /// The block-end margins of the last in-flow children (and of the children that collapse through after them), which
    /// collapse with the block-end margin of the box when it allows it; otherwise they are added to <see cref="BlockSize"/>.
    /// </summary>
    public MarginStrut EndMargin { get; init; }

    /// <summary>
    /// Whether the contents do not separate the block-start and block-end margins of the box: no line box, and the in-flow
    /// children all collapse through (https://drafts.csswg.org/css2/#collapsing-margins).
    /// </summary>
    public bool CollapsedThrough { get; init; }

    /// <summary>
    /// The block size of the area the in-flow contents are laid out in, when it differs from <see cref="BlockSize"/> (the
    /// content-based size of a flex container whose block size is definite), for the scrollable overflow (see
    /// <see cref="BoxFragment.ContentsBlockSize"/>), or null.
    /// </summary>
    public double? InFlowBlockSize { get; init; }

    /// <summary>
    /// The inline size of the area the in-flow contents are laid out in, when it is larger than the content box (the tracks of a
    /// grid that overflow it), for the scrollable overflow of a scroll container (see <see cref="BoxFragment.InFlowBounds"/>), or null.
    /// </summary>
    public double? InFlowInlineSize { get; init; }

    /// <summary>
    /// The offset of the start edges of the area the in-flow contents are laid out in, from the start edges of the content box, when
    /// it is negative (the tracks of the grid axis of a grid lanes container with <c>track-reverse</c> that overflow its start side,
    /// where its scroll origin is), for the scrollable overflow of a scroll container (see <see cref="BoxFragment.InFlowBounds"/>), or zero.
    /// </summary>
    public LogicalOffset InFlowStart { get; init; }

    /// <summary>Whether the contents are the ones of a line-clamp container (see <see cref="LineClamp"/>).</summary>
    public bool IsLineClampContainer { get; init; }
}

/// <summary>
/// A layout algorithm: lays out a box and its contents according to the formatting context it establishes (or continues).
/// <see cref="For"/> dispatches on the inner display type of the box (<see cref="GetKind"/>); the formatting contexts that are
/// not implemented yet lay out their boxes as block containers.
/// </summary>
/// <remarks>
/// The algorithms are stateless singletons: the state of a layout pass is in the <see cref="LayoutContext"/>, the state
/// attached to a box in <see cref="LayoutBox.LayoutState"/>. <see cref="Layout"/> sizes the box (CSS 2.1 §10.3.3 and §10.6.3
/// with the min/max sizes and <c>box-sizing</c>), then lays out its contents with <see cref="LayoutContents"/>; a
/// formatting context with another sizing algorithm overrides <see cref="Layout"/>.
/// </remarks>
internal abstract class FormattingContext
{
    /// <summary>
    /// The maximum number of times a box lays out its contents again when the margins that collapsed with its block-start margin
    /// differ from the ones looked ahead (see <see cref="Layout"/>).
    /// </summary>
    private const int MaxStartMarginRelayouts = 2;

    /// <summary>The kind of the formatting context.</summary>
    public abstract FormattingContextKind Kind { get; }

    /// <summary>Returns the kind of formatting context a box lays out its contents with.</summary>
    public static FormattingContextKind GetKind(ContainerBox box)
    {
        switch (box.Kind)
        {
            case ContainerBoxKind.Replaced:
                return FormattingContextKind.Replaced;
            case ContainerBoxKind.TableGrid:
                return FormattingContextKind.Table;
            case ContainerBoxKind.Viewport or ContainerBoxKind.TableWrapper:
                return FormattingContextKind.Block;
        }

        var display = box.Display;
        if (display.Internal is DisplayInternal.TableRowGroup or DisplayInternal.TableHeaderGroup or DisplayInternal.TableFooterGroup or DisplayInternal.TableRow
            or DisplayInternal.TableColumnGroup or DisplayInternal.TableColumn)
        {
            return FormattingContextKind.Table;
        }

        if (display.IsInternalRubyBox)
            return FormattingContextKind.Ruby;

        return display.Inside switch
        {
            DisplayInside.Flex or DisplayInside.WebkitBox => FormattingContextKind.Flex,
            DisplayInside.Grid or DisplayInside.GridLanes => FormattingContextKind.Grid,
            DisplayInside.Table => FormattingContextKind.Table,
            DisplayInside.Ruby => FormattingContextKind.Ruby,
            DisplayInside.Math => FormattingContextKind.Math,
            _ => box.ChildrenAreInline ? FormattingContextKind.Inline : FormattingContextKind.Block,
        };
    }

    /// <summary>Returns the layout algorithm of a box.</summary>
    public static FormattingContext For(ContainerBox box) => GetKind(box) switch
    {
        FormattingContextKind.Replaced => ReplacedFormattingContext.Instance,

        // https://html.spec.whatwg.org/multipage/rendering.html#frames-and-framesets: the grid of a frameset
        FormattingContextKind.Block or FormattingContextKind.Inline when FramesetFormattingContext.IsFrameset(box) => FramesetFormattingContext.Instance,

        // https://drafts.css-houdini.org/css-layout-api-1/#layout-api-containers: the boxes laid out by a registered layout class
        FormattingContextKind.Block or FormattingContextKind.Inline when box.LayoutApi is not null => LayoutApiFormattingContext.Instance,

        // https://drafts.csswg.org/css-multicol-1/: the multi-column containers
        FormattingContextKind.Block or FormattingContextKind.Inline when MultiColumnFormattingContext.IsMultiColumnContainer(box) => MultiColumnFormattingContext.Instance,
        FormattingContextKind.Flex => FlexFormattingContext.Instance,

        // The grid containers and the grid lanes containers (https://drafts.csswg.org/css-grid-3/)
        FormattingContextKind.Grid => GridFormattingContext.Instance,

        // https://drafts.csswg.org/css-tables-3/: the table wrapper box sizes itself from its table grid box and its captions
        FormattingContextKind.Block when box.Kind == ContainerBoxKind.TableWrapper => TableWrapperFormattingContext.Instance,

        // https://html.spec.whatwg.org/multipage/rendering.html#the-fieldset-and-legend-elements: the rendered legend and the
        // anonymous fieldset content box
        FormattingContextKind.Block when BlockFormattingContext.IsFieldset(box) => FieldsetFormattingContext.Instance,
        FormattingContextKind.Table => TableFormattingContext.Instance,

        // https://w3c.github.io/mathml-core/#layout-algorithms: the MathML boxes whose display is block math or inline math
        FormattingContextKind.Math => MathFormattingContext.Instance,

        // The ruby containers and the internal ruby boxes are laid out by the inline layout of their block container
        // (https://drafts.csswg.org/css-ruby-1/#ruby-layout, see InlineLayoutAlgorithm), the block-level ruby containers are block
        // containers with an inline ruby container (see BoxTreeBuilder)
        _ => box.ChildrenAreInline ? InlineFormattingContext.Instance : BlockFormattingContext.Instance,
    };

    /// <summary>
    /// Lays out a box in a constraint space given by its parent: computes the used size of its border box, its used margins,
    /// borders and paddings, lays out its contents and returns its fragment. The parent positions the fragment.
    /// </summary>
    /// <remarks>
    /// When the box participates in the block formatting context of its parent (<see cref="ConstraintSpace.BlockFormattingContext"/>),
    /// its block-axis margins collapse with the ones of its children (https://drafts.csswg.org/css2/#collapsing-margins): the
    /// result is in <see cref="BoxFragment.CollapsedMargins"/>, and the state of the block formatting context is updated with
    /// the box (its clearance, margins, borders, paddings and contents).
    /// </remarks>
    public virtual BoxFragment Layout(LayoutContext context, ContainerBox box, in ConstraintSpace space)
    {
        // https://drafts.csswg.org/css-overflow-3/#scrollbar-sizing: the classic scrollbars of a scroll container take space
        // (a replaced box clips its contents, except a textarea, the scroll container of its inner editor, and a list box)
        if (context.ScrollbarThickness <= 0 || (box.Kind == ContainerBoxKind.Replaced ? !FormControlContents.IsScrollContainer(box) : !box.Style.Layout.IsScrollContainer) || !ScrollContainers.IsScrollContainer(box))
            return LayoutWithScrollbars(context, box, space, default, Scrollbars.None);

        var layout = box.Style.Layout;
        var overflow = ScrollContainers.GetUsedOverflow(box);
        var thickness = ScrollContainers.GetScrollbarThickness(context, layout);
        if (thickness <= 0)
            return LayoutWithScrollbars(context, box, space, default, Scrollbars.None);

        // "To the extent that the presence of scrollbars can affect sizing, UAs must start with the assumption that no scrollbars
        // are needed, and recalculate sizes if it turns out they are": the scrollbars of the last layout are the first guess (a
        // relayout usually needs the same ones), then the box is laid out again with the scrollbars that its overflow needs. A
        // scrollbar added by a second pass is never removed (the presence of the scrollbars could otherwise oscillate).
        var writingDirection = WritingDirection.FromStyle(layout);
        var gutter = layout.Overflow.ScrollbarGutter;
        var forced = ScrollContainers.GetForcedScrollbars(overflow);
        var scrollbars = forced;
        if (box.ScrollingElement is { } element && context.Engine.TryGetScrollState(element) is { } previous)
        {
            scrollbars |= previous.Scrollbars & GetAutoScrollbars(overflow);
        }

        var fragment = LayoutWithScrollbars(context, box, space, ScrollContainers.GetScrollbarSides(scrollbars, overflow, writingDirection, gutter, thickness), scrollbars);
        for (var pass = 0; pass < 2; pass++)
        {
            var needed = ScrollContainers.GetNeededScrollbars(overflow, fragment.GetScrollingArea(), fragment.PaddingBoxRect);
            if (pass > 0)
            {
                needed |= scrollbars;
            }

            if (needed == scrollbars)
                break;

            scrollbars = needed;
            fragment = LayoutWithScrollbars(context, box, space, ScrollContainers.GetScrollbarSides(scrollbars, overflow, writingDirection, gutter, thickness), scrollbars);
        }

        return fragment;

        static Scrollbars GetAutoScrollbars((OverflowKind X, OverflowKind Y) overflow)
            => (overflow.X == OverflowKind.Auto ? Scrollbars.Horizontal : Scrollbars.None) | (overflow.Y == OverflowKind.Auto ? Scrollbars.Vertical : Scrollbars.None);
    }

    /// <summary>
    /// https://drafts.csswg.org/css-writing-modes-4/#orthogonal-auto: the fallback of the available inline space of an orthogonal
    /// flow includes "the nearest ancestor scrollport’s inner size (if that is fixed), capped by its inner max size (if that is
    /// fixed) and floored by its inner min size (if that is fixed)", in the physical axis of the inline axis of the box. Returns
    /// infinity when no size of the nearest scroll container limits it (the viewport is the initial containing block).
    /// </summary>
    private static double GetScrollportFallbackSize(ContainerBox box, bool horizontal)
    {
        for (var ancestor = box.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (!ScrollContainers.IsScrollContainer(ancestor))
                continue;

            var layout = ancestor.Style.Layout;
            var boxModel = layout.Box;
            var paddingAndBorder = ResolvePadding(boxModel, 0).Add(boxModel.BorderWidth);
            var edges = layout.BoxSizing == BoxSizing.BorderBox ? (horizontal ? paddingAndBorder.HorizontalSum() : paddingAndBorder.VerticalSum()) : 0;
            var size = double.PositiveInfinity;
            if (boxModel.GetSize(horizontal) is { IsLengthPercentage: true, Value.IsFixed: true } preferred)
            {
                size = Math.Max(0, preferred.Value.Length - edges);
            }

            if (boxModel.GetMaxSize(horizontal) is { IsLengthPercentage: true, Value.IsFixed: true } max)
            {
                size = Math.Min(size, Math.Max(0, max.Value.Length - edges));
            }

            if (boxModel.GetMinSize(horizontal) is { IsLengthPercentage: true, Value.IsFixed: true } min)
            {
                size = Math.Max(size, Math.Max(0, min.Value.Length - edges));
            }

            return size;
        }

        return double.PositiveInfinity;
    }

    /// <summary>
    /// Returns the block-step properties that apply to a box (https://drafts.csswg.org/css-rhythm/#block-step-size): they apply to
    /// the block-level boxes laid out by the block layout (the in-flow children and the floats of a block container), when the
    /// step unit is not zero.
    /// </summary>
    private static BlockStep? GetBlockStep(ContainerBox box)
        => box.Kind is ContainerBoxKind.Normal or ContainerBoxKind.Replaced && !box.IsAnonymous && box.IsBlockLevel && box.Display.Internal == DisplayInternal.None
            && box.Style.Layout.BlockStep is { Size: > 0 } blockStep && !PositionedLayout.IsAbsolutelyPositioned(box)
            && box.Parent is { } parent && GetKind(parent) is FormattingContextKind.Block or FormattingContextKind.Inline
            ? blockStep
            : null;

    /// <summary>
    /// Returns the space added to (or removed from, when negative) the outer block size of a box to round it to a multiple of the
    /// step unit (https://drafts.csswg.org/css-rhythm/#block-step-round). As the WPT tests of WebKit's implementation expect
    /// (css/css-rhythm/zero-outer-size-rounded-up-to-block-step-size.html), the multiple is at least the step unit: an empty box
    /// is as tall as one step.
    /// </summary>
    private static double GetBlockStepExtraSpace(BlockStep blockStep, double outerBlockSize)
    {
        // (a tolerance absorbs the rounding errors of the sizes that are already a multiple)
        var steps = outerBlockSize / blockStep.Size;
        var rounded = blockStep.Round switch
        {
            BlockStepRound.Down => Math.Floor(steps + 1e-6),
            BlockStepRound.Nearest => Math.Floor(steps + 0.5),
            _ => Math.Ceiling(steps - 1e-6),
        };
        var extra = (Math.Max(1, rounded) * blockStep.Size) - outerBlockSize;
        return Math.Abs(extra) < 1e-6 ? 0 : extra;
    }

    /// <summary>
    /// Returns where the space of the block step sizing goes (https://drafts.csswg.org/css-rhythm/#block-step-align): <c>auto</c>
    /// is the used <c>align-self</c> of the box when the space is inserted in its margins and is <c>start</c>, <c>end</c> or
    /// <c>center</c>, <c>center</c> otherwise.
    /// </summary>
    private static BlockStepAlign GetBlockStepAlignment(ContainerBox box, BlockStep blockStep)
    {
        if (blockStep.Align != BlockStepAlign.Auto)
            return blockStep.Align;

        if (blockStep.Insert != BlockStepInsert.MarginBox)
            return BlockStepAlign.Center;

        return BoxAlignment.GetAlignSelf(box.Style.Layout, box.Parent?.Style.Layout).Keyword switch
        {
            AlignmentKeyword.Start => BlockStepAlign.Start,
            AlignmentKeyword.End => BlockStepAlign.End,
            _ => BlockStepAlign.Center,
        };
    }

    /// <summary>Returns whether the contents of a box that overflow it in its block axis are not clipped.</summary>
    private static bool IsBlockOverflowVisible(ContainerBox box, WritingDirection writingDirection)
    {
        var (overflowX, overflowY) = ScrollContainers.GetUsedOverflow(box);
        return (writingDirection.IsHorizontal ? overflowY : overflowX) == OverflowKind.Visible && !UsedContainment.HasPaintContainment(box);
    }

    /// <summary>Lays out a box (see <see cref="Layout"/>) whose classic scrollbars take the given space between its borders and its paddings.</summary>
    private BoxFragment LayoutWithScrollbars(LayoutContext context, ContainerBox box, in ConstraintSpace space, Sides<double> scrollbarSides, Scrollbars scrollbars)
    {
        var layout = box.Style.Layout;
        var boxModel = layout.Box;
        var writingDirection = WritingDirection.FromStyle(layout);
        var containingBlock = space.WritingDirection;
        var isOrthogonal = writingDirection.IsOrthogonalTo(containingBlock);

        // https://drafts.csswg.org/css-box-4/#margins: the percentages of the margins and of the paddings refer to the inline size of the containing block
        // https://drafts.csswg.org/css-tables-3/#global-style-overrides: "The margin of table-cell boxes is ignored (as if it was
        // set to 0px)"; their collapsed borders are given by the table
        var border = space.TableCellBorder ?? boxModel.BorderWidth;
        var padding = ResolvePadding(boxModel, space.PercentageInlineSize);
        var margin = box.Display.Internal == DisplayInternal.TableCell ? default : ResolveMargins(boxModel, space.PercentageInlineSize);

        // https://drafts.csswg.org/css-box-4/#margin-trim: the block-axis margins trimmed by a container are "truncated to zero"
        // for the layout; they stay the resolved values of the margin properties (see BoxFragment.UntrimmedMargin)
        var untrimmedMargin = margin;
        if (space.TrimsBlockStartMargin)
        {
            margin = margin.With(containingBlock.BlockStart, 0);
        }

        if (space.TrimsBlockEndMargin)
        {
            margin = margin.With(containingBlock.BlockEnd, 0);
        }

        var paddingAndBorder = writingDirection.ToLogical(padding.Add(border));
        var logicalMargin = writingDirection.ToLogical(margin);

        // https://drafts.csswg.org/css-overflow-3/#scrollbar-sizing: the scrollbar gutters are between the borders and the
        // paddings: "When the box is intrinsically sized, this reserved space is added to the size of its contents. It is
        // otherwise subtracted from space allotted to the content area" (the box-sizing of the preferred sizes ignores them)
        var gutters = writingDirection.ToLogical(scrollbarSides);
        if (box.Kind == ContainerBoxKind.Replaced && context.ScrollbarThickness > 0)
        {
            // (the natural size of a textarea includes the space of its scrollbars)
            ReplacedFormattingContext.SetScrollbarGutters(box, gutters);
        }

        var edges = new LogicalSides<double>(paddingAndBorder.BlockStart + gutters.BlockStart, paddingAndBorder.InlineEnd + gutters.InlineEnd, paddingAndBorder.BlockEnd + gutters.BlockEnd, paddingAndBorder.InlineStart + gutters.InlineStart);

        // The inline size of the box, in its own writing mode. In an orthogonal flow, the available inline size is the block
        // size of the containing block, or a fallback when it is indefinite: the smallest of the fixed maximum block size of the
        // containing block, the fixed size of the nearest scrollport and the size of the initial containing block
        // (https://drafts.csswg.org/css-writing-modes-4/#orthogonal-auto; Blink and WebKit do not use the scrollport)
        var inlineIsHorizontal = writingDirection.IsHorizontal;
        var availableInlineSize = isOrthogonal
            ? space.AvailableBlockSize ?? Math.Min(
                Math.Min(space.OrthogonalFallbackInlineSize ?? double.PositiveInfinity, GetScrollportFallbackSize(box, inlineIsHorizontal)),
                inlineIsHorizontal ? context.InitialContainingBlockSize.Width : context.InitialContainingBlockSize.Height)
            : space.AvailableInlineSize;
        var inlineBasis = space.GetPercentageBasis(inlineIsHorizontal);
        if (isOrthogonal && inlineBasis is null && double.IsFinite(availableInlineSize))
        {
            // The percentages of the inline size of an orthogonal flow resolve against that available size when the block size of
            // its containing block is indefinite (as in the browsers: "50%" is half of the initial containing block)
            inlineBasis = availableInlineSize;
        }

        var fixedInlineSize = isOrthogonal ? space.FixedBlockSize : space.FixedInlineSize;

        // The block size, when it does not depend on the contents (resolved first: an aspect ratio transfers it to the inline axis)
        var blockIsHorizontal = !inlineIsHorizontal;
        var blockBasis = space.GetPercentageBasis(blockIsHorizontal);
        var preferredBlockSize = boxModel.GetSize(blockIsHorizontal);
        var preferredBlockBasis = blockBasis;
        if (preferredBlockBasis is null && space.QuirksPercentageBlockSize is { } quirksBasis && !isOrthogonal && UsesQuirksPercentageHeight(box, layout, preferredBlockSize))
        {
            preferredBlockBasis = quirksBasis;
        }

        // https://drafts.csswg.org/css-sizing-4/#stretch-fit-sizing: the stretch-fit sizes, when the size of the containing
        // block is definite in the axis
        var (inlineStretchFit, blockStretchFit) = GetStretchFitSizes(isOrthogonal, margin, paddingAndBorder, in space);
        var aspectRatio = GetPreferredAspectRatio(context, box, inlineIsHorizontal, paddingAndBorder);
        var isCompressedBlockSize = IntrinsicSizing.IsCompressibleFormControl(box)
            && (IntrinsicSizing.HasPercentageSize(box, blockIsHorizontal) || boxModel.GetMaxSize(blockIsHorizontal) is { IsLengthPercentage: true, Value.HasPercentage: true });
        var blockSizing = new AxisSizing(context, box)
        {
            Horizontal = blockIsHorizontal,
            IsInlineAxis = false,
            PercentageBasis = blockBasis,
            PaddingAndBorder = paddingAndBorder.BlockSum(),
            Edges = edges.BlockSum(),
            StretchSize = blockStretchFit,

            // The intrinsic block sizes of a replaced element are its natural block size (https://drafts.csswg.org/css-sizing-3/#intrinsic-sizes):
            // they are known before its layout. As in Gecko and WebKit, the ones of a compressible form control whose block size or
            // maximum block size is a percentage are zero (https://drafts.csswg.org/css-sizing-3/#min-content-zero; WPT
            // css/css-sizing/range-percent-intrinsic-size-2.html)
            ContentSize = box.Kind == ContainerBoxKind.Replaced ? (isCompressedBlockSize ? 0 : GetAutomaticBlockSize(context, box, 0, 0)) + edges.BlockSum() : null,
        };

        // The intrinsic block sizes of a replaced element with an aspect ratio are its inline size transferred through the ratio.
        // The intrinsic size keywords of its minimum and maximum block sizes, which limit the block size that is transferred to
        // the inline axis, resolve with a tentative inline size: the one the box has when its block size properties are ignored
        // (as Blink; https://github.com/w3c/csswg-drafts/issues/12333)
        var fixedBlockSize = isOrthogonal ? space.FixedInlineSize : space.FixedBlockSize;
        if (box.Kind == ContainerBoxKind.Replaced && aspectRatio is { } tentativeRatio && fixedBlockSize is null && (isOrthogonal ? space.FixedBlockSize : space.FixedInlineSize) is null
            && (boxModel.GetMinSize(blockIsHorizontal).IsContentBasedInBlockAxis || boxModel.GetMaxSize(blockIsHorizontal).IsContentBasedInBlockAxis))
        {
            var tentativeSizing = new AxisSizing(context, box)
            {
                Horizontal = inlineIsHorizontal,
                IsInlineAxis = true,
                PercentageBasis = inlineBasis,
                PaddingAndBorder = paddingAndBorder.InlineSum(),
                Edges = edges.InlineSum(),
                StretchSize = inlineStretchFit,
            };
            var tentativeInlineSize = tentativeSizing.Resolve(boxModel.GetSize(inlineIsHorizontal), SizingProperty.Preferred)
                ?? GetAutomaticInlineSize(context, box, availableInlineSize - logicalMargin.InlineSum(), edges.InlineSum(), tentativeSizing.GetIntrinsicSizingInput(), isFitContent: false);
            blockSizing = blockSizing with { ContentSize = tentativeRatio.ToBlock(tentativeSizing.Clamp(tentativeInlineSize)) };
        }

        // The preferred block size: an intrinsic size keyword behaves as auto (it is the automatic block size, known once the
        // contents are laid out); a calc-size() of a replaced element is resolved with its natural block size
        var resolvesPreferredBlockSize = (!preferredBlockSize.IsContentBasedInBlockAxis || box.Kind == ContainerBoxKind.Replaced && preferredBlockSize.Kind == SizeKind.CalcSize)
            && !(space.MeasuresIntrinsicBlockSize && !isOrthogonal);
        var specifiedBlockSize = fixedBlockSize ?? (resolvesPreferredBlockSize ? (blockSizing with { PercentageBasis = preferredBlockBasis }).Resolve(preferredBlockSize, SizingProperty.Preferred) : null);
        if (specifiedBlockSize is null && preferredBlockBasis is null && space.ScrollContainerPercentageBlockSizeIsZero && !isOrthogonal && preferredBlockSize is { IsLengthPercentage: true, Value.HasPercentage: true }
            && ScrollContainers.GetUsedOverflow(box) is var (overflowX, overflowY) && (overflowX is OverflowKind.Auto or OverflowKind.Scroll || overflowY is OverflowKind.Auto or OverflowKind.Scroll))
        {
            specifiedBlockSize = (blockSizing with { PercentageBasis = 0 }).Resolve(preferredBlockSize, SizingProperty.Preferred);
        }


        // https://drafts.csswg.org/css-align-3/#justify-block: the self-alignment of a block-level box in block layout
        var justification = space.FixedInlineSize is null && !space.IsInlineSizeIndefinite && !space.AutoInlineSizeIsFitContent && box.IsBlockLevel && box.IsInFlow
            ? GetBlockJustification(box, containingBlock)
            : null;

        // https://drafts.csswg.org/css-position-3/#abspos-auto-size: the stretch-fit size of an absolutely positioned box; the
        // stretched block size of an orthogonal block-level box (its block axis is the inline axis of its containing block)
        var stretchInlineSize = isOrthogonal ? space.StretchBlockSize : space.StretchInlineSize;
        var stretchBlockSize = isOrthogonal ? space.StretchInlineSize : space.StretchBlockSize;
        if (isOrthogonal && justification is { Alignment: AxisAlignment.Stretch })
        {
            stretchBlockSize ??= blockStretchFit;
        }
        if (specifiedBlockSize is null && stretchBlockSize is { } stretchBlock && preferredBlockSize.IsAuto && !space.MeasuresIntrinsicBlockSize)
        {
            specifiedBlockSize = Math.Max(stretchBlock, paddingAndBorder.BlockSum());
        }

        var preferredBlockBorderBoxSize = specifiedBlockSize;
        if (specifiedBlockSize is not null && fixedBlockSize is null)
        {
            // An intrinsic size keyword of the minimum or maximum block size is the automatic block size of the box
            // (https://drafts.csswg.org/css-sizing-3/#valdef-width-min-content): a definite block size that it limits is only known once
            // the contents are laid out. The box is laid out first in the specified size (its children resolve their percentages and
            // break their lines in it), which gives its automatic block size, then its contents are laid out in the limited size: the
            // lines of a flex container are not stretched to the specified size (https://github.com/w3c/csswg-drafts/issues/12123; WPT
            // css/css-flexbox/flex-container-max-content-002.tentative.html)
            if (box.Kind != ContainerBoxKind.Replaced && !isOrthogonal && blockSizing.ContentSize is null && !space.MeasuresIntrinsicBlockSize
                && (boxModel.GetMinSize(blockIsHorizontal).IsIntrinsic || boxModel.GetMaxSize(blockIsHorizontal).IsIntrinsic))
            {
                // (measured apart from the block formatting context of the parent: its floats and its margins are not changed)
                var measure = context.LayoutBox(box, space with { FixedBlockSize = specifiedBlockSize.Value, BlockFormattingContext = null, MeasuresAutomaticBlockSize = true });
                blockSizing = blockSizing with { ContentSize = measure.IntrinsicBlockSize };
            }

            specifiedBlockSize = blockSizing.Clamp(specifiedBlockSize.Value);
        }

        // The sizing of the inline axis: the stretch-fit size fills the available space (https://drafts.csswg.org/css-sizing-3/#stretch-fit-sizing),
        // the intrinsic sizes of the box depend on its block size when it is definite (its percentage block sizes and its
        // orthogonal flows: https://drafts.csswg.org/css-sizing-3/#intrinsic-sizes)
        var inlineSizing = new AxisSizing(context, box)
        {
            Horizontal = inlineIsHorizontal,
            IsInlineAxis = true,
            PercentageBasis = inlineBasis,
            PaddingAndBorder = paddingAndBorder.InlineSum(),
            Edges = edges.InlineSum(),
            StretchSize = inlineStretchFit,
            ContentBlockSize = specifiedBlockSize is { } definiteBlockSize ? Math.Max(0, definiteBlockSize - edges.BlockSum()) : null,

            // In quirks mode, the percentage heights of the descendants of an auto height block container resolve against the
            // height of an ancestor: the replaced elements transfer them to their intrinsic inline sizes
            QuirksPercentageBlockSize = specifiedBlockSize is null && space.QuirksPercentageBlockSize is { } childrenQuirksBasis && !isOrthogonal && PassesQuirksPercentageBlockSize(box, layout) ? childrenQuirksBasis : null,

            // The number of automatic repetitions of the rows of a grid container depends on its minimum and maximum block sizes
            BlockLimits = specifiedBlockSize is null && GridLayoutAlgorithm.UsesBlockSizeLimits(box) ? blockSizing.GetContentBoxLimits() : default,
        };

        // https://drafts.csswg.org/css-sizing-4/#aspect-ratio: the automatic size in one axis is computed from the size in the
        // other axis through the preferred aspect ratio, and the min/max constraints are transferred
        // (https://drafts.csswg.org/css-sizing-4/#aspect-ratio-size-transfers). "the resolved preferred size in the
        // ratio-determining axis (before applying min/max) gets transferred thru the ratio": it is also the min-content and
        // max-content size of the box (the intrinsic size keywords resolve to it)
        if (aspectRatio is { } preferredRatio && preferredBlockBorderBoxSize is { } ratioDeterminingSize)
        {
            inlineSizing = inlineSizing with { TransferredSize = preferredRatio.ToInline(ratioDeterminingSize) };
        }

        if (aspectRatio is { } limitsRatio && fixedBlockSize is null && GridLayoutAlgorithm.UsesInlineSizeLimits(box))
        {
            inlineSizing = inlineSizing with { TransferredLimits = GetTransferredMinMax(blockSizing, limitsRatio, toInline: true) };
        }

        var inlineIsRatioDependent = false;
        var inlineIsAutomatic = false;
        var inlineDependsOnContents = false;
        double inlineSize;
        var isAutoInlineSize = false;
        if (fixedInlineSize is { } fixedInline)
        {
            inlineSize = fixedInline;
        }
        else
        {
            double? transferredMin = null;
            double? transferredMax = null;
            var preferredInlineSize = boxModel.GetSize(inlineIsHorizontal);

            // The intrinsic size keywords of a box with an aspect ratio and a definite block size are the transferred size, which
            // is ratio-dependent (it has the automatic minimum size, https://drafts.csswg.org/css-sizing-4/#aspect-ratio-minimum)
            var preferredIsTransferred = inlineSizing.TransferredSize is not null && preferredInlineSize.IsIntrinsic;
            if (!preferredIsTransferred && inlineSizing.Resolve(preferredInlineSize, SizingProperty.Preferred) is { } specified)
            {
                inlineSize = specified;
                inlineDependsOnContents = preferredInlineSize.IsIntrinsic;

                // https://drafts.csswg.org/css-sizing-4/#aspect-ratio-size-transfers: the sizing constraints of the block axis are
                // "applied to any indefinite preferred size in the other axis": an intrinsic size keyword is one
                if (preferredInlineSize.IsIntrinsic && aspectRatio is { } intrinsicTransfer && fixedBlockSize is null && !(space.MeasuresIntrinsicBlockSize && !isOrthogonal))
                {
                    (transferredMin, transferredMax) = GetTransferredMinMax(blockSizing, intrinsicTransfer, toInline: true);
                }
            }
            else
            {
                // The stretch-fit size of an absolutely positioned box (its alignment is stretch, or it is not ratio-dependent)
                if (stretchInlineSize is { } stretchInline && preferredInlineSize.BehavesAs == SizeKind.Auto)
                {
                    inlineSize = Math.Max(stretchInline, paddingAndBorder.InlineSum());
                }
                else if (inlineSizing.TransferredSize is { } transferred)
                {
                    inlineIsRatioDependent = true;
                    inlineSize = transferred;
                }
                else
                {
                    // The auto margins of a block-level box that fills its containing block are 0; the size of a replaced box or of
                    // a shrink-to-fit box (a button) is given by its contents, its auto margins take the remaining space
                    var isFitContent = isOrthogonal || space.AutoInlineSizeIsFitContent || justification is { IsFitContent: true };
                    isAutoInlineSize = box.Kind != ContainerBoxKind.Replaced && !isFitContent && !BlockFormattingContext.UsesShrinkToFit(box);
                    inlineDependsOnContents = box.Kind != ContainerBoxKind.Replaced && !isAutoInlineSize;
                    inlineIsAutomatic = true;
                    inlineSize = GetAutomaticInlineSize(context, box, availableInlineSize - logicalMargin.InlineSum(), edges.InlineSum(), inlineSizing.GetIntrinsicSizingInput(), isFitContent);
                }

                // https://drafts.csswg.org/css-values-5/#calc-size: calc-size(auto, ...) resolves with the automatic size as its basis
                if (preferredInlineSize.Kind == SizeKind.CalcSize && (inlineSizing with { AutomaticSize = inlineSize }).Resolve(preferredInlineSize, SizingProperty.Preferred) is { } calcSize)
                {
                    inlineSize = calcSize;
                }

                // (the measure of the intrinsic block size ignores the minimum and maximum block sizes: they are not transferred either)
                if (aspectRatio is { } transfer && fixedBlockSize is null && !(space.MeasuresIntrinsicBlockSize && !isOrthogonal))
                {
                    (transferredMin, transferredMax) = GetTransferredMinMax(blockSizing, transfer, toInline: true);
                }
            }

            var clamped = inlineSizing.Clamp(inlineSize, transferredMin, transferredMax);

            // https://drafts.csswg.org/css-sizing-4/#aspect-ratio-minimum: the automatic minimum size in the ratio-dependent axis
            // of a non-replaced box that is not a scroll container is its min-content size (capped by its maximum size)
            if (inlineIsRatioDependent && UsesAspectRatioAutomaticMinimum(box, layout, inlineIsHorizontal))
            {
                var minContent = inlineSizing.GetContentIntrinsicSizes().MinContent;
                if (minContent > clamped)
                {
                    clamped = Math.Max(clamped, Math.Min(minContent, inlineSizing.Resolve(boxModel.GetMaxSize(inlineIsHorizontal), SizingProperty.Maximum) ?? double.PositiveInfinity));
                }
            }
            else if (inlineIsRatioDependent && UsesAspectRatioCalcSizeMinimum(box, layout, inlineIsHorizontal))
            {
                // https://drafts.csswg.org/css-values-5/#calc-size: the size keyword of min-width: calc-size(auto, …) is that
                // automatic minimum size
                var minContent = inlineSizing.GetContentIntrinsicSizes().MinContent;
                if ((inlineSizing with { AutomaticSize = minContent, ContentSize = minContent }).Resolve(boxModel.GetMinSize(inlineIsHorizontal), SizingProperty.Minimum) is { } minimum && minimum > clamped)
                {
                    clamped = Math.Max(clamped, Math.Min(minimum, inlineSizing.Resolve(boxModel.GetMaxSize(inlineIsHorizontal), SizingProperty.Maximum) ?? double.PositiveInfinity));
                }
            }

            // The scrollbar gutters do not enlarge a box that is too small for them (they overflow it, as in the browsers)
            isAutoInlineSize &= clamped == inlineSize;
            inlineSize = Math.Max(clamped, paddingAndBorder.InlineSum());
        }

        // The auto margins of the inline-level boxes and of the floats are 0 (https://drafts.csswg.org/css2/#inline-width), the
        // ones of the absolutely positioned boxes are resolved by the positioning (PositionedLayout)
        // (a box measured for its intrinsic size contribution has no available space to distribute)
        if (!isOrthogonal && fixedInlineSize is null && box.IsBlockLevel && box.IsInFlow && !space.IsInlineSizeIndefinite && !space.AutoInlineSizeIsFitContent)
        {
            margin = ResolveAutoInlineMargins(boxModel, containingBlock, margin, space.AvailableInlineSize, inlineSize, isAutoInlineSize, justification);
            logicalMargin = writingDirection.ToLogical(margin);
        }

        var contentInlineSize = Math.Max(0, inlineSize - edges.InlineSum());

        // The intrinsic block size of a replaced element with an aspect ratio is transferred from its used inline size: the
        // intrinsic size keywords of its minimum and maximum block sizes resolve to it
        double? replacedIntrinsicBlockSize = null;
        if (box.Kind == ContainerBoxKind.Replaced && aspectRatio is { } replacedRatio && fixedBlockSize is null)
        {
            replacedIntrinsicBlockSize = replacedRatio.ToBlock(inlineSize);
            blockSizing = blockSizing with { ContentSize = replacedIntrinsicBlockSize };
            if (preferredBlockBorderBoxSize is { } preferredBlock && (boxModel.GetMinSize(blockIsHorizontal).IsContentBasedInBlockAxis || boxModel.GetMaxSize(blockIsHorizontal).IsContentBasedInBlockAxis))
            {
                specifiedBlockSize = blockSizing.Clamp(preferredBlock);
            }
        }

        // The block size computed from the inline size through the aspect ratio: it is definite when the inline size is
        // (https://drafts.csswg.org/css-sizing-4/#aspect-ratio-automatic), so the contents are laid out in it
        var blockIsRatioDependent = false;
        double? ratioBlockSize = null;
        if (specifiedBlockSize is null && aspectRatio is { } blockRatio && !(space.MeasuresContentsWithoutAspectRatio && !isOrthogonal))
        {
            blockIsRatioDependent = true;
            var (transferredMin, transferredMax) = fixedInlineSize is null ? GetTransferredMinMax(inlineSizing, blockRatio, toInline: false) : default;

            // https://drafts.csswg.org/css2/#inline-replaced-height: "If 'height' and 'width' both have computed values of 'auto'
            // and the element also has an intrinsic height, then that intrinsic height is the used value of 'height'" (the
            // ratio only applies when the constraints changed the width)
            ratioBlockSize = (inlineIsAutomatic ? GetNaturalBlockSize(box, contentInlineSize) : null) is { } naturalBlockSize
                ? naturalBlockSize + paddingAndBorder.BlockSum()
                : blockRatio.ToBlock(inlineSize);
            specifiedBlockSize = blockSizing.Clamp(ratioBlockSize.Value, transferredMin, transferredMax);
        }

        // https://quirks.spec.whatwg.org/#the-html-element-fills-the-viewport-quirk and
        // https://quirks.spec.whatwg.org/#the-body-element-fills-the-html-element-quirk: the minimum border-box block size
        var quirksFillBlockSize = specifiedBlockSize is null && context.IsQuirksMode && !isOrthogonal ? GetQuirksFillBlockSize(context, box, layout, boxModel, writingDirection, logicalMargin, in space) : null;

        double? contentBlockSize = specifiedBlockSize is { } blockSize ? Math.Max(0, blockSize - edges.BlockSum()) : null;

        // The block formatting context of the parent, when the box participates in it (decided by the parent): its block-start
        // margin collapses with the margins before it, and with the ones of its first children when it has no border or padding
        // to separate them (https://drafts.csswg.org/css2/#collapsing-margins)
        var blockFormattingContext = space.BlockFormattingContext;
        var startMarginCollapsesWithChildren = blockFormattingContext is not null && paddingAndBorder.BlockStart == 0;
        var marginTrim = BlockFormattingContext.GetMarginTrim(box);
        var content = new ContentArea(writingDirection, new LogicalOffset(edges.InlineStart, edges.BlockStart), contentInlineSize, contentBlockSize)
        {
            IsBlockSizeIndefinite = space.IsFixedBlockSizeIndefinite && !isOrthogonal && fixedBlockSize is not null,
            InlineSizeDependsOnContents = inlineDependsOnContents,
            BlockSizeGrowsToContents = blockIsRatioDependent && UsesAspectRatioAutomaticMinimum(box, layout, blockIsHorizontal),
            AnonymousPercentageBlockSize = contentBlockSize is null && !isOrthogonal && box is { Kind: ContainerBoxKind.Anonymous, IsInlineBox: false, Display.Internal: DisplayInternal.None } ? space.PercentageBlockSize : null,
            // The baseline of an inline-block whose baseline-source is auto: its last baseline, computed as CSS 2 defines it
            UsesInlineBlockBaseline = box.IsAtomicInline ? box.Display.Inside == DisplayInside.FlowRoot && layout.VerticalAlign.BaselineSource == BaselineSource.Auto : space.UsesInlineBlockBaseline,
            MeasuresIntrinsicBlockSize = space.MeasuresIntrinsicBlockSize && !isOrthogonal,
            MeasuresAutomaticBlockSize = space.MeasuresAutomaticBlockSize && !isOrthogonal,
            IsTableCellMeasure = space.MeasuresIntrinsicBlockSize && box.Display.Internal == DisplayInternal.TableCell,
            IsRestrictedTableCell = space.IsRestrictedTableCell,
            ReplacedPercentageBlockSize = GetReplacedPercentageBlockSize(context, box, layout, isOrthogonal, blockIsHorizontal, edges.BlockSum(), in space),
            BlockFormattingContext = blockFormattingContext,
            StartMarginCollapsesWithChildren = startMarginCollapsesWithChildren,
            EndMarginMayCollapseWithChildren = blockFormattingContext is not null && paddingAndBorder.BlockEnd == 0,

            // https://drafts.csswg.org/css-box-4/#margin-trim: the margins of the children adjacent to the edges that the box trims,
            // and the ones that collapse with a margin of the box that is trimmed ("It also truncates any descendant margins
            // collapsed with such a margin"): the margins of the last children collapse with the block-end margin of the box
            // when nothing separates them and its block size is automatic
            TrimsBlockStartMargins = (marginTrim & MarginTrimSides.BlockStart) != MarginTrimSides.None || space.TrimsBlockStartMargin && startMarginCollapsesWithChildren,
            TrimsBlockEndMargins = (marginTrim & MarginTrimSides.BlockEnd) != MarginTrimSides.None
                || space.TrimsBlockEndMargin && blockFormattingContext is not null && paddingAndBorder.BlockEnd == 0 && specifiedBlockSize is null && quirksFillBlockSize is null,
            OutsideMarker = space.OutsideMarker,
            Edges = edges,
            QuirksPercentageBlockSize = context.IsQuirksMode ? GetChildrenQuirksPercentageBlockSize(box, layout, isOrthogonal, contentBlockSize, quirksFillBlockSize, paddingAndBorder, in space) : null,

            // The block size that the html and the body elements get from the quirks is definite for the stretch-fit size of
            // their children (through the anonymous block boxes), as in the browsers
            StretchUsesQuirksPercentageBlockSize = contentBlockSize is null && (quirksFillBlockSize is not null
                || space.StretchUsesQuirksPercentageBlockSize && !isOrthogonal && box is { Kind: ContainerBoxKind.Anonymous, IsInlineBox: false, Display.Internal: DisplayInternal.None }),
            MinBlockSize = contentBlockSize is null ? ToContentBlockSize(blockSizing.Resolve(boxModel.GetMinSize(blockIsHorizontal), SizingProperty.Minimum)) : null,
            MaxBlockSize = contentBlockSize is null ? ToContentBlockSize(blockSizing.Resolve(boxModel.GetMaxSize(blockIsHorizontal), SizingProperty.Maximum)) : null,
        };

        // https://drafts.csswg.org/css-contain-2/#containment-size: the automatic block size of a box with size containment is
        // computed as if it had no contents, "as if its in-flow contents totaled to" contain-intrinsic-size
        // (https://drafts.csswg.org/css-sizing-4/#intrinsic-size-override): its block size is known before its contents are laid out
        double? containedBlockSize = box.Kind != ContainerBoxKind.Replaced && IntrinsicSizing.HasSizeContainment(box, inlineAxis: false)
            ? ComputeBlockSize(IntrinsicSizing.IsContainIntrinsicSizeNone(box, blockIsHorizontal) ? ComputeEmptyContentBlockSize(context, box, content) : LastRememberedSizes.GetContainIntrinsicSize(box, blockIsHorizontal))
            : null;
        if (containedBlockSize is { } knownBlockSize && contentBlockSize is null)
        {
            content = content with { ContainedBlockSize = Math.Max(0, knownBlockSize - edges.BlockSum()) };
        }

        // https://drafts.csswg.org/css-contain-3/#size-container: the size of a size container does not depend on its contents (its
        // block size is known when it is definite or contained); the styles of its descendants may depend on it
        if ((layout.ContainerType & (ContainerType.Size | ContainerType.InlineSize)) != ContainerType.Normal)
        {
            context.OnSizeContainerLaidOut(box, content.BlockSize is null && containedBlockSize is { } contained ? content with { BlockSize = Math.Max(0, contained - edges.BlockSum()) } : content);
        }

        // The box whose block-start margin collapses with the margins of its first children, unless an ancestor whose margin
        // collapses with them did it, looks them ahead (see below); it lays out its contents again when they predicted other
        // margins than the ones that collapsed (a child with clearance separated its margins, see
        // BlockFormattingContextState.Separate, or a child expected to collapse through did not): the floats of its first children
        // and the position of its contents depend on them
        var looksAheadStartMargins = startMarginCollapsesWithChildren && !space.IsStartMarginCollapsedWithAncestor;
        var stateBeforeBox = looksAheadStartMargins ? blockFormattingContext!.Save() : default;
        MarginStrut? collapsedStartMargins = null;
        double? clearance = null;
        var collapseCount = 0;
        var multiColumnFlow = blockFormattingContext?.MultiColumnFlow;
        var spannerSequence = multiColumnFlow?.SpannerSequence ?? 0;
        FragmentBuilder builder;
        ContentLayoutResult result;
        for (var attempt = 0; ; attempt++)
        {
            var startMargin = MarginStrut.FromMargin(logicalMargin.BlockStart);
            if (blockFormattingContext is not null)
            {
                // The block-start margin of the box collapses with the margins of its first children when nothing separates them:
                // they are part of its position (the floats of its first children are placed below them, and its clearance depends
                // on them), so they are looked ahead (https://drafts.csswg.org/css2/#collapsing-margins), unless an ancestor whose
                // margin collapses with them did it. Adding a margin again to the adjoining margins does not change them.
                var clearSides = ExclusionSpace.GetClearSides(layout.Clear, containingBlock);
                if (collapsedStartMargins is { } collapsed)
                {
                    startMargin = collapsed;
                }
                else if (startMarginCollapsesWithChildren && (clearSides != ClearSides.None || !space.IsStartMarginCollapsedWithAncestor))
                {
                    startMargin = startMargin.Append(BlockFormattingContext.FindMarginsCollapsingWithChildren(box, writingDirection, contentInlineSize, blockFormattingContext, content.TrimsBlockStartMargins, content.TrimsBlockEndMargins));
                }

                // https://drafts.csswg.org/css2/#clearance: the clearance inhibits the collapsing with the margins before the box; it
                // separates the margins of the box from the ones of its parent too
                if (blockFormattingContext.IsSeparated(box))
                {
                    clearance = blockFormattingContext.GetSeparatedPosition(clearSides) - blockFormattingContext.GetPositionWithZeroClearance(startMargin);
                }
                else
                {
                    clearance = blockFormattingContext.ComputeClearance(clearSides, startMargin);
                    if (clearance is not null && space.IsStartMarginCollapsedWithAncestor)
                    {
                        blockFormattingContext.Separate(box);
                    }
                }

                if (clearance is not null)
                {
                    blockFormattingContext.CollapseMargins();
                }

                blockFormattingContext.AdjoinMargin(startMargin);
                if (!startMarginCollapsesWithChildren)
                {
                    blockFormattingContext.CollapseMargins();
                }

                blockFormattingContext.AdvanceBlockPosition(paddingAndBorder.BlockStart + (clearance ?? 0));
                collapseCount = blockFormattingContext.CollapseCount;

                // The content box in the coordinates of the block formatting context, where the floats of the children are placed:
                // its line-left edge, and its block-start edge after the margins that are not collapsed yet
                var lineLeft = writingDirection.LineLeft;
                content = content with
                {
                    BlockFormattingContextOffset = new LogicalOffset(
                        space.BlockFormattingContextLineOffset + margin.Get(lineLeft) + border.Get(lineLeft) + padding.Get(lineLeft) + scrollbarSides.Get(lineLeft),
                        blockFormattingContext.BlockOffset + blockFormattingContext.CurrentMargin.Sum),
                };
            }

            builder = new FragmentBuilder(writingDirection);

            // https://drafts.csswg.org/css-contain-2/#skips-its-contents: the contents of the box are not laid out, unless a geometry API
            // needs them (they do not change the size of the box, which has size containment)
            result = box.HasUnlaidSkippedContents && box.Kind != ContainerBoxKind.Replaced ? new ContentLayoutResult(0) : LayoutContents(context, box, builder, content);
            if (!looksAheadStartMargins || attempt == MaxStartMarginRelayouts)
                break;

            // The margins that collapsed with the block-start margin of the box, compared with the ones looked ahead (with the
            // margins before the box, unless it has clearance)
            var actualStartMargin = MarginStrut.FromMargin(logicalMargin.BlockStart).Append(result.StartMargin);
            var marginsBefore = clearance is null ? stateBeforeBox.CurrentMargin : default;
            if (Math.Abs(marginsBefore.Append(startMargin).Sum - marginsBefore.Append(actualStartMargin).Sum) < 0.01)
                break;

            blockFormattingContext!.Restore(stateBeforeBox);
            if (space.OutsideMarker is { } outsideMarker)
            {
                outsideMarker.IsPlaced = false;
            }

            collapsedStartMargins = actualStartMargin;
        }

        // https://drafts.csswg.org/css2/#collapsing-margins: "The bottom margin of an in-flow block box with a 'height' of 'auto'
        // collapses with its last in-flow block-level child's bottom margin, if the box has no bottom padding, and the box has no
        // bottom border"; otherwise the margins of the last children are inside the box. As in the browsers, when the minimum
        // size makes the box taller than its contents, the margins of the last children neither collapse with the margin of
        // the box nor make it taller: they are discarded (WPT css/CSS2/normal-flow/margin-collapse-min-height-001.html,
        // min-height-separates-margin.html). A maximum size does not change the collapsing (Blink and Gecko discard the
        // margins then too, WebKit does not: WPT css/CSS2/margin-padding-clear/margin-collapse-038.xht)
        var endMarginCollapsesWithChildren = blockFormattingContext is not null && paddingAndBorder.BlockEnd == 0 && specifiedBlockSize is null && quirksFillBlockSize is null;

        // (as in Blink, the margins of the last children that overflow a box whose block size is limited into a later
        // fragmentainer than its block-end edge are in a parallel flow: they do not collapse through it, see StripRelayout)
        endMarginCollapsesWithChildren &= context.StripRelayout?.SeparatesEndMargin(box) != true;

        // The body element fills the html element minus its margins: as in the browsers, its block-start margin is the collapsed
        // one (the margins of its first children that collapse with it are outside of it)
        if (quirksFillBlockSize is not null && startMarginCollapsesWithChildren)
        {
            var collapsedStartMargin = MarginStrut.FromMargin(logicalMargin.BlockStart).Append(result.StartMargin).Sum;
            quirksFillBlockSize -= Math.Max(0, collapsedStartMargin - logicalMargin.BlockStart);
        }

        var discardsEndMargin = false;
        var contentsBlockSize = result.BlockSize;

        // https://drafts.csswg.org/css-align-3/#baseline-align-content: the space that the baseline content-alignment adds on
        // the side of the baseline is a part of the contents (it increases the intrinsic size of the box)
        var contentShimStart = space.ContentShimStart;
        contentsBlockSize += contentShimStart + space.ContentShimEnd;
        double finalBlockSize;
        if (endMarginCollapsesWithChildren && containedBlockSize is null && !IsZeroMinimumSize(layout, blockIsHorizontal, blockBasis))
        {
            finalBlockSize = ComputeBlockSize(contentsBlockSize);
            if (finalBlockSize - Math.Max(GetAutomaticBlockSize(context, box, contentsBlockSize, contentInlineSize) + edges.BlockSum(), paddingAndBorder.BlockSum()) > 0.01)
            {
                endMarginCollapsesWithChildren = false;
                discardsEndMargin = true;
            }
        }
        else
        {
            endMarginCollapsesWithChildren = endMarginCollapsesWithChildren && IsZeroMinimumSize(layout, blockIsHorizontal, blockBasis);
            if (!endMarginCollapsesWithChildren)
            {
                contentsBlockSize += result.EndMargin.Sum;
            }

            finalBlockSize = containedBlockSize ?? ComputeBlockSize(contentsBlockSize);
        }

        if (endMarginCollapsesWithChildren && result.EndMargin.Sum != 0 && context.StripRelayout is { } stripRelayout && contentsBlockSize > finalBlockSize - edges.BlockSum() + 0.01)
        {
            stripRelayout.RecordCollapsedEndMargin(box, edges.BlockStart + contentsBlockSize);
        }

        // https://drafts.csswg.org/css-multicol-1/#column-span: the column spanners of the contents interrupt a box whose block
        // size does not follow its contents: it continues after them with the block size that remains
        var spannerOverflow = 0d;
        if (multiColumnFlow is not null && !isOrthogonal)
        {
            // The content that overflows the box is in the columns before the next column spanner
            var overflows = contentsBlockSize > finalBlockSize - edges.BlockSum() + 0.01 && IsBlockOverflowVisible(box, writingDirection);
            multiColumnFlow.SetOverflowEnd(box, overflows ? content.BlockFormattingContextOffset.Block + contentsBlockSize : null);
        }

        if (multiColumnFlow is not null && multiColumnFlow.SpannerSequence != spannerSequence && !isOrthogonal
            && (specifiedBlockSize is not null || Math.Abs(finalBlockSize - edges.BlockSum() - contentsBlockSize) > 0.01))
        {
            var ownContentBlockSize = Math.Max(0, finalBlockSize - edges.BlockSum());
            finalBlockSize += multiColumnFlow.ExtendBlockSize(box, spannerSequence, content.BlockFormattingContextOffset.Block, ownContentBlockSize, paddingAndBorder.BlockEnd) - ownContentBlockSize;
            spannerOverflow = multiColumnFlow.GetSpannerOverflow(box);
        }

        // The self-alignment of an orthogonal block-level box, whose size in the inline axis of its containing block is known now
        if (isOrthogonal && justification is not null)
        {
            margin = ResolveAutoInlineMargins(boxModel, containingBlock, margin, space.AvailableInlineSize, finalBlockSize, isAutoInlineSize: false, justification);
            logicalMargin = writingDirection.ToLogical(margin);
        }

        // https://drafts.csswg.org/css-rhythm/#block-step-size: the outer block size of a block-level box in block layout is rounded
        // to a multiple of the step unit, the space is inserted in its margins, its paddings or its content box
        var blockStepContentOffset = 0d;
        var blockStepContentGrowth = 0d;
        if (!isOrthogonal && GetBlockStep(box) is { } blockStep)
        {
            var blockStart = writingDirection.BlockStart;
            var blockEnd = writingDirection.BlockEnd;
            var outerBlockSize = finalBlockSize + margin.Get(blockStart) + margin.Get(blockEnd);
            var extra = GetBlockStepExtraSpace(blockStep, outerBlockSize);
            if (extra != 0)
            {
                // (the part of the space inserted before the box or its contents)
                var beforeRatio = GetBlockStepAlignment(box, blockStep) switch
                {
                    BlockStepAlign.Start => 0,
                    BlockStepAlign.End => 1,
                    _ => 0.5,
                };
                var before = extra * beforeRatio;
                if (blockStep.Insert == BlockStepInsert.MarginBox)
                {
                    margin = margin.With(blockStart, margin.Get(blockStart) + before).With(blockEnd, margin.Get(blockEnd) + extra - before);
                    logicalMargin = writingDirection.ToLogical(margin);
                }
                else
                {
                    // The space inserted in the paddings or in the content box is on both sides of the contents (a space removed by
                    // block-step-round: down shrinks the content box)
                    if (blockStep.Insert == BlockStepInsert.PaddingBox && extra > 0)
                    {
                        padding = padding.With(blockStart, padding.Get(blockStart) + before).With(blockEnd, padding.Get(blockEnd) + extra - before);
                    }
                    else
                    {
                        extra = Math.Max(extra, -Math.Max(0, finalBlockSize - edges.BlockSum()));
                        before = extra * beforeRatio;
                        blockStepContentGrowth = extra;
                    }

                    finalBlockSize += extra;
                    blockStepContentOffset = before;
                }
            }
        }

        var collapsedMargins = CollapsedBlockMargins.None;
        if (blockFormattingContext is not null)
        {
            // The block-start and block-end margins of a box are adjoining when it "does not establish a new block formatting
            // context and that has zero computed 'min-height', zero or 'auto' computed 'height', and no in-flow children": as
            // in https://github.com/w3c/csswg-drafts/issues/8919, the used block size is checked (0 after the min/max sizes)
            var collapsedThrough = startMarginCollapsesWithChildren && result.CollapsedThrough && paddingAndBorder.BlockSum() == 0 && finalBlockSize == 0;
            var startMargin = MarginStrut.FromMargin(logicalMargin.BlockStart);
            if (startMarginCollapsesWithChildren)
            {
                startMargin = startMargin.Append(result.StartMargin);
            }

            var endMargin = MarginStrut.FromMargin(logicalMargin.BlockEnd);
            if (endMarginCollapsesWithChildren)
            {
                endMargin = endMargin.Append(result.EndMargin);
            }

            collapsedMargins = CollapsedBlockMargins.Create(startMargin, endMargin, collapsedThrough, clearance);

            // A box that does not collapse through separates its block-start margin (collapsed with the margins of its first
            // children) from the next margins, even when its contents did not (e.g. the contents are skipped)
            if (!collapsedThrough && startMarginCollapsesWithChildren && blockFormattingContext.CollapseCount == collapseCount)
            {
                blockFormattingContext.CollapseMargins();
            }

            if (discardsEndMargin)
            {
                blockFormattingContext.DiscardMargins();
            }

            // The block size of the box may differ from the size of its contents (a specified size, the min/max sizes); the
            // margins of the last children are separated from the next margins by the block-end border and padding
            blockFormattingContext.AdvanceBlockPosition(finalBlockSize - paddingAndBorder.BlockSum() - contentsBlockSize + paddingAndBorder.BlockEnd);
            if (!endMarginCollapsesWithChildren && !collapsedThrough)
            {
                blockFormattingContext.CollapseMargins();
            }

            if (spannerOverflow > 0)
            {
                // The box ends before a column spanner of its contents: the content after the box is after the spanner
                blockFormattingContext.CollapseMargins();
                blockFormattingContext.AdvanceBlockPosition(spannerOverflow);
            }

            blockFormattingContext.AdjoinMargin(logicalMargin.BlockEnd);
        }

        // https://drafts.csswg.org/css-align-3/#distribution-block: align-content aligns the contents of a block container as a unit
        var contentAlignmentOffset = GetBlockContentAlignmentOffset(context, box, finalBlockSize - edges.BlockSum() - blockStepContentGrowth, contentsBlockSize, contentInlineSize) + blockStepContentOffset;
        var alignmentOffset = contentAlignmentOffset;
        if (contentAlignmentOffset != 0 || contentShimStart != 0)
        {
            contentsBlockSize += Math.Max(0, contentAlignmentOffset);
            contentAlignmentOffset += contentShimStart;
            builder.MoveChildren(new LogicalOffset(0, contentAlignmentOffset));
        }

        var size = writingDirection.ToPhysical(new LogicalSize(inlineSize, finalBlockSize));
        var fragment = new BoxFragment(box, size, border, padding, margin, builder.Build(size))
        {
            FirstBaseline = result.FirstBaseline + edges.BlockStart + contentAlignmentOffset,
            LastBaseline = result.LastBaseline + edges.BlockStart + contentAlignmentOffset,
            CollapsedMargins = collapsedMargins,
            IsLineClampContainer = result.IsLineClampContainer,
            Scrollbars = scrollbarSides,
            ScrollbarsDisplayed = scrollbars,

            // https://drafts.csswg.org/css-overflow-3/#scrollable-overflow-calculation: the in-flow contents, which the paddings
            // extend in the scrolling area
            // (a replaced box has no in-flow contents, except the inner editor of a text control)
            ContentsBlockSize = box.Kind == ContainerBoxKind.Replaced ? result.InFlowBlockSize : result.InFlowBlockSize ?? contentsBlockSize,
            IntrinsicBlockSize = containedBlockSize ?? ratioBlockSize ?? GetAutomaticBlockSize(context, box, contentsBlockSize - Math.Max(0, alignmentOffset), contentInlineSize) + edges.BlockSum(),
        };

        if (result.InFlowInlineSize is { } inFlowInlineSize && inFlowInlineSize > contentInlineSize && fragment.IsScrollContainer)
        {
            fragment.InFlowInlineSize = inFlowInlineSize;
        }

        if ((result.InFlowStart.Inline < 0 || result.InFlowStart.Block < 0) && fragment.IsScrollContainer)
        {
            fragment.InFlowStart = result.InFlowStart;
        }

        if (builder.GapGeometry is { } gaps)
        {
            if (contentAlignmentOffset != 0)
            {
                gaps.MoveBlock(contentAlignmentOffset);
            }

            fragment.GapGeometry = gaps;
        }

        if (builder.ColumnRects is { } columnRects)
        {
            if (contentAlignmentOffset != 0)
            {
                for (var i = 0; i < columnRects.Length; i++)
                {
                    columnRects[i] = columnRects[i] with { Offset = new LogicalOffset(columnRects[i].Offset.Inline, columnRects[i].Offset.Block + contentAlignmentOffset) };
                }
            }

            fragment.ColumnRects = columnRects;
        }

        if (space.TrimsBlockStartMargin || space.TrimsBlockEndMargin)
        {
            // The used margins without the trimming: the auto margins of the inline axis are resolved, the trimmed ones restored
            var blockStartSide = containingBlock.BlockStart;
            var blockEndSide = containingBlock.BlockEnd;
            fragment.UntrimmedMargin = margin.With(blockStartSide, untrimmedMargin.Get(blockStartSide)).With(blockEndSide, untrimmedMargin.Get(blockEndSide));
        }

        // https://drafts.csswg.org/css-position-3/#relpos-insets: the relative offset (the parent adds it to the position of the
        // fragment), then the absolutely positioned boxes whose containing block is the box
        PositionedLayout.SetRelativeOffset(box, fragment, space);
        PositionedLayout.LayoutOutOfFlowDescendants(context, box, fragment, builder);
        return fragment;

        double? ToContentBlockSize(double? borderBoxSize) => borderBoxSize is { } size ? Math.Max(0, size - edges.BlockSum()) : null;

        // The used block size (border box) from the block size of the contents
        double ComputeBlockSize(double sizingContentsBlockSize)
        {
            // The automatic block size; the intrinsic size keywords of the block axis resolve to it
            // (https://drafts.csswg.org/css-sizing-3/#valdef-width-min-content: "for a box's block size, unless otherwise specified,
            // this is equivalent to its automatic size")
            var automaticBlockSize = GetAutomaticBlockSize(context, box, sizingContentsBlockSize, contentInlineSize) + edges.BlockSum();

            // (the intrinsic block sizes of a box whose block size comes from its inline size through its aspect ratio are that
            // size, https://drafts.csswg.org/css-sizing-4/#aspect-ratio: min-height: min-content does not make it as tall as its
            // contents, only the automatic minimum size does: https://github.com/w3c/csswg-drafts/issues/5032)
            var contentSizing = blockSizing with { ContentSize = blockIsRatioDependent && ratioBlockSize is { } ratioSize ? ratioSize : replacedIntrinsicBlockSize ?? (isCompressedBlockSize ? edges.BlockSum() : automaticBlockSize) };
            double usedBlockSize;
            if (specifiedBlockSize is { } definite)
            {
                usedBlockSize = definite;
                if (fixedBlockSize is null && (boxModel.GetMinSize(blockIsHorizontal).IsContentBasedInBlockAxis || boxModel.GetMaxSize(blockIsHorizontal).IsContentBasedInBlockAxis))
                {
                    usedBlockSize = contentSizing.Clamp(usedBlockSize);
                }
            }
            else
            {
                usedBlockSize = contentSizing.Clamp(contentSizing.Resolve(preferredBlockSize, SizingProperty.Preferred) ?? automaticBlockSize);
            }

            if (blockIsRatioDependent && UsesAspectRatioAutomaticMinimum(box, layout, blockIsHorizontal))
            {
                // https://drafts.csswg.org/css-sizing-4/#aspect-ratio-minimum: the box grows to contain its contents (capped by its maximum size)
                var contentsSize = sizingContentsBlockSize + paddingAndBorder.BlockSum();
                if (contentsSize > usedBlockSize)
                {
                    usedBlockSize = Math.Max(usedBlockSize, Math.Min(contentsSize, blockSizing.Resolve(boxModel.GetMaxSize(blockIsHorizontal), SizingProperty.Maximum) ?? double.PositiveInfinity));
                }
            }
            else if (blockIsRatioDependent && UsesAspectRatioCalcSizeMinimum(box, layout, blockIsHorizontal))
            {
                // The size keyword of min-height: calc-size(auto, …) is the automatic minimum size: the size of the contents
                var contentsSize = sizingContentsBlockSize + paddingAndBorder.BlockSum();
                if ((blockSizing with { ContentSize = contentsSize }).Resolve(boxModel.GetMinSize(blockIsHorizontal), SizingProperty.Minimum) is { } minimum && minimum > usedBlockSize)
                {
                    usedBlockSize = Math.Max(usedBlockSize, Math.Min(minimum, blockSizing.Resolve(boxModel.GetMaxSize(blockIsHorizontal), SizingProperty.Maximum) ?? double.PositiveInfinity));
                }
            }

            if (quirksFillBlockSize is { } fill && fill > usedBlockSize)
            {
                usedBlockSize = fill;
            }

            return Math.Max(usedBlockSize, paddingAndBorder.BlockSum());
        }
    }

    /// <summary>Lays out the contents of a box in its content box, adds their fragments to <paramref name="builder"/> and returns their block size.</summary>
    /// <param name="builder">The builder of the children of the fragment of the box: the offsets are relative to its border box (see <see cref="ContentArea.Offset"/>).</param>
    protected abstract ContentLayoutResult LayoutContents(LayoutContext context, ContainerBox box, FragmentBuilder builder, in ContentArea content);

    /// <summary>
    /// Lays out the contents of a box in a content area given by another formatting context (see <see cref="LayoutContents"/>): the
    /// content of a multi-column container laid out in a strip of the column width (see <see cref="MultiColumnFormattingContext"/>).
    /// </summary>
    internal ContentLayoutResult LayoutFlow(LayoutContext context, ContainerBox box, FragmentBuilder builder, in ContentArea content) => LayoutContents(context, box, builder, content);

    /// <summary>
    /// Returns the border-box inline size of a box whose inline size is <c>auto</c>: by default, it fills the available space
    /// (https://drafts.csswg.org/css2/#blockwidth), the available inline size minus the margins. The <c>auto</c> inline size of
    /// the floats, the atomic inlines, the absolutely positioned boxes, the outside markers and the buttons
    /// (<see cref="BlockFormattingContext.UsesShrinkToFit"/>), and of the orthogonal flows
    /// (https://drafts.csswg.org/css-writing-modes-4/#auto-multicol), is their fit-content size (the shrink-to-fit size of CSS 2,
    /// https://drafts.csswg.org/css2/#shrink-to-fit-float): "min(max(preferred minimum width, available width), preferred width)".
    /// </summary>
    /// <param name="context">The layout pass.</param>
    /// <param name="box">The box.</param>
    /// <param name="availableSize">The available inline size minus the margins of the box.</param>
    /// <param name="paddingAndBorder">The paddings, borders and scrollbar gutters of the inline axis.</param>
    /// <param name="input">The input of the intrinsic sizes (the block size of the content box when it is definite).</param>
    /// <param name="isFitContent">
    /// Whether the size is the fit-content size whatever the box: the box establishes an orthogonal flow
    /// (https://drafts.csswg.org/css-writing-modes-4/#orthogonal-flows), or its parent asks for it (<see cref="ConstraintSpace.AutoInlineSizeIsFitContent"/>).
    /// </param>
    protected virtual double GetAutomaticInlineSize(LayoutContext context, ContainerBox box, double availableSize, double paddingAndBorder, in IntrinsicSizingInput input, bool isFitContent)
    {
        if (!isFitContent && !BlockFormattingContext.UsesShrinkToFit(box))
            return Math.Max(availableSize, paddingAndBorder);

        var intrinsic = IntrinsicSizing.GetIntrinsicInlineSizesForInput(context, box, input);
        return intrinsic.FitContent(Math.Max(0, availableSize - paddingAndBorder)) + paddingAndBorder;
    }

    /// <summary>
    /// Computes the min-content and max-content inline sizes of the content box of a box (https://drafts.csswg.org/css-sizing-3/#intrinsic-sizes)
    /// laid out by this formatting context. Called by <see cref="IntrinsicSizing.GetIntrinsicInlineSizes"/>, which caches them
    /// for the layout pass and handles size containment and the skipped contents. The flex, grid and table (#354) layouts override
    /// it with their own algorithms, using <see cref="IntrinsicSizing.GetContribution"/> for their items.
    /// </summary>
    protected internal abstract IntrinsicSizes ComputeIntrinsicInlineSizes(LayoutContext context, ContainerBox box, in IntrinsicSizingInput input);

    /// <summary>
    /// Returns the min-content and max-content inline sizes of the content box of a box with size containment whose
    /// <c>contain-intrinsic-size</c> is <c>none</c>: "as if the element had no content" (https://drafts.csswg.org/css-contain-2/#containment-size).
    /// By default 0; a grid container keeps its explicit tracks.
    /// </summary>
    protected internal virtual IntrinsicSizes ComputeEmptyIntrinsicInlineSizes(LayoutContext context, ContainerBox box, double? contentBlockSize) => default;

    /// <summary>
    /// Returns the automatic block size of the content box of a box with size containment whose <c>contain-intrinsic-size</c> is
    /// <c>none</c> in the block axis (see <see cref="ComputeEmptyIntrinsicInlineSizes"/>): by default 0.
    /// </summary>
    protected internal virtual double ComputeEmptyContentBlockSize(LayoutContext context, ContainerBox box, in ContentArea content) => 0;

    /// <summary>
    /// Returns the border-box stretch-fit sizes of a box in its inline and block axes (https://drafts.csswg.org/css-sizing-4/#stretch-fit-sizing),
    /// or null in an axis where the size of the containing block is indefinite (<c>stretch</c> then behaves as <c>auto</c>): the
    /// available size (the containing block, the space between the floats, or the inset-modified containing block of an
    /// absolutely positioned box) minus the margins (the <c>auto</c> margins are zero). In the block axis of the containing
    /// block, the margins that would collapse with the margins of the parent are treated as zero. In quirks mode, the block size
    /// of the html and body elements that fill the viewport is definite (<see cref="ConstraintSpace.StretchUsesQuirksPercentageBlockSize"/>).
    /// </summary>
    private static (double? Inline, double? Block) GetStretchFitSizes(bool isOrthogonal, Sides<double> margin, LogicalSides<double> paddingAndBorder, in ConstraintSpace space)
    {
        var containingBlockMargin = space.WritingDirection.ToLogical(margin);
        double? inlineAxis = space.IsInlineSizeIndefinite ? null : space.AvailableInlineSize - containingBlockMargin.InlineSum();
        var blockBasis = space.PercentageBlockSize ?? (space.StretchUsesQuirksPercentageBlockSize ? space.QuirksPercentageBlockSize : null);
        double? blockAxis = blockBasis is null
            ? null
            : (space.AvailableBlockSize ?? blockBasis.Value)
                - (space.StretchIgnoresBlockStartMargin ? 0 : containingBlockMargin.BlockStart) - (space.StretchIgnoresBlockEndMargin ? 0 : containingBlockMargin.BlockEnd);

        var (inline, block) = isOrthogonal ? (blockAxis, inlineAxis) : (inlineAxis, blockAxis);
        return (inline is { } inlineSize ? Math.Max(inlineSize, paddingAndBorder.InlineSum()) : null, block is { } blockSize ? Math.Max(blockSize, paddingAndBorder.BlockSum()) : null);
    }

    /// <summary>
    /// Returns the natural content-box block size of a box with an aspect ratio whose sizes are both <c>auto</c>, used instead of
    /// the size transferred through the ratio: the natural height of a replaced element whose used inline size is its natural
    /// inline size, else null (the other boxes have no natural size).
    /// </summary>
    protected virtual double? GetNaturalBlockSize(ContainerBox box, double contentInlineSize) => null;

    /// <summary>Returns the content-box block size of a box whose block size is <c>auto</c>: by default, the block size of its contents (https://drafts.csswg.org/css2/#normal-block).</summary>
    protected virtual double GetAutomaticBlockSize(LayoutContext context, ContainerBox box, double contentsBlockSize, double contentInlineSize) => contentsBlockSize;

    /// <summary>Returns whether the minimum size of a box in a physical axis is 0 (<c>auto</c>, 0, or a percentage of an indefinite size, which resolves against zero).</summary>
    private static bool IsZeroMinimumSize(LayoutStyle style, bool horizontal, double? percentageBasis)
    {
        // The intrinsic size keywords and stretch are treated as zero, like a percentage of an indefinite size (their size is
        // only known once the contents are laid out)
        var min = style.Box.GetMinSize(horizontal);
        return min.IsAuto || min.TryResolveMinimum(percentageBasis) is not > 0;
    }

    /// <summary>
    /// Returns whether a percentage <c>height</c> of a box resolves against <see cref="ConstraintSpace.QuirksPercentageBlockSize"/>
    /// when the height of its containing block is indefinite (https://quirks.spec.whatwg.org/#the-percentage-height-calculation-quirk):
    /// "if the computed value of the position property of element is relative, static, or sticky, the specified value for the
    /// height property of element is a &lt;percentage&gt;, and element does not have a computed value of the display property
    /// that is table-row, table-row-group, table-header-group, table-footer-group, table-cell or table-caption".
    /// </summary>
    internal static bool UsesQuirksPercentageHeight(ContainerBox box, LayoutStyle style, Size height)
    {
        if (!style.IsHorizontalWritingMode || !height.IsLengthPercentage || height.Value.Calc is not null || !height.Value.HasPercentage || height.Value.Length != 0)
            return false;

        return !style.IsAbsolutelyPositioned && box.Display.Internal is not (DisplayInternal.TableRow or DisplayInternal.TableRowGroup or DisplayInternal.TableHeaderGroup
            or DisplayInternal.TableFooterGroup or DisplayInternal.TableCell or DisplayInternal.TableCaption);
    }

    /// <summary>
    /// Returns, in quirks mode, the basis of the percentage heights of the children of a box when its block size is
    /// indefinite (https://quirks.spec.whatwg.org/#the-percentage-height-calculation-quirk): the steps of the algorithm skip the
    /// <c>auto</c> height block containers (the basis of their parent is passed through), and return the other containing
    /// blocks (their height, null when it is indefinite). The html and body elements that fill the viewport (see
    /// <see cref="GetQuirksFillBlockSize"/>) have a definite height for their children, as in the browsers.
    /// </summary>
    private static double? GetChildrenQuirksPercentageBlockSize(ContainerBox box, LayoutStyle style, bool isOrthogonal, double? contentBlockSize, double? quirksFillBlockSize, LogicalSides<double> paddingAndBorder, in ConstraintSpace space)
    {
        // "If element has a computed value of the display property that is table-cell, then return a UA-defined value": as in the
        // browsers, a cell gives no basis to the descendants of its children (the percentages of its children resolve against
        // its block size when it is definite: WPT css/css-tables/percentages-grandchildren-quirks-mode-001.html)
        if (box.Display.Internal == DisplayInternal.TableCell)
            return null;

        // (a block size imposed by the parent that is indefinite for the contents, the main size of an item of a column flex
        // container whose main size is indefinite, is not a height the percentages resolve against: the algorithm continues)
        var isBlockSizeIndefinite = space.IsFixedBlockSizeIndefinite && !isOrthogonal && space.FixedBlockSize is not null;
        if (contentBlockSize is not null && !isBlockSizeIndefinite)
            return contentBlockSize;

        if (quirksFillBlockSize is { } fill)
            return Math.Max(0, fill - paddingAndBorder.BlockSum());

        // "If element has a computed value of the display property that is table-cell, then return a UA-defined value. If
        // element has a computed value of the height property that is not auto, then return element. If element has a
        // computed value of the position property that is absolute, or if element is a not a block container or a table
        // wrapper box, then return element."
        if (isOrthogonal || !PassesQuirksPercentageBlockSize(box, style))
            return null;

        return space.QuirksPercentageBlockSize;
    }

    /// <summary>
    /// Returns the basis of the percentage block sizes of the replaced children of a box (see <see cref="ConstraintSpace.ReplacedPercentageBlockSize"/>):
    /// the content-box block size that the height of a table cell gives when it is a length (with the table cell height box
    /// sizing quirk, https://quirks.spec.whatwg.org/#the-table-cell-height-box-sizing-quirk), the basis of its parent for an
    /// anonymous block box, else null.
    /// </summary>
    private static double? GetReplacedPercentageBlockSize(LayoutContext context, ContainerBox box, LayoutStyle style, bool isOrthogonal, bool blockIsHorizontal, double blockEdges, in ConstraintSpace space)
    {
        if (isOrthogonal)
            return null;

        if (box.Display.Internal == DisplayInternal.TableCell)
        {
            if (style.Box.GetSize(blockIsHorizontal) is not { IsLengthPercentage: true, Value.IsFixed: true } height)
                return null;

            return style.BoxSizing == BoxSizing.BorderBox || context.IsQuirksMode ? Math.Max(0, height.Value.Length - blockEdges) : Math.Max(0, height.Value.Length);
        }

        return box is { Kind: ContainerBoxKind.Anonymous, IsInlineBox: false, Display.Internal: DisplayInternal.None } ? space.ReplacedPercentageBlockSize : null;
    }

    /// <summary>
    /// Returns whether a box whose block size is indefinite passes the basis of the percentage heights of the quirks mode to its
    /// children (see <see cref="GetChildrenQuirksPercentageBlockSize"/>): an <c>auto</c> height block container that is not a
    /// table cell nor absolutely positioned.
    /// </summary>
    internal static bool PassesQuirksPercentageBlockSize(ContainerBox box, LayoutStyle style)
        => box.Display.Internal != DisplayInternal.TableCell && style.IsHorizontalWritingMode && style.Box.Height.IsAuto && !style.IsAbsolutelyPositioned && box.IsBlockContainer;

    /// <summary>
    /// Returns, in quirks mode, the minimum border-box block size of the root html element
    /// (https://quirks.spec.whatwg.org/#the-html-element-fills-the-viewport-quirk: the initial containing block minus its
    /// margins) or of the body element (https://quirks.spec.whatwg.org/#the-body-element-fills-the-html-element-quirk: the
    /// content box of the html element minus its margins), or null when the quirks do not apply to the box. The sizes are the
    /// ones of the block flow direction of the box (its width in a vertical writing mode).
    /// </summary>
    private static double? GetQuirksFillBlockSize(LayoutContext context, ContainerBox box, LayoutStyle style, BoxModelStyle boxModel, WritingDirection writingDirection, LogicalSides<double> margin, in ConstraintSpace space)
    {
        // The quirks apply to the size in the block flow direction: "the computed value of the width property of element is auto
        // when element has a vertical writing mode, otherwise, the computed value of the height property of element is auto"
        var blockIsHorizontal = !writingDirection.IsHorizontal;
        if (box.PseudoElement is not null || box.Element is not { } element || !boxModel.GetSize(blockIsHorizontal).IsAuto || style.IsAbsolutelyPositioned || style.IsFloating)
            return null;

        if (element.IsHtml(TagId.Html) && style.Style.IsRoot)
            return (blockIsHorizontal ? context.InitialContainingBlockSize.Width : context.InitialContainingBlockSize.Height) - margin.BlockSum();

        // The body element of the document (the body child of the html root element), block-level
        if (element.IsHtml(TagId.Body) && element.ParentNode is Element { ParentNode: Document } parent && parent.IsHtml(TagId.Html) && box.IsBlockLevel
            && (space.PercentageBlockSize ?? space.QuirksPercentageBlockSize) is { } parentContentBlockSize)
        {
            return parentContentBlockSize - margin.BlockSum();
        }

        return null;
    }

    /// <summary>Returns the used paddings of a box (https://drafts.csswg.org/css-box-4/#paddings).</summary>
    public static Sides<double> ResolvePadding(BoxModelStyle box, double percentageBasis) => new(
        Math.Max(0, box.Padding.Top.Resolve(percentageBasis)),
        Math.Max(0, box.Padding.Right.Resolve(percentageBasis)),
        Math.Max(0, box.Padding.Bottom.Resolve(percentageBasis)),
        Math.Max(0, box.Padding.Left.Resolve(percentageBasis)));

    /// <summary>Returns the margins of a box, with the <c>auto</c> margins as 0 (https://drafts.csswg.org/css-box-4/#margins).</summary>
    public static Sides<double> ResolveMargins(BoxModelStyle box, double percentageBasis) => new(
        box.Margin.Top.Resolve(percentageBasis),
        box.Margin.Right.Resolve(percentageBasis),
        box.Margin.Bottom.Resolve(percentageBasis),
        box.Margin.Left.Resolve(percentageBasis));

    /// <summary>
    /// Returns the border-box size of a preferred size property (<c>width</c>, <c>height</c>) when it is a length or a percentage
    /// of a definite basis (https://drafts.csswg.org/css-sizing-3/#box-sizing), else null (<c>auto</c>, a percentage of an
    /// indefinite size, a keyword: see <see cref="AxisSizing"/> for the resolution of all the sizing values).
    /// </summary>
    public static double? ResolveBorderBoxSize(LayoutStyle style, Size size, double? percentageBasis, double paddingAndBorder)
    {
        if (size.TryResolve(percentageBasis) is not { } value)
            return null;

        return style.BoxSizing == BoxSizing.BorderBox ? Math.Max(value, paddingAndBorder) : Math.Max(0, value) + paddingAndBorder;
    }

    /// <summary>
    /// Returns the preferred aspect ratio of a box (https://drafts.csswg.org/css-sizing-4/#aspect-ratio) as a transfer between
    /// its border-box inline and block sizes, or null when it has none: the <c>aspect-ratio</c> property, or the natural aspect
    /// ratio of a replaced element (<see cref="GetNaturalAspectRatio"/>) when it is <c>auto</c>. The inline boxes and the
    /// internal table boxes have no preferred aspect ratio.
    /// </summary>
    private AspectRatioTransfer? GetPreferredAspectRatio(LayoutContext context, ContainerBox box, bool inlineIsHorizontal, LogicalSides<double> paddingAndBorder)
    {
        if (!HasPreferredAspectRatio(box))
            return null;

        var property = box.Style.Layout.Box.AspectRatio;
        return GetPreferredAspectRatio(box, inlineIsHorizontal, paddingAndBorder, property.IsAuto || !property.HasRatio ? GetNaturalAspectRatio(context, box) : null);
    }

    /// <summary>Returns whether a box may have a preferred aspect ratio: the inline boxes and the internal table boxes have none.</summary>
    private static bool HasPreferredAspectRatio(ContainerBox box) => !box.IsInlineBox && box.Kind is not (ContainerBoxKind.Viewport or ContainerBoxKind.TableGrid) && box.Display.Internal == DisplayInternal.None;

    /// <summary>
    /// Returns the preferred aspect ratio of a box (see <see cref="GetPreferredAspectRatio(LayoutContext, ContainerBox, bool, LogicalSides{double})"/>)
    /// given the natural aspect ratio (width / height) of its contents, or null when it has none.
    /// </summary>
    internal static AspectRatioTransfer? GetPreferredAspectRatio(ContainerBox box, bool inlineIsHorizontal, LogicalSides<double> paddingAndBorder, double? naturalRatio)
    {
        if (!HasPreferredAspectRatio(box))
            return null;

        var layout = box.Style.Layout;
        var property = layout.Box.AspectRatio;
        double ratio;
        bool appliesToContentBox;

        // "auto: Replaced elements with a natural aspect ratio use that aspect ratio [...] auto && <ratio>: [...] unless it is a
        // replaced element with a natural aspect ratio, in which case that aspect ratio is used instead. In all cases, size
        // calculations involving the aspect ratio work with the content box dimensions always." A degenerate ratio behaves as auto.
        if ((property.IsAuto || !property.HasRatio) && naturalRatio is { } natural)
        {
            ratio = natural;
            appliesToContentBox = true;
        }
        else if (property.Ratio is { } specified)
        {
            ratio = specified;

            // "<ratio>: [...] work with the dimensions of the box specified by box-sizing"
            appliesToContentBox = property.IsAuto || layout.BoxSizing == BoxSizing.ContentBox;
        }
        else
        {
            return null;
        }

        if (!(ratio > 0) || double.IsInfinity(ratio))
            return null;

        return new AspectRatioTransfer(
            inlineIsHorizontal ? ratio : 1 / ratio,
            appliesToContentBox ? paddingAndBorder.InlineSum() : 0,
            appliesToContentBox ? paddingAndBorder.BlockSum() : 0);
    }

    /// <summary>Returns the natural aspect ratio (width / height) of the contents of a replaced box, or null (the other boxes have none).</summary>
    protected virtual double? GetNaturalAspectRatio(LayoutContext context, ContainerBox box) => null;

    /// <summary>
    /// Returns the definite minimum and maximum sizes of an axis (the origin axis) transferred to the other axis through the
    /// aspect ratio (https://drafts.csswg.org/css-sizing-4/#aspect-ratio-size-transfers), as border-box sizes.
    /// </summary>
    internal static (double? Min, double? Max) GetTransferredMinMax(in AxisSizing origin, AspectRatioTransfer ratio, bool toInline)
    {
        var box = origin.Box.Style.Layout.Box;
        var min = origin.Resolve(box.GetMinSize(origin.Horizontal), SizingProperty.Minimum);
        var max = origin.Resolve(box.GetMaxSize(origin.Horizontal), SizingProperty.Maximum);
        return toInline
            ? (min is { } a ? ratio.ToInline(a) : null, max is { } b ? ratio.ToInline(b) : null)
            : (min is { } c ? ratio.ToBlock(c) : null, max is { } d ? ratio.ToBlock(d) : null);
    }

    /// <summary>
    /// Returns whether the automatic minimum size of a box in the ratio-dependent axis is its min-content size
    /// (https://drafts.csswg.org/css-sizing-4/#aspect-ratio-minimum): its minimum size is <c>auto</c>, and it is neither a
    /// replaced element nor a scroll container.
    /// </summary>
    private static bool UsesAspectRatioAutomaticMinimum(ContainerBox box, LayoutStyle style, bool horizontal)
        => box.Kind != ContainerBoxKind.Replaced && style.Box.GetMinSize(horizontal).IsAuto && (horizontal ? style.OverflowX : style.OverflowY) is OverflowKind.Visible or OverflowKind.Clip;

    /// <summary>Whether the minimum size of a box is a <c>calc-size()</c> of the automatic minimum size of its aspect ratio (see <see cref="UsesAspectRatioAutomaticMinimum"/>).</summary>
    private static bool UsesAspectRatioCalcSizeMinimum(ContainerBox box, LayoutStyle style, bool horizontal)
        => box.Kind != ContainerBoxKind.Replaced && style.Box.GetMinSize(horizontal) is { Kind: SizeKind.CalcSize, BehavesAs: SizeKind.Auto } && (horizontal ? style.OverflowX : style.OverflowY) is OverflowKind.Visible or OverflowKind.Clip;

    /// <summary>
    /// Returns the offset of the contents of a block container aligned by <c>align-content</c> in its content box
    /// (https://drafts.csswg.org/css-align-3/#distribution-block): the alignment subject is "the entire contents of the block, as
    /// a unit" (its automatic block size, which includes the floats of a block formatting context root); a content distribution
    /// uses its fallback alignment; "if no overflow-position is specified, and the block container is not a block-axis scroll
    /// container, then alignment is safe". The table cells are aligned by the table layout (with their <c>vertical-align</c>).
    /// </summary>
    /// <remarks>
    /// The overflowing contents of a block-axis scroll container are start-aligned by default too (the spec makes their default
    /// overflow alignment unsafe but extends the scrollable overflow area so that they can be scrolled to their start-aligned
    /// position, https://drafts.csswg.org/css-align-3/#overflow-scroll-position, which is not implemented).
    /// </remarks>
    private protected double GetBlockContentAlignmentOffset(LayoutContext context, ContainerBox box, double contentBoxBlockSize, double contentsBlockSize, double contentInlineSize)
    {
        // https://drafts.csswg.org/css-align-3/#align-justify-content: "Applies to: block containers, multicol containers, flex
        // containers, and grid containers" (the computed value is read first: the alignment group is not needed for normal); in a
        // multi-column container, the alignment subject is the column boxes (https://drafts.csswg.org/css-align-3/#distribution-multicol)
        if (this is not (BlockFormattingContext or InlineFormattingContext or MultiColumnFormattingContext) || box.Kind != ContainerBoxKind.Normal || box.SkipsContents
            || box.Display.Inside is not (DisplayInside.Flow or DisplayInside.FlowRoot) || box.Display.Internal is not (DisplayInternal.None or DisplayInternal.TableCaption)
            || box.Style.GetComputedValue("align-content") == "normal")
        {
            return 0;
        }

        var layout = box.Style.Layout;
        var value = layout.Alignment.AlignContent;
        var axis = AlignmentAxis.Block(WritingDirection.FromStyle(layout));
        var alignment = BoxAlignment.ResolveContentAlignment(value.Keyword, axis, AxisAlignment.Start);
        var freeSpace = contentBoxBlockSize - GetAutomaticBlockSize(context, box, contentsBlockSize, contentInlineSize);
        var safe = value.Overflow != OverflowAlignment.Unsafe;
        var offset = BoxAlignment.Distribute(alignment, freeSpace, count: 1, safe).Offset;
        if (offset != 0 && context.StripRelayout is { } stripRelayout)
        {
            // The alignment of the contents of a box that is fragmented is not defined: the contents of a box that is broken
            // are not aligned (one of the behaviors that the WPT tests accept), see StripRelayout
            if (stripRelayout.IsUnaligned(box))
                return 0;

            stripRelayout.RecordAlignedBox(box);
        }

        return offset;
    }

    /// <summary>
    /// Returns the self-alignment of a block-level box in normal flow (https://drafts.csswg.org/css-align-3/#justify-block), or
    /// null when it is <c>normal</c>: <c>auto</c> is the <c>justify-items</c> of the parent box; the anonymous boxes, the
    /// internal table boxes and the children of the other formatting contexts are not aligned this way.
    /// </summary>
    internal static BlockJustification? GetBlockJustification(ContainerBox box, WritingDirection containingBlock)
    {
        if (box.IsAnonymous || box.Parent is not { } parent || parent.EstablishesIndependentChildrenLayout || parent.Kind == ContainerBoxKind.TableWrapper
            || box.Kind == ContainerBoxKind.TableGrid || box.Display.Internal != DisplayInternal.None)
        {
            return null;
        }

        // The computed values are read first: the alignment groups of the styles are not needed for the default alignment
        var parentStyle = parent.Kind == ContainerBoxKind.Viewport ? null : parent.Style;
        var justifySelf = box.Style.GetComputedValue("justify-self");
        if (justifySelf == "normal" || justifySelf == "auto" && (parentStyle is null || parentStyle.GetComputedValue("justify-items") is "normal" or "legacy"))
            return GetDescendantAlignment(box, containingBlock);

        // The parent box of a block-level box split out of an inline box is the inline box, whose justify-items does not apply
        // (https://drafts.csswg.org/css-align-3/#valdef-justify-self-auto): auto behaves as normal
        if (justifySelf == "auto" && box.SplitInlineParent is not null)
            return null;

        var layout = box.Style.Layout;
        var value = BoxAlignment.GetJustifySelf(layout, parentStyle?.Layout);
        var axis = AlignmentAxis.Inline(containingBlock);
        if (value.Keyword is AlignmentKeyword.Normal or AlignmentKeyword.Auto or AlignmentKeyword.Legacy)
            return null;

        // "Values other than stretch cause a width/height of auto to be treated as fit-content"; the baseline values take their
        // fallback alignment; the default overflow alignment is unsafe (https://drafts.csswg.org/css-align-3/#auto-safety-default)
        var alignment = BoxAlignment.GetBaselineFallback(BoxAlignment.ResolveSelfAlignment(value.Keyword, axis, WritingDirection.FromStyle(layout), AxisAlignment.Start), axis);
        return new BlockJustification(alignment, value.Overflow == OverflowAlignment.Safe, IsFitContent: value.Keyword != AlignmentKeyword.Stretch);
    }

    /// <summary>
    /// Returns the alignment of a block-level box in normal flow by an ancestor that aligns its descendants (the <c>center</c>
    /// element, the <c>align</c> attribute of a <c>div</c>: https://html.spec.whatwg.org/multipage/rendering.html#align-descendants),
    /// or null: "the user agent is expected to align only those descendants that have both their 'margin-inline-start' and
    /// 'margin-inline-end' properties computing to a value other than 'auto', that are over-constrained and that have one of
    /// those two margins with a used value forced to a greater value [...] Aligned elements are expected to be aligned by having
    /// the used values of their margins on the line-left and line-right sides be set accordingly" (the box is aligned as with a
    /// safe self-alignment, see <see cref="ResolveAutoInlineMargins"/>). As in the browsers, an element with an align attribute
    /// of its own is aligned by its ancestors too, and the rendered legend of a fieldset is not aligned this way (WPT
    /// html/rendering/non-replaced-elements/the-fieldset-and-legend-elements/legend-align.html).
    /// </summary>
    private static BlockJustification? GetDescendantAlignment(ContainerBox box, WritingDirection containingBlock)
    {
        var alignment = box.Style.Parent?.Layout.DescendantAlignment ?? DescendantAlignment.None;
        if (alignment == DescendantAlignment.None || BlockFormattingContext.IsRenderedLegend(box))
            return null;

        // The line-left side is the inline-start side of a left-to-right containing block
        var axisAlignment = alignment switch
        {
            DescendantAlignment.Center => AxisAlignment.Center,
            DescendantAlignment.Left => containingBlock.IsRightToLeft ? AxisAlignment.End : AxisAlignment.Start,
            _ => containingBlock.IsRightToLeft ? AxisAlignment.Start : AxisAlignment.End,
        };

        return new BlockJustification(axisAlignment, Safe: true, IsFitContent: false);
    }

    /// <summary>
    /// Resolves the <c>auto</c> inline margins of a block-level box in normal flow (https://drafts.csswg.org/css2/#blockwidth):
    /// two <c>auto</c> margins center the box, one takes the remaining space; when the box is over-constrained, the inline-end
    /// margin (in the direction of the containing block) is ignored and computed from the other values. Without <c>auto</c>
    /// margin, a box with a self-alignment (<paramref name="justification"/>) is aligned in its containing block instead
    /// (https://drafts.csswg.org/css-align-3/#justify-block: "the rules for over-constrained computations in section 10.3.3 are
    /// ignored in favor of alignment"): its inline-start margin is increased by the alignment offset.
    /// </summary>
    internal static Sides<double> ResolveAutoInlineMargins(BoxModelStyle box, WritingDirection containingBlock, Sides<double> margin, double availableInlineSize, double inlineSize, bool isAutoInlineSize, BlockJustification? justification = null)
    {
        if (isAutoInlineSize)
            return margin;

        var startSide = containingBlock.InlineStart;
        var endSide = containingBlock.InlineEnd;
        var startIsAuto = GetMargin(box.Margin, startSide).IsAuto;
        var endIsAuto = GetMargin(box.Margin, endSide).IsAuto;
        var start = margin.Get(startSide);
        var end = margin.Get(endSide);
        var remaining = availableInlineSize - inlineSize - start - end;
        if (justification is { } alignment && !startIsAuto && !endIsAuto)
        {
            // "auto margins, because they effectively adjust the size of the margin area, take precedence over justify-self"
            start += BoxAlignment.GetOffset(alignment.Alignment, remaining, alignment.Safe);
            end = availableInlineSize - inlineSize - start;
        }
        else if (remaining < 0)
        {
            // The auto margins are treated as zero, and the end margin absorbs the difference
            start = startIsAuto ? 0 : start;
            end = availableInlineSize - inlineSize - start;
        }
        else if (startIsAuto && endIsAuto)
        {
            start = remaining / 2;
            end = remaining / 2;
        }
        else if (startIsAuto)
        {
            start = remaining;
        }
        else
        {
            end = availableInlineSize - inlineSize - start;
        }

        return SetMargin(SetMargin(margin, startSide, start), endSide, end);

        static LengthPercentageAuto GetMargin(Sides<LengthPercentageAuto> margins, PhysicalSide side) => margins.Get(side);

        static Sides<double> SetMargin(Sides<double> margins, PhysicalSide side, double value) => side switch
        {
            PhysicalSide.Top => margins with { Top = value },
            PhysicalSide.Right => margins with { Right = value },
            PhysicalSide.Bottom => margins with { Bottom = value },
            _ => margins with { Left = value },
        };
    }
}

/// <summary>The self-alignment of a block-level box in normal flow in the inline axis of its containing block (https://drafts.csswg.org/css-align-3/#justify-block).</summary>
/// <param name="Alignment">The alignment from the inline-start edge of the containing block.</param>
/// <param name="Safe">Whether an overflowing box is start-aligned (<c>safe</c>).</param>
/// <param name="IsFitContent">Whether the automatic inline size of the box is its fit-content size instead of its stretch-fit size.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct BlockJustification(AxisAlignment Alignment, bool Safe, bool IsFitContent);
