using System.Runtime.InteropServices;

namespace Meziantou.Framework;

internal static class MyersDiff
{
    private const int StackAllocationThreshold = 128;

    internal static DiffComputationResult Compute(string[] left, string[] right, IEqualityComparer<string> comparer, DiffBudget budget)
    {
        return Compute(left.AsSpan(), right.AsSpan(), comparer, budget);
    }

    internal static DiffComputationResult Compute(ReadOnlySpan<string> left, ReadOnlySpan<string> right, IEqualityComparer<string> comparer, DiffBudget budget)
    {
        var h = new Dictionary<string, int>(left.Length + right.Length, comparer);
        var leftCodesBuffer = left.Length <= StackAllocationThreshold ? stackalloc int[StackAllocationThreshold] : new int[left.Length];
        var rightCodesBuffer = right.Length <= StackAllocationThreshold ? stackalloc int[StackAllocationThreshold] : new int[right.Length];
        var leftCodes = leftCodesBuffer[..left.Length];
        var rightCodes = rightCodesBuffer[..right.Length];

        // No need to clear the code buffers: DiffCodes assigns every element.
        DiffCodes(left, h, leftCodes);
        DiffCodes(right, h, rightCodes);

        var leftModified = new bool[left.Length];
        var rightModified = new bool[right.Length];

        var max = left.Length + right.Length + 1;
        var vectorLength = (2 * max) + 2;

        var downVectorBuffer = vectorLength <= StackAllocationThreshold ? stackalloc int[StackAllocationThreshold] : new int[vectorLength];
        var downVector = downVectorBuffer[..vectorLength];
        downVector.Clear();

        var upVectorBuffer = vectorLength <= StackAllocationThreshold ? stackalloc int[StackAllocationThreshold] : new int[vectorLength];
        var upVector = upVectorBuffer[..vectorLength];
        upVector.Clear();

        LongCommonSubsequence(leftCodes, leftModified, 0, leftCodes.Length, rightCodes, rightModified, 0, rightCodes.Length, downVector, upVector, budget);

        Optimize(leftCodes, leftModified);
        Optimize(rightCodes, rightModified);
        return new DiffComputationResult(leftModified, rightModified);
    }

    private static void DiffCodes(ReadOnlySpan<string> chunks, Dictionary<string, int> h, Span<int> codes)
    {
        var lastUsedCode = h.Count;

        for (var i = 0; i < chunks.Length; i++)
        {
            ref var value = ref CollectionsMarshal.GetValueRefOrAddDefault(h, chunks[i], out var exists);
            if (!exists)
            {
                lastUsedCode++;
                value = lastUsedCode;
            }

            codes[i] = value;
        }
    }

    private static void Optimize(ReadOnlySpan<int> data, bool[] modified)
    {
        var startPos = 0;
        while (startPos < modified.Length)
        {
            while (startPos < modified.Length && !modified[startPos])
            {
                startPos++;
            }

            if (startPos >= modified.Length)
                break;

            // Slide the run of modified chunks forward while the chunk following the run is identical
            // to the first chunk of the run. endPos only ever moves forward, so sliding a run of length L
            // over a uniform region of length S costs O(L + S) instead of O(L * S).
            var endPos = startPos;
            while (true)
            {
                while (endPos < modified.Length && modified[endPos])
                {
                    endPos++;
                }

                if (endPos >= modified.Length || data[startPos] != data[endPos])
                    break;

                modified[startPos] = false;
                modified[endPos] = true;
                startPos++;
                endPos++;

                // Sliding over a single unmodified chunk merges the run with the one that follows it.
                while (startPos < endPos && !modified[startPos])
                {
                    startPos++;
                }
            }

            startPos = endPos;
        }
    }

    /// <summary>
    /// Finds the point where the shortest edit script of the region crosses its middle diagonal. Returns
    /// <see langword="false"/> when the region must be reported as replaced: the time limit is reached, or the edit cost
    /// limit is reached before the search moves away from the corners of the region.
    /// </summary>
    private static bool ShortestMiddleSnake(ReadOnlySpan<int> dataLeft, int lowerLeft, int upperLeft, ReadOnlySpan<int> dataRight, int lowerRight, int upperRight, Span<int> downVector, Span<int> upVector, DiffBudget budget, out SMSRD result)
    {
        var max = dataLeft.Length + dataRight.Length + 1;

        var downK = lowerLeft - lowerRight;
        var upK = upperLeft - upperRight;

        var delta = upperLeft - lowerLeft - (upperRight - lowerRight);
        var oddDelta = (delta & 1) != 0;

        var downOffset = max - downK;
        var upOffset = max - upK;

        var maxD = ((upperLeft - lowerLeft + upperRight - lowerRight) / 2) + 1;

        downVector[downOffset + downK + 1] = lowerLeft;
        upVector[upOffset + upK - 1] = upperLeft;

        for (var d = 0; d <= maxD; d++)
        {
            if (budget.IsExpired())
            {
                result = default;
                return false;
            }

            // Iteration d finds edit scripts of 2d-1 or 2d edits. Past the limit, split the region at the furthest
            // point the previous iteration reached, as git does: the diff stays valid but may not be minimal.
            if ((2L * d) - 1 > budget.MaxEditCost)
                return FindFurthestPoint(lowerLeft, upperLeft, lowerRight, upperRight, downVector, downOffset, downK, upVector, upOffset, upK, d - 1, out result);

            for (var k = downK - d; k <= downK + d; k += 2)
            {
                int x;
                if (k == downK - d)
                {
                    x = downVector[downOffset + k + 1];
                }
                else
                {
                    x = downVector[downOffset + k - 1] + 1;
                    if (k < downK + d && downVector[downOffset + k + 1] >= x)
                    {
                        x = downVector[downOffset + k + 1];
                    }
                }

                var y = x - k;

                while (x < upperLeft && y < upperRight && dataLeft[x] == dataRight[y])
                {
                    x++;
                    y++;
                }

                downVector[downOffset + k] = x;

                if (oddDelta && upK - d < k && k < upK + d)
                {
                    if (upVector[upOffset + k] <= downVector[downOffset + k])
                    {
                        result = new SMSRD { X = downVector[downOffset + k], Y = downVector[downOffset + k] - k };
                        return true;
                    }
                }
            }

            for (var k = upK - d; k <= upK + d; k += 2)
            {
                int x;
                if (k == upK + d)
                {
                    x = upVector[upOffset + k - 1];
                }
                else
                {
                    x = upVector[upOffset + k + 1] - 1;
                    if (k > upK - d && upVector[upOffset + k - 1] < x)
                    {
                        x = upVector[upOffset + k - 1];
                    }
                }

                var y = x - k;

                while (x > lowerLeft && y > lowerRight && dataLeft[x - 1] == dataRight[y - 1])
                {
                    x--;
                    y--;
                }

                upVector[upOffset + k] = x;

                if (!oddDelta && downK - d <= k && k <= downK + d)
                {
                    if (upVector[upOffset + k] <= downVector[downOffset + k])
                    {
                        result = new SMSRD { X = downVector[downOffset + k], Y = downVector[downOffset + k] - k };
                        return true;
                    }
                }
            }
        }

        throw new InvalidOperationException("Diff algorithm failed to find a middle snake.");
    }

