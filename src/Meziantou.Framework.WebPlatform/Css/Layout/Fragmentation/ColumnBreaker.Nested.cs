namespace Meziantou.Framework.WebPlatform.Css.Layout;

internal static partial class ColumnBreaker
{
    /// <summary>
    /// The progress of the breaker in a multi-column container nested in the fragmentation context
    /// (https://drafts.csswg.org/css-multicol-1/#nested-multicol, see <see cref="NestedColumns"/>), as in Blink: the rows of
    /// columns and the column spanners of the container follow each other in the outer fragmentainers. A row of columns is as
    /// tall as the block size that remains in the outer fragmentainer (or balanced in it when its content ends in it); the
    /// content that does not fit in its columns continues in a new row in the next outer fragmentainer. A column spanner is
    /// fragmented by the outer fragmentainers; the content that overflows a spanner with a definite block size continues in the
    /// next outer fragmentainers while the rows after the spanner are placed.
    /// </summary>
    private sealed class NestedColumnsCursor : IItemCursor
    {
        private enum Phase
        {
            Columns,
            Spanner,

            /// <summary>The content is placed: the block size of the content box that remains (a container with a definite block size).</summary>
            Tail,
            Done,
        }

        private readonly ParallelItem _item;
        private readonly NestedColumns _nested;
        private readonly FragmentationFlow _flow;
        private readonly int _generation;

        /// <summary>The offset of the block-start edge of the border box in the first outer fragmentainer.</summary>
        private readonly double _startOffset;
        private readonly double _startEdges;
        private readonly double _endEdges;

        /// <summary>Whether the block-start and block-end edges of the container are repeated in every outer fragmentainer (box-decoration-break: clone).</summary>
        private readonly bool _clones;

        /// <summary>The spanners whose border box is placed, and whose overflowing content continues.</summary>
        private readonly List<NestedSpanner> _overflowing = [];
        private readonly List<FragmentainerBreak> _buffer = [];
        private Phase _phase;
        private bool _started;

        /// <summary>The index of the row of columns (the content before the spanner of the same index) or of the spanner being placed.</summary>
        private int _set;

        /// <summary>The progress in the content of the row of columns being placed.</summary>
        private FlowCursor? _columns;

        /// <summary>The spanner being placed.</summary>
        private NestedSpanner? _spanner;

        /// <summary>Whether a forced break must be taken before the next content.</summary>
        private bool _breaksBeforeNext;

        /// <summary>Whether the content being placed was moved to the start of the current outer fragmentainer.</summary>
        private bool _isMoved;

        /// <summary>Whether some content of the container is placed.</summary>
        private bool _hasContent;

        /// <summary>Whether some content of the container is placed in the current outer fragmentainer.</summary>
        private bool _hasContentInFragmentainer;

        /// <summary>The block size of the content box used in the previous outer fragmentainers.</summary>
        private double _consumed;

        /// <summary>The block size of the current row of a fixed block size (column-height) used in the previous outer fragmentainers.</summary>
        private double _rowConsumed;

        /// <summary>The offset of the block-end edge of the border box in the outer fragmentainer where the content ends.</summary>
        private double _endOffset;

        /// <summary>Whether the block-end edge of the border box is placed (before the end of the content when the rows overflow a container with a definite block size).</summary>
        private bool _isBoxEnded;

        /// <summary>Whether the content before the current offset in the outer fragmentainer is only the block-start edges of the container at its start.</summary>
        private bool _startsAtEdges;

        public NestedColumnsCursor(ParallelItem item, NestedColumns nested, double startOffset)
        {
            _item = item;
            _nested = nested;
            _flow = nested.Flow;
            _startOffset = startOffset;
            _generation = _flow.NextGeneration();
            nested.Reset();
            var box = item.Box!;
            var edges = nested.WritingDirection.ToLogical(box.Border.Add(box.Padding).Add(box.Scrollbars));
            _startEdges = edges.BlockStart;
            _endEdges = edges.BlockEnd;
            _clones = box.Box.Style.Layout.Fragmentation.BoxDecorationBreak == BoxDecorationBreak.Clone;
        }

        public double GetPlacedOffset(double position) => _endOffset;

