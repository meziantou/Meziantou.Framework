namespace Meziantou.Framework;

internal static class HuntSzymanskiDiff
{
    internal static DiffComputationResult Compute(string[] left, string[] right, IEqualityComparer<string> comparer)
    {
        var rightPositions = BuildRightPositions(right, comparer);
        var links = BuildCandidateLinks(left, rightPositions);

        var leftModified = new bool[left.Length];
        var rightModified = new bool[right.Length];
        Array.Fill(leftModified, value: true);
        Array.Fill(rightModified, value: true);

        var current = links;
        while (current is not null)
        {
            leftModified[current.LeftIndex] = false;
            rightModified[current.RightIndex] = false;
            current = current.Previous;
        }

        return new DiffComputationResult(leftModified, rightModified);
    }

    private static Dictionary<string, List<int>> BuildRightPositions(string[] right, IEqualityComparer<string> comparer)
    {
        var positionsByToken = new Dictionary<string, List<int>>(comparer);
        for (var i = 0; i < right.Length; i++)
        {
            if (!positionsByToken.TryGetValue(right[i], out var positions))
            {
                positions = new List<int>();
                positionsByToken.Add(right[i], positions);
            }

            positions.Add(i);
        }

        return positionsByToken;
    }

    private static MatchNode? BuildCandidateLinks(string[] left, Dictionary<string, List<int>> rightPositions)
    {
        var thresholds = new List<int>();
        var links = new List<MatchNode?>();

        for (var leftIndex = 0; leftIndex < left.Length; leftIndex++)
        {
            if (!rightPositions.TryGetValue(left[leftIndex], out var matches))
                continue;

            for (var i = matches.Count - 1; i >= 0; i--)
            {
                var rightIndex = matches[i];
                var position = DiffAlgorithmHelpers.LowerBound(thresholds, rightIndex);
                var previous = position > 0 ? links[position - 1] : null;
                var node = new MatchNode(leftIndex, rightIndex, previous);

                if (position == thresholds.Count)
                {
                    thresholds.Add(rightIndex);
                    links.Add(node);
                }
                else if (rightIndex < thresholds[position])
                {
                    thresholds[position] = rightIndex;
                    links[position] = node;
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
