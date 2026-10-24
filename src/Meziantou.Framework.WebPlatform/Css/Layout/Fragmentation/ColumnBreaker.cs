using System.Runtime.InteropServices;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>A break between two fragmentainers chosen by <see cref="ColumnBreaker"/>.</summary>
/// <param name="Position">The position of the strip where the next fragmentainer starts.</param>
/// <param name="DisplayOffset">
/// The space at the start of the next fragmentainer taken by the block-start edges repeated by the boxes with
/// <c>box-decoration-break: clone</c> that contain the break: the content is displayed after it. It is reduced by the part
/// of the first line box of the fragmentainer that <c>text-box-trim</c> trims (see <see cref="BreakCandidate.StartTrim"/>):
/// the offset is then negative.
/// </param>
/// <param name="ContentEnd">The position of the strip where the content before the break ends (before the margins and the gaps that are truncated at the break).</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct FragmentainerBreak(double Position, double DisplayOffset, double ContentEnd);

/// <summary>
/// The block sizes of the fragmentainers of a portion of a strip: the fragmentainers of a row of columns have the same block
/// size (the rows after the last block size have it too).
/// </summary>
/// <param name="RowBlockSizes">The block size of the fragmentainers of each row.</param>
/// <param name="ColumnsPerRow">The number of fragmentainers of a row.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct FragmentainerSizes(double[] RowBlockSizes, int ColumnsPerRow)
{
    public double Get(int index) => RowBlockSizes[Math.Min(index / ColumnsPerRow, RowBlockSizes.Length - 1)];
}

/// <summary>
/// Chooses the breaks of the content of a fragmentation context (https://drafts.csswg.org/css-break-3/#breaking-rules) laid out in a
/// single strip (<see cref="FragmentationFlow"/>) for fragmentainers of a given block size, and the block size of balanced columns
/// (https://drafts.csswg.org/css-multicol-1/#cf).
/// </summary>
internal static partial class ColumnBreaker
{
    private const double Epsilon = 0.01;

    /// <summary>The maximum number of fragmentainers (a guard against degenerate inputs).</summary>
    internal const int MaxFragmentainers = 10_000;

    /// <summary>
    /// Fills fragmentainers of a block size with the content of a portion of the strip (https://drafts.csswg.org/css-break-3/#best-breaks):
    /// each fragmentainer ends at the forced break it contains, else at the latest possible break that keeps its content in it,
    /// preferring the breaks that violate no rule; when no possible break fits (monolithic content taller than the fragmentainer),
    /// the fragmentainer overflows and ends at the next possible break.
    /// </summary>
    /// <param name="flow">The strip.</param>
    /// <param name="start">The position where the first fragmentainer starts.</param>
    /// <param name="end">The end of the content of the portion (the next fragmentainers are not created when the content ends before them).</param>
    /// <param name="blockSize">The block size of the fragmentainers.</param>
    /// <param name="breaks">Receives the breaks: the starts of the fragmentainers after the first one.</param>
    /// <param name="minimalShortage">
    /// The smallest increase of the block size that lets a fragmentainer contain more content (the "minimal space shortage" of the
    /// column balancing), or +∞.
    /// </param>
    /// <param name="map">Receives the breaks of the flow: the fragmentainer and the offset of each position of the strip (the breaks of the parallel flows are in <see cref="ParallelItem.Map"/>).</param>
    /// <param name="baseIndex">The index of the first fragmentainer.</param>
    /// <returns>Whether a break that violates a rule was chosen.</returns>
    public static bool FindBreaks(FragmentationFlow flow, double start, double end, double blockSize, List<FragmentainerBreak> breaks, out double minimalShortage, FlowMap? map = null, int baseIndex = 0)
        => FindBreaks(flow, start, end, new FragmentainerSizes([blockSize], int.MaxValue), breaks, out minimalShortage, map, baseIndex);

    /// <summary>Fills fragmentainers of several block sizes with the content of a portion of the strip (see <see cref="FindBreaks(FragmentationFlow, double, double, double, List{FragmentainerBreak}, out double, FlowMap?, int)"/>).</summary>
    public static bool FindBreaks(FragmentationFlow flow, double start, double end, in FragmentainerSizes sizes, List<FragmentainerBreak> breaks, out double minimalShortage, FlowMap? map = null, int baseIndex = 0)
        => FindBreaks(flow, start, end, sizes, breaks, out minimalShortage, out _, map, baseIndex);

    /// <summary>Fills fragmentainers with the content of a portion of the strip (see <see cref="FindBreaks(FragmentationFlow, double, double, double, List{FragmentainerBreak}, out double, FlowMap?, int)"/>).</summary>
    /// <param name="lastExtent">Receives the block size of the last fragmentainer that is used by the content and by its parallel flows.</param>
    public static bool FindBreaks(FragmentationFlow flow, double start, double end, in FragmentainerSizes sizes, List<FragmentainerBreak> breaks, out double minimalShortage, out double lastExtent, FlowMap? map = null, int baseIndex = 0)
    {
        breaks.Clear();
        flow.ClearMonolithicSlices();
        minimalShortage = double.PositiveInfinity;
        lastExtent = 0;
        map ??= new FlowMap();
        map.Add(map.Count == 0 ? double.NegativeInfinity : start, baseIndex, start, isBreak: true);
        var cursor = new FlowCursor(flow, map, start, end, baseIndex, flow.NextGeneration(), canBreakBefore: false, includesStart: start <= Epsilon);
        var violating = false;
        while (breaks.Count < MaxFragmentainers)
        {
            var result = cursor.Advance(sizes.Get(breaks.Count));
            if (result.Shortage > Epsilon)
            {
                minimalShortage = Math.Min(minimalShortage, result.Shortage);
            }

            violating |= result.Appeal is BreakAppeal.Avoid or BreakAppeal.OrphansWidows or BreakAppeal.LastResort;
            if (result.IsFinished)
            {
                lastExtent = cursor.LastExtent;
                break;
            }

            breaks.Add(new FragmentainerBreak(result.Position, result.DisplayOffset, result.ContentEnd));
        }

        return violating;
    }

    /// <summary>The result of the placement of the content of a flow in a fragmentainer.</summary>
    /// <param name="IsFinished">Whether the content of the flow ends in the fragmentainer.</param>
    /// <param name="Position">The position of the strip where the flow continues in the next fragmentainer.</param>
    /// <param name="DisplayOffset">The offset of that position in the next fragmentainer.</param>
    /// <param name="Appeal">The appeal of the break (<see cref="BreakAppeal.LastResort"/> when the content overflows the fragmentainer).</param>
    /// <param name="Shortage">The smallest increase of the block size of the fragmentainer that lets it contain more content, or +∞.</param>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct AdvanceResult(bool IsFinished, double Position, double DisplayOffset, BreakAppeal Appeal, double Shortage)
    {
        /// <summary>The position of the strip where the content before the break ends (before the margins and the gaps that are truncated at the break).</summary>
        public double ContentEnd { get; init; } = Position;

        /// <summary>Whether the break slices monolithic content that does not fit in an empty page (see <see cref="FragmentationFlow.SlicesMonolithicContent"/>).</summary>
        public bool IsMonolithicSlice { get; init; }
    }

    /// <summary>The progress of the breaker in a parallel flow (see <see cref="FlowCursor"/>, <see cref="NestedColumnsCursor"/>).</summary>
    private interface IItemCursor
    {
        /// <summary>Places the content of the flow in the next fragmentainer.</summary>
        /// <param name="blockSize">The block size of the fragmentainer.</param>
        /// <param name="index">The index of the fragmentainer.</param>
        AdvanceResult Advance(double blockSize, int index);

        /// <summary>Returns the offset of a position in the fragmentainer that was just filled (before the break).</summary>
        double GetPlacedOffset(double position);
    }

    /// <summary>
    /// The progress of the breaker in a flow: the fragmentainers are filled one after the other (<see cref="Advance"/>). The
    /// parallel flows of the flow (<see cref="ParallelGroup"/>) have their own cursors: they are placed in the same fragmentainers,
    /// break independently, and resume at the start of the next fragmentainer; a track ends after its items (it grows when its
    /// items are pushed by their breaks), and the content of the flow that follows it is shifted.
    /// </summary>
    private sealed class FlowCursor : IItemCursor
    {
        private readonly FragmentationFlow _flow;
        private readonly FlowMap _map;
        private readonly double _end;
        private readonly int _generation;

