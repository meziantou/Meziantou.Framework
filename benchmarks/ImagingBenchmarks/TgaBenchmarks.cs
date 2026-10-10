using BenchmarkDotNet.Attributes;
using Meziantou.Framework.Imaging.Benchmarks.Infrastructure;
using Meziantou.Framework.Imaging.Benchmarks.Workloads;
using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.Benchmarks;

/// <summary>
/// TGA decoding and encoding on the sources of the PNG, WebP and QOI workloads: the 2048x1536 photo-like RGB source and the
/// 1024x768 RGBA source with opaque, translucent and transparent areas. TGA uses run-length packets, so these numbers
/// measure the row conversion, the packet loop and the I/O path, not an entropy coder; they are comparable with the PNG,
/// WebP, QOI, BMP and Netpbm rows measured on the same machine.
/// </summary>
[Config(typeof(WorkloadConfig))]
public class TgaBenchmarks : IDisposable
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
        WorkloadChecks.RequireLossless(BenchmarkInputs.TgaRgb, BenchmarkInputs.LargeRgb24Source);
        WorkloadChecks.RequireLossless(BenchmarkInputs.TgaRgba, BenchmarkInputs.ServiceRgba32Source);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _photo?.Dispose();
        _transparent?.Dispose();
    }

    [Benchmark]
    [Workload("2048x1536 run-length TGA -> Rgb24")]
    public int DecodeTgaRgb()
    {
        using var image = Image.Load<Rgb24>(BenchmarkInputs.TgaRgb);
        return image.Width;
    }

    [Benchmark]
    [Workload("1024x768 run-length TGA with alpha -> Rgba32")]
    public int DecodeTgaRgba()
    {
        using var image = Image.Load<Rgba32>(BenchmarkInputs.TgaRgba);
        return image.Width;
    }

    [Benchmark]
    [Workload("2048x1536 Rgb24 -> run-length TGA", ReturnsOutputBytes = true)]
    public long EncodeTgaRgb() => ImageWorkloads.Encode(_photo, new TgaEncoder { Compression = TgaCompression.RunLength }, _output);

    [Benchmark]
    [Workload("1024x768 Rgba32 -> run-length TGA", ReturnsOutputBytes = true)]
    public long EncodeTgaRgba() => ImageWorkloads.Encode(_transparent, new TgaEncoder { Compression = TgaCompression.RunLength }, _output);

    public void Dispose()
    {
        Cleanup();
        _output.Dispose();
        GC.SuppressFinalize(this);
    }
}
