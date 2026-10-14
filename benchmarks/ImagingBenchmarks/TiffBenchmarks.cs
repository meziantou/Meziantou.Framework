using BenchmarkDotNet.Attributes;
using Meziantou.Framework.Imaging.Benchmarks.Infrastructure;
using Meziantou.Framework.Imaging.Benchmarks.Workloads;
using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.Benchmarks;

/// <summary>
/// TIFF decoding and encoding on the 2048x1536 photo-like RGB source of the PNG workloads: LZW and Deflate strips written
/// by <see cref="TiffEncoder"/>, and uncompressed pages with the horizontal differencing predictor (8-bit RGB, and the
/// 1024x768 RGBA 16-bit source in big-endian order).
/// </summary>
[Config(typeof(WorkloadConfig))]
public class TiffBenchmarks : IDisposable
{
    private readonly MemoryStream _output = new();
    private Image<Rgb24> _photo = null!;

    [GlobalSetup]
    public void Setup()
    {
        _photo = Image.ImportPixelData<Rgb24>(BenchmarkInputs.LargeRgb24Source, BenchmarkInputs.LargeWidth, BenchmarkInputs.LargeHeight);

        // Untimed correctness gates: the format is lossless for these samples
        WorkloadChecks.RequireLossless(BenchmarkInputs.TiffLzw, BenchmarkInputs.LargeRgb24Source);
        WorkloadChecks.RequireLossless(BenchmarkInputs.TiffDeflate, BenchmarkInputs.LargeRgb24Source);
        WorkloadChecks.RequireLossless(BenchmarkInputs.TiffDeflateRowStrips, BenchmarkInputs.LargeRgb24Source);
        WorkloadChecks.RequireLossless(BenchmarkInputs.TiffPredictor8, BenchmarkInputs.LargeRgb24Source);
        WorkloadChecks.RequireLossless(BenchmarkInputs.TiffPredictor16, BenchmarkInputs.LargeRgba64Source);
    }

    [GlobalCleanup]
    public void Cleanup() => _photo?.Dispose();

    [Benchmark]
    [Workload("2048x1536 TIFF RGB LZW -> Rgb24")]
    public int DecodeTiffLzw()
    {
        using var image = Image.Load<Rgb24>(BenchmarkInputs.TiffLzw);
        return image.Width;
    }

    [Benchmark]
    [Workload("2048x1536 TIFF RGB Deflate -> Rgb24")]
    public int DecodeTiffDeflate()
    {
        using var image = Image.Load<Rgb24>(BenchmarkInputs.TiffDeflate);
        return image.Width;
    }

    [Benchmark]
    [Workload("2048x1536 TIFF RGB Deflate, one row per strip -> Rgb24")]
    public int DecodeTiffDeflateRowStrips()
    {
        using var image = Image.Load<Rgb24>(BenchmarkInputs.TiffDeflateRowStrips);
        return image.Width;
    }

    [Benchmark]
    [Workload("2048x1536 TIFF RGB with the horizontal predictor -> Rgb24")]
    public int DecodeTiffPredictor8()
    {
        using var image = Image.Load<Rgb24>(BenchmarkInputs.TiffPredictor8);
        return image.Width;
    }

    [Benchmark]
    [Workload("1024x768 TIFF RGBA 16-bit big-endian with the horizontal predictor -> Rgba64")]
    public int DecodeTiffPredictor16()
    {
        using var image = Image.Load<Rgba64>(BenchmarkInputs.TiffPredictor16);
        return image.Width;
    }

    [Benchmark]
    [Workload("2048x1536 Rgb24 -> TIFF LZW", ReturnsOutputBytes = true)]
    public long EncodeTiffLzw() => ImageWorkloads.Encode(_photo, new TiffEncoder { Compression = TiffCompression.Lzw }, _output);

    public void Dispose()
    {
        Cleanup();
        _output.Dispose();
        GC.SuppressFinalize(this);
    }
}
