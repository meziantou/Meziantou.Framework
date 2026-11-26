using System.Buffers;
using System.Runtime.InteropServices;

namespace Meziantou.Framework;

internal static class PatienceDiff
{
    private const int Missing = -2;
    private const int Duplicate = -1;

    /// <summary>Diffs two regions of chunk codes, from 1 to <paramref name="codeCount"/>.</summary>
    internal static void Compute(ReadOnlySpan<int> left, ReadOnlySpan<int> right, int codeCount, DiffBudget budget, Span<bool> leftModified, Span<bool> rightModified)
    {
        leftModified.Fill(true);
        rightModified.Fill(true);

        // One entry per code replaces the two dictionaries of strings built for each region
        var occurrences = ArrayPool<Occurrences>.Shared.Rent(codeCount + 1);
        try
        {
            occurrences.AsSpan(0, codeCount + 1).Clear();
            var search = new AnchorSearch(occurrences);
            ComputeRange(left, 0, left.Length, right, 0, right.Length, budget, leftModified, rightModified, ref search);
        }
        finally
        {
            ArrayPool<Occurrences>.Shared.Return(occurrences);
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

        var previousLeft = leftStart;
        var previousRight = rightStart;
        foreach (var anchor in anchors)
        {
            ComputeRange(left, previousLeft, anchor.LeftIndex, right, previousRight, anchor.RightIndex, budget, leftModified, rightModified, ref search);

            leftModified[anchor.LeftIndex] = false;
            rightModified[anchor.RightIndex] = false;

            previousLeft = anchor.LeftIndex + 1;
            previousRight = anchor.RightIndex + 1;
        }

        ComputeRange(left, previousLeft, leftEnd, right, previousRight, rightEnd, budget, leftModified, rightModified, ref search);
    }

    private static List<Anchor> FindAnchors(ReadOnlySpan<int> left, int leftStart, int leftEnd, ReadOnlySpan<int> right, int rightStart, int rightEnd, ref AnchorSearch search)
    {
        // A code whose entry has another stamp does not occur in this region, so the entries need no clearing between
        // regions
        var stamp = ++search.Stamp;
        var occurrences = search.Occurrences;
        for (var i = leftStart; i < leftEnd; i++)
        {
            ref var entry = ref occurrences[left[i]];
            if (entry.Stamp != stamp)
            {
                entry = new Occurrences { Stamp = stamp, LeftIndex = i, RightIndex = Missing };
            }
            else
            {
                entry.LeftIndex = Duplicate;
            }
        }

        for (var i = rightStart; i < rightEnd; i++)
        {
            ref var entry = ref occurrences[right[i]];
            if (entry.Stamp != stamp)
            {
                entry = new Occurrences { Stamp = stamp, LeftIndex = Missing, RightIndex = i };
            }
            else
            {
                entry.RightIndex = entry.RightIndex is Missing ? i : Duplicate;
            }
        }

        // Walking the range in order yields the anchors already sorted by left index, which is what the
        // longest-increasing-subsequence pass needs. Enumerating the dictionary instead produced them in
        // an arbitrary order and required a sort.
        var pairs = new List<Anchor>();
        for (var leftIndex = leftStart; leftIndex < leftEnd; leftIndex++)
        {
            // LeftIndex is the index of a chunk that appears once in the range, and a negative value otherwise, so
            // this only matches chunks that are unique in the range.
            var entry = occurrences[left[leftIndex]];
            if (entry.LeftIndex != leftIndex)
                continue;

            if (entry.RightIndex >= 0)
            {
                pairs.Add(new Anchor(leftIndex, entry.RightIndex));
            }
        }

        if (pairs.Count == 0)
            return pairs;

        return DiffAlgorithmHelpers.LongestIncreasingSubsequenceByRight(pairs);
    }

    private ref struct AnchorSearch(Occurrences[] occurrences)
    {
        public readonly Occurrences[] Occurrences = occurrences;
        public int Stamp;
    }

    /// <summary>Where a code occurs in the region searched for anchors.</summary>
    [StructLayout(LayoutKind.Auto)]
    private struct Occurrences
    {
        /// <summary>The search that wrote this entry. An entry of an earlier search is unused.</summary>
        public int Stamp;

        /// <summary>The index of the only occurrence on the left side, or <see cref="Missing"/> or <see cref="Duplicate"/>.</summary>
        public int LeftIndex;

        /// <summary>The index of the only occurrence on the right side, or <see cref="Missing"/> or <see cref="Duplicate"/>.</summary>
        public int RightIndex;
    }
}
