using System.Runtime.InteropServices;

namespace Meziantou.Framework;

internal static class DiffAlgorithmHelpers
{
    private const int StackAllocationThreshold = 128;

    internal static void ApplySubDiff(DiffComputationResult subDiff, bool[] leftModified, int leftOffset, bool[] rightModified, int rightOffset)
    {
        Array.Copy(subDiff.LeftModified, 0, leftModified, leftOffset, subDiff.LeftLength);
        Array.Copy(subDiff.RightModified, 0, rightModified, rightOffset, subDiff.RightLength);
    }

    internal static int LowerBound(List<int> values, int value)
    {
        var span = CollectionsMarshal.AsSpan(values);
        var low = 0;
        var high = span.Length;
        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            if (span[middle] < value)
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

    /// <summary>
    /// Reduces candidate anchors, already ordered by their left index, to the longest subsequence whose right
    /// indexes also increase. Anchors that cross each other cannot all be kept: an anchor pair is only usable
    /// when it preserves the order of both sides.
    /// </summary>
    internal static List<Anchor> LongestIncreasingSubsequenceByRight(List<Anchor> candidates)
    {
        var count = candidates.Count;
        var tailsBuffer = count <= StackAllocationThreshold ? stackalloc int[StackAllocationThreshold] : new int[count];
        var previousBuffer = count <= StackAllocationThreshold ? stackalloc int[StackAllocationThreshold] : new int[count];

        var tails = tailsBuffer[..count];
        var previous = previousBuffer[..count];
        previous.Fill(-1);
        var length = 0;

        var candidatesSpan = CollectionsMarshal.AsSpan(candidates);
        for (var i = 0; i < count; i++)
        {
            var position = LowerBoundByRight(candidatesSpan, tails, length, candidatesSpan[i].RightIndex);
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
            result.Add(candidatesSpan[index]);
            index = previous[index];
        }

        result.Reverse();
        return result;
    }

    private static int LowerBoundByRight(ReadOnlySpan<Anchor> candidates, ReadOnlySpan<int> tails, int length, int rightIndex)
    {
        var low = 0;
        var high = length;
        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            var value = candidates[tails[middle]].RightIndex;
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
}