    /// <summary>
    /// Finds, among the points the forward and backward searches reached at step <paramref name="d"/>, the one furthest
    /// from the corner it started from. Any point of the region splits it into two regions whose diffs give a valid diff,
    /// and a point strictly between the corners makes both regions smaller.
    /// </summary>
    private static bool FindFurthestPoint(int lowerLeft, int upperLeft, int lowerRight, int upperRight, Span<int> downVector, int downOffset, int downK, Span<int> upVector, int upOffset, int upK, int d, out SMSRD result)
    {
        var lowerSum = lowerLeft + lowerRight;
        var upperSum = upperLeft + upperRight;
        var bestProgress = 0;
        result = default;

        // The searches do not clamp the diagonals to the region, so skip the points that fall outside of it
        for (var k = downK - d; k <= downK + d; k += 2)
        {
            var x = downVector[downOffset + k];
            var y = x - k;
            if (x < lowerLeft || x > upperLeft || y < lowerRight || y > upperRight || x + y >= upperSum)
                continue;

            var progress = x + y - lowerSum;
            if (progress > bestProgress)
            {
                bestProgress = progress;
                result = new SMSRD { X = x, Y = y };
            }
        }

        for (var k = upK - d; k <= upK + d; k += 2)
        {
            var x = upVector[upOffset + k];
            var y = x - k;
            if (x < lowerLeft || x > upperLeft || y < lowerRight || y > upperRight || x + y <= lowerSum)
                continue;

            var progress = upperSum - (x + y);
            if (progress > bestProgress)
            {
                bestProgress = progress;
                result = new SMSRD { X = x, Y = y };
            }
        }

        return bestProgress > 0;
    }

    private static void LongCommonSubsequence(
        ReadOnlySpan<int> dataLeft,
        bool[] leftModified,
        int lowerLeft,
        int upperLeft,
        ReadOnlySpan<int> dataRight,
        bool[] rightModified,
        int lowerRight,
        int upperRight,
        Span<int> downVector,
        Span<int> upVector,
        DiffBudget budget)
    {
        // Recurse on the smaller of the two regions around the split and iterate on the larger one, so the depth stays
        // logarithmic even when the edit cost limit splits the regions close to their corners.
        while (true)
        {
            while (lowerLeft < upperLeft && lowerRight < upperRight && dataLeft[lowerLeft] == dataRight[lowerRight])
            {
                lowerLeft++;
                lowerRight++;
            }

            while (lowerLeft < upperLeft && lowerRight < upperRight && dataLeft[upperLeft - 1] == dataRight[upperRight - 1])
            {
                --upperLeft;
                --upperRight;
            }

            if (lowerLeft == upperLeft || lowerRight == upperRight || !ShortestMiddleSnake(dataLeft, lowerLeft, upperLeft, dataRight, lowerRight, upperRight, downVector, upVector, budget, out var smsrd))
            {
                leftModified.AsSpan(lowerLeft, upperLeft - lowerLeft).Fill(true);
                rightModified.AsSpan(lowerRight, upperRight - lowerRight).Fill(true);
                return;
            }

            var lowerSize = smsrd.X - lowerLeft + smsrd.Y - lowerRight;
            var upperSize = upperLeft - smsrd.X + upperRight - smsrd.Y;
            if (lowerSize <= upperSize)
            {
                LongCommonSubsequence(dataLeft, leftModified, lowerLeft, smsrd.X, dataRight, rightModified, lowerRight, smsrd.Y, downVector, upVector, budget);
                lowerLeft = smsrd.X;
                lowerRight = smsrd.Y;
            }
            else
            {
                LongCommonSubsequence(dataLeft, leftModified, smsrd.X, upperLeft, dataRight, rightModified, smsrd.Y, upperRight, downVector, upVector, budget);
                upperLeft = smsrd.X;
                upperRight = smsrd.Y;
            }
        }
    }

    [StructLayout(LayoutKind.Auto)]
    private struct SMSRD
    {
        public int X;
        public int Y;
    }
}
