namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// An absolutely positioned box waiting for the layout of its containing block
/// (https://drafts.csswg.org/css-position-3/#abspos-layout: "after the final size of the containing block has been
/// determined"): the formatting context of its parent records its static position, and the fragments carry the candidate up
/// to the fragment of <see cref="Host"/>, translating the static position into their coordinates.
/// </summary>
internal sealed class OutOfFlowCandidate
{
    private OutOfFlowCandidate(ContainerBox box, ContainerBox containingBlock, PhysicalSide staticHorizontalEdge, PhysicalSide staticVerticalEdge, bool centersHorizontally, bool centersVertically)
    {
        Box = box;
        ContainingBlock = containingBlock;
        Host = PositionedLayout.GetHost(containingBlock);
        StaticHorizontalEdge = staticHorizontalEdge;
        StaticVerticalEdge = staticVerticalEdge;
        CentersHorizontally = centersHorizontally;
        CentersVertically = centersVertically;
    }

    /// <summary>The absolutely positioned box.</summary>
    public ContainerBox Box { get; }

    /// <summary>
    /// The box that establishes the containing block (https://drafts.csswg.org/css-position-3/#def-cb): a positioned ancestor
    /// (the first part of an inline box), an ancestor that establishes a containing block for the fixed positioned boxes, or the
    /// viewport box (the initial containing block of an absolutely positioned box, the viewport of a fixed positioned box).
    /// </summary>
    public ContainerBox ContainingBlock { get; }

    /// <summary>
    /// The box whose layout lays out the candidate: the containing block, or the block container that contains all the
    /// fragments of an inline containing block (see <see cref="PositionedLayout.GetHost"/>).
    /// </summary>
    public ContainerBox Host { get; }

    /// <summary>
    /// The first fragment that carried the candidate (the fragment of the box whose formatting context recorded the static
    /// position, or of an absolutely positioned ancestor), for the fragmentation: the static position is in the flow of this
    /// fragment (see <see cref="FragmentationFlow"/>).
    /// </summary>
    public BoxFragment? StaticPositionContainer { get; set; }

    /// <summary>The static position (https://drafts.csswg.org/css-position-3/#static-position): a point in the coordinates of the fragment that carries the candidate.</summary>
    public PhysicalOffset StaticPosition { get; set; }

    /// <summary>
    /// The edge of the margin box of the box that is at the static position in the horizontal axis (<see cref="PhysicalSide.Left"/>
    /// or <see cref="PhysicalSide.Right"/>): the start edge in the writing mode of the static-position containing block
    /// (https://drafts.csswg.org/css2/#abs-non-replaced-width: "if the 'direction' property of the element establishing the
    /// static-position containing block is 'ltr' set 'left' to the static position [...] otherwise, set 'right'").
    /// </summary>
    public PhysicalSide StaticHorizontalEdge { get; }

    /// <summary>The edge of the margin box at the static position in the vertical axis (<see cref="PhysicalSide.Top"/> or <see cref="PhysicalSide.Bottom"/>).</summary>
    public PhysicalSide StaticVerticalEdge { get; }

    /// <summary>
    /// Whether the margin box of the box is centered on the static position in the horizontal axis instead of having an edge
    /// at it (<see cref="StaticHorizontalEdge"/> is then ignored): the static position of a child of a flex container aligned
    /// with <c>center</c> (https://drafts.csswg.org/css-flexbox-1/#abspos-items).
    /// </summary>
    public bool CentersHorizontally { get; }

    /// <summary>Whether the margin box of the box is centered on the static position in the vertical axis (see <see cref="CentersHorizontally"/>).</summary>
    public bool CentersVertically { get; }

    /// <summary>
    /// Whether the static-position rectangle of the box is its containing block (https://drafts.csswg.org/css-align-3/#staticpos-rect:
    /// "if that grid container also establishes the box's actual containing block, then the grid area specified by the
    /// grid-placement properties establishes its static-position rectangle instead"): the static position is the edge (or the
    /// center) of the containing block given by <see cref="StaticHorizontalEdge"/>/<see cref="StaticVerticalEdge"/>
    /// (<see cref="CentersHorizontally"/>/<see cref="CentersVertically"/>), <see cref="StaticPosition"/> is ignored.
    /// </summary>
    public bool StaticPositionIsContainingBlock { get; set; }

