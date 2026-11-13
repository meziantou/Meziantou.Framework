namespace Meziantou.Framework.Imaging.Barcodes.Internal;

/// <summary>
/// Finds the finder patterns of QR codes (ISO/IEC 18004, 12): a row crosses them dark, light, dark 3 modules wide,
/// light, dark (1:1:3:1:1), and the column and row through their center confirm them.
/// </summary>
internal static class PatternFinder
{
    public static readonly int[] FinderRatios = [1, 1, 3, 1, 1];

    public static List<PatternCenter> Find(BinaryImage image, int[] ratios, CancellationToken cancellationToken)
    {
        var modules = ratios.Sum();
        var centers = new List<PatternCenter>();
        Span<int> counts = stackalloc int[5];
        for (var y = 0; y < image.Height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // The last 5 runs of the row, the last one dark
            var runs = image.GetRowRuns(y);
            var x = 0;
            for (var i = 0; i < runs.Length; i++)
            {
                x += runs[i];
                if ((i & 1) is 0 || i < 5)
                    continue;

                for (var k = 0; k < 5; k++)
                {
                    counts[k] = runs[i - 4 + k];
                }

                if (!MatchesRatios(counts, ratios, modules))
                    continue;

                var centerX = x - counts[4] - counts[3] - (counts[2] / 2.0);
                var total = counts[0] + counts[1] + counts[2] + counts[3] + counts[4];
                if (TryConfirm(image, centerX, y + 0.5, ratios, modules, total) is { } center)
                {
                    Add(centers, center);
                }
            }
        }

        return centers;
    }

    private static bool MatchesRatios(ReadOnlySpan<int> counts, int[] ratios, int modules)
    {
        var total = 0;
        foreach (var count in counts)
        {
            if (count is 0)
                return false;

            total += count;
        }

        if (total < modules)
            return false;

        var moduleSize = total / (double)modules;
        for (var k = 0; k < 5; k++)
        {
            var expected = ratios[k] * moduleSize;
            if (Math.Abs(counts[k] - expected) > Math.Max(0.5, ratios[k] is 1 ? moduleSize * 0.6 : moduleSize * 1.5))
                return false;
        }

        return true;
    }

    // The column through the center must cross the pattern too, with about the same size; then the row through the center
    // of the column gives the center of the pattern
    private static PatternCenter? TryConfirm(BinaryImage image, double x, double y, int[] ratios, int modules, int horizontalTotal)
    {
        var columnCenter = CrossCheck(image, x, y, vertical: true, ratios, modules, horizontalTotal);
        if (columnCenter is not { } centerY)
            return null;

        var rowCenter = CrossCheck(image, x, centerY.Center, vertical: false, ratios, modules, horizontalTotal);
        if (rowCenter is not { } centerX)
            return null;

        var moduleSize = (centerX.Total + centerY.Total) / 2.0 / modules;
        return new PatternCenter(centerX.Center, centerY.Center, moduleSize, 1);
    }

    private static (double Center, int Total)? CrossCheck(BinaryImage image, double x, double y, bool vertical, int[] ratios, int modules, int expectedTotal)
    {
        var px = (int)Math.Floor(x);
        var py = (int)Math.Floor(y);
        bool IsDark(int offset) => vertical ? image.IsDark(px, py + offset) : image.IsDark(px + offset, py);

        if (!IsDark(0))
            return null;

        // From the center: the center run, then two runs on each side
        Span<int> counts = stackalloc int[5];
        var limit = expectedTotal * 2;
        var offset = 0;
        while (IsDark(offset) && offset > -limit)
        {
            counts[2]++;
            offset--;
        }

        for (var k = 1; k >= 0; k--)
        {
            var color = k is 1 ? false : true;
            while (offset > -limit && IsDark(offset) == color)
            {
                counts[k]++;
                offset--;
            }
        }

        var start = offset + 1;
        offset = 1;
        while (IsDark(offset) && offset < limit)
        {
            counts[2]++;
            offset++;
        }

        for (var k = 3; k <= 4; k++)
        {
            var color = k is 3 ? false : true;
            while (offset < limit && IsDark(offset) == color)
            {
                counts[k]++;
                offset++;
            }
        }

        // The pattern is followed by a light module on both sides
        if (IsDark(start - 1) || IsDark(offset))
            return null;

        if (!MatchesRatios(counts, ratios, modules))
            return null;

        var total = counts[0] + counts[1] + counts[2] + counts[3] + counts[4];
        if (Math.Abs(total - expectedTotal) > expectedTotal * 0.6)
            return null;

        var centerOffset = start + counts[0] + counts[1] + (counts[2] / 2.0);
        return ((vertical ? py : px) + centerOffset, total);
    }

    // Merges the detections of the same pattern on consecutive rows
    private static void Add(List<PatternCenter> centers, PatternCenter center)
    {
        for (var i = 0; i < centers.Count; i++)
        {
            var existing = centers[i];
            if (Math.Abs(existing.X - center.X) <= existing.ModuleSize * 1.5 && Math.Abs(existing.Y - center.Y) <= existing.ModuleSize * 1.5 && Math.Abs(existing.ModuleSize - center.ModuleSize) <= existing.ModuleSize * 0.5)
            {
                var count = existing.Count + 1;
                centers[i] = new PatternCenter(
                    ((existing.X * existing.Count) + center.X) / count,
                    ((existing.Y * existing.Count) + center.Y) / count,
                    ((existing.ModuleSize * existing.Count) + center.ModuleSize) / count,
                    count);
                return;
            }
        }

        centers.Add(center);
    }
}