        /// <summary>The origins of the positions of the flow in the current fragmentainer (the offset of a position is its distance to the origin of the last piece that starts before it): the content after a track that grew is shifted.</summary>
        private readonly List<(double From, double Origin)> _pieces = [];

        /// <summary>The parallel flows that were advanced in the current fragmentainer and constrain its break: where they start, the appeal of their break, and whether they continue in the next fragmentainer.</summary>
        private readonly List<(double Start, BreakAppeal Appeal, bool IsUnfinished)> _penalties = [];

        /// <summary>The parallel flows that started in the current fragmentainer (they have not started if the flow breaks before them).</summary>
        private readonly List<ParallelItem> _started = [];

        /// <summary>The parallel flows that started in a previous fragmentainer and continue.</summary>
        private List<ParallelItem> _open = [];
        private List<ParallelItem> _nextOpen = [];

        /// <summary>The extent of the parallel flows that overflow the current fragmentainer: the flow does not break beside them.</summary>
        private double _overflowStart;
        private double _overflowEnd;

        /// <summary>The offset in the current fragmentainer where the parallel flows that overflow it break.</summary>
        private double _overflowOffset;

        /// <summary>The offset in the current fragmentainer where the parallel flows that overflow it end.</summary>
        private double _overflowEndOffset;

        /// <summary>The parallel flows that were placed in the current fragmentainer.</summary>
        private readonly List<ParallelItem> _advanced = [];
        private double _position;
        private double _displayOffset;
        private int _index;
        private int _candidateIndex;
        private int _groupIndex;
        private bool _canBreakBefore;

        /// <summary>
        /// The block-end edge of the first content of the flow when it is monolithic and must be placed in the first fragmentainer
        /// of the flow even when it overflows it, else NaN: the contents of a box with a specified block size that fits in the
        /// fragmentainer (as in the browsers, https://github.com/w3c/csswg-drafts/issues/6056: the box is not pushed to the
        /// next fragmentainer because of its first line or monolithic child).
        /// </summary>
        private double _leadingEnd = double.NaN;

        /// <summary>The pushed floats of the flow that are fragmented apart and continue in the current fragmentainer (see <see cref="ApplyFloatShift"/>): the block size they used in the previous fragmentainers, and their breaks.</summary>
        private List<(FloatExtent Extent, double Consumed, FlowMap Map)>? _floats;

        /// <summary>The index of the first pushed float of the flow that has not started.</summary>
        private int _floatIndex;

        /// <summary>The shift of the content of the flow at the start of the current fragmentainer (see <see cref="ApplyFloatShift"/>).</summary>
        private double _shift;

        /// <summary>The box whose content is shifted at the start of the current fragmentainer, or null for the whole flow.</summary>
        private BoxFragment? _shiftedBox;

        /// <summary>
        /// The shift of the whole flow at the start of the next fragmentainer it is placed in: the flow is the contents of a box
        /// that is shifted by the flow of its container (see <see cref="ApplyFloatShift"/>).
        /// </summary>
        public double StartShift { get; set; }

        /// <summary>The box that is shifted by <see cref="StartShift"/>.</summary>
        public BoxFragment? StartShiftBox { get; set; }

        /// <summary>
        /// Whether the flow is pushed to the next fragmentainer only when it has no possible break in the current one (an item
        /// of a grid that is offset in its row: as in Blink, a break before it is not a possible break), else when it has no
        /// break that violates no rule.
        /// </summary>
        public bool BreaksBeforeAsLastResort { get; init; }

        /// <param name="includesStart">Whether the flow starts with its content: a forced break at its start, after a box without block size, is taken.</param>
        public FlowCursor(FragmentationFlow flow, FlowMap map, double start, double end, int index, int generation, bool canBreakBefore, bool includesStart = false)
        {
            _flow = flow;
            _map = map;
            _position = start;
            _end = end;
            _index = index;
            _generation = generation;
            _canBreakBefore = canBreakBefore;
            _candidateIndex = FindFirstAfter(flow.Candidates, includesStart ? start - (2 * Epsilon) : start);

            // The parallel flows that end before the start were placed by the cursor of the previous portion of the flow
            if (flow.Groups is { } groups)
            {
                while (_groupIndex < groups.Count && groups[_groupIndex].Start < start - Epsilon && groups[_groupIndex].TrackEnds[^1] <= start + Epsilon)
                {
                    _groupIndex++;
                }
            }
        }

        /// <summary>The position of the flow where the content of the next fragmentainer starts.</summary>
        public double Position => _position;

        /// <summary>The space at the start of the next fragmentainer before the content (see <see cref="FragmentainerBreak.DisplayOffset"/>).</summary>
        public double DisplayOffset => _displayOffset;

        /// <summary>The block size of the fragmentainer where the flow ended that is used by its content and by its parallel flows.</summary>
        public double LastExtent { get; private set; }

        AdvanceResult IItemCursor.Advance(double blockSize, int index) => Advance(blockSize);

        /// <summary>The offset of a position of the flow in the current fragmentainer.</summary>
        public double GetOffset(double position) => position - GetOrigin(position);

        private double GetOrigin(double position)
        {
            for (var i = _pieces.Count - 1; i > 0; i--)
            {
                if (_pieces[i].From <= position + Epsilon)
                    return _pieces[i].Origin;
            }

            return _pieces[0].Origin;
        }

        /// <summary>Sets the origin of the positions of the flow when it starts in the middle of a fragmentainer.</summary>
        public void SetOrigin(double origin) => _displayOffset = _position - origin;

        /// <summary>Sets the block-end edge of the first content of the flow, which is placed in its first fragmentainer even when it overflows it.</summary>
        public void SetLeadingEnd(double end) => _leadingEnd = end;

