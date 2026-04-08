namespace Meziantou.Framework.Internal;

internal static class MaskEvaluator
{
    /// <summary>
    /// Evaluates all 8 mask patterns and returns the one with the lowest penalty score.
    /// </summary>
    public static int FindBestMask(byte[] codewords, int version, ErrorCorrectionLevel ecLevel)
    {
        var bestMask = 0;
        var bestPenalty = int.MaxValue;

        for (var mask = 0; mask < 8; mask++)
        {
            var builder = new MatrixBuilder(version);
            builder.Build(codewords, ecLevel, mask);
            var penalty = EvaluatePenalty(builder.Modules, builder.Size);

            if (penalty < bestPenalty)
            {
                bestPenalty = penalty;
                bestMask = mask;
            }
        }

        return bestMask;
    }

    /// <summary>
    /// Evaluates the total penalty score for a QR code matrix.
    /// </summary>
    public static int EvaluatePenalty(bool[,] modules, int size)
    {
        return EvaluateRule1(modules, size)
            + EvaluateRule2(modules, size)
            + EvaluateRule3(modules, size)
            + EvaluateRule4(modules, size);
    }

    /// <summary>
    /// Rule 1: Penalize runs of 5+ same-color modules in a row or column.
    /// Penalty = N1 + (runLength - 5) for each run, where N1 = 3.
    /// </summary>
    private static int EvaluateRule1(bool[,] modules, int size)
    {
        var penalty = 0;

        // Check rows
        for (var row = 0; row < size; row++)
        {
            var runLength = 1;
            for (var col = 1; col < size; col++)
            {
                if (modules[row, col] == modules[row, col - 1])
                {
                    runLength++;
                }
                else
                {
                    if (runLength >= 5)
                    {
                        penalty += 3 + (runLength - 5);
                    }

                    runLength = 1;
                }
            }

            if (runLength >= 5)
            {
                penalty += 3 + (runLength - 5);
            }
        }

        // Check columns
        for (var col = 0; col < size; col++)
        {
            var runLength = 1;
            for (var row = 1; row < size; row++)
            {
                if (modules[row, col] == modules[row - 1, col])
                {
                    runLength++;
                }
                else
                {
                    if (runLength >= 5)
                    {
                        penalty += 3 + (runLength - 5);
                    }

                    runLength = 1;
                }
            }

            if (runLength >= 5)
            {
                penalty += 3 + (runLength - 5);
            }
        }

        return penalty;
    }

    /// <summary>
    /// Rule 2: Penalize 2x2 blocks of same-color modules. Penalty = 3 per block.
    /// </summary>
    private static int EvaluateRule2(bool[,] modules, int size)
    {
        var penalty = 0;

        for (var row = 0; row < size - 1; row++)
        {
            for (var col = 0; col < size - 1; col++)
            {
                var val = modules[row, col];
                if (val == modules[row, col + 1] &&
                    val == modules[row + 1, col] &&
                    val == modules[row + 1, col + 1])
                {
                    penalty += 3;
                }
            }
        }

        return penalty;
    }

    /// <summary>
    /// Rule 3: Penalize finder-like patterns (10111010000 or 00001011101). Penalty = 40 per occurrence.
    /// </summary>
    private static int EvaluateRule3(bool[,] modules, int size)
    {
        var penalty = 0;

        for (var row = 0; row < size; row++)
        {
            for (var col = 0; col <= size - 11; col++)
            {
                if (MatchesFinderLikePattern(modules, row, col, horizontal: true))
                {
                    penalty += 40;
                }
            }
        }

        for (var col = 0; col < size; col++)
        {
            for (var row = 0; row <= size - 11; row++)
            {
                if (MatchesFinderLikePattern(modules, row, col, horizontal: false))
                {
                    penalty += 40;
                }
            }
        }

        return penalty;
    }

    private static bool MatchesFinderLikePattern(bool[,] modules, int row, int col, bool horizontal)
    {
        // Pattern: dark, light, dark, dark, dark, light, dark, light, light, light, light
        // Or reversed: light, light, light, light, dark, light, dark, dark, dark, light, dark
        ReadOnlySpan<bool> pattern1 = [true, false, true, true, true, false, true, false, false, false, false];
        ReadOnlySpan<bool> pattern2 = [false, false, false, false, true, false, true, true, true, false, true];

        var match1 = true;
        var match2 = true;

        for (var i = 0; i < 11; i++)
        {
            var module = horizontal ? modules[row, col + i] : modules[row + i, col];

            if (module != pattern1[i])
            {
                match1 = false;
            }

            if (module != pattern2[i])
            {
                match2 = false;
            }

            if (!match1 && !match2)
            {
                return false;
            }
        }

        return match1 || match2;
    }

    /// <summary>
    /// Rule 4: Penalize deviation from 50% dark/light ratio.
    /// Penalty = 10 * floor(|(darkPercent - 50) / 5|).
    /// </summary>
    private static int EvaluateRule4(bool[,] modules, int size)
    {
        var darkCount = 0;
        var totalCount = size * size;

        for (var row = 0; row < size; row++)
        {
            for (var col = 0; col < size; col++)
            {
                if (modules[row, col])
                {
                    darkCount++;
                }
            }
        }

        var darkPercent = (darkCount * 100) / totalCount;
        var prevMultiple = darkPercent - (darkPercent % 5);
        var nextMultiple = prevMultiple + 5;
        var penalty = Math.Min(Math.Abs(prevMultiple - 50) / 5, Math.Abs(nextMultiple - 50) / 5);

        return penalty * 10;
    }
}
