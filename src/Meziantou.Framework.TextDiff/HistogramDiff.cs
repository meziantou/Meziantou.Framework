using System.Runtime.InteropServices;

namespace Meziantou.Framework;

internal static class HistogramDiff
{
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
        // Each level consumes a single anchor, so recursing on both sides of it made the recursion
        // depth proportional to the number of anchors. The region after the anchor is a tail call:
        // iterate on it instead of recursing, which leaves only the region before the anchor on the
        // stack. Recursing on both sides overflowed the stack on a few thousand changed chunks.
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

            var anchor = FindBestAnchor(left, leftStart, leftEnd, right, rightStart, rightEnd, comparer);
            if (anchor is null)
            {
                ApplyMyers(left, leftStart, leftEnd, right, rightStart, rightEnd, comparer, leftModified, rightModified);
                return;
            }

            ComputeRange(left, leftStart, anchor.Value.LeftIndex, right, rightStart, anchor.Value.RightIndex, comparer, leftModified, rightModified);
            leftModified[anchor.Value.LeftIndex] = false;
            rightModified[anchor.Value.RightIndex] = false;
            leftStart = anchor.Value.LeftIndex + 1;
            rightStart = anchor.Value.RightIndex + 1;
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

    private static Anchor? FindBestAnchor(string[] left, int leftStart, int leftEnd, string[] right, int rightStart, int rightEnd, IEqualityComparer<string> comparer)
    {
        // A single map holds the occurrence counts on both sides and the first right-hand position of
        // each token. This replaces a second dictionary and the List<int> that used to be allocated per
        // distinct token, which dominated the allocations of this pass.
        var stats = new Dictionary<string, TokenStats>(comparer);
        for (var i = leftStart; i < leftEnd; i++)
        {
            ref var entry = ref CollectionsMarshal.GetValueRefOrAddDefault(stats, left[i], out _);
            entry.LeftCount++;
        }

        // Walking backwards leaves the smallest index of each token in FirstRightIndex.
        for (var i = rightEnd - 1; i >= rightStart; i--)
        {
            ref var entry = ref CollectionsMarshal.GetValueRefOrAddDefault(stats, right[i], out _);
            entry.RightCount++;
            entry.FirstRightIndex = i;
        }

        Anchor? best = null;
        var bestScore = int.MaxValue;
        for (var leftIndex = leftStart; leftIndex < leftEnd; leftIndex++)
        {
            var entry = stats[left[leftIndex]];
            if (entry.RightCount is 0)
                continue;

            // leftIndex only increases and ties are won by the smallest leftIndex, so an anchor found
            // later can only replace the current best with a strictly lower score.
            var score = entry.LeftCount + entry.RightCount;
            if (score >= bestScore)
                continue;

            best = new Anchor(leftIndex, entry.FirstRightIndex);
            bestScore = score;

            // 2 is the lowest reachable score: the token occurs exactly once on each side. Nothing
            // later in the range can beat it, so stop scanning.
            if (score is 2)
                break;
        }

        return best;
    }

    [StructLayout(LayoutKind.Auto)]
    private struct TokenStats
    {
        public int LeftCount;
        public int RightCount;
        public int FirstRightIndex;
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct Anchor(int LeftIndex, int RightIndex);
}