        public AdvanceResult Advance(double blockSize, int index)
        {
            var isFirst = !_started;
            _started = true;
            _hasContentInFragmentainer = false;
            var shortage = double.PositiveInfinity;
            var appeal = BreakAppeal.Perfect;

            // The content that overflows the spanners placed in the previous outer fragmentainers
            for (var i = _overflowing.Count - 1; i >= 0; i--)
            {
                var result = ((FlowCursor)_overflowing[i].Cursor!).Advance(blockSize);
                shortage = Math.Min(shortage, result.Shortage);
                appeal = Worst(appeal, Normalize(result.Appeal));
                if (result.IsFinished)
                {
                    _overflowing.RemoveAt(i);
                }
            }

            if (_phase != Phase.Done)
            {
                var start = isFirst ? _startOffset + _startEdges : _clones ? _startEdges : 0;
                var offset = start;
                var contentBlockSize = _clones ? Math.Max(start, blockSize - _endEdges) : blockSize;
                var hadContent = _hasContent;
                _startsAtEdges = (isFirst && _startOffset <= Epsilon) || (!isFirst && _clones);
                while (_phase is Phase.Columns or Phase.Spanner)
                {
                    if (_breaksBeforeNext)
                    {
                        // A forced break after a spanner (not after the last content of the container: the break is after the container)
                        _breaksBeforeNext = false;
                        if (offset > Epsilon && HasContentLeft())
                            break;
                    }

                    if (_phase == Phase.Columns)
                    {
                        if (!PlaceColumns(contentBlockSize, index, start, ref offset, ref shortage, ref appeal))
                            break;

                        _phase = _set < _flow.Spanners.Count ? Phase.Spanner : Phase.Tail;
                    }
                    else
                    {
                        if (!PlaceSpanner(contentBlockSize, index, ref offset, ref shortage, ref appeal))
                            break;

                        _set++;
                        _phase = Phase.Columns;
                    }
                }

                if (!_isBoxEnded && _nested.BlockSize is { } definiteBlockSize && _nested.RowBlockSize is not null && offset - start >= definiteBlockSize - _consumed - Epsilon)
                {
                    // The rows of a fixed block size overflow the content box of a container with a definite block size
                    _endOffset = start + Math.Max(0, definiteBlockSize - _consumed) + _endEdges;
                    _isBoxEnded = true;
                }

                if (_phase == Phase.Tail)
                {
                    // The content box of a container with a definite block size ends after its block size, in this outer
                    // fragmentainer or in a next one
                    var remaining = _nested.BlockSize is { } containerBlockSize && !_isBoxEnded ? containerBlockSize - (_consumed + offset - start) : 0;
                    var available = Math.Max(0, contentBlockSize - offset);
                    if (remaining > available + Epsilon)
                    {
                        offset = Math.Max(offset, contentBlockSize);
                    }
                    else
                    {
                        offset += Math.Max(0, remaining);
                        _phase = Phase.Done;
                    }
                }

                _consumed += offset - start;
                if (_phase == Phase.Done && !_isBoxEnded)
                {
                    _endOffset = offset + _endEdges;
                    _isBoxEnded = true;
                }
                else if (isFirst && !hadContent && !_hasContent)
                {
                    // No content of the container fits after its start: a break before the container is better
                    appeal = Worst(appeal, BreakAppeal.Avoid);
                }
            }

            // (the content is placed when the block-end edge of the container is: the content that overflows the spanners may continue)
            return new AdvanceResult(_phase == Phase.Done && _overflowing.Count == 0, _isBoxEnded ? _item.IntrinsicEnd : _item.Start, 0, appeal, shortage);
        }

        /// <summary>The extent of the content of a row of columns in the strip: from the end of the spanner before it to the start of the next one.</summary>
        private (double Start, double End, double ExtentEnd, bool IsLast) GetSet(int set)
        {
            var spanners = _flow.Spanners;
            var isLast = set == spanners.Count;
            var start = set == 0 ? 0 : spanners[set - 1].MarginBoxEnd;
            var end = isLast ? Math.Max(_flow.ContentEnd, start) : Math.Max(spanners[set].MarginBoxStart, start);
            return (start, end, isLast ? Math.Max(_flow.ExtentEnd, end) : end, isLast);
        }

        /// <summary>Returns whether the container has content after the current position (a spanner, or content for a row of columns).</summary>
        private bool HasContentLeft()
        {
            if (_phase == Phase.Spanner || _set < _flow.Spanners.Count)
                return true;

            if (_phase != Phase.Columns)
                return false;

            var (start, _, extentEnd, _) = GetSet(_set);
            return extentEnd - (_columns?.Position ?? start) > Epsilon;
        }

