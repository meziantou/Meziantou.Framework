using BenchmarkDotNet.Attributes;
using Meziantou.Framework.Imaging.Benchmarks.Infrastructure;
using Meziantou.Framework.Imaging.Benchmarks.Workloads;
using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.Benchmarks;

/// <summary>PNG decoding and encoding, in memory: a small icon-sized image, a large 8-bit image and a large 16-bit image.</summary>
[Config(typeof(WorkloadConfig))]
public class PngBenchmarks : IDisposable
{
    private readonly MemoryStream _output = new();
    private Image<Rgba32> _small = null!;
    private Image<Rgb24> _large8 = null!;
    private Image<Rgba64> _large16 = null!;

    [GlobalSetup]
    public void Setup()
    {
        _small = Image.ImportPixelData<Rgba32>(BenchmarkInputs.SmallPngSource, 64, 64);
        _large8 = Image.ImportPixelData<Rgb24>(BenchmarkInputs.LargeRgb24Source, BenchmarkInputs.LargeWidth, BenchmarkInputs.LargeHeight);
        _large16 = Image.ImportPixelData<Rgba64>(BenchmarkInputs.LargeRgba64Source, BenchmarkInputs.Large16Width, BenchmarkInputs.Large16Height);

        // Untimed correctness gate: PNG is lossless, so decoding must give back the independent synthetic source exactly
        WorkloadChecks.RequireLossless(BenchmarkInputs.SmallPng, BenchmarkInputs.SmallPngSource);
        WorkloadChecks.RequireLossless(BenchmarkInputs.LargePng8, BenchmarkInputs.LargeRgb24Source);
        WorkloadChecks.RequireLossless(BenchmarkInputs.LargePng16, BenchmarkInputs.LargeRgba64Source);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _small?.Dispose();
        _large8?.Dispose();
        _large16?.Dispose();
    }

    [Benchmark]
    [Workload("64x64 RGBA8 PNG -> Rgba32")]
    public int LoadSmall()
    {
        using var image = Image.Load<Rgba32>(BenchmarkInputs.SmallPng);
        return image.Width;
    }

    [Benchmark]
    [Workload("64x64 Rgba32 -> PNG (adaptive filters, optimal zlib)", ReturnsOutputBytes = true)]
    public long SaveSmall() => ImageWorkloads.Encode(_small, new PngEncoder(), _output);

    [Benchmark]
    [Workload("2048x1536 RGB8 PNG -> Rgb24")]
    public int LoadLarge8()
    {
        using var image = Image.Load<Rgb24>(BenchmarkInputs.LargePng8);
        return image.Width;
    }

    [Benchmark]
    [Workload("2048x1536 Rgb24 -> PNG (adaptive filters, optimal zlib)", ReturnsOutputBytes = true)]
    public long SaveLarge8() => ImageWorkloads.Encode(_large8, new PngEncoder(), _output);

    [Benchmark]
    [Workload("2048x1536 Rgb24 -> PNG (adaptive filters, fastest zlib)", ReturnsOutputBytes = true)]
    public long SaveLarge8Fastest() => ImageWorkloads.Encode(_large8, new PngEncoder { CompressionLevel = System.IO.Compression.CompressionLevel.Fastest }, _output);

    [Benchmark]
    [Workload("1024x768 RGBA16 PNG -> Rgba64")]
    public int LoadLarge16()
    {
        using var image = Image.Load<Rgba64>(BenchmarkInputs.LargePng16);
        return image.Width;
    }

    [Benchmark]
    [Workload("1024x768 Rgba64 -> PNG (adaptive filters, optimal zlib)", ReturnsOutputBytes = true)]
    public long SaveLarge16() => ImageWorkloads.Encode(_large16, new PngEncoder(), _output);

    public void Dispose()
    {
        Cleanup();
        _output.Dispose();
        GC.SuppressFinalize(this);
    }
}
