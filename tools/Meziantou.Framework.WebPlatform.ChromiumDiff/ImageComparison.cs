using Meziantou.Framework.Imaging;

namespace ChromiumDiff;

/// <summary>The thresholds of the comparison of two images.</summary>
internal sealed record ComparisonThresholds(double MinSsim, double MaxDifferentPixels, double MaxAlphaDifferentPixels, int ChannelTolerance)
{
    /// <summary>
    /// The default thresholds: the text and the edges are anti-aliased differently by Skia and by the library, so a pixel is
    /// different when a channel differs by more than <see cref="ChannelTolerance"/> after a 3x3 blur.
    /// </summary>
    public static ComparisonThresholds Default { get; } = new(MinSsim: 0.98, MaxDifferentPixels: 0.002, MaxAlphaDifferentPixels: 0.002, ChannelTolerance: 24);

    /// <summary>The thresholds of the JPEG images (the encoders do not lose the same details).</summary>
    public static ComparisonThresholds Lossy { get; } = new(MinSsim: 0.9, MaxDifferentPixels: 0.03, MaxAlphaDifferentPixels: 0.01, ChannelTolerance: 32);
}

/// <summary>The metrics of the comparison of a Chromium image with a library image.</summary>
internal sealed record ImageMetrics(int ChromiumWidth, int ChromiumHeight, int LibraryWidth, int LibraryHeight, double Ssim, double DifferentPixels, double AlphaDifferentPixels, int MaxDifference)
{
    public bool SameSize => ChromiumWidth == LibraryWidth && ChromiumHeight == LibraryHeight;

    public bool Passes(ComparisonThresholds thresholds) => SameSize && Ssim >= thresholds.MinSsim && DifferentPixels <= thresholds.MaxDifferentPixels && AlphaDifferentPixels <= thresholds.MaxAlphaDifferentPixels;

    public string Describe() => (SameSize ? $"{ChromiumWidth}x{ChromiumHeight}" : $"size {ChromiumWidth}x{ChromiumHeight} vs {LibraryWidth}x{LibraryHeight}")
        + System.FormattableString.Invariant($", SSIM {Ssim:0.0000}, {DifferentPixels * 100:0.00}% different, {AlphaDifferentPixels * 100:0.00}% alpha different, max {MaxDifference}");
}

/// <summary>
/// Compares a Chromium image with a library image: the images are composited over white and over black (so that a
/// difference of transparency is a difference of color), blurred by a 3x3 box filter (the anti-aliasing of the text and of
/// the edges differs), and compared channel by channel. When the sizes differ, the common top-left area is compared.
/// </summary>
internal static class ImageComparison
{
    public static (ImageMetrics Metrics, Image<Rgba32> Diff) Compare(Image<Rgba32> chromium, Image<Rgba32> library, ComparisonThresholds thresholds)
    {
        var width = Math.Min(chromium.Width, library.Width);
        var height = Math.Min(chromium.Height, library.Height);
        var expected = GetPixels(chromium, width, height);
        var actual = GetPixels(library, width, height);

        var alphaDifferent = 0;
        for (var i = 0; i < expected.Length; i++)
        {
            if (Math.Abs(expected[i].A - actual[i].A) > 8)
                alphaDifferent++;
        }

        var different = new bool[expected.Length];
        var maxDifference = 0;
        var ssim = 1.0;
        foreach (var background in (ReadOnlySpan<byte>)[255, 0])
        {
            var e = Blur(Composite(expected, background), width, height);
            var a = Blur(Composite(actual, background), width, height);
            for (var i = 0; i < e.Length; i++)
            {
                var difference = Math.Max(Math.Abs(e[i].R - a[i].R), Math.Max(Math.Abs(e[i].G - a[i].G), Math.Abs(e[i].B - a[i].B)));
                maxDifference = Math.Max(maxDifference, difference);
                if (difference > thresholds.ChannelTolerance)
                    different[i] = true;
            }

            ssim = Math.Min(ssim, ComputeSsim(Composite(expected, background), Composite(actual, background), width, height));
        }

        var differentCount = different.Count(d => d);
        var count = Math.Max(1, expected.Length);
        var metrics = new ImageMetrics(chromium.Width, chromium.Height, library.Width, library.Height, ssim, (double)differentCount / count, (double)alphaDifferent / count, maxDifference);
        return (metrics, CreateDiffImage(expected, different, width, height));
    }

