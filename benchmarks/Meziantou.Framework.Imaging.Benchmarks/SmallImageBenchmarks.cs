using BenchmarkDotNet.Attributes;
using Meziantou.Framework.Imaging.Benchmarks.Infrastructure;
using Meziantou.Framework.Imaging.Benchmarks.Workloads;
using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.Benchmarks;

/// <summary>
/// Decoding the small images of a web page: 20 icons of 16x16 to 64x64 pixels per format (five sizes, four contents,
/// translucent where the format has alpha), encoded by the library at setup. The pixels are few, so the fixed cost of a
/// load (format detection, parsers, decoder state, the image itself) weighs as much as the decoding. The same icons are
/// encoded by <see cref="SaveWebP"/>, where the analysis of the encoder (histograms of sparse alphabets) weighs as much.
/// </summary>
[Config(typeof(WorkloadConfig))]
public class SmallImageBenchmarks : IDisposable
{
    private static readonly int[] Sizes = [16, 24, 32, 48, 64];

    private byte[][] _gif = [];
    private byte[][] _png = [];
    private byte[][] _jpeg = [];
    private byte[][] _webp = [];
    private Image<Rgba32>[] _icons = [];
    private readonly MemoryStream _output = new();

    [GlobalSetup]
    public void Setup()
    {
        _gif = Encode(new GifEncoder(), alpha: true);
        _png = Encode(new PngEncoder(), alpha: true);
        _jpeg = Encode(new JpegEncoder(), alpha: false);
        _webp = Encode(new WebPEncoder(), alpha: true);
        _icons = [.. Sizes.SelectMany(size => Enumerable.Range(0, 4).Select(seed => Image.ImportPixelData<Rgba32>(BenchmarkPixels.Rgba32(size, size, seed, alpha: true), size, size)))];
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        foreach (var icon in _icons)
        {
            icon.Dispose();
        }

        _icons = [];
    }

    [Benchmark]
    [Workload("20 GIF icons of 16x16 to 64x64 -> Rgba32", Frames = 20)]
    public int LoadGif() => Load(_gif);

    [Benchmark]
    [Workload("20 PNG icons of 16x16 to 64x64 -> Rgba32", Frames = 20)]
    public int LoadPng() => Load(_png);

    [Benchmark]
    [Workload("20 baseline JPEG icons of 16x16 to 64x64 -> Rgba32", Frames = 20)]
    public int LoadJpeg() => Load(_jpeg);

    [Benchmark]
    [Workload("20 lossless WebP icons of 16x16 to 64x64 -> Rgba32", Frames = 20)]
    public int LoadWebP() => Load(_webp);

    [Benchmark]
    [Workload("20 Rgba32 icons of 16x16 to 64x64 -> lossless WebP", Frames = 20, ReturnsOutputBytes = true)]
    public long SaveWebP()
    {
        long bytes = 0;
        foreach (var icon in _icons)
        {
            bytes += ImageWorkloads.Encode(icon, new WebPEncoder(), _output);
        }

        return bytes;
    }

    public void Dispose()
    {
        Cleanup();
        _output.Dispose();
        GC.SuppressFinalize(this);
    }

    private static int Load(byte[][] files)
    {
        var width = 0;
        foreach (var file in files)
        {
            using var image = Image.Load<Rgba32>(file);
            width += image.Width;
        }

        return width;
    }

    private static byte[][] Encode(ImageEncoder encoder, bool alpha)
    {
        var files = new List<byte[]>();
        foreach (var size in Sizes)
        {
            for (var seed = 0; seed < 4; seed++)
            {
                using var image = Image.ImportPixelData<Rgba32>(BenchmarkPixels.Rgba32(size, size, seed, alpha), size, size);
                using var output = new MemoryStream();
                if (alpha)
                {
                    image.Save(output, encoder);
                }
                else
                {
                    using var opaque = image.CloneAs<Rgb24>();
                    opaque.Save(output, encoder);
                }

                files.Add(output.ToArray());
            }
        }

        return [.. files];
    }
}
