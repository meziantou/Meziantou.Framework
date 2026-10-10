using Meziantou.Framework.Imaging.CorpusGenerator.Infrastructure;

namespace Meziantou.Framework.Imaging.CorpusGenerator.Common;

/// <summary>
/// Shared building blocks of every generator: the pixel patterns, the PNG/APNG and GIF writers, the container
/// inspections, the metadata expectations and the comparisons with independent decoders.
/// </summary>
internal static partial class CommonCorpus
{
    // -----------------------------------------------------------------------------------------------------------------
    // Hand-defined patterns (documented in tests/Meziantou.Framework.Imaging.Fixtures/README.md). Every channel and row differs so that channel swaps,
    // flips and transpositions are detected.
    // -----------------------------------------------------------------------------------------------------------------

    public static string CanonicalLayout(Img img) => img.Depth == 8 ? "rgba8" : "rgba16le";

    /// <summary>Builds an RGBA8 image from a literal character grid (one character per pixel).</summary>
    public static Img GridImage(IReadOnlyList<string> rows, IReadOnlyDictionary<char, Px> palette)
    {
        var height = rows.Count;
        var width = rows[0].Length;
        Py.Assert(rows.All(r => r.Length == width));
        return new Img(width, height, "rgba", 8, rows.SelectMany(r => r.Select(c => palette[c])));
    }

    /// <summary>Records the pattern function that produced an image (fixture provenance).</summary>
    public static Img Named(Img img, string patternName)
    {
        img.PatternName = patternName;
        return img;
    }