        /// <summary>
        /// Places rows of columns in the outer fragmentainer, from <paramref name="offset"/>. Returns false when the content of the
        /// rows continues in the next outer fragmentainer.
        /// </summary>
        private bool PlaceColumns(double blockSize, int index, double fragmentStart, ref double offset, ref double shortage, ref BreakAppeal appeal)
        {
            var (start, end, extentEnd, isLastSet) = GetSet(_set);
            var map = _nested.Map;
            if (_columns is null)
            {
                map.Add(map.Count == 0 ? double.NegativeInfinity : start, _nested.Columns.Count, start, isBreak: true);
                _columns = new FlowCursor(_flow, map, start, end, _nested.Columns.Count, _generation, canBreakBefore: false);
                _rowConsumed = 0;
                _nested.Fragmentainers.PortionStarts.Add(_nested.Columns.Count);
            }

            var columnCount = _nested.ColumnCount;
            var balances = !(isLastSet && _nested.FillsSequentially);

            // The absolutely positioned boxes of the strip are placed in the sequence of the columns once the in-flow content
            // is (see PlaceNestedOutOfFlow): as in a container that is not nested, the columns of a row without in-flow
            // content have a block size for them, a row before a spanner has all its columns, and the last row is balanced
            // with them
            var hasOutOfFlow = _flow.OutOfFlowItems is not null;
            var balancesOutOfFlow = hasOutOfFlow && isLastSet && ReferenceEquals(_nested.BalanceFlow, _flow);
            while (true)
            {
                var position = _columns.Position;
                var isEmpty = extentEnd - position <= Epsilon;
                var isAtStart = offset <= (_startsAtEdges ? fragmentStart : 0) + Epsilon;

                // The block size that remains in the outer fragmentainer, in the content box of the container and in its row
                var available = blockSize - offset;
                var overflows = false;
                if (_nested.BlockSize is { } containerBlockSize && _nested.RowBlockSize is null)
                {
                    // The content box of the container ends in the outer fragmentainer: the content that does not fit in the columns
                    // of its last row overflows in the inline direction
                    var remaining = containerBlockSize - (_consumed + offset - fragmentStart);
                    if (remaining <= available + Epsilon)
                    {
                        overflows = true;
                        available = Math.Max(remaining, isEmpty ? 0 : MinBlockSize);
                    }
                }

                if (_nested.RowBlockSize is { } rowBlockSize && !isEmpty)
                {
                    // A row of a fixed block size that follows other content of the container in the outer fragmentainer starts in it
                    // when it fits (the rows overflow a container with a definite block size)
                    if (_rowConsumed <= Epsilon && _hasContentInFragmentainer && rowBlockSize > available + Epsilon)
                        return false;

                    available = Math.Min(available, Math.Max(MinBlockSize, rowBlockSize - _rowConsumed));
                }

                if (!isEmpty && available <= Epsilon)
                {
                    if (!isAtStart)
                        return false;

                    available = MinBlockSize;
                }

                var columnBlockSize = isEmpty && !hasOutOfFlow ? 0
                    : balances || (_nested.RowBlockSize is not null && !_nested.FillsSequentially) ? Math.Max(0, Balance(_nested.BalanceFlow, position, end, columnCount, available, _buffer, extentEnd, balancesOutOfFlow, balancesOutOfFlow ? _nested.Fragmentainers : null))
                    : Math.Max(0, available);

                // As in Blink, a column that would break where a rule is violated (or overflow) is moved to the next outer
                // fragmentainer, where it may have more space: the row ends before it, unless a break that is as bad was already
                // taken (in the columns before it, or instead of a break before the row)
                var maxColumns = overflows ? MaxFragmentainers : columnCount;
                if (!isEmpty && !isAtStart)
                {
                    maxColumns = CountColumnsWithoutViolation(_nested.BalanceFlow, position, end, columnBlockSize, maxColumns, _hasContentInFragmentainer ? BreakAppeal.Perfect : _item.BreakBeforeAppeal);
                    if (maxColumns == 0)
                        return false;
                }

                var isFinished = false;
                var columns = 0;
                while (columns < maxColumns)
                {
                    var lastStart = _columns.Position;
                    var lastDisplayOffset = _columns.DisplayOffset;
                    var result = _columns.Advance(columnBlockSize);
                    var column = new NestedColumn
                    {
                        OuterIndex = index,
                        Start = lastStart,
                        DisplayOffset = lastDisplayOffset,
                        IndexInRow = columns,
                        BlockOffset = offset,
                        BlockSize = columnBlockSize,
                    };

                    _nested.Columns.Add(column);
                    _nested.Fragmentainers.BlockSizes.Add(columnBlockSize);
                    columns++;
                    shortage = Math.Min(shortage, result.Shortage);
                    appeal = Worst(appeal, Normalize(result.Appeal));
                    if (result.IsFinished)
                    {
                        isFinished = true;
                        if (!isLastSet)
                        {
                            // The boxes broken by a spanner end with their content
                            column.End = end;
                            column.BlockSize = Math.Max(0, end - lastStart + lastDisplayOffset);
                        }

                        break;
                    }

                    column.End = result.Position;
                    column.BlockSize = Math.Max(columnBlockSize, result.ContentEnd - lastStart + lastDisplayOffset);
                }

                _hasContent |= !isEmpty;
                _hasContentInFragmentainer |= !isEmpty;

                // The block size of the row: the columns filled sequentially are as tall as their content when it ends in the row
                var used = columnBlockSize;
                if (isFinished && !balances && columns == 1)
                {
                    used = Math.Min(columnBlockSize, Math.Max(0, _columns.LastExtent));
                }

                if (isFinished && isLastSet && !isEmpty && _nested.RowBlockSize is { } lastRowBlockSize)
                {
                    // The last row of the container has its whole block size, even when its content is balanced
                    used = Math.Max(used, Math.Min(Math.Max(0, blockSize - offset), lastRowBlockSize - _rowConsumed));
                }

                if (!isEmpty || _flow.Spanners.Count == 0)
                {
                    // (the column boxes of a container with a definite block size are as tall as its content box, also when their content is balanced)
                    // (the columns of a row without in-flow content only have a block size for the absolutely positioned boxes)
                    var rowExtent = isEmpty ? used : columnBlockSize;
                    _nested.Rows.Add(new NestedRow(index, columns, offset, offset + (_nested.BlockSize is null ? rowExtent : Math.Max(rowExtent, available)), IsContinuation: _rowConsumed > Epsilon));
                }

                _nested.LastRowOverflows = overflows;
                if (isFinished && !isLastSet && hasOutOfFlow)
                {
                    // As in Blink, the absolutely positioned boxes use all the columns of a row before they continue in the next
                    // row (after the column spanner): the columns without in-flow content
                    for (; columns < columnCount; columns++)
                    {
                        _nested.Columns.Add(new NestedColumn { OuterIndex = index, Start = double.PositiveInfinity, IndexInRow = columns, BlockOffset = offset, BlockSize = columnBlockSize });
                        _nested.Fragmentainers.BlockSizes.Add(columnBlockSize);
                    }
                }

                offset += used;
                if (isFinished)
                {
                    _columns = null;
                    return true;
                }

                // The next row of a fixed block size is in the same outer fragmentainer when it fits
                if (_nested.RowBlockSize is not { } fixedBlockSize || !_nested.Wraps)
                    return false;

                _rowConsumed += used;
                if (_rowConsumed < fixedBlockSize - Epsilon)
                    return false;

                _rowConsumed = 0;
                offset += _nested.RowGap;
                if (blockSize - offset < fixedBlockSize - Epsilon)
                    return false;
            }
        }

