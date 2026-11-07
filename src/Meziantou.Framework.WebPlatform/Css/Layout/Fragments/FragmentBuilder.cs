namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// Collects the child fragments of a container fragment (a box fragment or a line box) at logical positions, while the size
/// of the container is not known yet (e.g. an <c>auto</c> block size), then converts their positions to physical offsets
/// once it is (in <c>vertical-rl</c>, the x of a child depends on the final width of its container).
/// </summary>
internal sealed class FragmentBuilder
{
    private readonly List<(Fragment Fragment, LogicalOffset Offset)> _children = [];
    private List<(int ChildIndex, OutOfFlowCandidate Candidate, LogicalOffset Offset, PhysicalOffset Adjustment)>? _outOfFlow;
    private List<(int ChildIndex, OutOfFlowCandidate Candidate)>? _builtOutOfFlow;

    /// <param name="writingDirection">The writing mode and the direction of the logical coordinates of the children (the ones of the container box).</param>
    public FragmentBuilder(WritingDirection writingDirection)
    {
        WritingDirection = writingDirection;
    }

    /// <summary>The writing mode and the direction of the logical coordinates of the children.</summary>
    public WritingDirection WritingDirection { get; private set; }

    /// <summary>The number of children added so far.</summary>
    public int Count => _children.Count;

    /// <summary>
    /// The gaps of the container (https://drafts.csswg.org/css-gaps-1/#gaps), set by the layouts of the containers that have gaps
    /// when gap decorations are painted (see <see cref="BoxFragment.GapGeometry"/>), or null.
    /// </summary>
    public GapGeometry? GapGeometry { get; set; }

    /// <summary>The column boxes of a multi-column container (see <see cref="BoxFragment.ColumnRects"/>), or null.</summary>
    public LogicalRect[]? ColumnRects { get; set; }

    /// <summary>
    /// Adds a child fragment: <paramref name="offset"/> is the logical position of its inline-start/block-start corner (of the
    /// border box of a box fragment) relative to the inline-start/block-start corner of the border box of the container.
    /// </summary>
    public void AddChild(Fragment fragment, LogicalOffset offset) => _children.Add((fragment, offset));

    /// <summary>Returns a child fragment added so far.</summary>
    public Fragment GetChild(int index) => _children[index].Fragment;

    /// <summary>
    /// Adds an absolutely positioned box at its static position (https://drafts.csswg.org/css-position-3/#staticpos-rect):
    /// <paramref name="staticPosition"/> is the logical position of the corner of its hypothetical margin box (the inline-start
    /// and block-start corner in the writing mode of the container, the static-position containing block), relative to the
    /// border box of the container, and <paramref name="adjustment"/> a physical offset added to it (the relative offsets of the
    /// inline boxes it is in). The box is laid out when its containing block is (see <see cref="PositionedLayout.LayoutOutOfFlowDescendants"/>).
    /// </summary>
    public void AddOutOfFlowCandidate(ContainerBox box, LogicalOffset staticPosition, PhysicalOffset adjustment = default)
        => (_outOfFlow ??= []).Add((_children.Count, OutOfFlowCandidate.Create(box, WritingDirection), staticPosition, adjustment));

    /// <summary>
    /// Adds an absolutely positioned box whose static position is given for the edges <paramref name="inlineEdge"/> and
    /// <paramref name="blockEdge"/> of its margin box (e.g. the center of the box: the static position of a child of a flex
    /// container, https://drafts.csswg.org/css-flexbox-1/#abspos-items), in the logical axes of the container.
    /// </summary>
    /// <param name="box">The absolutely positioned box.</param>
    /// <param name="staticPosition">The static position, in the logical coordinates of the container.</param>
    /// <param name="inlineEdge">The edge of the margin box at the static position in the inline axis of the container.</param>
    /// <param name="blockEdge">The edge of the margin box at the static position in the block axis of the container.</param>
    /// <param name="staticPositionIsContainingBlock">
    /// Whether the static-position rectangle is the containing block of the box (a grid area: a child of a grid container that is
    /// its containing block, https://drafts.csswg.org/css-align-3/#staticpos-rect): the edges are then at the edges (or the
    /// center) of the containing block, see <see cref="OutOfFlowCandidate.StaticPositionIsContainingBlock"/>.
    /// </param>
    /// <param name="adjustment">A physical offset added to the static position (the relative offsets of the inline boxes the box is in).</param>
    /// <param name="safeInline">Whether the alignment of the box at its static position is <c>safe</c> in the inline axis (see <see cref="OutOfFlowCandidate.IsSafeHorizontally"/>).</param>
    /// <param name="safeBlock">Whether the alignment of the box at its static position is <c>safe</c> in the block axis.</param>
    public void AddOutOfFlowCandidate(ContainerBox box, LogicalOffset staticPosition, StaticPositionEdge inlineEdge, StaticPositionEdge blockEdge, bool staticPositionIsContainingBlock = false, PhysicalOffset adjustment = default,
        bool safeInline = false, bool safeBlock = false)
    {
        var candidate = OutOfFlowCandidate.Create(box, WritingDirection, inlineEdge, blockEdge);
        candidate.StaticPositionIsContainingBlock = staticPositionIsContainingBlock;
        if (safeInline || safeBlock)
        {
            candidate.SetSafeAlignment(WritingDirection, safeInline, safeBlock);
        }

        (_outOfFlow ??= []).Add((_children.Count, candidate, staticPosition, adjustment));
    }

