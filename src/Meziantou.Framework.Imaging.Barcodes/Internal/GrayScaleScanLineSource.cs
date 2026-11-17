namespace Meziantou.Framework.Imaging.Barcodes.Internal;

/// <summary>
/// The runs of the lines of a grayscale image, with the edges between the bars and the spaces placed between the pixels:
/// the luminance is sampled along the line, each sample is compared with the middle of the darkest and lightest samples
/// around it, and an edge is where the luminance crosses that threshold. The widths are in eighths of a pixel, so that the
/// narrow bars of a small or blurred barcode, 2 or 3 pixels wide, keep their proportions.
/// </summary>
internal sealed class GrayScaleScanLineSource : ScanLineSource
{
    private const int Units = 8;

    // The window of the threshold spans a few modules of a small barcode, wide bars and spaces included
    private const int HalfWindow = 12;

    // Less contrast than that is noise, not bars
    private const int MinimumContrast = 24;

    private readonly byte[] _luminance;

    private GrayScaleScanLineSource(int width, int height, byte[] luminance)
        : base(width, height, Units)
    {
        _luminance = luminance;
    }

    public static GrayScaleScanLineSource Create(Image<Gray8> image)
    {
        var pixels = new Gray8[image.Width * image.Height];
        image.Frames[0].CopyPixelDataTo(pixels);
        var luminance = new byte[pixels.Length];
        for (var i = 0; i < pixels.Length; i++)
        {
            luminance[i] = pixels[i].Value;
        }

        return new GrayScaleScanLineSource(image.Width, image.Height, luminance);
    }

    public override int[] GetRuns(PointD direction, PointD normal, double offset, double start, double end)
    {
        var count = Math.Max(0, (int)Math.Ceiling(end - start));
        var samples = new float[count];
        for (var i = 0; i < count; i++)
        {
            var along = start + i + 0.5;
            samples[i] = Sample((direction.X * along) + (normal.X * offset), (direction.Y * along) + (normal.Y * offset));
        }

        var (minimums, maximums) = GetWindowExtremes(samples, HalfWindow);
        var runs = new List<int> { 0 };
        var dark = false;
        var lastEdge = 0;
        for (var i = 0; i < count; i++)
        {
            var contrast = maximums[i] - minimums[i];
            var threshold = (maximums[i] + minimums[i]) / 2;
            var isDark = contrast >= MinimumContrast && samples[i] < threshold;
            if (isDark == dark)
                continue;

            // The edge between the previous sample and this one, where the luminance crosses the threshold
            var position = (double)i;
            if (i > 0)
            {
                var previous = samples[i - 1];
                var difference = previous - samples[i];
                var fraction = Math.Abs(difference) > 1e-3 ? Math.Clamp((previous - threshold) / difference, 0, 1) : 0.5;
                position = i - 1 + fraction;
            }

            var edge = Math.Max(lastEdge, (int)Math.Round(position * Units, MidpointRounding.AwayFromZero));
            runs[^1] += edge - lastEdge;
            runs.Add(0);
            lastEdge = edge;
            dark = isDark;
        }

        runs[^1] += (count * Units) - lastEdge;
        return [.. runs];
    }

    // The bilinear interpolation of the luminance; outside the image, white
    private float Sample(double x, double y)
    {
        x -= 0.5;
        y -= 0.5;
        var x0 = (int)Math.Floor(x);
        var y0 = (int)Math.Floor(y);
        var fx = (float)(x - x0);
        var fy = (float)(y - y0);
        var top = (Get(x0, y0) * (1 - fx)) + (Get(x0 + 1, y0) * fx);
        var bottom = (Get(x0, y0 + 1) * (1 - fx)) + (Get(x0 + 1, y0 + 1) * fx);
        return (top * (1 - fy)) + (bottom * fy);
    }

    private float Get(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height ? _luminance[(y * Width) + x] : 255;

    // The minimum and the maximum of the samples in a window around each sample (monotonic queues)
    private static (float[] Minimums, float[] Maximums) GetWindowExtremes(float[] samples, int halfWindow)
    {
        var minimums = new float[samples.Length];
        var maximums = new float[samples.Length];
        var minQueue = new int[samples.Length];
        var maxQueue = new int[samples.Length];
        int minHead = 0, minTail = 0, maxHead = 0, maxTail = 0;
        var next = 0;
        for (var i = 0; i < samples.Length; i++)
        {
            // Add the samples up to i + halfWindow, remove those before i - halfWindow
            for (; next < samples.Length && next <= i + halfWindow; next++)
            {
                while (minTail > minHead && samples[minQueue[minTail - 1]] >= samples[next])
                {
                    minTail--;
                }

                minQueue[minTail++] = next;
                while (maxTail > maxHead && samples[maxQueue[maxTail - 1]] <= samples[next])
                {
                    maxTail--;
                }

                maxQueue[maxTail++] = next;
            }

            while (minQueue[minHead] < i - halfWindow)
            {
                minHead++;
            }

            while (maxQueue[maxHead] < i - halfWindow)
            {
                maxHead++;
            }

            minimums[i] = samples[minQueue[minHead]];
            maximums[i] = samples[maxQueue[maxHead]];
        }

        return (minimums, maximums);
    }
}
