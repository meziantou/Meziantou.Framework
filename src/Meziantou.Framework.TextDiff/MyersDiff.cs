using System.Runtime.InteropServices;

namespace Meziantou.Framework;

internal static class MyersDiff
{
    private const int StackAllocationThreshold = 128;

    internal static DiffComputationResult Compute(string[] left, string[] right, IEqualityComparer<string> comparer)
    {
        return Compute(left.AsSpan(), right.AsSpan(), comparer);
    }

    internal static DiffComputationResult Compute(ReadOnlySpan<string> left, ReadOnlySpan<string> right, IEqualityComparer<string> comparer)
    {
        var h = new Dictionary<string, int>(left.Length + right.Length, comparer);
        var leftCodesBuffer = left.Length <= StackAllocationThreshold ? stackalloc int[StackAllocationThreshold] : new int[left.Length];
        var rightCodesBuffer = right.Length <= StackAllocationThreshold ? stackalloc int[StackAllocationThreshold] : new int[right.Length];
        var leftCodes = leftCodesBuffer[..left.Length];
        var rightCodes = rightCodesBuffer[..right.Length];
        leftCodes.Clear();
        rightCodes.Clear();

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

        LongCommonSubsequence(leftCodes, leftModified, 0, leftCodes.Length, rightCodes, rightModified, 0, rightCodes.Length, downVector, upVector);

        Optimize(leftCodes, leftModified);
        Optimize(rightCodes, rightModified);
        return new DiffComputationResult(leftModified, rightModified);
    }

    private static void DiffCodes(ReadOnlySpan<string> chunks, Dictionary<string, int> h, Span<int> codes)
    {
        var lastUsedCode = h.Count;

        for (var i = 0; i < chunks.Length; i++)
        {
            var s = chunks[i];
            if (!h.TryGetValue(s, out var value))
            {
                lastUsedCode++;
                h[s] = lastUsedCode;
                codes[i] = lastUsedCode;
            }
            else
            {
                codes[i] = value;
            }
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

            var endPos = startPos;
            while (endPos < modified.Length && modified[endPos])
            {
                endPos++;
            }

            if (endPos < modified.Length && data[startPos] == data[endPos])
            {
                modified[startPos] = false;
                modified[endPos] = true;
            }
            else
            {
                startPos = endPos;
            }
        }
    }

    private static SMSRD ShortestMiddleSnake(ReadOnlySpan<int> dataLeft, int lowerLeft, int upperLeft, ReadOnlySpan<int> dataRight, int lowerRight, int upperRight, Span<int> downVector, Span<int> upVector)
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
                        return new SMSRD { X = downVector[downOffset + k], Y = downVector[downOffset + k] - k };
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
                        return new SMSRD { X = downVector[downOffset + k], Y = downVector[downOffset + k] - k };
                    }
                }
            }
        }

        throw new InvalidOperationException("Diff algorithm failed to find a middle snake.");
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
        Span<int> upVector)
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

        if (lowerLeft == upperLeft)
        {
            while (lowerRight < upperRight)
            {
                rightModified[lowerRight++] = true;
            }
        }
        else if (lowerRight == upperRight)
        {
            while (lowerLeft < upperLeft)
            {
                leftModified[lowerLeft++] = true;
            }
        }
        else
        {
            var smsrd = ShortestMiddleSnake(dataLeft, lowerLeft, upperLeft, dataRight, lowerRight, upperRight, downVector, upVector);
            LongCommonSubsequence(dataLeft, leftModified, lowerLeft, smsrd.X, dataRight, rightModified, lowerRight, smsrd.Y, downVector, upVector);
            LongCommonSubsequence(dataLeft, leftModified, smsrd.X, upperLeft, dataRight, rightModified, smsrd.Y, upperRight, downVector, upVector);
        }
    }

    [StructLayout(LayoutKind.Auto)]
    private struct SMSRD
    {
        public int X;
        public int Y;
    }
}