    /// <summary>
    /// Whether the alignment of the box at its static position is <c>safe</c> in the horizontal axis
    /// (https://drafts.csswg.org/css-align-3/#overflow-values): a margin box that overflows the inset-modified containing block
    /// that the static position gives (https://drafts.csswg.org/css-position-3/#resolving-insets) is aligned with the start edge
    /// of that block (<see cref="SafeHorizontalEdge"/>) instead, as in Blink (https://github.com/w3c/csswg-drafts/issues/11934).
    /// </summary>
    public bool IsSafeHorizontally { get; private set; }

    /// <summary>Whether the alignment of the box at its static position is <c>safe</c> in the vertical axis (see <see cref="IsSafeHorizontally"/>).</summary>
    public bool IsSafeVertically { get; private set; }

    /// <summary>The start edge of the static-position containing block in the horizontal axis: the edge of the fallback alignment of <see cref="IsSafeHorizontally"/>.</summary>
    public PhysicalSide SafeHorizontalEdge { get; private set; }

    /// <summary>The start edge of the static-position containing block in the vertical axis (see <see cref="SafeHorizontalEdge"/>).</summary>
    public PhysicalSide SafeVerticalEdge { get; private set; }

    /// <summary>Makes the alignment of the box at its static position safe in the logical axes of the static-position containing block.</summary>
    public void SetSafeAlignment(WritingDirection staticPositionContainingBlock, bool inline, bool block)
    {
        var inlineIsHorizontal = staticPositionContainingBlock.IsHorizontal;
        IsSafeHorizontally = inlineIsHorizontal ? inline : block;
        IsSafeVertically = inlineIsHorizontal ? block : inline;
        SafeHorizontalEdge = inlineIsHorizontal ? staticPositionContainingBlock.InlineStart : staticPositionContainingBlock.BlockStart;
        SafeVerticalEdge = inlineIsHorizontal ? staticPositionContainingBlock.BlockStart : staticPositionContainingBlock.InlineStart;
    }

    /// <summary>Creates the candidate of an absolutely positioned box whose static-position containing block has the given writing mode and direction.</summary>
    public static OutOfFlowCandidate Create(ContainerBox box, WritingDirection staticPositionContainingBlock)
        => Create(box, staticPositionContainingBlock, StaticPositionEdge.Start, StaticPositionEdge.Start);

    /// <summary>
    /// Creates the candidate of an absolutely positioned box whose static position is at the given edges of its margin box, in
    /// the logical axes of the static-position containing block (https://drafts.csswg.org/css-position-3/#staticpos-rect).
    /// </summary>
    public static OutOfFlowCandidate Create(ContainerBox box, WritingDirection staticPositionContainingBlock, StaticPositionEdge inlineEdge, StaticPositionEdge blockEdge)
    {
        var inlineSide = inlineEdge == StaticPositionEdge.End ? staticPositionContainingBlock.InlineEnd : staticPositionContainingBlock.InlineStart;
        var blockSide = blockEdge == StaticPositionEdge.End ? staticPositionContainingBlock.BlockEnd : staticPositionContainingBlock.BlockStart;
        var inlineIsHorizontal = staticPositionContainingBlock.IsHorizontal;
        var horizontal = inlineIsHorizontal ? inlineSide : blockSide;
        var vertical = inlineIsHorizontal ? blockSide : inlineSide;
        var centersHorizontally = (inlineIsHorizontal ? inlineEdge : blockEdge) == StaticPositionEdge.Center;
        var centersVertically = (inlineIsHorizontal ? blockEdge : inlineEdge) == StaticPositionEdge.Center;
        return new OutOfFlowCandidate(box, PositionedLayout.FindContainingBlock(box), horizontal, vertical, centersHorizontally, centersVertically);
    }
}

/// <summary>The edge of the margin box of an absolutely positioned box that is at its static position, in one logical axis of the static-position containing block.</summary>
internal enum StaticPositionEdge : byte
{
    /// <summary>The start edge (the static position of the block and inline layouts).</summary>
    Start,

    /// <summary>The center of the margin box.</summary>
    Center,

    /// <summary>The end edge.</summary>
    End,
}
