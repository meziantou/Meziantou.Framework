using BenchmarkDotNet.Attributes;
using Meziantou.Framework.Imaging.Benchmarks.Infrastructure;
using Meziantou.Framework.Imaging.Benchmarks.Workloads;
using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.Benchmarks;

/// <summary>
/// BMP decoding and encoding on the sources of the PNG, WebP and QOI workloads: the 2048x1536 photo-like RGB source and the
/// 1024x768 RGBA source with opaque, translucent and transparent areas. BMP is uncompressed, so these numbers measure the
/// row conversion and the I/O path, not an entropy coder; they are comparable with the PNG, WebP, QOI, TGA and Netpbm rows
/// measured on the same machine.
/// </summary>
[Config(typeof(WorkloadConfig))]
public class BmpBenchmarks : IDisposable
{
    private readonly MemoryStream _output = new();
    private Image<Rgb24> _photo = null!;
    private Image<Rgba32> _transparent = null!;

    [GlobalSetup]
    public void Setup()
    {
        _photo = Image.ImportPixelData<Rgb24>(BenchmarkInputs.LargeRgb24Source, BenchmarkInputs.LargeWidth, BenchmarkInputs.LargeHeight);
        _transparent = Image.ImportPixelData<Rgba32>(BenchmarkInputs.ServiceRgba32Source, BenchmarkInputs.ServiceWidth, BenchmarkInputs.ServiceHeight);

        // Untimed correctness gates: the format is lossless for these samples
        WorkloadChecks.RequireLossless(BenchmarkInputs.BmpRgb, BenchmarkInputs.LargeRgb24Source);
        WorkloadChecks.RequireLossless(BenchmarkInputs.BmpRgba, BenchmarkInputs.ServiceRgba32Source);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _photo?.Dispose();
        _transparent?.Dispose();
    }

    [Benchmark]
    [Workload("2048x1536 BMP 24-bit -> Rgb24")]
    public int DecodeBmpRgb()
    {
        using var image = Image.Load<Rgb24>(BenchmarkInputs.BmpRgb);
        return image.Width;
    }

    [Benchmark]
    [Workload("1024x768 BMP 32-bit with an alpha mask -> Rgba32")]
    public int DecodeBmpRgba()
    {
        using var image = Image.Load<Rgba32>(BenchmarkInputs.BmpRgba);
        return image.Width;
    }

    [Benchmark]
    [Workload("2048x1536 Rgb24 -> BMP 24-bit", ReturnsOutputBytes = true)]
    public long EncodeBmpRgb() => ImageWorkloads.Encode(_photo, new BmpEncoder(), _output);

    [Benchmark]
    [Workload("1024x768 Rgba32 -> BMP 32-bit", ReturnsOutputBytes = true)]
    public long EncodeBmpRgba() => ImageWorkloads.Encode(_transparent, new BmpEncoder(), _output);

    public void Dispose()
    {
        Cleanup();
        _output.Dispose();
        GC.SuppressFinalize(this);
    }
}