        /// <summary>
        /// Places a column spanner in the outer fragmentainer, from <paramref name="offset"/>. Returns false when its border box
        /// continues in the next outer fragmentainer (or starts there).
        /// </summary>
        private bool PlaceSpanner(double blockSize, int index, ref double offset, ref double shortage, ref BreakAppeal appeal)
        {
            var writingDirection = _nested.WritingDirection;
            var placement = _flow.Spanners[_set];
            var fragment = placement.Fragment;
            var fragmentation = fragment.Box.Style.Layout.Fragmentation;
            var margin = writingDirection.ToLogical(fragment.Margin);
            var size = writingDirection.ToLogical(fragment.Size).Block;
            var rowStart = 0d;
            if (_spanner is null)
            {
                // A forced break before the spanner (not before the first content of the container: the break is before the container)
                var isAtStart = offset <= Epsilon;
                if (!_isMoved && !isAtStart && _hasContent && FragmentationFlow.IsForcedBreak(fragmentation.BreakBefore))
                {
                    _isMoved = true;
                    return false;
                }

                // The margins of two adjacent sibling spanners collapse
                var top = offset + margin.BlockStart;
                if (_set > 0 && placement.MarginBoxStart - _flow.Spanners[_set - 1].MarginBoxEnd <= Epsilon && fragment.Box.Parent == _flow.Spanners[_set - 1].Fragment.Box.Parent)
                {
                    var previousEnd = writingDirection.ToLogical(_flow.Spanners[_set - 1].Fragment.Margin).BlockEnd;
                    top -= previousEnd + margin.BlockStart - MarginStrut.FromMargin(previousEnd).Append(margin.BlockStart).Sum;
                }

                // A spanner whose content does not fit (a monolithic spanner, a first line...) is moved to the next outer fragmentainer
                var flow = FragmentationFlow.CollectBox(fragment, writingDirection);
                if (!_isMoved && !isAtStart && size > blockSize - top + Epsilon
                    && (blockSize - top <= Epsilon || CountColumnsWithoutViolation(flow, 0, flow.ContentEnd, blockSize - top, 1, BreakAppeal.Perfect) == 0))
                {
                    _isMoved = true;
                    return false;
                }

                var map = new FlowMap();
                map.Add(double.NegativeInfinity, index, -top, isBreak: false);
                var cursor = new FlowCursor(flow, map, 0, flow.ContentEnd, index, flow.NextGeneration(), canBreakBefore: false);
                cursor.SetOrigin(-top);
                _spanner = new NestedSpanner { Fragment = fragment, Flow = flow, Map = map, Cursor = cursor };
                _nested.Spanners.Add(_spanner);
                rowStart = top;
            }

            _isMoved = false;
            _hasContent = true;
            _hasContentInFragmentainer = true;
            var spannerCursor = (FlowCursor)_spanner.Cursor!;
            var result = spannerCursor.Advance(blockSize);
            shortage = Math.Min(shortage, result.Shortage);
            appeal = Worst(appeal, Normalize(result.Appeal));
            if (!result.IsFinished && result.Position < size - Epsilon)
            {
                _nested.Rows.Add(new NestedRow(index, -1, rowStart, Math.Max(rowStart, blockSize)));
                return false;
            }

            // The border box of the spanner ends in the outer fragmentainer: the content that overflows it continues apart
            var endOffset = spannerCursor.GetPlacedOffset(size);
            _spanner.End = (index, endOffset);
            _nested.Rows.Add(new NestedRow(index, -1, rowStart, endOffset));
            offset = endOffset + margin.BlockEnd;
            if (!result.IsFinished)
            {
                _overflowing.Add(_spanner);
            }

            _spanner = null;
            _breaksBeforeNext = FragmentationFlow.IsForcedBreak(fragmentation.BreakAfter);
            return true;
        }