    /// <summary>
    /// Adds an absolutely positioned box whose candidate was created by another builder (the content of a multi-column container
    /// laid out in a strip, see <see cref="MultiColumnFormattingContext"/>) at a new static position, in the logical coordinates of the container.
    /// </summary>
    internal void AddOutOfFlowCandidate(OutOfFlowCandidate candidate, LogicalOffset staticPosition)
        => (_outOfFlow ??= []).Add((_children.Count, candidate, staticPosition, PhysicalOffset.Zero));

    /// <summary>
    /// Moves the children added so far and the static positions of the out-of-flow candidates by a logical offset (the contents
    /// of a block container aligned by <c>align-content</c>, https://drafts.csswg.org/css-align-3/#distribution-block).
    /// </summary>
    public void MoveChildren(LogicalOffset offset)
    {
        for (var i = 0; i < _children.Count; i++)
        {
            var (fragment, position) = _children[i];
            _children[i] = (fragment, new LogicalOffset(position.Inline + offset.Inline, position.Block + offset.Block));
        }

        if (_outOfFlow is not null)
        {
            for (var i = 0; i < _outOfFlow.Count; i++)
            {
                var (childIndex, candidate, position, adjustment) = _outOfFlow[i];
                _outOfFlow[i] = (childIndex, candidate, new LogicalOffset(position.Inline + offset.Inline, position.Block + offset.Block), adjustment);
            }
        }
    }

    /// <summary>Removes the children and sets the writing direction, to reuse the builder (<see cref="Build"/> returns a new array).</summary>
    public void Reset(WritingDirection writingDirection)
    {
        WritingDirection = writingDirection;
        _children.Clear();
        _outOfFlow = null;
        _builtOutOfFlow = null;
        GapGeometry = null;
        ColumnRects = null;
    }

    /// <summary>
    /// Converts the positions of the children (and of the static positions of the out-of-flow candidates) for a container of
    /// the given physical size and returns them. The relative offsets of the box fragments are added to their positions.
    /// </summary>
    public Fragment[]? Build(PhysicalSize containerSize)
    {
        if (_outOfFlow is not null)
        {
            _builtOutOfFlow = new(_outOfFlow.Count);
            foreach (var (childIndex, candidate, offset, adjustment) in _outOfFlow)
            {
                candidate.StaticPosition = WritingDirection.ToPhysical(offset, containerSize, PhysicalSize.Zero) + adjustment;
                _builtOutOfFlow.Add((childIndex, candidate));
            }

            _outOfFlow = null;
        }

        if (_children.Count == 0)
            return null;

        var result = new Fragment[_children.Count];
        for (var i = 0; i < _children.Count; i++)
        {
            var (fragment, offset) = _children[i];
            var position = WritingDirection.ToPhysical(offset, containerSize, fragment.Size);
            if (fragment is BoxFragment box)
            {
                position += box.RelativeOffset;
            }

            fragment.Offset = position;
            result[i] = fragment;
        }

        return result;
    }

    /// <summary>
    /// Returns the out-of-flow candidates added to the builder, with their static positions converted by <see cref="Build"/>, and
    /// the index of the child they precede (for the tree order), or null.
    /// </summary>
    internal List<(int ChildIndex, OutOfFlowCandidate Candidate)>? TakeOutOfFlowCandidates()
    {
        var result = _builtOutOfFlow;
        _builtOutOfFlow = null;
        return result;
    }
}
