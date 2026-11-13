namespace Meziantou.Framework.Barcodes.Internal.Linear;

/// <summary>Matches runs of a scan line with the patterns of a symbology: widths in modules.</summary>
internal static class RunPatterns
{
    // A run measured more than this far from the width of the pattern, in modules, does not match
    private const double MaxDeviation = 0.75;

    /// <summary>
    /// Finds the pattern closest to the runs, once the runs are scaled to the modules of the patterns.
    /// </summary>
    /// <returns>The index of the pattern, or -1 when no pattern is close enough.</returns>
    public static int FindBest(ScanLine line, int start, int[][] patterns, int modules)
    {
        var count = patterns[0].Length;
        if (start + count > line.Count)
            return -1;

        var scale = modules / line.GetWidth(start, count);
        if (double.IsInfinity(scale))
            return -1;

        var best = -1;
        var bestError = double.MaxValue;
        for (var p = 0; p < patterns.Length; p++)
        {
            var pattern = patterns[p];
            var error = 0d;
            var isClose = true;
            for (var i = 0; i < count; i++)
            {
                var deviation = (line.Runs[start + i] * scale) - pattern[i];
                if (Math.Abs(deviation) > MaxDeviation)
                {
                    isClose = false;
                    break;
                }

                error += deviation * deviation;
            }

            if (isClose && error < bestError)
            {
                bestError = error;
                best = p;
            }
        }

        return best;
    }

    /// <summary>Gets whether the runs match one pattern.</summary>
    public static bool Matches(ScanLine line, int start, int[] pattern)
    {
        var modules = 0;
        foreach (var width in pattern)
        {
            modules += width;
        }

        return FindBest(line, start, [pattern], modules) is 0;
    }

    /// <summary>
    /// Rounds the runs to whole modules, so that they are <paramref name="modules"/> modules wide in total: the runs whose
    /// rounding is the furthest from their width get the modules left over, or give the modules in excess.
    /// </summary>
    /// <returns><see langword="false"/> when a run is less than half a module, or the runs are far from whole modules.</returns>
    public static bool TryRound(ScanLine line, int start, int count, int modules, Span<int> result)
    {
        if (start + count > line.Count)
            return false;

        var scale = modules / line.GetWidth(start, count);
        if (double.IsInfinity(scale))
            return false;

        Span<double> remainders = stackalloc double[count];
        var total = 0;
        for (var i = 0; i < count; i++)
        {
            var value = line.Runs[start + i] * scale;
            var rounded = Math.Max(1, (int)Math.Round(value, MidpointRounding.AwayFromZero));
            result[i] = rounded;
            remainders[i] = value - rounded;
            total += rounded;
        }

        while (total != modules)
        {
            // Add a module where the run is the most under its width, or remove one where it is the most over it
            var index = -1;
            for (var i = 0; i < count; i++)
            {
                if (total < modules ? index < 0 || remainders[i] > remainders[index] : result[i] > 1 && (index < 0 || remainders[i] < remainders[index]))
                {
                    index = i;
                }
            }

            if (index < 0)
                return false;

            var delta = total < modules ? 1 : -1;
            result[index] += delta;
            remainders[index] -= delta;
            total += delta;
        }

        for (var i = 0; i < count; i++)
        {
            if (Math.Abs(remainders[i]) > 1)
                return false;
        }

        return true;
    }

    /// <summary>Converts a pattern of modules, '1' for dark, to the widths of its runs.</summary>
    public static int[] FromModules(string modules)
    {
        var runs = new List<int>();
        var count = 1;
        for (var i = 1; i <= modules.Length; i++)
        {
            if (i < modules.Length && modules[i] == modules[i - 1])
            {
                count++;
                continue;
            }

            runs.Add(count);
            count = 1;
        }

        return [.. runs];
    }

    /// <summary>Converts a pattern of widths written as digits, such as "212222", to an array.</summary>
    public static int[] FromDigits(string digits)
    {
        var runs = new int[digits.Length];
        for (var i = 0; i < digits.Length; i++)
        {
            runs[i] = digits[i] - '0';
        }

        return runs;
    }
}
