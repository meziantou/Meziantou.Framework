using System.Buffers;
using System.Runtime.InteropServices;

namespace Meziantou.Framework;

internal static class HistogramDiff
{
    // A token that occurs this often on both sides anchors the region badly: it splits it at an
    // arbitrary one of its many occurrences and the result is worse than not anchoring at all. Give up
    // on the region and let the fallback algorithm handle it, as git's histogram diff does.
    private const int MaxAnchorScore = 64;

    /// <summary>Diffs two regions of chunk codes, from 1 to <paramref name="codeCount"/>.</summary>
    internal static void Compute(ReadOnlySpan<int> left, ReadOnlySpan<int> right, int codeCount, DiffBudget budget, Span<bool> leftModified, Span<bool> rightModified)
    {
        leftModified.Fill(true);
        rightModified.Fill(true);

        // One entry per code replaces the dictionary of strings built for each pass
        var stats = ArrayPool<TokenStats>.Shared.Rent(codeCount + 1);
        try
        {
            stats.AsSpan(0, codeCount + 1).Clear();
            var search = new AnchorSearch(stats);
            ComputeRange(left, 0, left.Length, right, 0, right.Length, budget, leftModified, rightModified, ref search);
        }
        finally
        {
            ArrayPool<TokenStats>.Shared.Return(stats);
        }
    }

    private static void ComputeRange(
        ReadOnlySpan<int> left,
        int leftStart,
        int leftEnd,
        ReadOnlySpan<int> right,
        int rightStart,
        int rightEnd,
        DiffBudget budget,
        Span<bool> leftModified,
        Span<bool> rightModified,
        ref AnchorSearch search)
    {
        // Each pass keeps every anchor it can find instead of a single one. Consuming one anchor per
        // occurrence table made the search quadratic: the table was rebuilt over the whole remaining
        // region for each anchor, so a region with k anchors cost O(region * k).
        //
        // The region after the last anchor is a tail call, so iterate on it and leave only the gaps
        // between anchors on the stack. Recursing on it too overflowed the stack on a few thousand
        // changed chunks.
        while (true)
        {
            while (leftStart < leftEnd && rightStart < rightEnd && left[leftStart] == right[rightStart])
            {
                leftModified[leftStart] = false;
                rightModified[rightStart] = false;
                leftStart++;
                rightStart++;
            }

            while (leftStart < leftEnd && rightStart < rightEnd && left[leftEnd - 1] == right[rightEnd - 1])
            {
                leftEnd--;
                rightEnd--;
                leftModified[leftEnd] = false;
                rightModified[rightEnd] = false;
            }

            // Every chunk starts as modified, so a region left once the time limit is reached is reported as replaced
            if (leftStart >= leftEnd || rightStart >= rightEnd || budget.IsExpired())
                return;

            var anchors = FindAnchors(left, leftStart, leftEnd, right, rightStart, rightEnd, ref search);
            if (anchors.Count == 0)
            {
                MyersDiff.Compute(
                    left[leftStart..leftEnd],
                    right[rightStart..rightEnd],
                    budget,
                    leftModified[leftStart..leftEnd],
                    rightModified[rightStart..rightEnd]);
                return;
            }

            foreach (var anchor in anchors)
            {
                ComputeRange(left, leftStart, anchor.LeftIndex, right, rightStart, anchor.RightIndex, budget, leftModified, rightModified, ref search);
                leftModified[anchor.LeftIndex] = false;
                rightModified[anchor.RightIndex] = false;
                leftStart = anchor.LeftIndex + 1;
                rightStart = anchor.RightIndex + 1;
            }
        }
    }

    private static List<Anchor> FindAnchors(ReadOnlySpan<int> left, int leftStart, int leftEnd, ReadOnlySpan<int> right, int rightStart, int rightEnd, ref AnchorSearch search)
    {
        // A single entry per code holds the occurrence counts on both sides and the first position of the code on
        // each side. A code whose entry has another stamp does not occur in this pass, so the entries need no
        // clearing between passes.
        var stamp = ++search.Stamp;
        var stats = search.Stats;
        for (var i = leftEnd - 1; i >= leftStart; i--)
        {
            ref var entry = ref stats[left[i]];
            if (entry.Stamp != stamp)
            {
                entry = new TokenStats { Stamp = stamp };
            }

            entry.LeftCount++;
            entry.FirstLeftIndex = i;
        }

        // Walking backwards leaves the smallest index of each token in FirstLeftIndex/FirstRightIndex.
        for (var i = rightEnd - 1; i >= rightStart; i--)
        {
            ref var entry = ref stats[right[i]];
            if (entry.Stamp != stamp)
            {
                entry = new TokenStats { Stamp = stamp };
            }

            entry.RightCount++;
            entry.FirstRightIndex = i;
        }

        // The histogram rule: anchor on the least frequent token the two sides share.
        var bestScore = int.MaxValue;
        for (var i = leftStart; i < leftEnd; i++)
        {
            var entry = stats[left[i]];
            if (entry.RightCount is 0)
                continue;

            var score = entry.LeftCount + entry.RightCount;
            if (score < bestScore)
            {
                bestScore = score;
            }
        }

        if (bestScore > MaxAnchorScore)
            return [];

        // Walking the range in order yields the candidates already sorted by left index, which is what
        // the longest-increasing-subsequence pass needs.
        var candidates = new List<Anchor>();
        for (var leftIndex = leftStart; leftIndex < leftEnd; leftIndex++)
        {
            var entry = stats[left[leftIndex]];
            if (entry.RightCount is 0 || entry.LeftCount + entry.RightCount != bestScore)
                continue;

            // Keep one candidate per token so a repeated token cannot contribute several crossing pairs.
            if (entry.FirstLeftIndex != leftIndex)
                continue;

            candidates.Add(new Anchor(leftIndex, entry.FirstRightIndex));
        }

        if (candidates.Count == 0)
            return candidates;

        return DiffAlgorithmHelpers.LongestIncreasingSubsequenceByRight(candidates);
    }

    private ref struct AnchorSearch(TokenStats[] stats)
    {
        public readonly TokenStats[] Stats = stats;
        public int Stamp;
    }

    [StructLayout(LayoutKind.Auto)]
    private struct TokenStats
    {
        /// <summary>The pass that wrote this entry. An entry of an earlier pass is unused.</summary>
        public int Stamp;
        public int LeftCount;
        public int RightCount;
        public int FirstLeftIndex;
        public int FirstRightIndex;
    }
}