        /// <summary>
        /// Places the content of the flow in the next fragmentainer (https://drafts.csswg.org/css-break-3/#best-breaks): it ends at
        /// the forced break it contains, else at the latest possible break that keeps its content in it, preferring the breaks that
        /// violate no rule; when no possible break fits (monolithic content taller than the fragmentainer), the fragmentainer
        /// overflows and ends at the next possible break.
        /// </summary>
        public AdvanceResult Advance(double blockSize)
        {
            _pieces.Clear();
            _pieces.Add((double.NegativeInfinity, _position - _displayOffset));
            if (!double.IsNaN(_leadingEnd))
            {
                blockSize = Math.Max(blockSize, GetOffset(_leadingEnd));
                _leadingEnd = double.NaN;
            }

            ApplyFloatShift(blockSize);
            if (_flow.RepeatedTables is { } tables)
            {
                // https://drafts.csswg.org/css-tables-3/#repeated-headers: as in Blink, the header and the footer of a table are
                // repeated when they are not taller than a quarter of the fragmentainer where the table starts
                foreach (var (tableStart, _, sections) in tables)
                {
                    if (tableStart >= _position - Epsilon && GetOffset(tableStart) < blockSize - Epsilon)
                    {
                        sections.IsHeaderDisabled = sections.HeaderBlockSize * 4 > blockSize + Epsilon;
                        sections.IsFooterDisabled = sections.FooterBlockSize * 4 > blockSize + Epsilon;
                    }
                }
            }

            _penalties.Clear();
            _started.Clear();
            _overflowStart = double.PositiveInfinity;
            _overflowEnd = double.NegativeInfinity;
            _overflowOffset = 0;
            _overflowEndOffset = 0;
            _advanced.Clear();
            var shortage = double.PositiveInfinity;

            // The parallel flows: the ones that continue, then the ones that start in the fragmentainer, track after track
            foreach (var item in _open)
            {
                if (_shift > Epsilon && item.Cursor is FlowCursor itemCursor && (_shiftedBox is null || IsInside(item, _shiftedBox)))
                {
                    itemCursor.StartShift = _shift;
                    itemCursor.StartShiftBox = _shiftedBox;
                }

                AdvanceItem(item, blockSize, ref shortage);
            }

            var blockedFrom = double.PositiveInfinity;
            ParallelGroup? blockingGroup = null;
            var groups = _flow.Groups;
            if (groups is not null)
            {
                for (var i = _groupIndex; i < groups.Count; i++)
                {
                    var group = groups[i];
                    if (group.Generation != _generation)
                    {
                        group.Generation = _generation;
                        group.ClosedTracks = 0;
                    }

                    if (group.ClosedTracks == group.TrackStarts.Length)
                        continue;

                    // (a group without block size at the end of the flow is a part of it)
                    if ((group.Start >= _end - Epsilon && group.TrackEnds[^1] > _end + Epsilon) || group.Start > _end + Epsilon || !PlaceTracks(group, blockSize, ref shortage, ref blockedFrom))
                        break;

                    if (blockedFrom < double.PositiveInfinity)
                    {
                        blockingGroup = group;
                        break;
                    }
                }
            }

            // The boxes that are broken by the end of the fragmentainer
            foreach (var item in _advanced)
            {
                var itemAppeal = item.Appeal;
                var isItemUnfinished = !TryFinishBox(item, blockSize, ref shortage) || !item.IsFlowDone;
                if (isItemUnfinished && item.Avoid)
                {
                    itemAppeal = Worst(itemAppeal, BreakAppeal.Avoid);
                }

                if (isItemUnfinished || itemAppeal != BreakAppeal.Perfect)
                {
                    _penalties.Add((item.Start, itemAppeal, isItemUnfinished));
                }
            }

            var isUnfinished = false;
            var worstAppeal = BreakAppeal.Perfect;
            foreach (var penalty in _penalties)
            {
                isUnfinished |= penalty.IsUnfinished;
                worstAppeal = Worst(worstAppeal, penalty.Appeal);
            }

            // (the contents of a parallel flow end before the edges that the boxes around it repeat at the end of the fragmentainer)
            var candidates = _flow.Candidates;
            var endReserve = _flow.OwnerItem?.Group is { } ownerGroup ? ownerGroup.CloneEndEdges + (ownerGroup.HasEnded ? 0 : ownerGroup.OwnCloneEnd) : 0;
            if (!isUnfinished && GetOffset(_end) <= blockSize - endReserve + Epsilon && !HasForcedBreak())
            {
                SetLastExtent();
                Commit(double.PositiveInfinity);
                return new AdvanceResult(IsFinished: true, _end, 0, worstAppeal, shortage);
            }

            // The possible breaks of the flow
            BreakCandidate? forced = null;
            BreakCandidate? perfect = null;

            // The first break between line boxes that has enough line boxes before it (orphans) but not after it (widows)
            BreakCandidate? widows = null;
            BreakCandidate? avoid = null;
            BreakCandidate? lastResort = null;
            var scannedAll = true;

            // The breaks after a forced break that does not fit are not considered: the fragmentainer must end before it
            var scanEnd = Math.Min(_end, blockedFrom);
            for (var i = _candidateIndex; i < candidates.Count; i++)
            {
                var candidate = candidates[i].ResolveRepeated();
                if (candidate.Position >= _end - Epsilon)
                    break;

                if (candidate.IsBeforeFloat && !_flow.BreaksBeforeFloats)
                    continue;

                // The content after a track that continues in the next fragmentainer
                if (candidate.Position >= blockedFrom - Epsilon)
                {
                    scannedAll = false;
                    break;
                }

                if (candidate.IsPastEndOnly && GetOffset(candidate.Position) < blockSize - Epsilon)
                    continue;

                if (GetOffset(candidate.Position) > blockSize + Epsilon || candidate.IsPastEndOnly)
                {
                    // The fragmentainer ends in the gap before the content after the break: the gap disappears
                    if (_flow.GetGapStart(candidate.Position) is var gapStart && gapStart < candidate.Position - Epsilon
                        && GetOffset(gapStart) <= blockSize + Epsilon && IsValid(candidate.Position))
                    {
                        if (candidate.Appeal == BreakAppeal.Forced)
                        {
                            forced = candidate;
                            break;
                        }

                        Record(candidate);
                        continue;
                    }

                    // A forced break after a track whose monolithic item overflows the fragmentainer (the tracks after it are
                    // shifted after the item): the next track starts the next fragmentainer
                    if (candidate.Appeal == BreakAppeal.Forced && candidate.Position > _overflowStart + Epsilon && candidate.Position <= _overflowEnd + Epsilon
                        && candidate.Position > _position + Epsilon && ColumnBreaker.IsValid(_flow, candidate.Position, _position))
                    {
                        forced = candidate;
                        break;
                    }

                    // The fragmentainer ends in the margins between the content before the break and the content after it: the
                    // margins adjoining the break are truncated (https://drafts.csswg.org/css-break-3/#break-margins)
                    var endBefore = GetOffset(_flow.GetContentEndBefore(candidate)) + candidate.CloneEndEdges;
                    // (also in the part of the last line box that text-box-trim trims at the end of the fragmentainer)
                    if ((candidate.IsBetweenMargins || candidate.EndTrim > Epsilon) && endBefore <= blockSize + Epsilon && candidate.Position > _position + Epsilon && IsValid(candidate.Position))
                    {
                        if (candidate.IsPastEndOnly)
                        {
                            // (a fragmentainer that contains the box avoids the break: the balanced columns are stretched)
                            shortage = Math.Min(shortage, GetOffset(candidate.BoxEnd) - blockSize);
                        }

                        Record(candidate);
                        scannedAll = false;
                        break;
                    }

                    // The first content that does not fit. The content before a break inside a box, before its block-end edges,
                    // may fit while the edges do not (an empty box with a block size): the content after the next breaks, or the
                    // end of the flow, tells by how much the fragmentainer is too short
                    if (endBefore <= blockSize + Epsilon)
                        continue;

                    shortage = Math.Min(shortage, endBefore - blockSize);
                    scannedAll = false;
                    break;
                }

                if (candidate.Position <= _position + Epsilon)
                {
                    // https://drafts.csswg.org/css-break-3/#forced-breaks: a forced break at the start of the fragmentainer is taken
                    // when it is at a class A break point (after a box without block size), as in the browsers
                    if (candidate.Appeal == BreakAppeal.Forced && candidate.Position >= _position - Epsilon)
                    {
                        forced = candidate;
                        break;
                    }

                    continue;
                }

                // (a forced break between two tracks is taken beside the monolithic item of the first one that overflows the fragmentainer)
                if (!(candidate.Appeal == BreakAppeal.Forced ? ColumnBreaker.IsValid(_flow, candidate.Position, _position) : IsValid(candidate.Position)) || candidate.OnlyEdgesFrom <= _position + Epsilon)
                    continue;

                var contentEnd = GetOffset(_flow.GetContentEndBefore(candidate)) + candidate.CloneEndEdges;
                var fits = contentEnd <= blockSize + Epsilon;
                if (candidate.Appeal == BreakAppeal.Forced)
                {
                    if (fits)
                    {
                        forced = candidate;
                    }
                    else
                    {
                        shortage = Math.Min(shortage, contentEnd - blockSize);
                    }

                    scanEnd = candidate.Position;
                    scannedAll = false;
                    break;
                }

                if (fits)
                {
                    Record(candidate);
                }
                else
                {
                    shortage = Math.Min(shortage, contentEnd - blockSize);
                }
            }

            // The ranges of breakable positions: the latest position in the fragmentainer
            foreach (var unresolvedRange in _flow.Ranges)
            {
                var range = unresolvedRange.ResolveRepeated();
                if (range.Position >= scanEnd - Epsilon)
                    break;

                if (range.RangeEnd <= _position + Epsilon)
                    continue;

                var limit = GetOrigin(Math.Max(range.Position, _position)) + blockSize;
                if (range.Position > limit + Epsilon)
                {
                    shortage = Math.Min(shortage, range.Position + range.CloneEndEdges - limit);
                    continue;
                }

                // A break at the start of the range would be a break before the content of the box, which is not a possible break
                // (https://drafts.csswg.org/css-break-3/#possible-breaks: no class C break point without a gap)
                var rangePosition = _flow.ClampToValidPosition(Math.Min(Math.Min(range.RangeEnd, scanEnd), limit - range.CloneEndEdges), _position);
                if (double.IsNaN(rangePosition) || rangePosition < range.Position - Epsilon || (!range.IncludesRangeStart && rangePosition <= range.Position + Epsilon) || IsBesideOverflow(rangePosition))
                    continue;

                // A break at the end of the range is not a break inside the box: it is the break after its content (the class A
                // break between the box and its next sibling, with its own rules, or a class C break)
                if (range.ExcludesRangeEnd && rangePosition >= range.RangeEnd - Epsilon && HasCandidateAt(range.RangeEnd))
                    continue;

                if (range.IsInFloat && HasCandidateAt(rangePosition))
                    continue;

                Record(range with { Position = rangePosition, ContentEnd = rangePosition });
            }

            // A break inside a track: its parallel flows break independently (or the track has no content at the break)
            var limitPosition = GetLimitPosition(Math.Max(blockSize, _overflowOffset));
            if (forced is null)
            {
                if (blockingGroup is not null)
                {
                    // (the items of the track that continue are broken beside the monolithic item of the track that overflows)
                    var blockedInside = limitPosition - blockingGroup.CloneEndEdges - (RepeatsOwnEdges(blockingGroup) ? blockingGroup.OwnCloneEnd : 0);
                    if (RepeatsOwnEdges(blockingGroup) && blockingGroup.ClosedTracks < blockingGroup.TrackStarts.Length)
                    {
                        blockedInside = WithContentProgress(blockingGroup, blockingGroup.ClosedTracks, blockedInside, blockedFrom);
                    }

                    RecordInside(blockingGroup, Math.Min(blockedFrom, blockedInside), besideOverflow: true);
                }
                else if (groups is not null)
                {
                    for (var i = _groupIndex; i < groups.Count && groups[i].Start < limitPosition - Epsilon; i++)
                    {
                        var group = groups[i];
                        if (group.Generation != _generation)
                            continue;

                        // In a track, or in the gutter after it when it does not disappear at the breaks (an item spans across it)
                        for (var track = group.ClosedTracks; track < group.TrackStarts.Length; track++)
                        {
                            // (a box that repeats its own edges at the break is broken in its content box)
                            var repeatsOwnEdges = RepeatsOwnEdges(group);
                            var inside = limitPosition - group.CloneEndEdges - (repeatsOwnEdges ? group.OwnCloneEnd : 0);
                            var end = track + 1 < group.TrackStarts.Length ? group.TrackStarts[track + 1] : group.TrackEnds[track];
                            if (repeatsOwnEdges)
                            {
                                inside = WithContentProgress(group, track, inside, end);
                            }

                            if (group.TrackStarts[track] + (repeatsOwnEdges ? group.OwnCloneStart : 0) < inside - Epsilon && inside < end - Epsilon && (inside < group.TrackEnds[track] - Epsilon || _flow.SkipGap(inside) <= inside))
                            {
                                RecordInside(group, inside);
                            }
                        }
                    }
                }

                // The content of the flow ends in the fragmentainer, but not the one of its parallel flows (the boxes without block
                // size at the end of the flow are before the break: the contents that overflow them continue from this fragmentainer)
                if (isUnfinished && blockingGroup is null && GetOffset(_end) <= blockSize + Epsilon && !IsBesideOverflow(_end))
                {
                    Record(new BreakCandidate(_end, _end, BreakAppeal.Perfect) { Sequence = AfterContentSequence });
                }

                // The whole flow is pushed to the next fragmentainer (also the content that does not fit below the floats of
                // the fragmentainer, see ApplyFloatShift)
                if (_shift <= Epsilon && _canBreakBefore && BreaksBeforeAsLastResort)
                {
                    // (a break before the first content of a row: only when the item cannot be broken)
                    lastResort ??= new BreakCandidate(_position, _position, BreakAppeal.LastResort);
                }
                else if (_canBreakBefore || _shift > Epsilon)
                {
                    perfect ??= new BreakCandidate(_position, _position, BreakAppeal.Perfect);
                }
            }

            if (scannedAll)
            {
                shortage = Math.Min(shortage, GetOffset(_end) - blockSize);
            }

            BreakCandidate next;
            BreakAppeal appeal;
            var isMonolithicSlice = false;
            if (forced is { } forcedBreak)
            {
                (next, appeal) = (forcedBreak, BreakAppeal.Perfect);
            }
            else if (perfect is { } perfectBreak)
            {
                (next, appeal) = (perfectBreak, BreakAppeal.Perfect);
            }
            else if (widows is { } widowsBreak)
            {
                (next, appeal) = (widowsBreak, BreakAppeal.OrphansWidows);
            }
            else if (avoid is { } avoidBreak)
            {
                (next, appeal) = (avoidBreak, avoidBreak.Appeal == BreakAppeal.OrphansWidows ? BreakAppeal.OrphansWidows : BreakAppeal.Avoid);
            }
            else if (lastResort is { } lastResortBreak)
            {
                (next, appeal) = (lastResortBreak, BreakAppeal.LastResort);
            }
            else if (_flow.SlicesMonolithicContent && GetLimitPosition(blockSize) is var sliceEnd && sliceEnd > _position + Epsilon && sliceEnd < _end - Epsilon)
            {
                // The pages slice the monolithic content that does not fit in an empty page (PageBreakRules): the next page shows
                // the rest of it, as in the browsers (https://drafts.csswg.org/css-break-3/#monolithic-breaking)
                (next, appeal) = (new BreakCandidate(sliceEnd, sliceEnd, BreakAppeal.LastResort), BreakAppeal.LastResort);
                isMonolithicSlice = true;
            }
            else if (FindFirstValid(_flow, _candidateIndex, _position, Math.Min(_end, blockedFrom), _overflowStart, _overflowEnd) is { } overflow)
            {
                // Nothing fits: the fragmentainer overflows with its unbreakable content, and the next one starts at the next possible
                // break (a taller fragmentainer would avoid it: as a break that violates a rule, it makes the balancing stretch the columns)
                (next, appeal) = (overflow.ResolveRepeated(), BreakAppeal.LastResort);
            }
            else if (isUnfinished && blockingGroup is null && _end > _position + Epsilon)
            {
                // The content of the flow overflows the fragmentainer and ends; its parallel flows continue in the next one
                (next, appeal) = (new BreakCandidate(_end, _end, BreakAppeal.LastResort), BreakAppeal.LastResort);
            }
            else
            {
                // The rest of the content does not fit in the last fragmentainer
                SetLastExtent();
                Commit(double.PositiveInfinity);
                return new AdvanceResult(IsFinished: true, _end, 0, BreakAppeal.LastResort, shortage);
            }

            // A flow that is pushed to the next fragmentainer starts again there
            var isPushed = (_canBreakBefore || _shift > Epsilon) && next.Position <= _position + Epsilon;
            Commit(next.Position, isPushed, isAfterContent: next.Sequence == AfterContentSequence);

            // https://drafts.csswg.org/css-gaps-1/#gaps: "the gap disappears when it coincides with a fragmentation break"
            // (also the block-end margin of the last child of a box: https://drafts.csswg.org/css-break-3/#break-margins)
            var position = next.Position;
            if (!next.KeepsGap && _flow.SkipGap(position) is var afterGap && afterGap > position && afterGap <= _end + Epsilon)
            {
                position = afterGap;
            }

            // (the edges repeated at the break by a box whose border box has a specified block size are taken from it)
            position += next.SkipAfter;

            // The boxes that end in the truncated gap end at the end of the fragmentainer
            var endBeforeBreak = Math.Min(next.ContentEnd, next.Position);
            if (_flow.GetGapStart(position) is var truncatedFrom && truncatedFrom < position - Epsilon)
            {
                endBeforeBreak = Math.Min(endBeforeBreak, Math.Max(truncatedFrom, _position));
                _map.AddTruncation(truncatedFrom, position, _index, Math.Max(blockSize, GetPlacedOffset(truncatedFrom)));
            }

            if (position >= _end - Epsilon && !isUnfinished && next.Appeal != BreakAppeal.Forced)
            {
                // The flow ends with the truncated gap
                LastExtent = Math.Min(blockSize, GetPlacedOffset(position));
                return new AdvanceResult(IsFinished: true, _end, 0, appeal, shortage);
            }

            // (the breaks at the same position that follow the break in the content are not taken yet)
            var sequence = Math.Abs(position - next.Position) <= Epsilon ? next.Sequence : 0;
            if (_flow.OwnerItem is not null)
            {
                _map.SetBreakEnd(_index, Math.Max(blockSize, GetPlacedOffset(endBeforeBreak)));
            }

            // https://drafts.csswg.org/css-inline-3/#text-box-trim: the first formatted line of the fragmentainer is trimmed on its
            // block-start side: the trimmed part of the line box is before the block-start edge of the fragmentainer
            var displayOffset = next.CloneStartEdges - (Math.Abs(position - next.Position) <= Epsilon ? next.StartTrim : 0);
            _index++;
            _map.Add(position, _index, position - displayOffset, isBreak: true, sequence);
            _position = position;
            _displayOffset = displayOffset;
            _canBreakBefore = false;
            _candidateIndex = FindFirstAfter(candidates, position - (2 * Epsilon));
            while (_candidateIndex < candidates.Count && candidates[_candidateIndex].Position <= position + Epsilon && (sequence == 0 || candidates[_candidateIndex].Sequence <= sequence))
            {
                _candidateIndex++;
            }

            // (the content before the break ends with the edges that the boxes broken there repeat: they overflow the fragmentainer
            // when the repeated edges do not fit in it)
            if (isMonolithicSlice)
            {
                _flow.AddMonolithicSlice(_index);
            }

            return new AdvanceResult(IsFinished: false, position, displayOffset, appeal, shortage) { ContentEnd = endBeforeBreak + next.CloneEndEdges, IsMonolithicSlice = isMonolithicSlice };

            // Records a possible break that fits: the latest one of the best appeal is chosen. The parallel flows that start before the
            // break and are broken by the end of the fragmentainer lower its appeal
            void Record(BreakCandidate candidate)
            {
                // A break beside a box with break-inside: avoid (in a parallel flow, or overflowing content) breaks the box
                var effective = candidate.Appeal;
                var violatesWidowsOnly = false;
                if (candidate.Lines is { } lines && effective is BreakAppeal.Perfect or BreakAppeal.OrphansWidows)
                {
                    // https://drafts.csswg.org/css-break-3/#widows-orphans: as in Blink, the line boxes before the break are counted
                    // in the fragmentainer (the fragment of the block container)
                    var before = candidate.LineIndex - lines.GetFirstLine(_position);
                    var after = lines.Tops.Length - candidate.LineIndex;
                    effective = before >= lines.Orphans && after >= lines.Widows ? BreakAppeal.Perfect : BreakAppeal.OrphansWidows;
                    violatesWidowsOnly = effective == BreakAppeal.OrphansWidows && before >= lines.Orphans;
                }

                if (effective == BreakAppeal.Perfect && _flow.IsInsideAvoidedBox(candidate.Position))
                {
                    effective = BreakAppeal.Avoid;
                }

                foreach (var penalty in _penalties)
                {
                    if (penalty.Start < candidate.Position - Epsilon)
                    {
                        effective = Worst(effective, penalty.Appeal);
                    }
                }

                if (violatesWidowsOnly && effective == BreakAppeal.OrphansWidows)
                {
                    // (the first one of the last block container)
                    if (widows is not { } firstBreak || !ReferenceEquals(firstBreak.Lines, candidate.Lines))
                    {
                        widows = candidate with { Appeal = effective };
                    }

                    return;
                }

                ref var best = ref perfect;
                if (effective is BreakAppeal.Avoid or BreakAppeal.OrphansWidows)
                {
                    best = ref avoid;
                    candidate = candidate with { Appeal = effective };
                }
                else if (effective == BreakAppeal.LastResort)
                {
                    best = ref lastResort;
                }

                // (the breaks inside boxes with break-inside: avoid prefer the ones inside fewer of them, see BreakCandidate.AvoidDepth)
                if (best is not { } current
                    || (current.AvoidDepth > 0 && candidate.AvoidDepth > 0 && current.AvoidDepth != candidate.AvoidDepth && effective == BreakAppeal.Avoid && current.Appeal == BreakAppeal.Avoid
                        ? candidate.AvoidDepth < current.AvoidDepth
                        : candidate.Position >= current.Position))
                {
                    best = candidate;
                }
            }

            void RecordInside(ParallelGroup group, double position, bool besideOverflow = false)
            {
                position = _flow.ClampToValidPosition(position, _position - (2 * Epsilon));
                if (double.IsNaN(position) || position < _position - Epsilon || (IsBesideOverflow(position) && !besideOverflow))
                    return;

                var repeatsOwnEdges = RepeatsOwnEdges(group);
                if (repeatsOwnEdges && position <= _position + Epsilon)
                    return;

                // (no break inside the block-end edges of the box: a break before them, when the content progresses)
                var groupEnd = group.TrackEnds[^1];
                if (group.BlockEndEdges > Epsilon && position > groupEnd - group.BlockEndEdges + Epsilon && position < groupEnd - Epsilon)
                {
                    position = groupEnd - group.BlockEndEdges;
                    if (position <= _position + Epsilon)
                        return;
                }

                Record(new BreakCandidate(position, position, group.Avoid ? BreakAppeal.Avoid : BreakAppeal.Perfect)
                {
                    AvoidDepth = group.AvoidDepth,
                    CloneStartEdges = group.CloneStartEdges + (repeatsOwnEdges ? group.OwnCloneStart : 0),
                    CloneEndEdges = group.CloneEndEdges + (repeatsOwnEdges ? group.OwnCloneEnd : 0),
                    KeepsGap = true,
                    SkipAfter = repeatsOwnEdges && group.ShrinksAtBreaks ? Math.Min(group.OwnCloneStart + group.OwnCloneEnd, Math.Max(0, group.TrackEnds[^1] - group.OwnCloneEnd - position)) : 0,
                });
            }

            // The repeated edges of a box take the whole fragmentainer: as in Blink and Gecko, each fragment of the box has 1px
            // of its content at least (the fragmentation makes progress), and overflows the fragmentainer
            double WithContentProgress(ParallelGroup group, int track, double inside, double end)
            {
                // (the box ends in the fragmentainer when the rest of its content is not taller: no fragment with its edges only)
                var progress = Math.Max(group.TrackStarts[track] + group.OwnCloneStart, _position) + MinimumContentProgress;
                return progress < end - group.OwnCloneEnd - Epsilon ? Math.Max(inside, progress) : inside;
            }

            bool IsValid(double position) => !IsBesideOverflow(position) && _flow.ClampToValidPosition(position, _position) is var clamped && !double.IsNaN(clamped) && Math.Abs(clamped - position) < Epsilon;

            // (a box does not repeat its edges at a break that slices its monolithic contents: as in Blink, the page grows over them)
            bool RepeatsOwnEdges(ParallelGroup group)
                => !group.HasEnded && group.OwnCloneStart + group.OwnCloneEnd > 0 && !group.Items.Exists(item => item.IsMonolithicSlice && item.AdvancedIndex == _index);
        }

