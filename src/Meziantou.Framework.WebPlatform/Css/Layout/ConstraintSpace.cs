using System.Runtime.InteropServices;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The input of the layout of a box, given by the formatting context of its parent: the containing block
/// (https://drafts.csswg.org/css-display/#containing-block) and the available space
/// (https://drafts.csswg.org/css-sizing-3/#available). The logical sizes are in the writing mode of the containing block
/// (<see cref="WritingDirection"/>), which is not the one of the box in an orthogonal flow.
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct ConstraintSpace
{
    /// <summary>The writing mode and the direction of the containing block: the axes of the logical sizes of the space.</summary>
    public WritingDirection WritingDirection { get; init; }

    /// <summary>The available inline size (https://drafts.csswg.org/css-sizing-3/#available): for a block-level box in normal flow, the inline size of its containing block.</summary>
    public double AvailableInlineSize { get; init; }

    /// <summary>The available block size, or null when it is indefinite.</summary>
    public double? AvailableBlockSize { get; init; }

    /// <summary>
    /// The fallback of the available inline space of an orthogonal flow when <see cref="AvailableBlockSize"/> is indefinite, given by
    /// the containing block (its fixed maximum block size, https://drafts.csswg.org/css-writing-modes-4/#orthogonal-auto), or null:
    /// the size of the initial containing block is the other limit.
    /// </summary>
    public double? OrthogonalFallbackInlineSize { get; init; }

    /// <summary>The inline size of the containing block, the basis of the percentages of the margins, the paddings and of the size in that axis.</summary>
    public double PercentageInlineSize { get; init; }

    /// <summary>The block size of the containing block when it is definite (https://drafts.csswg.org/css-sizing-3/#definite), else null: the percentages of the size in that axis then behave as <c>auto</c>.</summary>
    public double? PercentageBlockSize { get; init; }

    /// <summary>
    /// Whether the inline size of the containing block is indefinite, because the box is measured for its intrinsic size
    /// contribution to it (https://drafts.csswg.org/css-sizing-3/#cyclic-percentage-contribution, see
    /// <see cref="IntrinsicSizing.GetContribution"/>): the percentages of the sizes in that axis and <c>stretch</c> behave as
    /// <c>auto</c> (<see cref="GetPercentageBasis"/> returns null); the margins and paddings resolve against
    /// <see cref="PercentageInlineSize"/> (0).
    /// </summary>
    public bool IsInlineSizeIndefinite { get; init; }

    /// <summary>
    /// Whether the block-start margin of the box is treated as zero for its stretch-fit block size
    /// (https://drafts.csswg.org/css-sizing-4/#stretch-fit-sizing): "if the ancestor element percentages resolve against does not
    /// have a block-start border or padding and is not an independent formatting context, treat the element's block-start
    /// margin as zero for the purpose of calculating this size" (it simulates the collapsing of the margins with the parent).
    /// </summary>
    public bool StretchIgnoresBlockStartMargin { get; init; }

    /// <summary>Whether the block-end margin of the box is treated as zero for its stretch-fit block size (see <see cref="StretchIgnoresBlockStartMargin"/>).</summary>
    public bool StretchIgnoresBlockEndMargin { get; init; }

    /// <summary>The inline size of the border box imposed by the parent formatting context (e.g. a stretched flex or grid item, a table cell), or null.</summary>
    public double? FixedInlineSize { get; init; }

    /// <summary>The block size of the border box imposed by the parent formatting context, or null.</summary>
    public double? FixedBlockSize { get; init; }

    /// <summary>
    /// Whether <see cref="FixedBlockSize"/> is the size of the box but is indefinite for its contents: the percentage block sizes
    /// of its descendants behave as <c>auto</c>. The main size of a flex item of a column flex container whose main size is
    /// indefinite (https://drafts.csswg.org/css-flexbox-1/#definite-sizes: the post-flexing main size is definite only when the
    /// container has a definite main size).
    /// </summary>
    public bool IsFixedBlockSizeIndefinite { get; init; }

    /// <summary>
    /// Whether the box is laid out to measure its intrinsic block size (<see cref="BoxFragment.IntrinsicBlockSize"/>): its
    /// preferred block size behaves as <c>auto</c>, so the percentage block sizes of its contents behave as <c>auto</c> too (the
    /// min-content and max-content block sizes, https://drafts.csswg.org/css-sizing-3/#min-content-block-size, e.g. the content
    /// size suggestion of a flex item, https://drafts.csswg.org/css-flexbox-1/#content-size-suggestion).
    /// </summary>
    public bool MeasuresIntrinsicBlockSize { get; init; }

    /// <summary>
    /// Whether the box is laid out in its specified block size (<see cref="FixedBlockSize"/>) to find its automatic block size,
    /// which an intrinsic size keyword of its minimum or maximum block size resolves to: its contents are not stretched to that size
    /// (the lines of a single-line flex container keep the cross size of their items).
    /// </summary>
    public bool MeasuresAutomaticBlockSize { get; init; }

    /// <summary>
    /// With <see cref="MeasuresIntrinsicBlockSize"/>: whether the preferred aspect ratio of the box does not give its block size
    /// (the measured size is the block size of its contents): the content size suggestion of a non-replaced flex item
    /// (https://drafts.csswg.org/css-flexbox-1/#content-size-suggestion), which has no transferred size suggestion.
    /// </summary>
    public bool MeasuresContentsWithoutAspectRatio { get; init; }

    /// <summary>
    /// The border-box size the <c>auto</c> size of the box resolves to in the inline axis of the containing block, instead of
    /// its automatic size: the stretch-fit size of an absolutely positioned box whose insets are both set in that axis
    /// (https://drafts.csswg.org/css-position-3/#abspos-auto-size). The min/max sizes still apply. Null for the other boxes.
    /// </summary>
    public double? StretchInlineSize { get; init; }

    /// <summary>The border-box size the <c>auto</c> size of the box resolves to in the block axis of the containing block (see <see cref="StretchInlineSize"/>), or null.</summary>
    public double? StretchBlockSize { get; init; }

    /// <summary>
    /// Whether an <c>auto</c> inline size of the box is its fit-content size in the available inline size instead of filling it
    /// (https://drafts.csswg.org/css-sizing-3/#fit-content-size): the cross size of a flex item that is not stretched
    /// (https://drafts.csswg.org/css-flexbox-1/#algo-cross-item: "treating auto as fit-content"). Its <c>auto</c> margins are 0.
    /// </summary>
    public bool AutoInlineSizeIsFitContent { get; init; }

    /// <summary>
    /// The block formatting context the box participates in (https://drafts.csswg.org/css2/#block-formatting), set by a block
    /// container for its in-flow block-level children that do not establish a new formatting context: the box keeps its state
    /// up to date (its position, the collapsing of its margins) and passes it to its children. Null for the other boxes.
    /// </summary>
    public BlockFormattingContextState? BlockFormattingContext { get; init; }

    /// <summary>
    /// The line-left edge of the content box of the containing block in the coordinates of the <see cref="BlockFormattingContext"/>
    /// (see <see cref="ExclusionSpace"/>): the floats of the context are placed in these coordinates.
    /// </summary>
    public double BlockFormattingContextLineOffset { get; init; }

    /// <summary>
    /// Whether the block-start margin of the box, collapsed with the margins of its first children, was already added to the
    /// adjoining margins of the <see cref="BlockFormattingContext"/> by an ancestor whose block-start margin collapses with them
    /// (see <see cref="BlockFormattingContext.FindMarginsCollapsingWithChildren"/>).
    /// </summary>
    public bool IsStartMarginCollapsedWithAncestor { get; init; }

    /// <summary>
    /// Whether the block-start margin of the box (in the writing mode of the containing block) is trimmed by <c>margin-trim</c>
    /// (https://drafts.csswg.org/css-box-4/#margin-trim): the margin is adjacent to the block-start edge of a block container
    /// that trims it (its containing block, or an ancestor when the margin collapses with a trimmed margin). The layout treats
    /// it as zero (see <see cref="BoxFragment.UntrimmedMargin"/>).
    /// </summary>
    public bool TrimsBlockStartMargin { get; init; }

    /// <summary>Whether the block-end margin of the box is trimmed by <c>margin-trim</c> (see <see cref="TrimsBlockStartMargin"/>).</summary>
    public bool TrimsBlockEndMargin { get; init; }

    /// <summary>
    /// The outside marker of a list item whose content is block-level, passed to its first in-flow child: the marker is placed
    /// next to the first line box of the list item (see <see cref="PendingOutsideMarker"/>), or null.
    /// </summary>
    public PendingOutsideMarker? OutsideMarker { get; init; }

    /// <summary>
    /// Whether a percentage block size of a scroll container resolves against 0 when the block size of its containing block is
    /// indefinite: the children of a table cell in its first layout (https://drafts.csswg.org/css-tables-3/#row-layout:
    /// "descendants of table cells whose height depends on percentages of their parent cell's height [...] are considered to have
    /// an auto height if they have overflow set to visible, clip, or hidden or if they are replaced elements, and a 0px height if
    /// they have not").
    /// </summary>
    public bool ScrollContainerPercentageBlockSizeIsZero { get; init; }

    /// <summary>
    /// Whether the box is a table cell whose block size is restricted: the block size of the table is not <c>auto</c>, or its
    /// row group, its row or the cell has a height that is a length. In the first layout of the cell (<see cref="MeasuresIntrinsicBlockSize"/>), the
    /// percentage block sizes of its scroll container children then resolve against 0 (<see cref="ScrollContainerPercentageBlockSizeIsZero"/>);
    /// as in the browsers, they behave as <c>auto</c> in a cell that nothing restricts (WPT
    /// css/css-tables/percent-height-overflow-auto-in-unrestricted-block-size-cell.tentative.html).
    /// </summary>
    public bool IsRestrictedTableCell { get; init; }

    /// <summary>
    /// The basis of the percentage block sizes of a replaced element (an image, a canvas, a video, an embedded document) whose
    /// containing block is a table cell with a height that is a length (or an anonymous block box of that cell): the content
    /// box of the cell with that height, in the first layout of the cell (where the other percentages behave as <c>auto</c>)
    /// and in its final layout (where they resolve against the block size the row gives to the cell). As in the browsers, such
    /// an element keeps the same size in the two layouts and does not make its row taller than the height of the cell asks
    /// (WPT css/css-tables/percent-height-replaced-in-percent-cell-002.html; css-tables-3 #row-layout treats its percentages as
    /// <c>auto</c> in the first layout). Null for the other boxes.
    /// </summary>
    public double? ReplacedPercentageBlockSize { get; init; }

    /// <summary>
    /// The used borders of a table cell in the collapsing border model (https://drafts.csswg.org/css-tables-3/#border-collapsing):
    /// the half of the collapsed borders of its sides, which replace its own borders. Null for the other boxes.
    /// </summary>
    public Sides<double>? TableCellBorder { get; init; }

    /// <summary>
    /// In quirks mode, the basis of a percentage <c>height</c> when the block size of the containing block is indefinite
    /// (https://quirks.spec.whatwg.org/#the-percentage-height-calculation-quirk): the height of the nearest ancestor containing
    /// block that is not an <c>auto</c> height block container (the block size of the containing block when it is definite),
    /// or null. Always null in no-quirks mode.
    /// </summary>
    public double? QuirksPercentageBlockSize { get; init; }

    /// <summary>
    /// In quirks mode, whether <see cref="QuirksPercentageBlockSize"/> is the block size that the html or the body element gets
    /// from the quirks that make them fill the viewport (https://quirks.spec.whatwg.org/#the-html-element-fills-the-viewport-quirk,
    /// https://quirks.spec.whatwg.org/#the-body-element-fills-the-html-element-quirk) and the box is a child of that element (or
    /// of an anonymous block box of it): as in the browsers, that size is definite for the stretch-fit block size of the box
    /// (https://drafts.csswg.org/css-sizing-4/#stretch-fit-sizing).
    /// </summary>
    public bool StretchUsesQuirksPercentageBlockSize { get; init; }

    /// <summary>
    /// Whether the last baseline of the box is used as the baseline of an inline-block whose <c>baseline-source</c> is
    /// <c>auto</c> (the box is in its normal flow): a block container that is a scroll container then gives the block-end edge
    /// of its margin box as its baseline (see <see cref="BlockFormattingContext"/>).
    /// </summary>
    public bool UsesInlineBlockBaseline { get; init; }

    /// <summary>
    /// The stretch size constraint of an embellished operator laid out by the math layout (https://w3c.github.io/mathml-core/#dfn-inline-stretch-size-constraint),
    /// or null: the target size of its core operator stretched along the inline or the block axis.
    /// </summary>
    public MathStretchConstraint? MathStretch { get; init; }

    /// <summary>
    /// The space that the baseline content-alignment of the box adds before its contents
    /// (https://drafts.csswg.org/css-align-3/#baseline-align-content: "Baseline content-alignment effectively increases the
    /// padding on the box to align the alignment baseline of its contents with that of other baseline-aligned boxes in its
    /// baseline-sharing group"): the box participates in first baseline content-alignment in its container (a grid item).
    /// </summary>
    public double ContentShimStart { get; init; }

    /// <summary>The space that the baseline content-alignment of the box adds after its contents (last baseline content-alignment, see <see cref="ContentShimStart"/>).</summary>
    public double ContentShimEnd { get; init; }

    /// <summary>
    /// Returns the space of the children of a box whose content box has the given size (the containing block of its in-flow
    /// children), in the writing mode of the box.
    /// </summary>
    public static ConstraintSpace ForContentBox(WritingDirection writingDirection, double inlineSize, double? blockSize, double? quirksPercentageBlockSize = null) => new()
    {
        WritingDirection = writingDirection,
        AvailableInlineSize = inlineSize,
        AvailableBlockSize = blockSize,
        PercentageInlineSize = inlineSize,
        PercentageBlockSize = blockSize,
        QuirksPercentageBlockSize = quirksPercentageBlockSize,
    };

    /// <summary>Returns the size of the containing block in a physical axis (the basis of the percentages of <c>width</c> or <c>height</c>), or null when it is indefinite.</summary>
    public double? GetPercentageBasis(bool horizontal) => WritingDirection.IsHorizontal == horizontal ? (IsInlineSizeIndefinite ? null : PercentageInlineSize) : PercentageBlockSize;
}
