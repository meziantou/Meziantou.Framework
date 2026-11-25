using System.Buffers;
using System.Runtime.InteropServices;

namespace Meziantou.Framework;

internal static class HuntSzymanskiDiff
{
    /// <summary>Diffs two regions of chunk codes, from 1 to <paramref name="codeCount"/>.</summary>
    internal static void Compute(ReadOnlySpan<int> left, ReadOnlySpan<int> right, int codeCount, DiffBudget budget, Span<bool> leftModified, Span<bool> rightModified)
    {
        leftModified.Fill(true);
        rightModified.Fill(true);

        // Trim the common prefix and suffix first, as the other three algorithms already do. The number
        // of candidate pairs is quadratic in the number of repeated chunks inside the searched region,
        // so keeping unchanged chunks out of that region matters more here than elsewhere.
        var leftStart = 0;
        var leftEnd = left.Length;
        var rightStart = 0;
        var rightEnd = right.Length;

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

        // The positions of each code on the right side, as linked lists in decreasing order: lastPositions gives the
        // last position of a code, and previousPositions the position before a given one. This replaces a dictionary
        // of strings and a list per distinct chunk.
        var lastPositions = ArrayPool<int>.Shared.Rent(codeCount + 1);
        var previousPositions = ArrayPool<int>.Shared.Rent(right.Length);
        try
        {
            lastPositions.AsSpan(0, codeCount + 1).Fill(-1);
            for (var i = rightStart; i < rightEnd; i++)
            {
                ref var last = ref lastPositions[right[i]];
                previousPositions[i] = last;
                last = i;
            }

            var nodes = new MatchNodes();
            for (var current = BuildCandidateLinks(left, leftStart, leftEnd, lastPositions, previousPositions, budget, nodes); current >= 0; current = nodes[current].Previous)
            {
                var node = nodes[current];
                leftModified[node.LeftIndex] = false;
                rightModified[node.RightIndex] = false;
            }
        }
        finally
        {
            ArrayPool<int>.Shared.Return(lastPositions);
            ArrayPool<int>.Shared.Return(previousPositions);
        }
    }

    /// <summary>Returns the index in <paramref name="nodes"/> of the last match of a longest common subsequence, or -1.</summary>
    private static int BuildCandidateLinks(ReadOnlySpan<int> left, int leftStart, int leftEnd, int[] lastPositions, int[] previousPositions, DiffBudget budget, MatchNodes nodes)
    {
        var thresholds = new List<int>();
        var links = new List<int>(); // The index of a node in nodes

        for (var leftIndex = leftStart; leftIndex < leftEnd; leftIndex++)
        {
            // Without a match, every chunk of the region stays modified: it is reported as replaced
            if (budget.IsExpired())
                return -1;

            for (var rightIndex = lastPositions[left[leftIndex]]; rightIndex >= 0; rightIndex = previousPositions[rightIndex])
            {
                var position = DiffAlgorithmHelpers.LowerBound(thresholds, rightIndex);

                // The node is only added once the candidate is known to improve the threshold list.
                // On input with many duplicate chunks half of the candidates were added and dropped.
                if (position == thresholds.Count)
                {
                    thresholds.Add(rightIndex);
                    links.Add(nodes.Add(new MatchNode(leftIndex, rightIndex, position > 0 ? links[position - 1] : -1)));
                }
                else if (rightIndex < thresholds[position])
                {
                    thresholds[position] = rightIndex;
                    links[position] = nodes.Add(new MatchNode(leftIndex, rightIndex, position > 0 ? links[position - 1] : -1));
                }
            }
        }

        return links.Count == 0 ? -1 : links[^1];
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct MatchNode(int LeftIndex, int RightIndex, int Previous);

    /// <summary>
    /// The nodes, stored in blocks rather than as linked objects: a region with many repeated chunks creates millions
    /// of nodes, and the garbage collector had to trace each of them. Fixed-size blocks are never copied, and stay out
    /// of the large object heap, unlike the array of a growing list.
    /// </summary>
    private sealed class MatchNodes
    {
        private const int BlockShift = 12;
        private const int BlockSize = 1 << BlockShift; // 48 KB per block
        private const int BlockMask = BlockSize - 1;

        private readonly List<MatchNode[]> _blocks = [];
        private int _count;

        public MatchNode this[int index] => _blocks[index >> BlockShift][index & BlockMask];

        /// <summary>Adds <paramref name="node"/> and returns its index.</summary>
        public int Add(MatchNode node)
        {
            var offset = _count & BlockMask;
            if (offset is 0)
            {
                _blocks.Add(new MatchNode[BlockSize]);
            }

            _blocks[^1][offset] = node;
            return _count++;
        }
    }
}