    public static Img PatternCornerMarkers(int w, int h)
    {
        var pixels = new List<Px>();
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var p = new Px((x * 40 + 7) % 256, (y * 50 + 3) % 256, ((x + y) * 17 + 90) % 256, 255 - x * 10 - y * 3);
                if ((x, y) == (0, 0))
                    p = new Px(255, 0, 0, 255);
                else if ((x, y) == (w - 1, 0))
                    p = new Px(0, 255, 0, 255);
                else if ((x, y) == (0, h - 1))
                    p = new Px(0, 0, 255, 255);
                else if ((x, y) == (w - 1, h - 1))
                    p = new Px(255, 255, 255, 128);
                pixels.Add(p);
            }
        }

        return new Img(w, h, "rgba", 8, pixels);
    }

    public static Img PatternRgbOdd(int w, int h) =>
        new(w, h, "rgb", 8, Grid(w, h, (x, y) => new Px((x * 37 + 11) % 256, (y * 59 + 101) % 256, (x * y * 13 + 29) % 256)));

    public static Img PatternCheckerboardGray(int w, int h, int low = 0x20, int high = 0xE0)
    {
        var pixels = Grid(w, h, (x, y) => new Px((x + y) % 2 == 0 ? high : low));
        pixels[0] = new Px(0xFF); // asymmetric marker: detects flips/rotations
        pixels[^1] = new Px(0x00);
        return new Img(w, h, "gray", 8, pixels);
    }

    // Low byte 3x + 2y + 0x11 (never zero, never equal to the high byte): an 8-bit bottleneck or a byte swap changes them
    public static Img PatternGray16LowBits(int w, int h) =>
        new(w, h, "gray", 16, Grid(w, h, (x, y) => new Px(((x * 0x2003) + (y * 0x0102) + 0x0111) & 0xFFFF)));

    public static Img PatternRgba16LowBits(int w, int h) =>
        new(w, h, "rgba", 16, Grid(w, h, (x, y) => new Px((x * 12850 + 1) & 0xFFFF, (65535 - x * y * 1000 - 3) & 0xFFFF, (y * 0x1111 + x + 0x0102) & 0xFFFF, 65535 - x * 4369 - y)));

    public static Img PatternRgb16(int w, int h) =>
        new(w, h, "rgb", 16, Grid(w, h, (x, y) => new Px((x * 21845 + y + 1) & 0xFFFF, (y * 30000 + x * 7 + 0x0203) & 0xFFFF, (65535 - x * 1111 - y * 2222) & 0xFFFF)));

    // Gray and alpha differ in their low bytes; the last pixel is fully transparent with a defined, nonzero gray value
    public static Img PatternGraya16LowBits(int w, int h)
    {
        var pixels = Grid(w, h, (x, y) => new Px(((x * 0x2003) + (y * 0x0102) + 0x0111) & 0xFFFF, (65535 - x * 0x1001 - y * 3) & 0xFFFF));
        pixels[^1] = new Px(pixels[^1][0], 0);
        return new Img(w, h, "graya", 16, pixels);
    }

    public static Img PatternGrayColumn(int w, int h)
    {
        var pixels = Grid(w, h, (x, y) => new Px((y * 29 + x * 61 + 7) % 256));
        pixels[0] = new Px(0xFF);
        pixels[^1] = new Px(0x00);
        return new Img(w, h, "gray", 8, pixels);
    }

    public static Img PatternGrayAlphaRamp(int w, int h) =>
        new(w, h, "graya", 8, Grid(w, h, (x, y) =>
        {
            var alpha = y == 0 ? x * 36 : 255 - x * 36;
            return new Px((x * 32 + 16 + y) % 256, alpha); // alpha 0 keeps a defined, nonzero hidden gray value
        }));

    public static Img PatternRgbaAdam7(int w, int h) =>
        new(w, h, "rgba", 8, Grid(w, h, (x, y) => new Px((x * 28 + 1) % 256, (y * 28 + 2) % 256, ((x ^ y) * 16 + 3) % 256, 255 - ((x + y) % 3) * 60)));

    /// <summary>Smooth, strongly colored gradient for lossy JPEG fixtures (small upsampling disagreements, big swap errors).</summary>
    public static Img PatternSmoothColor(int w, int h) =>
        Named(new Img(w, h, "rgb", 8, Grid(w, h, (x, y) => new Px(
            (int)Py.Round(40 + 180.0 * x / Math.Max(1, w - 1)),
            (int)Py.Round(200 - 150.0 * y / Math.Max(1, h - 1)),
            (int)Py.Round(60 + 40.0 * (x + y) / Math.Max(1, w + h - 2))))), "PatternSmoothColor");

    public static Img PatternSmoothGray(int w, int h) =>
        Named(new Img(w, h, "gray", 8, Grid(w, h, (x, y) => new Px((int)Py.Round(30 + 190.0 * (x + 2 * y) / Math.Max(1, (w - 1) + 2 * (h - 1)))))), "PatternSmoothGray");

    /// <summary>High-frequency colored content for lossy JPEG fixtures: sharp edges, a line pattern, a smooth chroma wave and
    /// seeded noise, so that the IDCT (many AC coefficients), quantization and chroma upsampling are all exercised; the
    /// channels differ everywhere (channel swaps and flips are gross errors).</summary>
    public static Img PatternDetailColor(int w, int h, int seed)
    {
        var rng = new PyRandom(seed);
        var pixels = new List<Px>();
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var edge = (x / 3 + y / 5) % 2 != 0 ? 200 : 40;
                var line = (x + 2 * y) % 7 == 0 ? 80 : 0;
                var r = Py.Round(edge * 0.6 + 70.0 * x / Math.Max(1, w - 1)) + rng.RandInt(-20, 20);
                var g = Py.Round(200 - 150.0 * y / Math.Max(1, h - 1)) + line + rng.RandInt(-20, 20);
                var b = Py.Round(110 + 80 * Math.Sin(x * 0.7) * Math.Cos(y * 0.5)) + rng.RandInt(-15, 15);
                pixels.Add(new Px(Clamp8(r), Clamp8(g), Clamp8(b)));
            }
        }

        return Named(new Img(w, h, "rgb", 8, pixels), "PatternDetailColor");
    }

    public static Img PatternDetailGray(int w, int h, int seed)
    {
        var rng = new PyRandom(seed);
        var pixels = new List<Px>();
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var value = ((x / 2 + y / 3) % 2 != 0 ? 190 : 50) + Py.Round(30 * Math.Sin(x * 0.9 + y * 0.4)) + rng.RandInt(-25, 25);
                pixels.Add(new Px(Clamp8(value)));
            }
        }

        return Named(new Img(w, h, "gray", 8, pixels), "PatternDetailGray");
    }

    public static int Clamp8(long value) => (int)Math.Min(255, Math.Max(0, value));

    /// <summary>Row-major pixels of a w x h grid (y outer, x inner).</summary>
    public static List<Px> Grid(int w, int h, Func<int, int, Px> pixel)
    {
        var pixels = new List<Px>(w * h);
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
                pixels.Add(pixel(x, y));
        }

        return pixels;
    }
}