        /// <summary>The smallest block size of the content of a fragment of a box whose repeated edges (<c>box-decoration-break: clone</c>) do not fit in a fragmentainer.</summary>
        private const double MinimumContentProgress = 1;

        private bool IsBesideOverflow(double position) => position > _overflowStart + Epsilon && position < _overflowEnd - Epsilon;

        /// <summary>
        /// Shifts the content of the flow at the start of the current fragmentainer below the pushed floats that a box of the flow
        /// avoids (see <see cref="FragmentationFlow.GetFloatAvoider"/>): the box was laid out in the strip before the float, which
        /// is in a later fragmentainer, so its part in the fragmentainer of the float (and in the next ones, while the float
        /// continues) starts below the float, as in Blink. These floats are fragmented apart, by the block size of the
        /// fragmentainers (they are not shifted with the content).
        /// </summary>
        private void ApplyFloatShift(double blockSize)
        {
            _shift = StartShift;
            _shiftedBox = null;
            StartShift = 0;
            if (_shift > Epsilon && StartShiftBox is { } shiftedBox && ReferenceEquals(_flow.OwnerItem?.Box, shiftedBox))
            {
                // (the flow is the contents of the box that is shifted: the slicer places its fragment with the breaks of its contents)
                _map.AddStartShift(_index, _shift, shiftedBox);
            }

            StartShiftBox = null;
            if (_flow.HasPushedFloats && _flow.OwnFloats is { } floats)
            {
                // The floats that start the fragmentainer
                for (; _floatIndex < floats.Count && floats[_floatIndex].Start <= _position + Epsilon; _floatIndex++)
                {
                    var extent = floats[_floatIndex];
                    if (extent.IsPushed && extent.Start >= _position - Epsilon && _flow.GetFloatAvoider(extent, _position) is not null)
                    {
                        var map = new FlowMap();
                        _flow.SetFloatMap(extent.Fragment, map);
                        (_floats ??= []).Add((extent, 0, map));
                    }
                }

                if (_floats is not null)
                {
                    var shift = 0d;
                    for (var i = 0; i < _floats.Count; i++)
                    {
                        var (extent, consumed, map) = _floats[i];
                        var remaining = extent.End - extent.Start - consumed;
                        if (remaining <= Epsilon)
                            continue;

                        map.Add(consumed == 0 ? double.NegativeInfinity : extent.Start + consumed, _index, extent.Start + consumed - _displayOffset, isBreak: true);
                        if (_flow.GetFloatAvoider(extent, _position) is { } avoider)
                        {
                            shift = Math.Max(shift, Math.Min(remaining, blockSize - _displayOffset));
                            _shiftedBox = avoider;
                        }

                        _floats[i] = (extent, extent.IsMonolithic ? consumed + remaining : consumed + Math.Max(MinFloatProgress, blockSize - _displayOffset), map);
                    }

                    if (shift > Epsilon && _shift <= Epsilon)
                    {
                        _shift = shift;
                        _map.AddStartShift(_index, shift, _shiftedBox!);
                    }
                    else
                    {
                        _shiftedBox = null;
                    }
                }
            }

            if (_shift > Epsilon)
            {
                AddShift(_position, _shift);
            }
        }

