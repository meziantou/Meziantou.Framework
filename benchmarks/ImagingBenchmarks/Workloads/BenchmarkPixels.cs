namespace Meziantou.Framework.Imaging.Benchmarks.Workloads;

/// <summary>
/// Deterministic synthetic source pixels for the benchmark workloads, generated at run time (large inputs are never
/// committed). The content is photo-like (smooth gradients, soft shapes and a low-amplitude texture) so that codecs see
/// realistic entropy, and is computed from closed-form expressions only: it never depends on the code under test.
/// </summary>
internal static class BenchmarkPixels
{
    /// <summary>Gets interleaved 8-bit RGB samples.</summary>
    public static byte[] Rgb24(int width, int height, int seed)
    {
        var pixels = new byte[width * height * 3];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = ((y * width) + x) * 3;
                for (var c = 0; c < 3; c++)
                {
                    pixels[offset + c] = (byte)Math.Clamp((int)Math.Round(Sample(x, y, c, width, height, seed) * 255, MidpointRounding.AwayFromZero), 0, 255);
                }
            }
        }

        return pixels;
    }

    /// <summary>Gets interleaved 8-bit RGBA samples (straight alpha with opaque, translucent and fully transparent areas when <paramref name="alpha"/> is set).</summary>
    public static Rgba32[] Rgba32(int width, int height, int seed, bool alpha)
    {
        var pixels = new Rgba32[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var a = alpha ? Alpha(x, y, width, height) : 1;
                pixels[(y * width) + x] = new Rgba32(To8(Sample(x, y, 0, width, height, seed)), To8(Sample(x, y, 1, width, height, seed)), To8(Sample(x, y, 2, width, height, seed)), To8(a));
            }
        }

        return pixels;
    }

    /// <summary>Gets 16-bit RGBA samples whose low bytes carry real information (no 8-bit replication).</summary>
    public static Rgba64[] Rgba64(int width, int height, int seed, bool alpha)
    {
        var pixels = new Rgba64[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var a = alpha ? Alpha(x, y, width, height) : 1;
                pixels[(y * width) + x] = new Rgba64(To16(Sample(x, y, 0, width, height, seed)), To16(Sample(x, y, 1, width, height, seed)), To16(Sample(x, y, 2, width, height, seed)), To16(a));
            }
        }

        return pixels;
    }

    /// <summary>
    /// Gets the frames of an animation: a static textured background with a few moving opaque shapes, so that consecutive
    /// frames differ in small regions (as in typical GIF/APNG content).
    /// </summary>
    public static Rgba32[][] AnimationFrames(int width, int height, int frameCount, int seed)
    {
        var background = Rgba32(width, height, seed, alpha: false);
        var frames = new Rgba32[frameCount][];
        for (var f = 0; f < frameCount; f++)
        {
            var frame = (Rgba32[])background.Clone();
            for (var shape = 0; shape < 3; shape++)
            {
                var size = (Math.Min(width, height) / 6) + (shape * 4);
                var cx = (int)((width - size) * (0.5 + (0.45 * Math.Sin((f * (0.11 + (shape * 0.05))) + shape))));
                var cy = (int)((height - size) * (0.5 + (0.45 * Math.Cos((f * (0.07 + (shape * 0.03))) + (2 * shape)))));
                var color = new Rgba32((byte)(60 + (shape * 80)), (byte)(200 - (shape * 70)), (byte)(40 + (shape * 90)));
                for (var y = cy; y < cy + size; y++)
                {
                    for (var x = cx; x < cx + size; x++)
                    {
                        var dx = x - cx - (size / 2);
                        var dy = y - cy - (size / 2);
                        if ((dx * dx) + (dy * dy) <= size * size / 4)
                        {
                            frame[(y * width) + x] = color;
                        }
                    }
                }
            }

            frames[f] = frame;
        }

        return frames;
    }

    private static double Sample(int x, int y, int channel, int width, int height, int seed)
    {
        var u = x / (double)width;
        var v = y / (double)height;
        var phase = (seed * 0.37) + (channel * 1.9);
        var value = 0.45
            + (0.25 * Math.Sin((u * 5.1) + phase) * Math.Cos((v * 3.7) - phase))
            + (0.15 * Math.Sin(((u + v) * 13.3) + (channel * 0.7)))
            + (0.08 * Math.Cos(Math.Sqrt(((u - 0.3) * (u - 0.3)) + ((v - 0.6) * (v - 0.6))) * 40));

        // Low-amplitude texture (sensor-like noise), +-2.5%
        value += (Hash(x, y, channel + (seed * 8)) - 0.5) * 0.05;
        return Math.Clamp(value, 0, 1);
    }

    private static double Alpha(int x, int y, int width, int height)
    {
        // Opaque center, smooth falloff, fully transparent corners
        var dx = (x - (width / 2.0)) / width;
        var dy = (y - (height / 2.0)) / height;
        var distance = Math.Sqrt((dx * dx) + (dy * dy));
        return Math.Clamp(1.6 - (distance * 3.2), 0, 1);
    }

    private static double Hash(int x, int y, int salt)
    {
        var h = (uint)((x * 374761393) + (y * 668265263) + (salt * 1274126177));
        h = (h ^ (h >> 13)) * 1274126177;
        h ^= h >> 16;
        return (h & 0xFFFF) / 65535.0;
    }

    private static byte To8(double value) => (byte)Math.Clamp((int)Math.Round(value * 255, MidpointRounding.AwayFromZero), 0, 255);

    private static ushort To16(double value) => (ushort)Math.Clamp((int)Math.Round(value * 65535, MidpointRounding.AwayFromZero), 0, 65535);
}
