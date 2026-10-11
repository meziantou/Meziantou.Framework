using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Benchmarks.Workloads;
using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.Benchmarks;

/// <summary>
/// The <c>webp-quality</c> command: encoded size, reconstruction quality and encoding time of WebP output across
/// compression modes, qualities and efforts, on deterministic synthetic inputs, printed as Markdown tables.
/// </summary>
/// <remarks>
/// Every output is decoded by the library and compared with the source over the visible pixels: RGB PSNR and BT.601 luma
/// PSNR (4:2:0 chroma subsampling legitimately averages chroma), and the maximum alpha error (alpha is always lossless).
/// Times are the median of several runs after a warm-up, on one thread; they depend on the machine and are indicative only.
/// </remarks>
internal static class WebPQualityReport
{
    public static void Run(string[] args)
    {
        var size = args.Length > 0 ? int.Parse(args[0], CultureInfo.InvariantCulture) : 512;
        var runs = args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 3;
        Console.WriteLine($"# WebP quality and size ({size}x{size * 3 / 4} inputs, median of {runs} runs)");
        Console.WriteLine();
        Console.WriteLine($"- Runtime: .NET {Environment.Version}, {RuntimeInformation.OSDescription}, {RuntimeInformation.ProcessArchitecture}, {Environment.ProcessorCount} logical cores");
        Console.WriteLine();

        var width = size;
        var height = size * 3 / 4;
        var inputs = new (string Name, Rgba32[] Pixels)[]
        {
            ("photo", BenchmarkPixels.Rgba32(width, height, seed: 11, alpha: false)),
            ("graphics", Graphics(width, height)),
            ("transparent", BenchmarkPixels.Rgba32(width, height, seed: 12, alpha: true)),
        };

        foreach (var (name, pixels) in inputs)
        {
            Console.WriteLine($"## {name}");
            Console.WriteLine();
            Console.WriteLine("| Mode | Setting | Bytes | Bits/pixel | PSNR (dB) | Luma PSNR (dB) | Max alpha error | Encode (ms) |");
            Console.WriteLine("| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |");
            using var image = Image.ImportPixelData<Rgba32>(pixels, width, height);
            foreach (var quality in new[] { 25, 50, 75, 90, 100 })
            {
                foreach (var effort in new[] { 0, 5, 9 })
                {
                    var encoder = new WebPEncoder { Compression = WebPCompression.Lossy, Quality = quality, Effort = effort };
                    Report("lossy", $"q{quality} e{effort}", pixels, width, height, runs, () => Save(image, encoder));
                }
            }

            foreach (var effort in new[] { 0, 5, 9 })
            {
                var encoder = new WebPEncoder { Effort = effort };
                Report("lossless", $"e{effort}", pixels, width, height, runs, () => Save(image, encoder));
            }

            Console.WriteLine();
        }
    }

    private static void Report(string mode, string setting, Rgba32[] source, int width, int height, int runs, Func<byte[]> encode)
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
        var pixels = new Rgba32[width * height];
        ((ImageFrame<Rgba32>)decoded.Frames[0]).CopyPixelDataTo(pixels);
        var (psnr, luma, alpha) = Measure(source, pixels);
        var bitsPerPixel = data.Length * 8.0 / (width * height);
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

    private static byte[] Save(Image image, WebPEncoder encoder)
    {
        using var stream = new MemoryStream();
        image.Save(stream, encoder);
        return stream.ToArray();
    }

    /// <summary>Synthetic graphics: flat colors, sharp axis-aligned and diagonal edges, thin lines and small glyph-like blocks.</summary>
    private static Rgba32[] Graphics(int width, int height)
    {
        Rgba32[] palette = [new(250, 250, 250), new(30, 30, 30), new(220, 40, 40), new(40, 120, 220), new(250, 200, 30), new(60, 170, 80)];
        var pixels = new Rgba32[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var color = palette[0];
                if ((x / 64 + y / 48) % 5 == 1)
                {
                    color = palette[2 + ((x / 64) % 4)];
                }

                if (Math.Abs(x - y - (width / 4)) < 3 || y % 97 == 0 || x % 131 == 0)
                {
                    color = palette[1];
                }

                if (y % 24 < 12 && x % 9 < 6 && (x / 9 + y / 24) % 3 != 0 && y > height / 2)
                {
                    color = palette[1]; // "text"
                }

                pixels[(y * width) + x] = color;
            }
        }

        return pixels;
    }
}