        /// <summary>The smallest block size of a float that a fragmentainer consumes (the fragmentation makes progress).</summary>
        private const double MinFloatProgress = 1;

        /// <summary>Returns whether a parallel flow is in a box (the box of its container, or a descendant).</summary>
        private static bool IsInside(ParallelItem item, BoxFragment box)
        {
            for (Fragment? current = item.Group?.Container; current is not null; current = current.Parent)
            {
                if (ReferenceEquals(current, box))
                    return true;
            }

            return false;
        }

        /// <summary>Sets <see cref="LastExtent"/> when the flow ends in the current fragmentainer.</summary>
        private void SetLastExtent()
        {
            var extent = GetOffset(_end);
            foreach (var item in _advanced)
            {
                if (item.IsBoxDone && item.EndIndex == _index)
                {
                    extent = Math.Max(extent, item.EndOffset);
                }

                if (item.AdvancedIndex == _index && item.Cursor is FlowCursor cursor)
                {
                    extent = Math.Max(extent, cursor.LastExtent);
                }
            }

            LastExtent = extent;
        }

        /// <summary>Shifts the content of the flow from a position in the current fragmentainer.</summary>
        private void AddShift(double from, double delta)
        {
            var index = _pieces.Count;
            while (index > 1 && _pieces[index - 1].From > from + Epsilon)
            {
                index--;
            }

            var origin = _pieces[index - 1].Origin - delta;
            if (index > 1 && Math.Abs(_pieces[index - 1].From - from) <= Epsilon)
            {
                _pieces[index - 1] = (from, origin);
            }
            else
            {
                _pieces.Insert(index, (from, origin));
                index++;
            }

            for (var i = index; i < _pieces.Count; i++)
            {
                _pieces[i] = (_pieces[i].From, _pieces[i].Origin - delta);
            }
        }

