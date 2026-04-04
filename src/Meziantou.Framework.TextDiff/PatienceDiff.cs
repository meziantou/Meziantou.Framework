using System.Runtime.InteropServices;

namespace Meziantou.Framework;

internal static class PatienceDiff
{
    internal static DiffComputationResult Compute(string[] left, string[] right, IEqualityComparer<string> comparer)
    {
        var leftModified = new bool[left.Length];
        var rightModified = new bool[right.Length];
        Array.Fill(leftModified, value: true);
        Array.Fill(rightModified, value: true);

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
        var leftSlice = new string[leftEnd - leftStart];
        var rightSlice = new string[rightEnd - rightStart];
        Array.Copy(left, leftStart, leftSlice, 0, leftSlice.Length);
        Array.Copy(right, rightStart, rightSlice, 0, rightSlice.Length);

        var subDiff = MyersDiff.Compute(leftSlice, rightSlice, comparer);
        DiffAlgorithmHelpers.ApplySubDiff(subDiff, leftModified, leftStart, rightModified, rightStart);
    }

    private static List<Anchor> FindAnchors(string[] left, int leftStart, int leftEnd, string[] right, int rightStart, int rightEnd, IEqualityComparer<string> comparer)
    {
        var leftUnique = FindUniquePositions(left, leftStart, leftEnd, comparer);
        var rightUnique = FindUniquePositions(right, rightStart, rightEnd, comparer);

        var pairs = new List<Anchor>();
        foreach (var entry in leftUnique)
        {
            if (entry.Value >= 0 && rightUnique.TryGetValue(entry.Key, out var rightIndex) && rightIndex >= 0)
            {
                pairs.Add(new Anchor(entry.Value, rightIndex));
            }
        }

        if (pairs.Count == 0)
            return pairs;

        pairs.Sort((a, b) => a.LeftIndex.CompareTo(b.LeftIndex));
        return LongestIncreasingSubsequenceByRight(pairs);
    }

    private static Dictionary<string, int> FindUniquePositions(string[] values, int start, int end, IEqualityComparer<string> comparer)
    {
        const int Duplicate = -1;
        var result = new Dictionary<string, int>(comparer);
        for (var i = start; i < end; i++)
        {
            var current = values[i];
            if (result.TryGetValue(current, out _))
            {
                result[current] = Duplicate;
            }
            else
            {
                result[current] = i;
            }
        }

        return result;
    }

    private static List<Anchor> LongestIncreasingSubsequenceByRight(List<Anchor> pairs)
    {
        var tails = new int[pairs.Count];
        var previous = new int[pairs.Count];
        Array.Fill(previous, -1);
        var length = 0;

        for (var i = 0; i < pairs.Count; i++)
        {
            var position = LowerBoundByRight(pairs, tails, length, pairs[i].RightIndex);
            if (position > 0)
            {
                previous[i] = tails[position - 1];
            }

            tails[position] = i;
            if (position == length)
            {
                length++;
            }
        }

        var result = new List<Anchor>(length);
        var index = tails[length - 1];
        while (index >= 0)
        {
            result.Add(pairs[index]);
            index = previous[index];
        }

        result.Reverse();
        return result;
    }

    private static int LowerBoundByRight(List<Anchor> pairs, int[] tails, int length, int rightIndex)
    {
        var low = 0;
        var high = length;
        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            var value = pairs[tails[middle]].RightIndex;
            if (value < rightIndex)
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

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct Anchor(int LeftIndex, int RightIndex);
}
