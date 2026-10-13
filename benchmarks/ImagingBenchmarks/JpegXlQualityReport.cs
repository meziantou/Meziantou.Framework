using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Benchmarks.Workloads;
using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.Benchmarks;

/// <summary>
/// The <c>jxl-quality</c> command: encoded size, reconstruction quality, encoding and decoding time, and allocations of
/// JPEG XL output across compression modes, qualities and efforts, on the deterministic synthetic inputs of the
/// <c>webp-quality</c> command, printed as Markdown tables. Rows of WebP, AVIF, JPEG and PNG output at the same settings
/// give points of comparison, and a last table measures JPEG recompression.
/// </summary>
/// <remarks>
/// Every output is decoded by the library and compared with the source over the visible pixels: RGB PSNR and BT.601 luma
/// PSNR, and the maximum alpha error (alpha is always lossless). Times are the median of several runs after a warm-up, on
/// one thread; they depend on the machine and are indicative only. Allocations are the managed bytes allocated by one
/// encoding: buffers rented from the pool are not counted.
/// </remarks>
internal static class JpegXlQualityReport
{
    public static void Run(string[] args)
    {
        var size = args.Length > 0 ? int.Parse(args[0], CultureInfo.InvariantCulture) : 512;
        var runs = args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 3;
        var width = size;
        var height = size * 3 / 4;
        Console.WriteLine($"# JPEG XL quality and size ({width}x{height} inputs, median of {runs} runs)");
        Console.WriteLine();
        Console.WriteLine($"- Runtime: .NET {Environment.Version}, {RuntimeInformation.OSDescription}, {RuntimeInformation.ProcessArchitecture}, {Environment.ProcessorCount} logical cores");
        Console.WriteLine();

        var inputs = new (string Name, Rgba32[] Pixels)[]
        {
            ("photo", BenchmarkPixels.Rgba32(width, height, seed: 11, alpha: false)),
            ("graphics", WebPQualityReport.Graphics(width, height)),
            ("transparent", BenchmarkPixels.Rgba32(width, height, seed: 12, alpha: true)),
        };

        foreach (var (name, pixels) in inputs)
        {
            Console.WriteLine($"## {name}");
            Console.WriteLine();
            Console.WriteLine("| Mode | Setting | Bytes | Bits/pixel | PSNR (dB) | Luma PSNR (dB) | Max alpha error | Encode (ms) | Decode (ms) | Allocated (KB) |");
            Console.WriteLine("| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
            using var image = Image.ImportPixelData<Rgba32>(pixels, width, height);
            foreach (var quality in new[] { 25, 50, 75, 90, 100 })
            {
                foreach (var effort in new[] { 0, 5, 9 })
                {
                    Report("JPEG XL lossy", $"q{quality} e{effort}", pixels, width, height, runs, image, new JpegXlEncoder { Compression = JpegXlCompression.Lossy, Quality = quality, Effort = effort });
                }

                Report("JPEG XL lossy progressive", $"q{quality} e5", pixels, width, height, runs, image, new JpegXlEncoder { Compression = JpegXlCompression.Lossy, Quality = quality, Progressive = true });
                Report("WebP lossy", $"q{quality} e5", pixels, width, height, runs, image, new WebPEncoder { Compression = WebPCompression.Lossy, Quality = quality });
                Report("AVIF lossy 4:4:4", $"q{quality} e5", pixels, width, height, runs, image, new AvifEncoder { Compression = AvifCompression.Lossy, Quality = quality, ChromaSubsampling = AvifChromaSubsampling.Ratio444 });
                if (name != "transparent")
                {
                    // A JPEG file has no alpha channel
                    Report("JPEG 4:4:4", $"q{quality}", pixels, width, height, runs, image, new JpegEncoder { Quality = quality, ChromaSubsampling = JpegChromaSubsampling.Ratio444 });
                }
            }

            foreach (var effort in new[] { 0, 5, 9 })
            {
                Report("JPEG XL lossless", $"e{effort}", pixels, width, height, runs, image, new JpegXlEncoder { Effort = effort });
            }

            Report("JPEG XL lossless progressive", "e5", pixels, width, height, runs, image, new JpegXlEncoder { Progressive = true });
            Report("WebP lossless", "e5", pixels, width, height, runs, image, new WebPEncoder());
            Report("AVIF lossless", "e5", pixels, width, height, runs, image, new AvifEncoder());
            Report("PNG", "default", pixels, width, height, runs, image, new PngEncoder());
            Console.WriteLine();
        }

        ReportRecompression(width, height, runs);
    }

    private static void ReportRecompression(int width, int height, int runs)
    {
        Console.WriteLine("## JPEG recompression");
        Console.WriteLine();
        Console.WriteLine("The JPEG file is recompressed without decoding its pixels, and rebuilt byte for byte.");
        Console.WriteLine();
        Console.WriteLine("| JPEG file | JPEG bytes | JPEG XL bytes | Saved | Recompress (ms) | Rebuild (ms) |");
        Console.WriteLine("| --- | ---: | ---: | ---: | ---: | ---: |");
        using var image = Image.ImportPixelBytes<Rgb24>(BenchmarkPixels.Rgb24(width, height, seed: 11), width, height);
        using var output = new MemoryStream();
        foreach (var (subsampling, name) in new[] { (JpegChromaSubsampling.Ratio444, "4:4:4"), (JpegChromaSubsampling.Ratio420, "4:2:0") })
        {
            foreach (var quality in new[] { 50, 75, 90, 95 })
            {
                var jpeg = Save(image, new JpegEncoder { Quality = quality, ChromaSubsampling = subsampling });
                ImageWorkloads.Recompress(jpeg, output);
                var recompressed = output.ToArray();
                ImageWorkloads.Reconstruct(recompressed, output);
                if (!output.GetBuffer().AsSpan(0, (int)output.Length).SequenceEqual(jpeg))
                    throw new InvalidOperationException("The rebuilt JPEG file is not the JPEG file that was recompressed.");

                var recompress = Time(runs, () => ImageWorkloads.Recompress(jpeg, output));
                var rebuild = Time(runs, () => ImageWorkloads.Reconstruct(recompressed, output));
                var saved = 100.0 * (jpeg.Length - recompressed.Length) / jpeg.Length;
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"| {name} q{quality} | {jpeg.Length} | {recompressed.Length} | {saved:0.0} % | {recompress:0.0} | {rebuild:0.0} |"));
            }
        }