    private static Rgba32[] GetPixels(Image<Rgba32> image, int width, int height)
    {
        var all = new Rgba32[image.Width * image.Height];
        image.Frames[0].CopyPixelDataTo(all);
        if (width == image.Width && height == image.Height)
            return all;

        var result = new Rgba32[width * height];
        for (var y = 0; y < height; y++)
        {
            all.AsSpan(y * image.Width, width).CopyTo(result.AsSpan(y * width, width));
        }

        return result;
    }

    private static Rgba32[] Composite(Rgba32[] pixels, byte background)
    {
        var result = new Rgba32[pixels.Length];
        for (var i = 0; i < pixels.Length; i++)
        {
            var p = pixels[i];
            result[i] = new Rgba32(Blend(p.R, p.A, background), Blend(p.G, p.A, background), Blend(p.B, p.A, background));
        }

        return result;

        static byte Blend(byte color, byte alpha, byte background) => (byte)(((color * alpha) + (background * (255 - alpha)) + 127) / 255);
    }

    private static Rgba32[] Blur(Rgba32[] pixels, int width, int height)
    {
        var result = new Rgba32[pixels.Length];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                int r = 0, g = 0, b = 0, n = 0;
                for (var dy = -1; dy <= 1; dy++)
                {
                    var yy = y + dy;
                    if (yy < 0 || yy >= height)
                        continue;

                    for (var dx = -1; dx <= 1; dx++)
                    {
                        var xx = x + dx;
                        if (xx < 0 || xx >= width)
                            continue;

                        var p = pixels[(yy * width) + xx];
                        r += p.R;
                        g += p.G;
                        b += p.B;
                        n++;
                    }
                }

                result[(y * width) + x] = new Rgba32((byte)(r / n), (byte)(g / n), (byte)(b / n));
            }
        }

        return result;
    }

    private static double ComputeSsim(Rgba32[] expected, Rgba32[] actual, int width, int height)
    {
        if (width < 8 || height < 8)
            return expected.AsSpan().SequenceEqual(actual) ? 1 : 0;

        using var e = ToImage(expected, width, height);
        using var a = ToImage(actual, width, height);
        return ImageSimilarity.ComputeSsim(e.Frames[0], a.Frames[0]);
    }

    /// <summary>The diff image: the Chromium image faded, with the different pixels in red.</summary>
    private static Image<Rgba32> CreateDiffImage(Rgba32[] expected, bool[] different, int width, int height)
    {
        var pixels = new Rgba32[Math.Max(1, width * height)];
        for (var i = 0; i < expected.Length; i++)
        {
            var p = expected[i];
            var gray = (byte)(((p.R + p.G + p.B) / 3 * p.A / 255 / 4) + 191);
            pixels[i] = different[i] ? new Rgba32(255, 0, 0) : new Rgba32(gray, gray, gray);
        }

        return ToImage(pixels, Math.Max(1, width), Math.Max(1, height));
    }

    /// <summary>Creates an image from its pixels (row by row).</summary>
    public static Image<Rgba32> ToImage(Rgba32[] pixels, int width, int height)
    {
        var image = new Image<Rgba32>(width, height);
        image.Frames[0].ProcessPixelRows(pixels, static (accessor, pixels) =>
        {
            for (var y = 0; y < accessor.Height; y++)
                pixels.AsSpan(y * accessor.Width, accessor.Width).CopyTo(accessor.GetRowSpan(y));
        });

        return image;
    }
}
