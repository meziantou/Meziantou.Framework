using BenchmarkDotNet.Attributes;
using Meziantou.Framework.Imaging.Benchmarks.Infrastructure;
using Meziantou.Framework.Imaging.Benchmarks.Workloads;
using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.Benchmarks;

/// <summary>
/// Color reduction of a 1920x1080 photo to a palette: building the palette (median cut), mapping the pixels to it with and
/// without dithering, and the still images that GIF and indexed PNG save from true-color pixels. Each in-place operation
/// clones the source first so it can be repeated; <see cref="Clone"/> measures that copy alone. The animations of
/// <see cref="AnimationBenchmarks"/> reduce the colors of small frames; these are the photo-sized cases.
/// </summary>
[Config(typeof(WorkloadConfig))]
public class QuantizationBenchmarks : IDisposable
{
    private readonly MemoryStream _output = new();
    private Image<Rgba32> _photo = null!;
    private Image<Rgba32> _transparent = null!;
    private ImagePalette _palette = null!;

    [GlobalSetup]
    public void Setup()
    {
        _photo = Image.ImportPixelData<Rgba32>(BenchmarkInputs.ResizeRgba32OpaqueSource, BenchmarkInputs.ResizeWidth, BenchmarkInputs.ResizeHeight);
        _transparent = Image.ImportPixelData<Rgba32>(BenchmarkInputs.ResizeRgba32Source, BenchmarkInputs.ResizeWidth, BenchmarkInputs.ResizeHeight);
        using var builder = new PaletteBuilder();
        builder.Add(_photo);
        _palette = builder.Build();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _photo?.Dispose();
        _transparent?.Dispose();
    }

    [Benchmark(Baseline = true)]
    [Workload("1920x1080 Rgba32 clone only (no operation)")]
    public int Clone()
    {
        using var copy = _photo.Clone();
        return copy.Width;
    }

    [Benchmark]
    [Workload("1920x1080 Rgba32 palette of 256 colors (median cut), no mapping")]
    public int BuildPalette()
    {
        using var builder = new PaletteBuilder();
        builder.Add(_photo);
        return builder.Build().Count;
    }

    [Benchmark]
    [Workload("1920x1080 Rgba32 mapped to a given palette of 256 colors, no dithering")]
    public int MapToPalette() => Apply(_photo, new QuantizeOptions { Palette = _palette });

    [Benchmark]
    [Workload("1920x1080 Rgba32 reduced to 256 colors, no dithering")]
    public int Quantize256() => Apply(_photo, new QuantizeOptions());

    [Benchmark]
    [Workload("1920x1080 Rgba32 reduced to 256 colors, Floyd-Steinberg dithering")]
    public int Quantize256FloydSteinberg() => Apply(_photo, new QuantizeOptions { Dithering = DitheringMode.FloydSteinberg });

    [Benchmark]
    [Workload("1920x1080 Rgba32 reduced to 256 colors, Bayer 8x8 ordered dithering")]
    public int Quantize256Bayer() => Apply(_photo, new QuantizeOptions { Dithering = DitheringMode.Bayer8x8 });

    [Benchmark]
    [Workload("1920x1080 Rgba32 reduced to 16 colors, Atkinson dithering")]
    public int Quantize16Atkinson() => Apply(_photo, new QuantizeOptions { MaxColors = 16, Dithering = DitheringMode.Atkinson });

    [Benchmark]
    [Workload("1920x1080 Rgba32 with transparency reduced to 256 colors, Floyd-Steinberg dithering")]
    public int Quantize256Transparent() => Apply(_transparent, new QuantizeOptions { Dithering = DitheringMode.FloydSteinberg });

    [Benchmark]
    [Workload("1920x1080 Rgba32 -> still GIF, 256 colors, no dithering", ReturnsOutputBytes = true)]
    public long SaveGif() => ImageWorkloads.Encode(_photo, new GifEncoder(), _output);

    [Benchmark]
    [Workload("1920x1080 Rgba32 -> still GIF, 256 colors, Floyd-Steinberg dithering", ReturnsOutputBytes = true)]
    public long SaveGifDithered() => ImageWorkloads.Encode(_photo, new GifEncoder { Quantization = new QuantizeOptions { Dithering = DitheringMode.FloydSteinberg } }, _output);

    [Benchmark]
    [Workload("1920x1080 Rgba32 with transparency -> still GIF, alpha threshold, Floyd-Steinberg dithering", ReturnsOutputBytes = true)]
    public long SaveGifTransparent() => ImageWorkloads.Encode(_transparent, new GifEncoder { Quantization = new QuantizeOptions { Dithering = DitheringMode.FloydSteinberg } }, _output);

    [Benchmark]
    [Workload("1920x1080 Rgba32 with transparency -> indexed PNG, 256 colors with tRNS, Floyd-Steinberg dithering", ReturnsOutputBytes = true)]
    public long SavePngIndexed() => ImageWorkloads.Encode(_transparent, new PngEncoder { PixelLayout = PngPixelLayout.Indexed, AllowColorReduction = true, Quantization = new QuantizeOptions { Dithering = DitheringMode.FloydSteinberg } }, _output);

    public void Dispose()
    {
        Cleanup();
        _output.Dispose();
        GC.SuppressFinalize(this);
    }

    private static int Apply(Image source, QuantizeOptions options)
    {
        using var result = ImageWorkloads.FilterCopy(source, image => image.Quantize(options));
        return result.Width;
    }
}
