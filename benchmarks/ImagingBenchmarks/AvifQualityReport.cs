using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Benchmarks.Workloads;
using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.Benchmarks;

/// <summary>
/// The <c>avif-quality</c> command: encoded size, reconstruction quality and encoding time of AVIF output across
/// compression modes, qualities and efforts, on the deterministic synthetic inputs of the <c>webp-quality</c> command, on
/// a page of text and on a photograph with edges and little noise, printed as Markdown tables. A row of lossy WebP output at the same quality gives a point of
/// comparison. A second part encodes the text and the graphics at four times the size, without loss, and a third one
/// image sequences: a pan over a photograph by whole samples, a drift by fractions of
/// a sample, a pan with a cut to another scene, a cross-fade and a page of text that scrolls.
/// </summary>
/// <remarks>
/// Arguments: the width of the inputs (256), the number of timed runs (3), and a text that the names of the inputs to
/// encode contain (all of them). Every output is decoded by the library and compared with the source over the visible pixels: RGB PSNR and BT.601 luma
/// PSNR (4:2:0 chroma subsampling legitimately averages chroma), and the maximum alpha error (alpha is always lossless).
/// Times are the median of several runs after a warm-up, on one thread; they depend on the machine and are indicative only.
/// The AVIF encoder is slow: the default inputs are smaller than the ones of <c>webp-quality</c>.
/// </remarks>
internal static class AvifQualityReport
{
    public static void Run(string[] args)
    {
        var size = args.Length > 0 ? int.Parse(args[0], CultureInfo.InvariantCulture) : 256;
        var runs = args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 3;

        // An optional third argument keeps the inputs whose name contains it ("sequence" for every image sequence)
        var filter = args.Length > 2 ? args[2] : "";
        Console.WriteLine($"# AVIF quality and size ({size}x{size * 3 / 4} inputs, median of {runs} runs)");
        Console.WriteLine();
        Console.WriteLine($"- Runtime: .NET {Environment.Version}, {RuntimeInformation.OSDescription}, {RuntimeInformation.ProcessArchitecture}, {Environment.ProcessorCount} logical cores");
        Console.WriteLine();

        var width = size;
        var height = size * 3 / 4;
        var inputs = new (string Name, Rgba32[] Pixels)[]
        {
            ("photo", BenchmarkPixels.Rgba32(width, height, seed: 11, alpha: false)),
            ("graphics", WebPQualityReport.Graphics(width, height)),
            ("transparent", BenchmarkPixels.Rgba32(width, height, seed: 12, alpha: true)),
            ("text", Text(width, height, scroll: 0)),
            ("edges", Edges(width, height)),
        };

        foreach (var (name, pixels) in inputs)
        {
            if (!name.Contains(filter, StringComparison.Ordinal))
                continue;

            Console.WriteLine($"## {name}");
            Console.WriteLine();
            Console.WriteLine("| Mode | Setting | Bytes | Bits/pixel | PSNR (dB) | Luma PSNR (dB) | Max alpha error | Encode (ms) |");
            Console.WriteLine("| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |");
            using var image = Image.ImportPixelData<Rgba32>(pixels, width, height);
            foreach (var quality in new[] { 25, 50, 75, 90 })
            {
                foreach (var effort in new[] { 0, 5, 9 })
                {
                    var encoder = new AvifEncoder { Compression = AvifCompression.Lossy, Quality = quality, Effort = effort };
                    Report("lossy 4:2:0", $"q{quality} e{effort}", pixels, width, height, runs, () => Save(image, encoder));
                }

                var full = new AvifEncoder { Compression = AvifCompression.Lossy, Quality = quality, ChromaSubsampling = AvifChromaSubsampling.Ratio444 };
                Report("lossy 4:4:4", $"q{quality} e5", pixels, width, height, runs, () => Save(image, full));
                var webp = new WebPEncoder { Compression = WebPCompression.Lossy, Quality = quality };
                Report("WebP lossy", $"q{quality} e5", pixels, width, height, runs, () => Save(image, webp));
            }

            foreach (var effort in new[] { 0, 5, 9 })
            {
                var encoder = new AvifEncoder { Effort = effort };
                Report("lossless", $"e{effort}", pixels, width, height, runs, () => Save(image, encoder));
            }

            var losslessWebP = new WebPEncoder();
            Report("WebP lossless", "e5", pixels, width, height, runs, () => Save(image, losslessWebP));
            Console.WriteLine();
        }

        // The content of a screen at four times the size, without loss: a block may be the copy of another one that is
        // far enough before it, which a small image does not have
        var (screenWidth, screenHeight) = (4 * width, 4 * height);
        var screens = new (string Name, Rgba32[] Pixels)[]
        {
            ("screen: text", Text(screenWidth, screenHeight, scroll: 0)),
            ("screen: graphics", WebPQualityReport.Graphics(screenWidth, screenHeight)),
        };

        foreach (var (name, pixels) in screens)
        {
            if (!name.Contains(filter, StringComparison.Ordinal))
                continue;

            Console.WriteLine($"## {name} ({screenWidth}x{screenHeight})");
            Console.WriteLine();
            Console.WriteLine("| Mode | Setting | Bytes | Bits/pixel | PSNR (dB) | Luma PSNR (dB) | Max alpha error | Encode (ms) |");
            Console.WriteLine("| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |");
            using var image = Image.ImportPixelData<Rgba32>(pixels, screenWidth, screenHeight);
            foreach (var effort in new[] { 0, 5, 9 })
            {
                var encoder = new AvifEncoder { Effort = effort };
                Report("lossless", $"e{effort}", pixels, screenWidth, screenHeight, runs, () => Save(image, encoder));
            }

            var losslessWebP = new WebPEncoder();
            Report("WebP lossless", "e5", pixels, screenWidth, screenHeight, runs, () => Save(image, losslessWebP));
            Console.WriteLine();
        }

        // Image sequences, at half the size: every image but the first one is predicted from the ones that precede it
        var sequenceWidth = Math.Max(16, width / 2);
        var sequenceHeight = Math.Max(16, height / 2);
        const int FrameCount = 8;
        var sequences = new (string Name, Rgba32[][] Frames)[]
        {
            ("pan", [.. Enumerable.Range(0, FrameCount).Select(i => Pan(sequenceWidth, sequenceHeight, seed: 11, i))]),
            ("drift", [.. Enumerable.Range(0, FrameCount).Select(i => Drift(sequenceWidth, sequenceHeight, seed: 11, i))]),
            ("cut", [.. Enumerable.Range(0, FrameCount).Select(i => Pan(sequenceWidth, sequenceHeight, seed: i < FrameCount / 2 ? 11 : 13, i))]),
            ("fade", [.. Enumerable.Range(0, FrameCount).Select(i => Fade(Pan(sequenceWidth, sequenceHeight, seed: 11, 0), Pan(sequenceWidth, sequenceHeight, seed: 13, 0), i, FrameCount - 1))]),
            ("scroll", [.. Enumerable.Range(0, FrameCount).Select(i => Text(sequenceWidth, sequenceHeight, scroll: 3 * i))]),
        };

        foreach (var (name, frames) in sequences)
        {
            if (!$"sequence: {name}".Contains(filter, StringComparison.Ordinal))
                continue;

            Console.WriteLine($"## sequence: {name} ({FrameCount} images of {sequenceWidth}x{sequenceHeight})");
            Console.WriteLine();
            Console.WriteLine("| Mode | Setting | Bytes | Bits/pixel | PSNR (dB) | Luma PSNR (dB) | Max alpha error | Encode (ms) |");
            Console.WriteLine("| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |");
            using var image = Image.ImportPixelData<Rgba32>(frames[0], sequenceWidth, sequenceHeight);
            for (var i = 1; i < frames.Length; i++)
            {
                using var frame = Image.ImportPixelData<Rgba32>(frames[i], sequenceWidth, sequenceHeight);
                image.AppendFrame(frame.Frames[0]);
            }

            foreach (var frame in image.Frames)
            {
                frame.Metadata.Duration = FrameDuration.FromMilliseconds(40);
            }

            foreach (var quality in new[] { 50, 75 })
            {
                foreach (var effort in new[] { 0, 5, 9 })
                {
                    var encoder = new AvifEncoder { Compression = AvifCompression.Lossy, Quality = quality, Effort = effort };
                    Report("lossy 4:2:0", $"q{quality} e{effort}", frames, sequenceWidth, sequenceHeight, runs, () => Save(image, encoder));
                }

                var keyFrames = new AvifEncoder { Compression = AvifCompression.Lossy, Quality = quality, KeyFrameInterval = 1 };
                Report("lossy 4:2:0, key frames only", $"q{quality} e5", frames, sequenceWidth, sequenceHeight, runs, () => Save(image, keyFrames));
            }

            foreach (var effort in new[] { 0, 5, 9 })
            {
                var encoder = new AvifEncoder { Effort = effort };
                Report("lossless", $"e{effort}", frames, sequenceWidth, sequenceHeight, runs, () => Save(image, encoder));
            }

            Console.WriteLine();
        }
    }

