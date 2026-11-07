using System.Runtime.InteropServices;

namespace Meziantou.Framework;

internal static class PatienceDiff
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

        var previousLeft = leftStart;
        var previousRight = rightStart;
        foreach (var anchor in anchors)
        {
            ComputeRange(left, previousLeft, anchor.LeftIndex, right, previousRight, anchor.RightIndex, comparer, leftModified, rightModified);

            leftModified[anchor.LeftIndex] = false;
            rightModified[anchor.RightIndex] = false;

            previousLeft = anchor.LeftIndex + 1;
            previousRight = anchor.RightIndex + 1;
        }

        ComputeRange(left, previousLeft, leftEnd, right, previousRight, rightEnd, comparer, leftModified, rightModified);
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
        var leftUnique = FindUniquePositions(left, leftStart, leftEnd, comparer);
        var rightUnique = FindUniquePositions(right, rightStart, rightEnd, comparer);

        // Walking the range in order yields the anchors already sorted by left index, which is what the
        // longest-increasing-subsequence pass needs. Enumerating the dictionary instead produced them in
        // an arbitrary order and required a sort.
        var pairs = new List<Anchor>();
        for (var leftIndex = leftStart; leftIndex < leftEnd; leftIndex++)
        {
            var chunk = left[leftIndex];

            // FindUniquePositions stores the index of a chunk that appears once and -1 for a chunk that
            // appears several times, so this only matches chunks that are unique in the range.
            if (leftUnique[chunk] != leftIndex)
                continue;

            if (rightUnique.TryGetValue(chunk, out var rightIndex) && rightIndex >= 0)
            {
                pairs.Add(new Anchor(leftIndex, rightIndex));
            }
        }

        if (pairs.Count == 0)
            return pairs;

        return DiffAlgorithmHelpers.LongestIncreasingSubsequenceByRight(pairs);
    }

    private static Dictionary<string, int> FindUniquePositions(string[] values, int start, int end, IEqualityComparer<string> comparer)
    {
        const int Duplicate = -1;
        var result = new Dictionary<string, int>(comparer);
        for (var i = start; i < end; i++)
        {
            ref var position = ref CollectionsMarshal.GetValueRefOrAddDefault(result, values[i], out var exists);
            position = exists ? Duplicate : i;
        }

        return result;
    }
}
