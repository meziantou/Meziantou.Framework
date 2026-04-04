using System.Runtime.InteropServices;

namespace Meziantou.Framework;

internal static class MyersDiff
{
    internal static DiffComputationResult Compute(string[] left, string[] right, IEqualityComparer<string> comparer)
    {
        return Compute(left.AsSpan(), right.AsSpan(), comparer);
    }

    internal static DiffComputationResult Compute(ReadOnlySpan<string> left, ReadOnlySpan<string> right, IEqualityComparer<string> comparer)
    {
        const int StackAllocationThreshold = 128;

        var h = new Dictionary<string, int>(left.Length + right.Length, comparer);
        var dataLeft = new DiffData(DiffCodes(left, h));
        var dataRight = new DiffData(DiffCodes(right, h));

        var max = dataLeft.Length + dataRight.Length + 1;
        var vectorLength = (2 * max) + 2;

        var downVectorBuffer = vectorLength <= StackAllocationThreshold ? stackalloc int[StackAllocationThreshold] : new int[vectorLength];
        var downVector = downVectorBuffer[..vectorLength];
        downVector.Clear();

        var upVectorBuffer = vectorLength <= StackAllocationThreshold ? stackalloc int[StackAllocationThreshold] : new int[vectorLength];
        var upVector = upVectorBuffer[..vectorLength];
        upVector.Clear();

        LongCommonSubsequence(dataLeft, 0, dataLeft.Length, dataRight, 0, dataRight.Length, downVector, upVector);

        Optimize(dataLeft);
        Optimize(dataRight);
        return new DiffComputationResult(dataLeft.ToModifiedArray(), dataRight.ToModifiedArray());
    }

    private static int[] DiffCodes(ReadOnlySpan<string> chunks, Dictionary<string, int> h)
    {
        var lastUsedCode = h.Count;
        var codes = new int[chunks.Length];

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

        return codes;
    }

    private static void Optimize(DiffData data)
    {
        var startPos = 0;
        while (startPos < data.Length)
        {
            while (startPos < data.Length && !data.Modified[startPos])
            {
                startPos++;
            }

            var endPos = startPos;
            while (endPos < data.Length && data.Modified[endPos])
            {
                endPos++;
            }

            if (endPos < data.Length && data.Data[startPos] == data.Data[endPos])
            {
                data.Modified[startPos] = false;
                data.Modified[endPos] = true;
            }
            else
            {
                startPos = endPos;
            }
        }
    }

    private static SMSRD ShortestMiddleSnake(DiffData dataLeft, int lowerLeft, int upperLeft, DiffData dataRight, int lowerRight, int upperRight, Span<int> downVector, Span<int> upVector)
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

                while (x < upperLeft && y < upperRight && dataLeft.Data[x] == dataRight.Data[y])
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

                while (x > lowerLeft && y > lowerRight && dataLeft.Data[x - 1] == dataRight.Data[y - 1])
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

    private static void LongCommonSubsequence(DiffData dataLeft, int lowerLeft, int upperLeft, DiffData dataRight, int lowerRight, int upperRight, Span<int> downVector, Span<int> upVector)
    {
        while (lowerLeft < upperLeft && lowerRight < upperRight && dataLeft.Data[lowerLeft] == dataRight.Data[lowerRight])
        {
            lowerLeft++;
            lowerRight++;
        }

        while (lowerLeft < upperLeft && lowerRight < upperRight && dataLeft.Data[upperLeft - 1] == dataRight.Data[upperRight - 1])
        {
            --upperLeft;
            --upperRight;
        }

        if (lowerLeft == upperLeft)
        {
            while (lowerRight < upperRight)
            {
                dataRight.Modified[lowerRight++] = true;
            }
        }
        else if (lowerRight == upperRight)
        {
            while (lowerLeft < upperLeft)
            {
                dataLeft.Modified[lowerLeft++] = true;
            }
        }
        else
        {
            var smsrd = ShortestMiddleSnake(dataLeft, lowerLeft, upperLeft, dataRight, lowerRight, upperRight, downVector, upVector);
            LongCommonSubsequence(dataLeft, lowerLeft, smsrd.X, dataRight, lowerRight, smsrd.Y, downVector, upVector);
            LongCommonSubsequence(dataLeft, smsrd.X, upperLeft, dataRight, smsrd.Y, upperRight, downVector, upVector);
        }
    }

    [StructLayout(LayoutKind.Auto)]
    private struct SMSRD
    {
        public int X;
        public int Y;
    }

    private sealed class DiffData
    {
        internal DiffData(int[] initData)
        {
            Data = initData;
            Length = initData.Length;
            Modified = new bool[initData.Length];
        }

        internal int Length { get; }
        internal int[] Data { get; }
        internal bool[] Modified { get; }

        internal bool[] ToModifiedArray()
        {
            return Modified;
        }
    }
}
