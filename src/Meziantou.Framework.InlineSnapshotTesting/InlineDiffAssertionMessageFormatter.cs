using System.Runtime.InteropServices;

namespace Meziantou.Framework.InlineSnapshotTesting;

internal sealed class InlineDiffAssertionMessageFormatter : AssertionMessageFormatter
{
    private static readonly string[] LineSeparators = ["\r\n", "\n", "\r"];

    private InlineDiffAssertionMessageFormatter()
    {
    }

    public static AssertionMessageFormatter Instance { get; } = new InlineDiffAssertionMessageFormatter();

    public override string FormatMessage(string? expected, string? actual)
    {
        expected ??= "";
        actual ??= "";

        var sb = new StringBuilder();
        sb.AppendLine("- Snapshot");
        sb.AppendLine("+ Received");
        sb.AppendLine();
        sb.AppendLine();

        var expectedLines = expected.Split(LineSeparators, StringSplitOptions.None);
        var actualLines = actual.Split(LineSeparators, StringSplitOptions.None);
        var (dataLeft, dataRight) = DiffLines(expectedLines, actualLines);

        var lineLeft = 0;
        var lineRight = 0;
        while (lineLeft < dataLeft.Length || lineRight < dataRight.Length)
        {
            if ((lineLeft < dataLeft.Length) && (!dataLeft.Modified[lineLeft])
              && (lineRight < dataRight.Length) && (!dataRight.Modified[lineRight]))
            {
                // equal lines
                sb.Append("  ").AppendLine(expectedLines[lineLeft]);
                lineLeft++;
                lineRight++;
            }
            else
            {
                // deleted/inserted lines
                while (lineLeft < dataLeft.Length && (lineRight >= dataRight.Length || dataLeft.Modified[lineLeft]))
                {
                    sb.Append("- ").AppendLine(expectedLines[lineLeft]);
                    lineLeft++;
                }

                while (lineRight < dataRight.Length && (lineLeft >= dataLeft.Length || dataRight.Modified[lineRight]))
                {
                    sb.Append("+ ").AppendLine(actualLines[lineRight]);
                    lineRight++;
                }
            }
        }

        sb.Remove(sb.Length - Environment.NewLine.Length, Environment.NewLine.Length);
        return sb.ToString();
    }

    private static (DiffData Left, DiffData Right) DiffLines(string[] left, string[] right)
    {
        var h = new Dictionary<string, int>(left.Length + right.Length, StringComparer.Ordinal);
        var dataLeft = new DiffData(DiffCodes(left, h));
        var dataRight = new DiffData(DiffCodes(right, h));

        var max = dataLeft.Length + dataRight.Length + 1;
        var downVector = new int[(2 * max) + 2];
        var upVector = new int[(2 * max) + 2];

        LongCommonSubsequence(dataLeft, 0, dataLeft.Length, dataRight, 0, dataRight.Length, downVector, upVector);

        Optimize(dataLeft);
        Optimize(dataRight);
        return (dataLeft, dataRight);
    }