        Console.WriteLine();
    }

    private static void Report(string mode, string setting, Rgba32[] source, int width, int height, int runs, Image image, ImageEncoder encoder)
    {
        var data = Save(image, encoder); // warm-up, and the measured output
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        Save(image, encoder);
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated - data.Length;
        var encode = Time(runs, () => Save(image, encoder));
        var decode = Time(runs, () =>
        {
            using var loaded = Image.Load<Rgba32>(data);
        });

        using var decoded = Image.Load<Rgba32>(data);
        var pixels = new Rgba32[width * height];
        decoded.Frames[0].CopyPixelDataTo(pixels);
        var (psnr, luma, alpha) = AvifQualityReport.Measure(source, pixels);
        var bitsPerPixel = data.Length * 8.0 / (width * height);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"| {mode} | {setting} | {data.Length} | {bitsPerPixel:0.000} | {Format(psnr)} | {Format(luma)} | {alpha} | {encode:0.0} | {decode:0.0} | {Math.Max(allocated, 0) / 1024.0:0} |"));

        static string Format(double value) => double.IsPositiveInfinity(value) ? "lossless" : value.ToString("0.00", CultureInfo.InvariantCulture);
    }

    private static double Time(int runs, Action action)
    {
        action(); // warm-up
        var times = new List<double>();
        for (var i = 0; i < runs; i++)
        {
            var stopwatch = Stopwatch.StartNew();
            action();
            times.Add(stopwatch.Elapsed.TotalMilliseconds);
        }

        times.Sort();
        return times[times.Count / 2];
    }

    private static byte[] Save(Image image, ImageEncoder encoder)
    {
        using var stream = new MemoryStream();
        image.Save(stream, encoder);
        return stream.ToArray();
    }
}
