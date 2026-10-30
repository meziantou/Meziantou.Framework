using BenchmarkDotNet.Attributes;
using Meziantou.Framework.Imaging.Benchmarks.Infrastructure;
using Meziantou.Framework.Imaging.Benchmarks.Workloads;
using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.Benchmarks;

/// <summary>
/// TIFF decoding and encoding on the 2048x1536 photo-like RGB source of the PNG workloads: LZW and Deflate strips written
/// by <see cref="TiffEncoder"/>, uncompressed pages with the horizontal differencing predictor (8-bit RGB, and the
/// 1024x768 RGBA 16-bit source in big-endian order) and without it (8-bit RGB, 16-bit RGB), and a 2048x1536 bilevel text
/// page, uncompressed and compressed with CCITT Group 4. <see cref="Workers"/> decodes and encodes the LZW, Deflate and
/// CCITT strips with several workers; the other pages are decoded by one whatever its value.
/// </summary>
[Config(typeof(WorkloadConfig))]
public class TiffBenchmarks : IDisposable
{
    private readonly MemoryStream _output = new();
    private Image<Rgb24> _photo = null!;
    private ImageDecodeOptions _options = ImageDecodeOptions.Default;

    /// <summary>Gets or sets <see cref="ImageConfiguration.MaxDegreeOfParallelism"/> (1 is the default configuration).</summary>
    [Params(1, 8)]
    public int Workers { get; set; } = 1;

    [GlobalSetup]
    public void Setup()
    {
        var configuration = new ImageConfiguration { MaxDegreeOfParallelism = Workers };
        _options = new ImageDecodeOptions { Configuration = configuration };
        _photo = Image.ImportPixelData<Rgb24>(BenchmarkInputs.LargeRgb24Source, BenchmarkInputs.LargeWidth, BenchmarkInputs.LargeHeight, configuration: configuration);

        // Untimed correctness gates: the format is lossless for these samples
        WorkloadChecks.RequireLossless(BenchmarkInputs.TiffLzw, BenchmarkInputs.LargeRgb24Source);
        WorkloadChecks.RequireLossless(BenchmarkInputs.TiffDeflate, BenchmarkInputs.LargeRgb24Source);
        WorkloadChecks.RequireLossless(BenchmarkInputs.TiffDeflateRowStrips, BenchmarkInputs.LargeRgb24Source);
        WorkloadChecks.RequireLossless(BenchmarkInputs.TiffPredictor8, BenchmarkInputs.LargeRgb24Source);
        WorkloadChecks.RequireLossless(BenchmarkInputs.TiffPredictor16, BenchmarkInputs.LargeRgba64Source);
        WorkloadChecks.RequireLossless(BenchmarkInputs.TiffUncompressed, BenchmarkInputs.LargeRgb24Source);
        WorkloadChecks.RequireLossless(BenchmarkInputs.TiffBilevel, BenchmarkInputs.LargeBilevelSource);
        WorkloadChecks.RequireLossless(BenchmarkInputs.TiffCcittGroup4, BenchmarkInputs.LargeBilevelSource);
        WorkloadChecks.RequireLossless(BenchmarkInputs.TiffRgb16, BenchmarkInputs.Rgb16Pixels);
    }

    [GlobalCleanup]
    public void Cleanup() => _photo?.Dispose();

    [Benchmark]
    [Workload("2048x1536 TIFF RGB LZW -> Rgb24")]
    public int DecodeTiffLzw()
    {
        using var image = Image.Load<Rgb24>(BenchmarkInputs.TiffLzw, _options);
        return image.Width;
    }

    [Benchmark]
    [Workload("2048x1536 TIFF RGB Deflate -> Rgb24")]
    public int DecodeTiffDeflate()
    {
        using var image = Image.Load<Rgb24>(BenchmarkInputs.TiffDeflate, _options);
        return image.Width;
    }

    [Benchmark]
    [Workload("2048x1536 TIFF RGB Deflate, one row per strip -> Rgb24")]
    public int DecodeTiffDeflateRowStrips()
    {
        using var image = Image.Load<Rgb24>(BenchmarkInputs.TiffDeflateRowStrips, _options);
        return image.Width;
    }

    [Benchmark]
    [Workload("2048x1536 TIFF 8-bit indexed (256 colors), uncompressed -> Rgb24")]
    public int DecodeTiffIndexed8()
    {
        using var image = Image.Load<Rgb24>(BenchmarkInputs.TiffIndexed8, _options);
        return image.Width;
    }

    [Benchmark]
    [Workload("2048x1536 TIFF RGB with the horizontal predictor -> Rgb24")]
    public int DecodeTiffPredictor8()
    {
        using var image = Image.Load<Rgb24>(BenchmarkInputs.TiffPredictor8, _options);
        return image.Width;
    }

    [Benchmark]
    [Workload("1024x768 TIFF RGBA 16-bit big-endian with the horizontal predictor -> Rgba64")]
    public int DecodeTiffPredictor16()
    {
        using var image = Image.Load<Rgba64>(BenchmarkInputs.TiffPredictor16, _options);
        return image.Width;
    }

    [Benchmark]
    [Workload("2048x1536 TIFF RGB, uncompressed -> Rgb24")]
    public int DecodeTiffUncompressed()
    {
        using var image = Image.Load<Rgb24>(BenchmarkInputs.TiffUncompressed, _options);
        return image.Width;
    }

    [Benchmark]
    [Workload("1024x768 TIFF RGB 16-bit little-endian, uncompressed -> Rgba64")]
    public int DecodeTiffRgb16()
    {
        using var image = Image.Load<Rgba64>(BenchmarkInputs.TiffRgb16, _options);
        return image.Width;
    }

    [Benchmark]
    [Workload("2048x1536 TIFF bilevel (text page), uncompressed -> Gray8")]
    public int DecodeTiffBilevel()
    {
        using var image = Image.Load<Gray8>(BenchmarkInputs.TiffBilevel, _options);
        return image.Width;
    }

    [Benchmark]
    [Workload("2048x1536 TIFF bilevel (text page), CCITT Group 4 -> Gray8")]
    public int DecodeTiffCcittGroup4()
    {
        using var image = Image.Load<Gray8>(BenchmarkInputs.TiffCcittGroup4, _options);
        return image.Width;
    }

    [Benchmark]
    [Workload("2048x1536 Rgb24 -> TIFF LZW", ReturnsOutputBytes = true)]
    public long EncodeTiffLzw() => ImageWorkloads.Encode(_photo, new TiffEncoder { Compression = TiffCompression.Lzw }, _output);

    [Benchmark]
    [Workload("2048x1536 Rgb24 -> TIFF Deflate", ReturnsOutputBytes = true)]
    public long EncodeTiffDeflate() => ImageWorkloads.Encode(_photo, new TiffEncoder { Compression = TiffCompression.Deflate }, _output);

    public void Dispose()
    {
        Cleanup();
        _output.Dispose();
        GC.SuppressFinalize(this);
    }
}