    private static void Optimize(DiffData data)
    {
        int startPos, endPos;

        startPos = 0;
        while (startPos < data.Length)
        {
            while ((startPos < data.Length) && (!data.Modified[startPos]))
            {
                startPos++;
            }

            endPos = startPos;
            while ((endPos < data.Length) && data.Modified[endPos])
            {
                endPos++;
            }

            if ((endPos < data.Length) && (data.Data[startPos] == data.Data[endPos]))
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

    private static int[] DiffCodes(string[] lines, Dictionary<string, int> h)
    {
        var lastUsedCode = h.Count;
        var codes = new int[lines.Length];

        for (var i = 0; i < lines.Length; ++i)
        {
            var s = lines[i];
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

    private static SMSRD ShortestMiddleSnake(DiffData dataLeft, int lowerLeft, int upperLeft, DiffData dataRight, int lowerRight, int upperRight, int[] downVector, int[] upVector)
    {
        var max = dataLeft.Length + dataRight.Length + 1;

        var downK = lowerLeft - lowerRight; // the k-line to start the forward search
        var upK = upperLeft - upperRight; // the k-line to start the reverse search

        var delta = upperLeft - lowerLeft - (upperRight - lowerRight);
        var oddDelta = (delta & 1) != 0;

        // The vectors in the publication accepts negative indexes. the vectors implemented here are 0-based
        // and are access using a specific offset: UpOffset UpVector and DownOffset for DownVector
        var downOffset = max - downK;
        var upOffset = max - upK;

        var maxD = ((upperLeft - lowerLeft + upperRight - lowerRight) / 2) + 1;

        // init vectors
        downVector[downOffset + downK + 1] = lowerLeft;
        upVector[upOffset + upK - 1] = upperLeft;

        for (var d = 0; d <= maxD; d++)
        {
            // Extend the forward path.
            for (var k = downK - d; k <= downK + d; k += 2)
            {
                // find the only or better starting point
                int x, y;
                if (k == downK - d)
                {
                    x = downVector[downOffset + k + 1]; // down
                }
                else
                {
                    x = downVector[downOffset + k - 1] + 1; // a step to the right
                    if ((k < downK + d) && (downVector[downOffset + k + 1] >= x))
                    {
                        x = downVector[downOffset + k + 1]; // down
                    }
                }

                y = x - k;

                // find the end of the furthest reaching forward D-path in diagonal k.
                while ((x < upperLeft) && (y < upperRight) && (dataLeft.Data[x] == dataRight.Data[y]))
                {
                    x++;
                    y++;
                }

                downVector[downOffset + k] = x;

                // overlap ?
                if (oddDelta && (upK - d < k) && (k < upK + d))
                {
                    if (upVector[upOffset + k] <= downVector[downOffset + k])
                    {
                        return new SMSRD
                        {
                            X = downVector[downOffset + k],
                            Y = downVector[downOffset + k] - k,
                        };
                    }
                }
            }

            // Extend the reverse path.
            for (var k = upK - d; k <= upK + d; k += 2)
            {
                // find the only or better starting point
                int x, y;
                if (k == upK + d)
                {
                    x = upVector[upOffset + k - 1]; // up
                }
                else
                {
                    x = upVector[upOffset + k + 1] - 1; // left
                    if ((k > upK - d) && (upVector[upOffset + k - 1] < x))
                    {
                        x = upVector[upOffset + k - 1]; // up
                    }
                }

                y = x - k;

                while ((x > lowerLeft) && (y > lowerRight) && (dataLeft.Data[x - 1] == dataRight.Data[y - 1]))
                {
                    // diagonal
                    x--;
                    y--;
                }

                upVector[upOffset + k] = x;

                // overlap ?
                if (!oddDelta && (downK - d <= k) && (k <= downK + d))
                {
                    if (upVector[upOffset + k] <= downVector[downOffset + k])
                    {
                        return new SMSRD
                        {
                            X = downVector[downOffset + k],
                            Y = downVector[downOffset + k] - k,
                        };
                    }
                }
            }
        }

        throw new InvalidOperationException("Should not be here");
    }

    private static void LongCommonSubsequence(DiffData dataLeft, int lowerLeft, int upperLeft, DiffData dataRight, int lowerRight, int upperRight, int[] downVector, int[] upVector)
    {
        // Fast walkthrough equal lines at the start
        while (lowerLeft < upperLeft && lowerRight < upperRight && dataLeft.Data[lowerLeft] == dataRight.Data[lowerRight])
        {
            lowerLeft++;
            lowerRight++;
        }

        // Fast walkthrough equal lines at the end
        while (lowerLeft < upperLeft && lowerRight < upperRight && dataLeft.Data[upperLeft - 1] == dataRight.Data[upperRight - 1])
        {
            --upperLeft;
            --upperRight;
        }

        if (lowerLeft == upperLeft)
        {
            // mark as inserted lines.
            while (lowerRight < upperRight)
            {
                dataRight.Modified[lowerRight++] = true;
            }
        }
        else if (lowerRight == upperRight)
        {
            // mark as deleted lines.
            while (lowerLeft < upperLeft)
                dataLeft.Modified[lowerLeft++] = true;

        }
        else
        {
            // Find the middle snake and length of an optimal path for A and B
            var smsrd = ShortestMiddleSnake(dataLeft, lowerLeft, upperLeft, dataRight, lowerRight, upperRight, downVector, upVector);

            // The path is from LowerX to (x,y) and (x,y) to UpperX
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
            Modified = new bool[initData.Length + 2];
        }

        /// <summary>Number of elements (lines).</summary>
        public int Length { get; }

        /// <summary>Buffer of numbers that will be compared.</summary>
        public int[] Data { get; }

        /// <summary>
        /// This is the result of the diff.
        /// This means deletedLeft in the first Data or inserted in the second Data.
        /// </summary>
        public bool[] Modified { get; }
    }
}