        /// <summary>
        /// Returns the number of fragmentainers of a block size, up to a maximum, that the content of a portion of a strip fills
        /// without a break that violates more rules than the breaks taken before it (content that overflows is the worst break).
        /// </summary>
        /// <param name="accepted">The worst appeal that is accepted for the break of the first fragmentainer (the appeal of the break that would be taken instead, before the content).</param>
        private static int CountColumnsWithoutViolation(FragmentationFlow flow, double start, double end, double blockSize, int max, BreakAppeal accepted)
        {
            var map = new FlowMap();
            map.Add(double.NegativeInfinity, 0, start, isBreak: true);
            var cursor = new FlowCursor(flow, map, start, end, 0, flow.NextGeneration(), canBreakBefore: false);
            for (var count = 0; count < max; count++)
            {
                var result = cursor.Advance(blockSize);
                var appeal = result.Appeal == BreakAppeal.Forced ? BreakAppeal.Perfect : result.Appeal;
                if (appeal > accepted)
                    return count;

                // The next fragmentainers: the worst break taken so far
                accepted = count == 0 ? appeal : Worst(accepted, appeal);
                if (result.IsFinished)
                    break;
            }

            return max;
        }

        /// <summary>The appeal of a break of the content for the outer flow: a break that leaves unbreakable content overflowing is avoided.</summary>
        private static BreakAppeal Normalize(BreakAppeal appeal) => appeal switch
        {
            BreakAppeal.Avoid or BreakAppeal.LastResort => BreakAppeal.Avoid,
            _ => BreakAppeal.Perfect,
        };

        private static BreakAppeal Worst(BreakAppeal a, BreakAppeal b) => a > b ? a : b;
    }

    /// <summary>The smallest block size of a column for the fragmentation of its content, as in Blink: the content makes progress.</summary>
    private const double MinBlockSize = 1;
}