        /// <summary>Returns whether the flow has a possible break at a position (not in a range).</summary>
        private bool HasCandidateAt(double position)
        {
            var candidates = _flow.Candidates;
            for (var i = FindFirstAfter(candidates, position - (2 * Epsilon)); i < candidates.Count && candidates[i].Position <= position + Epsilon; i++)
            {
                if (Math.Abs(candidates[i].Position - position) <= Epsilon)
                    return true;
            }

            return false;
        }

        /// <summary>Returns whether the rest of the flow has a forced break.</summary>
        private bool HasForcedBreak()
        {
            var candidates = _flow.Candidates;
            for (var i = _candidateIndex; i < candidates.Count && candidates[i].Position < _end - Epsilon; i++)
            {
                if (candidates[i].Appeal == BreakAppeal.Forced && candidates[i].Position >= _position - Epsilon)
                    return true;
            }

            return false;
        }

        /// <summary>Returns the position of the flow that is at the end of the current fragmentainer.</summary>
        private double GetLimitPosition(double blockSize)
        {
            for (var i = 0; i < _pieces.Count; i++)
            {
                var position = _pieces[i].Origin + blockSize;
                if (i + 1 == _pieces.Count || position < _pieces[i + 1].From)
                    return Math.Max(position, i == 0 ? position : _pieces[i].From);
            }

            return _pieces[^1].Origin + blockSize;
        }

        /// <summary>
        /// Places the tracks of a group that are in the current fragmentainer: starts their parallel flows, and ends the tracks whose
        /// items all end in the fragmentainer (the content after a track that grew is shifted). Returns false when the next track
        /// starts after the fragmentainer; sets <paramref name="blockedFrom"/> to the end of the track that continues in the next
        /// fragmentainer.
        /// </summary>
        private bool PlaceTracks(ParallelGroup group, double blockSize, ref double shortage, ref double blockedFrom)
        {
            for (var track = group.ClosedTracks; track < group.TrackStarts.Length; track++)
            {
                var isAfterFragmentainer = false;
                var spillover = GetSpannedGapSpillover(group, track, _position);
                foreach (var item in group.StartingItems[track])
                {
                    if (item.Generation == _generation)
                        continue;

                    // An item at the start of the fragmentainer is placed in it even when it does not fit
                    // (an item that starts before its track, with a negative margin, starts with the track)
                    var offset = GetOffset(item.Start) - spillover;
                    var flowStart = Math.Max(item.Start, group.TrackStarts[track]);
                    if (GetOffset(flowStart) - spillover >= blockSize - Epsilon && flowStart > _position + Epsilon)
                    {
                        isAfterFragmentainer = true;
                        continue;
                    }

                    item.Generation = _generation;
                    item.IsBoxDone = false;
                    item.Group!.HasEnded = false;
                    item.IsFlowDone = false;
                    item.IsContentPlaced = false;
                    item.Map = new FlowMap();
                    item.Map.Add(double.NegativeInfinity, _index, GetOrigin(item.Start) + spillover, isBreak: false);
                    if (item.Nested is { } nested)
                    {
                        item.Cursor = new NestedColumnsCursor(item, nested, offset);
                    }
                    else
                    {
                        // (a new generation: the parallel flows of the item start again when the item does, after a break before it)
                        var cursor = new FlowCursor(item.Flow, item.Map, item.Start, item.Flow.ContentEnd, _index, item.Flow.NextGeneration(), item.CanBreakBefore && offset > Epsilon, includesStart: true) { BreaksBeforeAsLastResort = item.BreaksBeforeAsLastResort };
                        cursor.SetOrigin(GetOrigin(item.Start) + spillover);
                        if (!item.CanGrow && !double.IsNaN(item.LeadingMonolithicEnd) && GetOffset(item.End) <= blockSize + Epsilon)
                        {
                            cursor.SetLeadingEnd(item.LeadingMonolithicEnd);
                        }

                        item.Cursor = cursor;
                    }

                    _started.Add(item);
                    AdvanceItem(item, blockSize, ref shortage);
                }

                if (isAfterFragmentainer)
                    return false;

                // The track ends when its items end
                var isNestedTrack = group.Items is [{ Nested: not null }] && ReferenceEquals(group.Container, group.Items[0].Box);
                var trackEnd = group.TrackEnds[track];
                var end = GetOffset(trackEnd);
                var endMargin = 0d;
                foreach (var item in group.EndingItems[track])
                {
                    if (!item.CountsForTrack)
                        continue;

                    if (item.Generation != _generation || !TryFinishBox(item, blockSize, ref shortage))
                    {
                        blockedFrom = trackEnd;
                        return true;
                    }

                    if (item.EndIndex == _index && group.CanGrow)
                    {
                        // (a nested multi-column container may also be shorter than in the strip, where its rows are not limited)
                        end = isNestedTrack ? item.EndOffset : Math.Max(end, item.EndOffset + item.MarginEnd);
                        endMargin = Math.Max(endMargin, item.MarginEnd);
                    }
                }

                if (end > GetOffset(trackEnd) + Epsilon || (end < GetOffset(trackEnd) - Epsilon && isNestedTrack))
                {
                    // The content after the track is shifted (it may overlap the end margins of the items)
                    AddShift(Math.Max(_position, trackEnd - endMargin), end - GetOffset(trackEnd));
                }
            }

            return true;
        }

