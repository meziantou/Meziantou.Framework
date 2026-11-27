using System.Buffers;
using System.Runtime.InteropServices;

namespace Meziantou.Framework;

internal static class DiffAlgorithmHelpers
{
    private const int StackAllocationThreshold = 128;

    internal static int CommonPrefixLength(ReadOnlySpan<string> left, ReadOnlySpan<string> right, IEqualityComparer<string> comparer)
    {
        var minLength = Math.Min(left.Length, right.Length);
        var length = 0;
        while (length < minLength && comparer.Equals(left[length], right[length]))
        {
            length++;
        }

        return length;
    }

    internal static int CommonSuffixLength(ReadOnlySpan<string> left, ReadOnlySpan<string> right, IEqualityComparer<string> comparer)
    {
        var minLength = Math.Min(left.Length, right.Length);
        var length = 0;
        while (length < minLength && comparer.Equals(left[^(length + 1)], right[^(length + 1)]))
        {
            length++;
        }

        return length;
    }

    /// <summary>
    /// Gives each chunk a code from 1 to the returned number of distinct chunks, so that two chunks have the same code
    /// when <paramref name="comparer"/> considers them equal. The algorithms compare codes instead of strings.
    /// </summary>
    internal static int ComputeCodes(ReadOnlySpan<string> left, ReadOnlySpan<string> right, IEqualityComparer<string> comparer, Span<int> leftCodes, Span<int> rightCodes)
    {
        // Most chunks of one side are found on the other side, so this rarely grows the dictionary. Sizing it for
        // both sides allocated twice the entries needed in most diffs.
        var codes = new Dictionary<string, int>(Math.Max(left.Length, right.Length), comparer);
        AssignCodes(left, codes, leftCodes);
        AssignCodes(right, codes, rightCodes);
        return codes.Count;

        static void AssignCodes(ReadOnlySpan<string> chunks, Dictionary<string, int> codes, Span<int> result)
        {
            for (var i = 0; i < chunks.Length; i++)
            {
                ref var code = ref CollectionsMarshal.GetValueRefOrAddDefault(codes, chunks[i], out var exists);
                if (!exists)
                {
                    code = codes.Count;
                }

                result[i] = code;
            }
        }
    }

    internal static void Return<T>(T[]? array)
    {
        if (array is not null)
        {
            ArrayPool<T>.Shared.Return(array);
        }
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
