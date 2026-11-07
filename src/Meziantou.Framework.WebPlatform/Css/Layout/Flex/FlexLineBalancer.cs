namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The line breaks of a balanced flex container (<c>flex-wrap: balance</c>, https://drafts.csswg.org/css-flexbox-2/#algo-line-break
/// and https://drafts.csswg.org/css-flexbox-2/#algo-balance).
/// </summary>
/// <remarks>
/// <para>
/// The balancing line count is the number of lines of the greedy line breaking (with the sizes of the items floored at zero),
/// or the minimum flex line count when it is larger (<c>flex-line-count</c>, at most the number of items). The items are
/// then partitioned into that many contiguous, non-empty sequences whose sizes (the items and the gaps between them) do not
/// exceed the available size, a single overflowing item excepted, so that the sum of the squares of the differences between
/// the size of each sequence and the available size is minimal; among the partitions with the minimum error, the one that
/// assigns the most items to the first line wins, then the most items to the second line, and so on.
/// </para>
/// <para>
/// The number of lines is fixed and the sum of the sizes of the lines does not depend on the breaks, so the error is minimal
/// when the sum of the squares of the sizes of the lines is: the available size only limits the lines, and may be infinite
/// (a column container with an indefinite block size). That cost satisfies the quadrangle inequality, also with the limit (a
/// part of a line that fits also fits): the last break that is optimal for the lines after a break does not move backward
/// when that break moves forward, which the divide and conquer of each line uses. The breaks of a line are searched between
/// the earliest and the latest positions that leave a valid partition on both sides; the best next break of each position is
/// kept to rebuild the partition from the first line, which gives the tie-breaking rule.
/// </para>
/// </remarks>
internal static class FlexLineBalancer
{
    /// <summary>The tolerance of the comparison of a line with the available size (as the greedy line breaking of the flex layout).</summary>
    private const double Epsilon = 1e-7;

    /// <summary>The relative tolerance of the comparison of two errors: the sums of the same sizes in another order are equal.</summary>
    private const double RelativeTolerance = 1e-12;

    /// <summary>The largest size of an item: the squares of the sums stay finite.</summary>
    private const double MaxItemSize = 1e12;

    /// <summary>
    /// The largest number of break positions that are explored (one integer each): a container with thousands of items on
    /// thousands of lines that could each start at thousands of positions is not balanced.
    /// </summary>
    private const int MaxPositionCount = 1 << 22;

    /// <summary>
    /// Returns the end (exclusive) of each line of the balanced partition of the items, or null when the container is too large
    /// to be balanced (the caller breaks the lines greedily).
    /// </summary>
    /// <param name="sizes">The outer hypothetical main sizes of the items (a negative size is floored at zero).</param>
    /// <param name="gap">The gap between two items of a line.</param>
    /// <param name="available">The available main size (the inner main size of the container), or positive infinity.</param>
    /// <param name="minimumLineCount">The minimum number of lines (<c>flex-line-count</c>): it is limited by the number of items.</param>
    public static int[]? Balance(ReadOnlySpan<double> sizes, double gap, double available, int minimumLineCount)
    {
        var count = sizes.Length;
        if (count == 0)
            return [];

        // "For this purpose, floor the outer hypothetical main size of each flex item at zero": the sizes are then monotonic
        var sums = new double[count + 1];
        for (var i = 0; i < count; i++)
        {
            var size = sizes[i];
            sums[i + 1] = sums[i] + (size > 0 ? Math.Min(size, MaxItemSize) : 0);
        }

        if (!(gap > 0))
        {
            gap = 0;
        }

        // The last item that fits on a line with each first item (a line has at least one item: "If the very first uncollected
        // item wouldn't fit, collect just it into the line"), as the end of the line: it does not decrease from an item to the next
        var limit = available + Epsilon;
        var lineEnds = new int[count];
        var end = 0;
        for (var i = 0; i < count; i++)
        {
            end = Math.Max(end, i + 1);
            while (end < count && GetSize(sums, gap, i, end + 1) <= limit)
            {
                end++;
            }

            lineEnds[i] = end;
        }

        // "Determine the number of lines that would be created by the above algorithm"
        var lineCount = 0;
        for (var i = 0; i < count; i = lineEnds[i])
        {
            lineCount++;
        }

        // "the larger of that count and the minimum flex line count", which "is clamped from above by the number of flex items"
        lineCount = Math.Max(lineCount, Math.Min(minimumLineCount, count));
        var result = new int[lineCount];
        result[^1] = count;
        if (lineCount == 1)
            return result;

        if (lineCount == count)
        {
            for (var i = 0; i < count; i++)
            {
                result[i] = i + 1;
            }

            return result;
        }

        // The latest and the earliest positions of each break (latest[k] and earliest[k] bound the start of the line k): every
        // line has an item and fits, and so do the lines on the other side
        var latest = new int[lineCount + 1];
        for (var k = 1; k <= lineCount; k++)
        {
            latest[k] = Math.Min(lineEnds[latest[k - 1]], count - (lineCount - k));
        }

        var earliest = new int[lineCount + 1];
        earliest[lineCount] = count;
        for (var k = lineCount - 1; k >= 0; k--)
        {
            earliest[k] = Math.Max(GetFirstStart(lineEnds, earliest[k + 1]), k);
        }

        long positionCount = 0;
        var largestWindow = 0;
        for (var k = 0; k < lineCount; k++)
        {
            var window = latest[k] - earliest[k] + 1;
            if (window <= 0)
                return null;

            positionCount += window;
            largestWindow = Math.Max(largestWindow, window);
        }

        if (latest[lineCount] != count || earliest[0] != 0 || positionCount > MaxPositionCount)
            return null;

        // The best next break of each start of each line but the last one, and the error of the lines from that start
        var offsets = new int[lineCount];
        for (var k = 1; k < lineCount; k++)
        {
            offsets[k] = offsets[k - 1] + (latest[k - 1] - earliest[k - 1] + 1);
        }

        var nextBreaks = new int[offsets[^1]];
        var costs = new double[largestWindow];
        var nextCosts = new double[largestWindow];
        for (var start = earliest[lineCount - 1]; start <= latest[lineCount - 1]; start++)
        {
            var size = GetSize(sums, gap, start, count);
            nextCosts[start - earliest[lineCount - 1]] = size * size;
        }

        for (var k = lineCount - 2; k >= 0; k--)
        {
            var solver = new LineSolver(sums, lineEnds, gap, nextCosts, earliest[k + 1], costs, nextBreaks.AsSpan(offsets[k]), earliest[k]);
            solver.Solve(earliest[k], latest[k], earliest[k + 1], latest[k + 1]);
            (costs, nextCosts) = (nextCosts, costs);
        }

        var position = 0;
        for (var k = 0; k < lineCount - 1; k++)
        {
            position = nextBreaks[offsets[k] + position - earliest[k]];
            result[k] = position;
        }

        return result;
    }

    /// <summary>The size of a line: the sizes of the items from <paramref name="start"/> to <paramref name="end"/> (exclusive) and the gaps between them.</summary>
    private static double GetSize(double[] sums, double gap, int start, int end) => sums[end] - sums[start] + gap * (end - start - 1);

    /// <summary>Returns the first item whose line can end at <paramref name="end"/> (the ends of the lines do not decrease).</summary>
    private static int GetFirstStart(int[] lineEnds, int end)
    {
        var low = 0;
        var high = end - 1;
        while (low < high)
        {
            var middle = (low + high) >>> 1;
            if (lineEnds[middle] >= end)
            {
                high = middle;
            }
            else
            {
                low = middle + 1;
            }
        }

        return low;
    }

    /// <summary>
    /// Computes the smallest error of the lines that follow each possible start of a line, and the last next break that gives
    /// it, from the errors of the lines that follow each possible start of the next line.
    /// </summary>
    private readonly ref struct LineSolver(double[] sums, int[] lineEnds, double gap, double[] nextCosts, int nextFirst, double[] costs, Span<int> nextBreaks, int first)
    {
        private readonly double[] _sums = sums;
        private readonly int[] _lineEnds = lineEnds;
        private readonly double[] _nextCosts = nextCosts;
        private readonly double[] _costs = costs;
        private readonly Span<int> _nextBreaks = nextBreaks;

        /// <summary>
        /// Solves the starts from <paramref name="low"/> to <paramref name="high"/>, whose best next breaks are between
        /// <paramref name="lowBreak"/> and <paramref name="highBreak"/>: the middle start is solved by trying its next breaks,
        /// which then bound the ones of the starts before it and after it.
        /// </summary>
        public void Solve(int low, int high, int lowBreak, int highBreak)
        {
            while (low <= high)
            {
                var start = (low + high) >>> 1;
                var from = Math.Max(lowBreak, start + 1);
                var to = Math.Min(highBreak, _lineEnds[start]);
                var best = double.PositiveInfinity;
                var bestBreak = from;
                for (var next = from; next <= to; next++)
                {
                    var size = GetSize(_sums, gap, start, next);
                    var cost = size * size + _nextCosts[next - nextFirst];

                    // The last of the breaks with the same error: "select the sequence that assigns the most items to the first line"
                    if (cost <= best + Math.Max(best, 1) * RelativeTolerance)
                    {
                        best = Math.Min(best, cost);
                        bestBreak = next;
                    }
                }

                _costs[start - first] = best;
                _nextBreaks[start - first] = bestBreak;
                Solve(low, start - 1, lowBreak, bestBreak);
                low = start + 1;
                lowBreak = bestBreak;
            }
        }
    }
}