        /// <summary>Places a parallel flow in the current fragmentainer.</summary>
        private void AdvanceItem(ParallelItem item, double blockSize, ref double shortage)
        {
            _advanced.Add(item);
            item.Appeal = BreakAppeal.Perfect;
            item.OverflowExtent = 0;
            if (item.IsFlowDone)
                return;

            var cursor = (IItemCursor)item.Cursor!;
            if (item.Group is { OwnCloneStart: > 0 } or { OwnCloneEnd: > 0 })
            {
                // The edges that the box of the flow repeats are not repeated once the box has ended
                item.Group.HasEnded = item.IsBoxDone || GetOffset(item.End) <= blockSize - item.Group.CloneEndEdges + Epsilon;
            }

            var result = cursor.Advance(blockSize, _index);
            shortage = Math.Min(shortage, result.Shortage);
            item.IsFlowDone = result.IsFinished;
            // (as in Blink, the orphans and widows of an item do not push its track to the next fragmentainer)
            // (they do in the contents of a box of a block container that are a parallel flow)
            item.Appeal = result.Appeal == BreakAppeal.Forced || (result.Appeal == BreakAppeal.OrphansWidows && !item.IsDefiniteBox) ? BreakAppeal.Perfect : result.Appeal;
            item.AdvancedIndex = _index;
            item.IsMonolithicSlice = result.IsMonolithicSlice;
            item.IsContentPlaced = result.IsFinished || (!item.IsMonolithic && result.Position >= item.IntrinsicEnd - Epsilon);
            if (item.Appeal != BreakAppeal.LastResort)
                return;

            if (item.IsDefiniteBox)
            {
                // The box extends over its monolithic contents that overflow the fragmentainer, up to its block size (as in
                // Blink); the flow of the container breaks beside the contents that overflow the box
                item.OverflowExtent = cursor.GetPlacedOffset(result.IsFinished ? item.Flow.ContentEnd : item.Flow.GetGapStart(result.Position));
                _overflowOffset = Math.Max(_overflowOffset, Math.Min(item.OverflowExtent, GetOffset(item.End)));
                return;
            }

            if (result.IsFinished)
            {
                // The item overflows the fragmentainer (monolithic content): its track ends after it
                _overflowStart = Math.Min(_overflowStart, item.Start);
                _overflowEnd = Math.Max(_overflowEnd, Math.Max(item.End, item.Flow.ContentEnd));
                _overflowEndOffset = Math.Max(_overflowEndOffset, cursor.GetPlacedOffset(item.End));

                // (the item ends after the fragmentainer; the other items of its track are broken at the end of the fragmentainer)
                item.OverflowExtent = cursor.GetPlacedOffset(Math.Max(item.End, item.Flow.ContentEnd));
            }
            else
            {
                // The fragmentainer overflows with the unbreakable content of the item: the flow continues after it
                // (after the content: the gap before the next content disappears at the break)
                _overflowOffset = Math.Max(_overflowOffset, cursor.GetPlacedOffset(item.Flow.GetGapStart(result.Position)));
            }
        }

        /// <summary>
        /// Places the block-end edge of the box of a parallel flow when it is in the current fragmentainer: it follows the contents
        /// of the box when its block size is automatic, and is at least where the flow of the container places it.
        /// </summary>
        private bool TryFinishBox(ParallelItem item, double blockSize, ref double shortage)
        {
            if (item.IsBoxDone)
                return true;

            // The block-end edges repeated by the boxes with box-decoration-break: clone need room at the end of the fragmentainer
            // (as in Blink, an item whose block size follows its track ends with the monolithic item of the track that overflows
            // the fragmentainer; an item with a definite block size is broken at the end of the fragmentainer beside it)
            var available = Math.Max(Math.Max(blockSize - item.Group!.CloneEndEdges, item.CanGrow ? _overflowEndOffset : 0), item.OverflowExtent);
            var placed = item.IsFlowDone || item.IsContentPlaced;
            var end = GetOffset(item.End);
            var isNested = item.Nested is not null && item.CanGrow;
            if ((placed || !item.CanGrow) && (item.IsMonolithic ? item.IsFlowDone : isNested || end <= available + Epsilon))
            {
                if (placed && item.CanGrow && item.AdvancedIndex == _index)
                {
                    var placedEnd = ((IItemCursor)item.Cursor!).GetPlacedOffset(item.IntrinsicEnd);
                    end = (isNested && !item.HasMinimumEnd) || item.IsRowSubgrid ? placedEnd : Math.Max(end, placedEnd);
                }
                else if (item.IsRowSubgrid && item.IsFlowDone && item.AdvancedIndex == _index)
                {
                    // (a subgrid ends with its rows, without the gaps that disappeared at its breaks)
                    end = Math.Min(end, ((IItemCursor)item.Cursor!).GetPlacedOffset(item.IntrinsicEnd));
                }

                item.IsBoxDone = true;
                item.EndIndex = _index;
                item.EndOffset = end;
                return true;
            }

            if (!item.CanGrow || placed)
            {
                shortage = Math.Min(shortage, end - available);
            }

            return false;
        }

        /// <summary>Returns the offset of a position in the fragmentainer that was just filled (before the break).</summary>
        public double GetPlacedOffset(double position) => _lastPieces.Count == 0 ? GetOffset(position) : position - GetLastOrigin(position);

        private readonly List<(double From, double Origin)> _lastPieces = [];

        private double GetLastOrigin(double position)
        {
            for (var i = _lastPieces.Count - 1; i > 0; i--)
            {
                if (_lastPieces[i].From <= position + Epsilon)
                    return _lastPieces[i].Origin;
            }

            return _lastPieces[0].Origin;
        }

        /// <summary>
        /// Ends the current fragmentainer with a break at a position: the parallel flows that start after the break have not
        /// started, the tracks whose items are placed are closed, and the shifts of the content before the break are recorded.
        /// </summary>
        /// <summary>
        /// The order of the break after the content of a flow whose parallel flows continue (see <see cref="BreakCandidate.Sequence"/>):
        /// the boxes without block size at its position are before it, with the start of the contents that overflow them.
        /// </summary>
        private const int AfterContentSequence = int.MaxValue;

        private void Commit(double position, bool isPushed = false, bool isAfterContent = false)
        {
            _lastPieces.Clear();
            _lastPieces.AddRange(_pieces);
            for (var i = 1; i < _pieces.Count; i++)
            {
                if (_pieces[i].From <= position + Epsilon)
                {
                    _map.Add(_pieces[i].From, _index, _pieces[i].Origin, isBreak: false);
                }
            }

            _nextOpen.Clear();
            foreach (var item in _open)
            {
                if (!item.IsFlowDone || !item.IsBoxDone)
                {
                    _nextOpen.Add(item);
                }
            }

            foreach (var item in _started)
            {
                // (an item starts with its track at the earliest)
                var itemStart = Math.Max(item.Start, item.Group!.TrackStarts[item.StartTrack]);
                if (isPushed || (itemStart >= position - (isAfterContent ? -Epsilon : Epsilon) && itemStart > _position + Epsilon))
                {
                    // The item is after the break: it starts in the next fragmentainer
                    item.Generation = 0;
                    item.Map = null;
                    item.Cursor = null;
                }
                else if (!item.IsFlowDone || !item.IsBoxDone)
                {
                    _nextOpen.Add(item);
                }
            }

            (_open, _nextOpen) = (_nextOpen, _open);
            var groups = _flow.Groups;
            if (groups is null || isPushed)
                return;

            for (var i = _groupIndex; i < groups.Count; i++)
            {
                var group = groups[i];
                if (group.Generation != _generation)
                    break;

                var track = group.ClosedTracks;
                for (; track < group.TrackStarts.Length; track++)
                {
                    if (!IsClosed(group, track, position))
                        break;
                }

                group.ClosedTracks = track;
                if (track == group.TrackStarts.Length && i == _groupIndex)
                {
                    _groupIndex++;
                }
            }
        }

        private bool IsClosed(ParallelGroup group, int track, double position)
        {
            if (group.TrackStarts[track] >= position - Epsilon && group.TrackStarts[track] > _position + Epsilon)
                return false;

            foreach (var item in group.StartingItems[track])
            {
                if (item.Generation != _generation)
                    return false;
            }

            foreach (var item in group.EndingItems[track])
            {
                if (item.CountsForTrack && (item.Generation != _generation || !item.IsBoxDone))
                    return false;
            }

            return group.StartingItems[track].Count > 0 || group.EndingItems[track].Count > 0 || group.TrackEnds[track] <= position + Epsilon;
        }

        private static BreakAppeal Worst(BreakAppeal a, BreakAppeal b) => a > b ? a : b;
    }

