using BenchmarkDotNet.Attributes;
using Meziantou.Framework.Imaging.Benchmarks.Infrastructure;
using Meziantou.Framework.Imaging.Benchmarks.Workloads;
using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.Benchmarks;

/// <summary>
/// Netpbm decoding and encoding on the sources of the PNG, WebP and QOI workloads: the 2048x1536 photo-like RGB source and
/// the 1024x768 RGBA source with opaque, translucent and transparent areas. Netpbm is uncompressed, so these numbers measure
/// the row conversion and the I/O path, not an entropy coder; they are comparable with the PNG, WebP, QOI, BMP and TGA rows
/// measured on the same machine.
/// </summary>
[Config(typeof(WorkloadConfig))]
public class PnmBenchmarks : IDisposable
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
        WorkloadChecks.RequireLossless(BenchmarkInputs.PnmRgb, BenchmarkInputs.LargeRgb24Source);
        WorkloadChecks.RequireLossless(BenchmarkInputs.PnmRgba, BenchmarkInputs.ServiceRgba32Source);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _photo?.Dispose();
        _transparent?.Dispose();
    }

    [Benchmark]
    [Workload("2048x1536 binary PPM -> Rgb24")]
    public int DecodePnmRgb()
    {
        using var image = Image.Load<Rgb24>(BenchmarkInputs.PnmRgb);
        return image.Width;
    }

    [Benchmark]
    [Workload("1024x768 binary PAM RGB_ALPHA -> Rgba32")]
    public int DecodePnmRgba()
    {
        using var image = Image.Load<Rgba32>(BenchmarkInputs.PnmRgba);
        return image.Width;
    }

    [Benchmark]
    [Workload("2048x1536 Rgb24 -> binary PPM", ReturnsOutputBytes = true)]
    public long EncodePnmRgb() => ImageWorkloads.Encode(_photo, new PnmEncoder(), _output);

    [Benchmark]
    [Workload("1024x768 Rgba32 -> binary PAM RGB_ALPHA", ReturnsOutputBytes = true)]
    public long EncodePnmRgba() => ImageWorkloads.Encode(_transparent, new PnmEncoder(), _output);

    public void Dispose()
    {
        Cleanup();
        _output.Dispose();
        GC.SuppressFinalize(this);
    }
}