    // A photograph with less noise than "photo" (a low sensitivity) and with edges: bands and a diagonal line that are
    // brighter than what they cross, which is where a lossy coding leaves the errors that the in-loop filters reduce
    internal static Rgba32[] Edges(int width, int height)
    {
        var pixels = new Rgba32[width * height];
        Span<double> color = stackalloc double[3];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var u = x / (double)width;
                var v = y / (double)height;
                var isEdge = ((x / 37) + (y / 29)) % 3 == 0 || Math.Abs((2 * x) - y - 60) < 4;
                for (var channel = 0; channel < 3; channel++)
                {
                    var phase = 4.07 + (channel * 1.9);
                    var value = 0.45
                        + (0.25 * Math.Sin((u * 5.1) + phase) * Math.Cos((v * 3.7) - phase))
                        + (0.15 * Math.Sin(((u + v) * 13.3) + (channel * 0.7)))
                        + (0.08 * Math.Cos(Math.Sqrt(((u - 0.3) * (u - 0.3)) + ((v - 0.6) * (v - 0.6))) * 40));

                    // A noise of 1% of the range, from a hash of the position
                    var hash = (uint)((x * 374761393) + (y * 668265263) + ((channel + 88) * 1274126177));
                    hash = (hash ^ (hash >> 13)) * 1274126177;
                    hash ^= hash >> 16;
                    value += (((hash & 0xFFFF) / 65535.0) - 0.5) * 0.01;
                    if (isEdge)
                        value += 0.3 - (0.2 * channel);

                    color[channel] = Math.Clamp(value, 0, 1);
                }

                pixels[(y * width) + x] = new Rgba32(To8(color[0]), To8(color[1]), To8(color[2]));
            }
        }

        return pixels;

        static byte To8(double value) => (byte)Math.Round(value * 255, MidpointRounding.AwayFromZero);
    }

    // A page of text: lines of glyphs of 5x7 dots, drawn from a small alphabet so that they repeat as letters do, dark on
    // a light page, with a title line in another color. The page scrolls by whole rows of samples
    internal static Rgba32[] Text(int width, int height, int scroll)
    {
        const int GlyphCount = 40;
        const int CellWidth = 6;
        const int LineHeight = 11;
        var glyphs = new ulong[GlyphCount];
        var state = 0x9E3779B97F4A7C15UL;
        for (var i = 0; i < GlyphCount; i++)
        {
            // Two draws that are combined: about half of the dots of a glyph are inked
            state = (state * 6364136223846793005UL) + 1442695040888963407UL;
            var bits = state >> 20;
            state = (state * 6364136223846793005UL) + 1442695040888963407UL;
            glyphs[i] = (bits | (state >> 24)) & ((1UL << 35) - 1);
        }

        var page = new Rgba32(250, 250, 246);
        var ink = new Rgba32(28, 28, 32);
        var title = new Rgba32(20, 70, 160);
        var pixels = new Rgba32[width * height];
        for (var y = 0; y < height; y++)
        {
            var pageY = y + scroll;
            var line = pageY / LineHeight;
            var glyphY = (pageY % LineHeight) - 2;
            for (var x = 0; x < width; x++)
            {
                var color = page;
                var column = x / CellWidth;
                var glyphX = x % CellWidth;
                var hash = (uint)((line * 7919) + (column * 104729) + (line * column));
                hash ^= hash >> 13;
                hash *= 0x5BD1E995;
                hash ^= hash >> 15;

                // Words of 2 to 8 letters, and a margin
                var isSpace = column < 2 || (column + (line * 3)) % (2 + (line % 7)) == 0;
                if (!isSpace && glyphX < 5 && glyphY is >= 0 and < 7 && ((glyphs[hash % GlyphCount] >> ((glyphY * 5) + glyphX)) & 1) is not 0)
                    color = line % 9 == 0 ? title : ink;

                pixels[(y * width) + x] = color;
            }
        }

        return pixels;
    }

    // The window of a larger photograph, which moves by two samples to the right and one down at every image
    private static Rgba32[] Pan(int width, int height, int seed, int index)
    {
        const int Margin = 32;
        var (sourceWidth, sourceHeight) = (width + Margin, height + Margin);
        var source = BenchmarkPixels.Rgba32(sourceWidth, sourceHeight, seed, alpha: false);
        var pixels = new Rgba32[width * height];
        for (var y = 0; y < height; y++)
        {
            source.AsSpan(((y + index) * sourceWidth) + (2 * index), width).CopyTo(pixels.AsSpan(y * width, width));
        }

        return pixels;
    }

    // The same photograph at four times the size, reduced by averaging blocks of 4x4 samples: the window moves by a
    // sample and a half to the right and three quarters of a sample down at every image
    private static Rgba32[] Drift(int width, int height, int seed, int index)
    {
        const int Scale = 4;
        const int Margin = 16;
        var (sourceWidth, sourceHeight) = (Scale * (width + Margin), Scale * (height + Margin));
        var source = BenchmarkPixels.Rgba32(sourceWidth, sourceHeight, seed, alpha: false);
        var pixels = new Rgba32[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                int r = 0, g = 0, b = 0;
                for (var j = 0; j < Scale; j++)
                {
                    var row = source.AsSpan((((Scale * y) + j + (3 * index)) * sourceWidth) + (Scale * x) + (6 * index), Scale);
                    foreach (var sample in row)
                    {
                        r += sample.R;
                        g += sample.G;
                        b += sample.B;
                    }
                }

                pixels[(y * width) + x] = new Rgba32((byte)((r + 8) / 16), (byte)((g + 8) / 16), (byte)((b + 8) / 16));
            }
        }

        return pixels;
    }

    private static Rgba32[] Fade(Rgba32[] from, Rgba32[] to, int index, int last)
    {
        var pixels = new Rgba32[from.Length];
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = new Rgba32(Mix(from[i].R, to[i].R), Mix(from[i].G, to[i].G), Mix(from[i].B, to[i].B));
        }

        return pixels;

        byte Mix(byte a, byte b) => (byte)(((a * (last - index)) + (b * index) + (last / 2)) / last);
    }

    private static void Report(string mode, string setting, Rgba32[] source, int width, int height, int runs, Func<byte[]> encode)
        => Report(mode, setting, [source], width, height, runs, encode);

    private static void Report(string mode, string setting, Rgba32[][] source, int width, int height, int runs, Func<byte[]> encode)
    {
        var data = encode(); // warm-up, and the measured output
        var times = new List<double>();
        for (var i = 0; i < runs; i++)
        {
            var stopwatch = Stopwatch.StartNew();
            encode();
            times.Add(stopwatch.Elapsed.TotalMilliseconds);
        }

        times.Sort();
        using var decoded = Image.Load<Rgba32>(data);
        if (decoded.Frames.Count != source.Length)
            throw new InvalidOperationException($"The output has {decoded.Frames.Count} images instead of {source.Length}.");

        // The errors of every image together
        var total = new Rgba32[source.Length * width * height];
        var pixels = new Rgba32[source.Length * width * height];
        for (var i = 0; i < source.Length; i++)
        {
            source[i].CopyTo(total.AsSpan(i * width * height));
            ((ImageFrame<Rgba32>)decoded.Frames[i]).CopyPixelDataTo(pixels.AsSpan(i * width * height, width * height));
        }

        var (psnr, luma, alpha) = Measure(total, pixels);
        var bitsPerPixel = data.Length * 8.0 / total.Length;
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"| {mode} | {setting} | {data.Length} | {bitsPerPixel:0.000} | {Format(psnr)} | {Format(luma)} | {alpha} | {times[times.Count / 2]:0.0} |"));

        static string Format(double value) => double.IsPositiveInfinity(value) ? "lossless" : value.ToString("0.00", CultureInfo.InvariantCulture);
    }

    private static (double Psnr, double Luma, int Alpha) Measure(Rgba32[] source, Rgba32[] decoded)
    {
        double squares = 0, lumaSquares = 0;
        var count = 0;
        var alpha = 0;
        for (var i = 0; i < source.Length; i++)
        {
            var s = source[i];
            var d = decoded[i];
            alpha = Math.Max(alpha, Math.Abs(s.A - d.A));
            if (s.A == 0)
                continue;

            squares += Square(s.R - d.R) + Square(s.G - d.G) + Square(s.B - d.B);
            lumaSquares += Square((0.299 * (s.R - d.R)) + (0.587 * (s.G - d.G)) + (0.114 * (s.B - d.B)));
            count++;
        }

        return (Psnr(squares, count * 3), Psnr(lumaSquares, count), alpha);

        static double Square(double value) => value * value;
        static double Psnr(double squares, int count) => squares == 0 ? double.PositiveInfinity : 10 * Math.Log10(255.0 * 255.0 * count / squares);
    }

    private static byte[] Save(Image image, ImageEncoder encoder)
    {
        using var stream = new MemoryStream();
        image.Save(stream, encoder);
        return stream.ToArray();
    }
}