    /// <summary>
    /// Returns the block size of balanced columns (https://drafts.csswg.org/css-multicol-1/#cf): the smallest block size for which
    /// the content of the portion of the strip fits in <paramref name="columnCount"/> columns, as in the browsers: the initial
    /// block size distributes the content between the forced breaks into the columns (and is at least the tallest unbreakable
    /// content), then the columns are stretched by the minimal space shortage until the content fits without violating a rule,
    /// or the maximum block size is reached.
    /// </summary>
    /// <param name="flow">The strip.</param>
    /// <param name="start">The start of the portion of the strip.</param>
    /// <param name="end">The end of the portion.</param>
    /// <param name="columnCount">The number of columns.</param>
    /// <param name="maxBlockSize">The maximum block size of the columns (the available block size), or +∞.</param>
    /// <param name="breaks">A buffer for the breaks.</param>
    /// <param name="extentEnd">The end of the content of the portion and of its parallel flows.</param>
    /// <param name="includesEnd">Whether the portion is the last one of the strip.</param>
    /// <param name="outOfFlow">The fragmentainers before the portion, when the columns are the last ones of the strip: they are stretched until the absolutely positioned boxes fit in them (see <see cref="PlaceOutOfFlow"/>).</param>
    public static double Balance(FragmentationFlow flow, double start, double end, int columnCount, double maxBlockSize, List<FragmentainerBreak> breaks, double extentEnd = double.NaN, bool includesEnd = false, OutOfFlowFragmentainers? outOfFlow = null)
    {
        if (flow.Floats is null)
            return BalanceCore(flow, start, end, columnCount, maxBlockSize, breaks, extentEnd, includesEnd, outOfFlow);

        // The floats that do not fit in a column are pushed to the next one: the columns are balanced as if the flow could
        // break before them (the content is laid out again once the floats are pushed, see FloatPush)
        flow.BreaksBeforeFloats = true;
        try
        {
            return BalanceCore(flow, start, end, columnCount, maxBlockSize, breaks, extentEnd, includesEnd, outOfFlow);
        }
        finally
        {
            flow.BreaksBeforeFloats = false;
        }
    }

    private static double BalanceCore(FragmentationFlow flow, double start, double end, int columnCount, double maxBlockSize, List<FragmentainerBreak> breaks, double extentEnd, bool includesEnd, OutOfFlowFragmentainers? outOfFlow)
    {
        // (as in Blink, the absolutely positioned boxes are content of the columns for their balancing)
        extentEnd = double.IsNaN(extentEnd) ? end : Math.Max(end, extentEnd);
        extentEnd = flow.GetOutOfFlowExtent(start, end, includesEnd, extentEnd);
        if (flow.OutOfFlowItems is null)
        {
            outOfFlow = null;
        }

        if (extentEnd - start <= Epsilon && outOfFlow is null)
            return 0;

        // The content runs between the forced breaks, and the implicit breaks distributed to the tallest runs
        // (each break in a table that repeats its header and its footer adds their block size to the content)
        var repeatedSize = flow.GetRepeatedSize(start, end);
        var runs = new List<(double Length, int Breaks)>();
        var runStart = start;
        foreach (var candidate in flow.GetForcedBreaks(start, end))
        {
            runs.Add((Math.Max(0, candidate.ContentEnd - runStart), 0));
            runStart = candidate.Position;
        }

        runs.Add((Math.Max(0, extentEnd - runStart), 0));
        var forcedBreakCount = runs.Count - 1;
        for (var found = runs.Count; found < columnCount; found++)
        {
            var tallest = 0;
            for (var i = 1; i < runs.Count; i++)
            {
                if (GetRunBlockSize(runs[i]) > GetRunBlockSize(runs[tallest]))
                {
                    tallest = i;
                }
            }

            runs[tallest] = (runs[tallest].Length, runs[tallest].Breaks + 1);
        }

        var blockSize = 0d;
        foreach (var run in runs)
        {
            blockSize = Math.Max(blockSize, GetRunBlockSize(run));
        }

        blockSize = Math.Max(blockSize, flow.GetOutOfFlowTallestUnbreakable(start, end, includesEnd));
        blockSize = Math.Min(CeilToLayoutUnit(Math.Max(blockSize, flow.GetTallestUnbreakable(start, end))), maxBlockSize);
        for (var iteration = 0; iteration < 64; iteration++)
        {
            bool violating;
            double shortage;
            var count = 0;
            if (outOfFlow is null)
            {
                violating = FindBreaks(flow, start, end, blockSize, breaks, out shortage);
            }
            else
            {
                // The absolutely positioned boxes need new columns after the last ones, or overflow them: a block size that is
                // too small by the block size of what is in the new columns, shared between the columns, at least
                var baseIndex = outOfFlow.BlockSizes.Count;
                var map = outOfFlow.Map.Clone();
                violating = FindBreaks(flow, start, end, blockSize, breaks, out shortage, map, baseIndex);
                var placed = PlaceOutOfFlow(flow, map, outOfFlow.BlockSizes, blockSize, outOfFlow.PortionStarts, baseIndex + columnCount);
                violating |= placed.IsViolating;
                count = placed.Count - baseIndex;
                shortage = Math.Min(shortage, placed.Shortage);
                if (placed.Overflow > Epsilon)
                {
                    var needed = placed.Overflow / columnCount;
                    shortage = double.IsPositiveInfinity(shortage) ? needed : Math.Max(shortage, needed);
                }
            }

            count = Math.Max(count, breaks.Count + 1);
            if (!violating && count <= columnCount)
                break;

            // No stretching avoids the overflow when the forced breaks already create the columns
            if (columnCount <= forcedBreakCount + 1 || double.IsPositiveInfinity(shortage))
                break;

            var stretched = Math.Min(CeilToLayoutUnit(blockSize + shortage), maxBlockSize);
            if (stretched <= blockSize)
                break;

            blockSize = stretched;
        }

        return blockSize;

        double GetRunBlockSize((double Length, int Breaks) run) => CeilToLayoutUnit((run.Length + (run.Breaks * repeatedSize)) / (run.Breaks + 1));
    }

    /// <summary>Rounds a block size up to the precision of the layout units of the browsers (1/64 px), so that the balanced columns are not shorter than their contents.</summary>
    private static double CeilToLayoutUnit(double value) => Math.Ceiling(value * 64 - 1e-6) / 64;

    private static bool IsValid(FragmentationFlow flow, double position, double current)
        => flow.ClampToValidPosition(position, current) is var clamped && !double.IsNaN(clamped) && Math.Abs(clamped - position) < Epsilon;

    /// <summary>Returns the first possible break after a position (any appeal), or null.</summary>
    private static BreakCandidate? FindFirstValid(FragmentationFlow flow, int index, double current, double end, double excludedStart = double.PositiveInfinity, double excludedEnd = double.NegativeInfinity)
    {
        BreakCandidate? result = null;
        var candidates = flow.Candidates;
        for (var i = index; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            if (candidate.Position >= end - Epsilon)
                break;

            // (as in Blink, the block-end edges of a box are placed with its last content when it overflows the fragmentainer)
            if ((candidate.IsBeforeFloat && !flow.BreaksBeforeFloats) || candidate.IsBeforeEndEdges || candidate.IsPastEndOnly)
                continue;

            if (candidate.Position > current + Epsilon && !(candidate.OnlyEdgesFrom <= current + Epsilon) && IsValid(flow, candidate.Position, current) && !(candidate.Position > excludedStart + Epsilon && candidate.Position < excludedEnd - Epsilon))
            {
                result = candidate;
                break;
            }
        }

        foreach (var range in flow.Ranges)
        {
            if (range.Position >= end - Epsilon || range.RangeEnd <= current + Epsilon)
                continue;

            // The start of the range when it is a possible break, else its end (the empty block size of the box overflows the
            // fragmentainer with the content before it)
            var position = range.IncludesRangeStart && range.Position > current + Epsilon ? range.Position : range.RangeEnd;
            if (result is { } found && found.Position <= position)
                continue;

            if (position > excludedStart + Epsilon && position < excludedEnd - Epsilon)
                continue;

            if (IsValid(flow, position, current))
            {
                result = range with { Position = position, ContentEnd = position };
            }
        }

        return result;
    }

    /// <summary>Returns the index of the first candidate after a position.</summary>
    private static int FindFirstAfter(List<BreakCandidate> candidates, double position)
    {
        var low = 0;
        var high = candidates.Count;
        while (low < high)
        {
            var middle = (low + high) / 2;
            if (candidates[middle].Position <= position + Epsilon)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }
}
