using System.Runtime.InteropServices;

namespace Meziantou.Framework;

internal static class HistogramDiff
{
    // A token that occurs this often on both sides anchors the region badly: it splits it at an
    // arbitrary one of its many occurrences and the result is worse than not anchoring at all. Give up
    // on the region and let the fallback algorithm handle it, as git's histogram diff does.
    private const int MaxAnchorScore = 64;

    internal static DiffComputationResult Compute(string[] left, string[] right, IEqualityComparer<string> comparer)
    {
        var leftModified = new bool[left.Length];
        var rightModified = new bool[right.Length];
        Array.Fill(leftModified, true);
        Array.Fill(rightModified, true);

        ComputeRange(left, 0, left.Length, right, 0, right.Length, comparer, leftModified, rightModified);
        return new DiffComputationResult(leftModified, rightModified);
    }

    private static void ComputeRange(
        string[] left,
        int leftStart,
        int leftEnd,
        string[] right,
        int rightStart,
        int rightEnd,
        IEqualityComparer<string> comparer,
        bool[] leftModified,
        bool[] rightModified)
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
            while (leftStart < leftEnd && rightStart < rightEnd && comparer.Equals(left[leftStart], right[rightStart]))
            {
                leftModified[leftStart] = false;
                rightModified[rightStart] = false;
                leftStart++;
                rightStart++;
            }

            while (leftStart < leftEnd && rightStart < rightEnd && comparer.Equals(left[leftEnd - 1], right[rightEnd - 1]))
            {
                leftEnd--;
                rightEnd--;
                leftModified[leftEnd] = false;
                rightModified[rightEnd] = false;
            }

            if (leftStart >= leftEnd || rightStart >= rightEnd)
                return;

            var anchors = FindAnchors(left, leftStart, leftEnd, right, rightStart, rightEnd, comparer);
            if (anchors.Count == 0)
            {
                ApplyMyers(left, leftStart, leftEnd, right, rightStart, rightEnd, comparer, leftModified, rightModified);
                return;
            }

            foreach (var anchor in anchors)
            {
                ComputeRange(left, leftStart, anchor.LeftIndex, right, rightStart, anchor.RightIndex, comparer, leftModified, rightModified);
                leftModified[anchor.LeftIndex] = false;
                rightModified[anchor.RightIndex] = false;
                leftStart = anchor.LeftIndex + 1;
                rightStart = anchor.RightIndex + 1;
            }
        }
    }

    private static void ApplyMyers(
        string[] left,
        int leftStart,
        int leftEnd,
        string[] right,
        int rightStart,
        int rightEnd,
        IEqualityComparer<string> comparer,
        bool[] leftModified,
        bool[] rightModified)
    {
        var subDiff = MyersDiff.Compute(
            left.AsSpan(leftStart, leftEnd - leftStart),
            right.AsSpan(rightStart, rightEnd - rightStart),
            comparer);

        DiffAlgorithmHelpers.ApplySubDiff(subDiff, leftModified, leftStart, rightModified, rightStart);
    }

    private static List<Anchor> FindAnchors(string[] left, int leftStart, int leftEnd, string[] right, int rightStart, int rightEnd, IEqualityComparer<string> comparer)
    {
        // A single map holds the occurrence counts on both sides and the first position of each token on
        // each side. This replaces a second dictionary and the List<int> that used to be allocated per
        // distinct token, which dominated the allocations of this pass.
        var stats = new Dictionary<string, TokenStats>(comparer);
        for (var i = leftEnd - 1; i >= leftStart; i--)
        {
            ref var entry = ref CollectionsMarshal.GetValueRefOrAddDefault(stats, left[i], out _);
            entry.LeftCount++;
            entry.FirstLeftIndex = i;
        }

        // Walking backwards leaves the smallest index of each token in FirstLeftIndex/FirstRightIndex.
        for (var i = rightEnd - 1; i >= rightStart; i--)
        {
            ref var entry = ref CollectionsMarshal.GetValueRefOrAddDefault(stats, right[i], out _);
            entry.RightCount++;
            entry.FirstRightIndex = i;
        }

        // The histogram rule: anchor on the least frequent token the two sides share.
        var bestScore = int.MaxValue;
        foreach (var entry in stats.Values)
        {
            if (entry.LeftCount is 0 || entry.RightCount is 0)
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

    [StructLayout(LayoutKind.Auto)]
    private struct TokenStats
    {
        public int LeftCount;
        public int RightCount;
        public int FirstLeftIndex;
        public int FirstRightIndex;
    }
}
