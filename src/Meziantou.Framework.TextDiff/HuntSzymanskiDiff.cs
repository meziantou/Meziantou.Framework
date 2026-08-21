using System.Runtime.InteropServices;

namespace Meziantou.Framework;

internal static class HuntSzymanskiDiff
{
    internal static DiffComputationResult Compute(string[] left, string[] right, IEqualityComparer<string> comparer)
    {
        var leftModified = new bool[left.Length];
        var rightModified = new bool[right.Length];
        Array.Fill(leftModified, true);
        Array.Fill(rightModified, true);

        // Trim the common prefix and suffix first, as the other three algorithms already do. The number
        // of candidate pairs is quadratic in the number of repeated chunks inside the searched region,
        // so keeping unchanged chunks out of that region matters more here than elsewhere.
        var leftStart = 0;
        var leftEnd = left.Length;
        var rightStart = 0;
        var rightEnd = right.Length;

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

        var rightPositions = BuildRightPositions(right, rightStart, rightEnd, comparer);
        var current = BuildCandidateLinks(left, leftStart, leftEnd, rightPositions);
        while (current is not null)
        {
            leftModified[current.LeftIndex] = false;
            rightModified[current.RightIndex] = false;
            current = current.Previous;
        }

        return new DiffComputationResult(leftModified, rightModified);
    }

    private static Dictionary<string, List<int>> BuildRightPositions(string[] right, int rightStart, int rightEnd, IEqualityComparer<string> comparer)
    {
        var positionsByToken = new Dictionary<string, List<int>>(comparer);
        for (var i = rightStart; i < rightEnd; i++)
        {
            ref var positions = ref CollectionsMarshal.GetValueRefOrAddDefault(positionsByToken, right[i], out _);
            positions ??= new List<int>();

            positions.Add(i);
        }

        return positionsByToken;
    }

    private static MatchNode? BuildCandidateLinks(string[] left, int leftStart, int leftEnd, Dictionary<string, List<int>> rightPositions)
    {
        var thresholds = new List<int>();
        var links = new List<MatchNode?>();

        for (var leftIndex = leftStart; leftIndex < leftEnd; leftIndex++)
        {
            if (!rightPositions.TryGetValue(left[leftIndex], out var matches))
                continue;

            for (var i = matches.Count - 1; i >= 0; i--)
            {
                var rightIndex = matches[i];
                var position = DiffAlgorithmHelpers.LowerBound(thresholds, rightIndex);

                // The node is only allocated once the candidate is known to improve the threshold list.
                // On input with many duplicate chunks half of the candidates were allocated and dropped.
                if (position == thresholds.Count)
                {
                    thresholds.Add(rightIndex);
                    links.Add(new MatchNode(leftIndex, rightIndex, position > 0 ? links[position - 1] : null));
                }
                else if (rightIndex < thresholds[position])
                {
                    thresholds[position] = rightIndex;
                    links[position] = new MatchNode(leftIndex, rightIndex, position > 0 ? links[position - 1] : null);
                }
            }
        }

        return links.Count == 0 ? null : links[^1];
    }

    private sealed class MatchNode(int leftIndex, int rightIndex, MatchNode? previous)
    {
        internal int LeftIndex { get; } = leftIndex;
        internal int RightIndex { get; } = rightIndex;
        internal MatchNode? Previous { get; } = previous;
    }
}
