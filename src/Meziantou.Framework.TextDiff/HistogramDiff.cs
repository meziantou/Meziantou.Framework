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
        ComputeRange(left, anchor.Value.LeftIndex + 1, leftEnd, right, anchor.Value.RightIndex + 1, rightEnd, comparer, leftModified, rightModified);
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
        var leftFrequency = CountOccurrences(left, leftStart, leftEnd, comparer);
        var rightFrequency = CountOccurrences(right, rightStart, rightEnd, comparer);

        var rightPositions = BuildPositions(right, rightStart, rightEnd, comparer);

        Anchor? best = null;
        var bestScore = int.MaxValue;
        for (var leftIndex = leftStart; leftIndex < leftEnd; leftIndex++)
        {
            var token = left[leftIndex];
            if (!rightPositions.TryGetValue(token, out var positions))
                continue;

            var score = leftFrequency[token] + rightFrequency[token];
            for (var i = 0; i < positions.Count; i++)
            {
                var rightIndex = positions[i];
                if (best is null || score < bestScore || (score == bestScore && IsBetterTieBreak(leftIndex, rightIndex, best.Value)))
                {
                    best = new Anchor(leftIndex, rightIndex);
                    bestScore = score;
                }
            }
        }

        return best;
    }

    private static bool IsBetterTieBreak(int leftIndex, int rightIndex, Anchor current)
    {
        if (leftIndex != current.LeftIndex)
            return leftIndex < current.LeftIndex;

        return rightIndex < current.RightIndex;
    }

    private static Dictionary<string, int> CountOccurrences(string[] values, int start, int end, IEqualityComparer<string> comparer)
    {
        var result = new Dictionary<string, int>(comparer);
        for (var i = start; i < end; i++)
        {
            ref var count = ref CollectionsMarshal.GetValueRefOrAddDefault(result, values[i], out var exists);
            count = exists ? count + 1 : 1;
        }

        return result;
    }

    private static Dictionary<string, List<int>> BuildPositions(string[] values, int start, int end, IEqualityComparer<string> comparer)
    {
        var result = new Dictionary<string, List<int>>(comparer);
        for (var i = start; i < end; i++)
        {
            ref var positions = ref CollectionsMarshal.GetValueRefOrAddDefault(result, values[i], out _);
            positions ??= new List<int>();

            positions.Add(i);
        }

        return result;
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct Anchor(int LeftIndex, int RightIndex);
}
