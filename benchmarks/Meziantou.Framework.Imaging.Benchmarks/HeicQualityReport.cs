using System.Globalization;
using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Benchmarks.Workloads;
using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.Benchmarks;

/// <summary>
/// The <c>heic-quality</c> command: encoded size, reconstruction quality and encoding time of HEIC output across
/// compression modes, qualities and efforts, on the inputs and with the measures of the <c>avif-quality</c> command,
/// printed as Markdown tables. A row of lossy AVIF output at the same quality gives a point of comparison: the two
/// encoders map a quality to a quantizer in their own way, so the row compares what a setting gives, not the formats.
/// </summary>
/// <remarks>The HEIC encoder is slow, and its highest effort tries every intra mode: the default inputs are small.</remarks>
internal static class HeicQualityReport
{
    public static void Run(string[] args)
    {
        var size = args.Length > 0 ? int.Parse(args[0], CultureInfo.InvariantCulture) : 256;
        var runs = args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 3;
        Console.WriteLine($"# HEIC quality and size ({size}x{size * 3 / 4} inputs, median of {runs} runs)");
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
        };

        foreach (var (name, pixels) in inputs)
        {
            Console.WriteLine($"## {name}");
            Console.WriteLine();
            Console.WriteLine("| Mode | Setting | Bytes | Bits/pixel | PSNR (dB) | Luma PSNR (dB) | Max alpha error | Encode (ms) |");
            Console.WriteLine("| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |");
            using var image = Image.ImportPixelData<Rgba32>(pixels, width, height);
            foreach (var quality in new[] { 25, 50, 75, 90 })
            {
                foreach (var effort in new[] { 0, 5, 9 })
                {
                    var encoder = new HeicEncoder { Compression = HeicCompression.Lossy, Quality = quality, Effort = effort };
                    AvifQualityReport.Report("lossy 4:2:0", $"q{quality} e{effort}", pixels, width, height, runs, () => AvifQualityReport.Save(image, encoder));
                }

                var full = new HeicEncoder { Compression = HeicCompression.Lossy, Quality = quality, ChromaSubsampling = HeicChromaSubsampling.Ratio444 };
                AvifQualityReport.Report("lossy 4:4:4", $"q{quality} e5", pixels, width, height, runs, () => AvifQualityReport.Save(image, full));
                var optimized = new HeicEncoder { Compression = HeicCompression.Lossy, Quality = quality, OptimizeQuantization = true };
                AvifQualityReport.Report("lossy 4:2:0 optimized", $"q{quality} e5", pixels, width, height, runs, () => AvifQualityReport.Save(image, optimized));
                var avif = new AvifEncoder { Compression = AvifCompression.Lossy, Quality = quality };
                AvifQualityReport.Report("AVIF lossy 4:2:0", $"q{quality} e5", pixels, width, height, runs, () => AvifQualityReport.Save(image, avif));
            }

            foreach (var effort in new[] { 0, 5, 9 })
            {
                var encoder = new HeicEncoder { Effort = effort };
                AvifQualityReport.Report("lossless", $"e{effort}", pixels, width, height, runs, () => AvifQualityReport.Save(image, encoder));
            }

            var losslessAvif = new AvifEncoder();
            AvifQualityReport.Report("AVIF lossless", "e5", pixels, width, height, runs, () => AvifQualityReport.Save(image, losslessAvif));
            Console.WriteLine();
        }
    }
}
