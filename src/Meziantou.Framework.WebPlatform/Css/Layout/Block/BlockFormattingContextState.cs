using System.Runtime.InteropServices;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The state of a block formatting context (https://drafts.csswg.org/css2/#block-formatting) shared by the boxes that
/// participate in it, while they are laid out in order: the current position in the block axis relative to the block
/// formatting context root, the margins that are adjoining at that position and are not collapsed yet, and the floats
/// placed so far (<see cref="Exclusions"/>).
/// </summary>
/// <remarks>
/// <para>
/// A block formatting context root creates the state; the in-flow block-level boxes that do not establish a new formatting
/// context receive it in their <see cref="ConstraintSpace.BlockFormattingContext"/> (and pass it to their children), so the
/// position of every box of the context is known in the coordinates of the root while it is laid out: this is what the
/// floats (#344) need, as their placement depends on the floats placed before them anywhere in the context, and on the
/// margins that collapse through their ancestors.
/// </para>
/// <para>
/// The coordinates are logical, in the writing mode of the root, relative to the block-start edge of its content box. The
/// position advances with the borders, paddings and contents of the boxes; the margins are accumulated in
/// <see cref="CurrentMargin"/> (<see cref="AdjoinMargin(double)"/>) and added to the position when something separates them
/// from the next margins (<see cref="CollapseMargins"/>): a border, a padding, a line box, clearance, a block formatting
/// context root. The boxes that participate in the context keep it up to date in the same order as their fragments are
/// placed, so at the end of the contents of the root, the position plus the pending margins is the block size of its contents.
/// </para>
/// <para>
/// The layout result cache (<see cref="LayoutResultCache"/>) records the changes of the state made by the layout of a box
/// (<see cref="Operation"/>) and makes them again when it uses the result of the box: each method that changes the position or
/// the margins records its operation; a change that is not relative to the current position invalidates the recording
/// (<see cref="Restore"/>, see <see cref="InvalidRecordingCount"/>).
/// </para>
/// </remarks>
internal sealed class BlockFormattingContextState
{
    /// <summary>The floats placed in the block formatting context.</summary>
    public ExclusionSpace Exclusions { get; } = new();

    /// <summary>
    /// The multi-column container whose content establishes the block formatting context (the column boxes, see
    /// <see cref="MultiColumnFormattingContext"/>), or null: the column spanners of the context are laid out in its content box.
    /// </summary>
    public MultiColumnFlow? MultiColumnFlow { get; init; }

    /// <summary>The line clamping of the root when it is a line-clamp container (https://drafts.csswg.org/css-overflow-4/#line-clamp), or null.</summary>
    public LineClamp? LineClamp { get; init; }

    /// <summary>
    /// The group alignment of the line boxes of the boxes being laid out (<c>text-group-align</c>, see <see cref="Layout.TextGroupAlignment"/>):
    /// the one of the nearest ancestor block container that participates in the context (or is its root), or null.
    /// </summary>
    public TextGroupAlignment? TextGroupAlignment { get; set; }

    /// <summary>The current position, after the margins collapsed so far, relative to the block-start edge of the content box of the root.</summary>
    public double BlockOffset { get; private set; }

    /// <summary>The adjoining margins at the current position, not collapsed yet: the next margins may still collapse with them.</summary>
    public MarginStrut CurrentMargin { get; private set; }

    /// <summary>The position of the block-start border edge of the next box if its margins collapse with the current ones (no clearance).</summary>
    public double GetPositionWithoutClearance(MarginStrut blockStartMargin) => BlockOffset + CurrentMargin.Append(blockStartMargin).Sum;

    /// <summary>The position of the block-start border edge of the next box if its margins are separated from the current ones by clearance of 0.</summary>
    public double GetPositionWithZeroClearance(MarginStrut blockStartMargin) => BlockOffset + CurrentMargin.Sum + blockStartMargin.Sum;

    /// <summary>Moves the current position (borders, paddings, contents).</summary>
    public void AdvanceBlockPosition(double distance)
    {
        BlockOffset += distance;
        _operations?.Add(new Operation(OperationKind.Advance, distance, default));
    }

    /// <summary>Adds a margin to the adjoining margins at the current position.</summary>
    public void AdjoinMargin(double margin)
    {
        CurrentMargin = CurrentMargin.Append(margin);
        _operations?.Add(new Operation(OperationKind.AdjoinMargin, margin, default));
    }

    /// <summary>Adds a set of adjoining margins to the adjoining margins at the current position.</summary>
    public void AdjoinMargin(MarginStrut margin)
    {
        CurrentMargin = CurrentMargin.Append(margin);
        _operations?.Add(new Operation(OperationKind.AdjoinMargins, 0, margin));
    }

    /// <summary>The number of calls to <see cref="CollapseMargins"/>: tells whether something separated the margins since a given point.</summary>
    public int CollapseCount { get; private set; }

    /// <summary>Collapses the adjoining margins at the current position: they no longer collapse with the next margins.</summary>
    public void CollapseMargins()
    {
        BlockOffset += CurrentMargin.Sum;
        CurrentMargin = default;
        CollapseCount++;
        _operations?.Add(new Operation(OperationKind.CollapseMargins, 0, default));
    }

    /// <summary>
    /// Discards the adjoining margins at the current position: the margins of the last children of a box that its minimum
    /// size makes taller than its contents (they do not collapse with the margins after the box).
    /// </summary>
    public void DiscardMargins()
    {
        CurrentMargin = default;
        _operations?.Add(new Operation(OperationKind.DiscardMargins, 0, default));
    }

    /// <summary>
    /// Sets the adjoining margins at the current position back to a previous value: the margins added since then are trimmed
    /// by <c>margin-trim</c> (https://drafts.csswg.org/css-box-4/#margin-trim: the margins of a box that collapses through at the
    /// block-start edge of a container that trims them).
    /// </summary>
    public void RestoreMargins(MarginStrut margins)
    {
        CurrentMargin = margins;
        _operations?.Add(new Operation(OperationKind.RestoreMargins, 0, margins));
    }

    /// <summary>
    /// The boxes whose block-start margin was adjoining to the block-start margin of an ancestor (see
    /// <see cref="ConstraintSpace.IsStartMarginCollapsedWithAncestor"/>) and that got clearance (<c>clear</c>, a block formatting
    /// context root moved down below the floats, a column spanner): their margins are separated from the margins of their
    /// ancestors (see <see cref="Separate"/>).
    /// </summary>
    private HashSet<ContainerBox>? _separatedBoxes;

    /// <summary>The number of boxes whose margins were separated from the margins of their ancestors (see <see cref="Separate"/>).</summary>
    public int SeparationCount { get; private set; }

    /// <summary>Returns whether the margins of a box were separated from the margins of its ancestors (see <see cref="Separate"/>).</summary>
    public bool IsSeparated(ContainerBox box) => _separatedBoxes is not null && _separatedBoxes.Contains(box);

    /// <summary>
    /// Records that a box whose block-start margin was adjoining to the block-start margin of an ancestor got clearance: the
    /// ancestor (and the floats before the box) were placed as if the margins collapsed, so the outermost ancestor whose margin
    /// collapsed with them lays its contents out again without the margins of the box (https://drafts.csswg.org/css2/#clearance:
    /// "clearance inhibits margin collapsing"), as the browsers do. In that layout, the box is placed below the floats or after
    /// the margins before it, whichever is lower (<see cref="GetSeparatedPosition"/>), even when the floats moved up.
    /// </summary>
    public void Separate(ContainerBox box)
    {
        if ((_separatedBoxes ??= []).Add(box))
        {
            SeparationCount++;
        }
    }

    /// <summary>
    /// Returns the position of the block-start border edge of a box whose margins were separated from the margins of its
    /// ancestors (see <see cref="Separate"/>): after the margins before it (without its own block-start margin), and below the
    /// floats it clears.
    /// </summary>
    public double GetSeparatedPosition(ClearSides sides)
    {
        var position = BlockOffset + CurrentMargin.Sum;
        return Exclusions.GetClearanceOffset(sides, position) is { } clearOffset ? Math.Max(position, clearOffset) : position;
    }

    /// <summary>Returns the state of the context before a box is laid out, to lay it out again (see <see cref="Restore"/>).</summary>
    public Snapshot Save() => new(BlockOffset, CurrentMargin, CollapseCount, Exclusions.Save());

    /// <summary>Restores the state of the context saved by <see cref="Save"/>: the floats placed since then are removed.</summary>
    public void Restore(in Snapshot snapshot)
    {
        // (a restored position is not relative to the position of a box whose layout is recorded)
        InvalidRecordingCount++;
        BlockOffset = snapshot.BlockOffset;
        CurrentMargin = snapshot.CurrentMargin;
        CollapseCount = snapshot.CollapseCount;
        Exclusions.Restore(snapshot.Exclusions);
    }

    /// <summary>The operations that changed the state, recorded for the layout result cache (see <see cref="StartRecording"/>), or null.</summary>
    private List<Operation>? _operations;

    /// <summary>
    /// The number of changes of the state that cannot be recorded as operations relative to the current position (see
    /// <see cref="Restore"/>): the layout of a box during which it changed is not kept by the layout result cache.
    /// </summary>
    public int InvalidRecordingCount { get; private set; }

    /// <summary>
    /// Starts recording the operations that change the state (the layout of a box whose result may be kept by the layout result
    /// cache, see <see cref="LayoutResultCache"/>) and returns the index of the next operation.
    /// </summary>
    public int StartRecording() => (_operations ??= []).Count;

    /// <summary>Returns the operations recorded since an index returned by <see cref="StartRecording"/>.</summary>
    public Operation[] GetRecordedOperations(int start) => _operations is null ? [] : _operations.GetRange(start, _operations.Count - start).ToArray();

    /// <summary>
    /// Makes again the changes of the state recorded by the layout of a box (see <see cref="GetRecordedOperations"/>): the layout
    /// of a box in a context without float does not depend on its position, it changes the state relatively to it. The
    /// operations are recorded as one operation when the state is recorded.
    /// </summary>
    public void Replay(Operation[] operations)
    {
        _operations?.Add(new Operation(OperationKind.Replay, 0, default, operations));
        Apply(operations);
    }

    private void Apply(Operation[] operations)
    {
        // (the methods that change the state are called without recording them: the caller recorded the operations)
        var recorded = _operations;
        _operations = null;
        try
        {
            foreach (var operation in operations)
            {
                switch (operation.Kind)
                {
                    case OperationKind.Advance:
                        AdvanceBlockPosition(operation.Value);
                        break;
                    case OperationKind.AdjoinMargin:
                        AdjoinMargin(operation.Value);
                        break;
                    case OperationKind.AdjoinMargins:
                        AdjoinMargin(operation.Margins);
                        break;
                    case OperationKind.CollapseMargins:
                        CollapseMargins();
                        break;
                    case OperationKind.DiscardMargins:
                        DiscardMargins();
                        break;
                    case OperationKind.RestoreMargins:
                        RestoreMargins(operation.Margins);
                        break;
                    default:
                        Apply(operation.Operations!);
                        break;
                }
            }
        }
        finally
        {
            _operations = recorded;
        }
    }

    /// <summary>The kind of a change of the state (see <see cref="Operation"/>).</summary>
    internal enum OperationKind : byte
    {
        Advance,
        AdjoinMargin,
        AdjoinMargins,
        CollapseMargins,
        DiscardMargins,
        RestoreMargins,

        /// <summary>The operations of a box whose layout result was used again (see <see cref="Replay"/>).</summary>
        Replay,
    }

    /// <summary>A change of the state recorded for the layout result cache: the methods that change the state, with their argument.</summary>
    [StructLayout(LayoutKind.Auto)]
    internal readonly record struct Operation(OperationKind Kind, double Value, MarginStrut Margins, Operation[]? Operations = null);

    /// <summary>A saved state of the context (see <see cref="Save"/>).</summary>
    [StructLayout(LayoutKind.Auto)]
    public readonly record struct Snapshot(double BlockOffset, MarginStrut CurrentMargin, int CollapseCount, ExclusionSpace.Snapshot Exclusions);

    /// <summary>
    /// Computes the clearance of a box (https://drafts.csswg.org/css2/#clearance) whose block-start margin, collapsed with the
    /// margins of its children that would be adjoining to it, is <paramref name="blockStartMargin"/>; null when the box is
    /// not cleared (<c>clear: none</c>, no float to clear, or its hypothetical position is already past the floats).
    /// </summary>
    /// <remarks>
    /// "Computing the clearance of an element on which 'clear' is set is done by first determining the hypothetical position
    /// of the element's top border edge. This position is where the actual top border edge would have been if the element's
    /// 'clear' property had been 'none'. If this hypothetical position of the element's top border edge is not past the
    /// relevant floats, then clearance is introduced, and margins collapse according to the rules in 8.3.1. Then the amount
    /// of clearance is set to the greater of: the amount necessary to place the border edge of the block even with the bottom
    /// outer edge of the lowest float that is to be cleared; the amount necessary to place the top border edge of the block at
    /// its hypothetical position." With clearance, the margins before the box and its block-start margin are no longer
    /// adjoining: the clearance is measured from the position where they are collapsed separately.
    /// </remarks>
    public double? ComputeClearance(ClearSides sides, MarginStrut blockStartMargin)
    {
        if (sides == ClearSides.None)
            return null;

        var hypotheticalPosition = GetPositionWithoutClearance(blockStartMargin);
        if (Exclusions.GetClearanceOffset(sides, hypotheticalPosition) is not { } clearOffset || hypotheticalPosition >= clearOffset)
            return null;

        return Math.Max(hypotheticalPosition, clearOffset) - GetPositionWithZeroClearance(blockStartMargin);
    }
}
