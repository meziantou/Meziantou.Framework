namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Selects the pixels of a frame whose color is close to the one of a seed pixel (the magic wand of image editors). Every
/// pixel is read as a 16-bit color with the rules of pixel conversion and compared with the seed sample by sample, as
/// <see cref="ProcessingKernels.ReplaceColor{TPixel}"/> does; a contiguous selection is then the set of matching pixels
/// reached from the seed through matching neighbors, filled one horizontal run at a time.
/// </summary>
internal static class ColorSelector
{
    /// <summary>The number of runs filled between two checks of the cancellation token.</summary>
    private const int RunsPerCancellationCheck = 4096;

    public static PixelMask Select(ImageFrame frame, Point seed, ColorSelectionOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var tolerance = ProcessingKernels.GetToleranceSteps(options.Tolerance);
        var matches = FindMatches(frame, seed, tolerance, cancellationToken);
        if (!options.Contiguous)
            return matches;

        return Fill(matches, seed, options.Connectivity == PixelConnectivity.Eight, cancellationToken);
    }

    /// <summary>Gets the mask of every pixel whose four 16-bit samples are within <paramref name="tolerance"/> of those of the seed.</summary>
    private static PixelMask FindMatches(ImageFrame frame, Point seed, int tolerance, CancellationToken cancellationToken)
    {
        var width = frame.Width;
        var height = frame.Height;
        var matches = new PixelMask(width, height);
        var storage = frame.GetStorage();
        using var lease = storage.AcquireLease();
        Rgba64 color = default;
        frame.ReadConvertedRgba64(storage, seed.Y, seed.X, new Span<Rgba64>(ref color));
        Span<Rgba64> pixels = stackalloc Rgba64[64];
        for (var y = 0; y < height; y++)
        {
            if (y % ProcessingKernels.RowsPerCancellationCheck == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var rowStart = y * width;
            for (var start = 0; start < width; start += pixels.Length)
            {
                var read = pixels[..Math.Min(pixels.Length, width - start)];
                frame.ReadConvertedRgba64(storage, y, start, read);
                for (var i = 0; i < read.Length; i++)
                {
                    var pixel = read[i];
                    if (Math.Abs(pixel.R - color.R) <= tolerance && Math.Abs(pixel.G - color.G) <= tolerance && Math.Abs(pixel.B - color.B) <= tolerance && Math.Abs(pixel.A - color.A) <= tolerance)
                    {
                        matches.Set(rowStart + start + i);
                    }
                }
            }
        }

        return matches;
    }

    /// <summary>
    /// Gets the matching pixels connected to the seed. A popped pixel that is not filled yet is extended to its whole
    /// horizontal run of matches, which is filled at once, and the first pixel of each run of matches not filled yet in
    /// the rows above and below (one more pixel on each side with diagonals) is pushed. A filled run is always a whole
    /// run of matches, so a pixel that matches and is not filled belongs to a run not filled yet.
    /// </summary>
    private static PixelMask Fill(PixelMask matches, Point seed, bool diagonals, CancellationToken cancellationToken)
    {
        var width = matches.Width;
        var height = matches.Height;
        var selection = new PixelMask(width, height);
        var pending = new Stack<Point>();
        pending.Push(seed);
        var runs = 0;
        while (pending.TryPop(out var point))
        {
            var rowStart = point.Y * width;
            if (selection.Get(rowStart + point.X) || !matches.Get(rowStart + point.X))
                continue;

            if (++runs % RunsPerCancellationCheck == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var left = point.X;
            while (left > 0 && matches.Get(rowStart + left - 1))
            {
                left--;
            }

            var right = point.X;
            while (right < width - 1 && matches.Get(rowStart + right + 1))
            {
                right++;
            }

            for (var x = left; x <= right; x++)
            {
                selection.Set(rowStart + x);
            }

            var first = diagonals ? Math.Max(0, left - 1) : left;
            var last = diagonals ? Math.Min(width - 1, right + 1) : right;
            if (point.Y > 0)
            {
                PushRuns(matches, selection, pending, point.Y - 1, first, last);
            }

            if (point.Y < height - 1)
            {
                PushRuns(matches, selection, pending, point.Y + 1, first, last);
            }
        }

        return selection;
    }

    /// <summary>Pushes the first pixel of every run of matches not filled yet between two columns of a row.</summary>
    private static void PushRuns(PixelMask matches, PixelMask selection, Stack<Point> pending, int y, int first, int last)
    {
        var rowStart = y * matches.Width;
        var inRun = false;
        for (var x = first; x <= last; x++)
        {
            var index = rowStart + x;
            var candidate = matches.Get(index) && !selection.Get(index);
            if (candidate && !inRun)
            {
                pending.Push(new Point(x, y));
            }

            inRun = candidate;
        }
    }
}
