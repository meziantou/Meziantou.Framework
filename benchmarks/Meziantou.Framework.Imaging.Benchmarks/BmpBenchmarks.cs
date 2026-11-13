using BenchmarkDotNet.Attributes;
using Meziantou.Framework.Imaging.Benchmarks.Infrastructure;
using Meziantou.Framework.Imaging.Benchmarks.Workloads;
using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.Benchmarks;

/// <summary>
/// BMP decoding and encoding on the sources of the PNG, WebP and QOI workloads: the 2048x1536 photo-like RGB source and the
/// 1024x768 RGBA source with opaque, translucent and transparent areas. BMP is mostly uncompressed, so these numbers measure
/// the row conversion and the I/O path, not an entropy coder (except the 8-bit run-length rows); they are comparable with the PNG, WebP, QOI, TGA and Netpbm rows
/// measured on the same machine.
/// </summary>
[Config(typeof(WorkloadConfig))]
public class BmpBenchmarks : IDisposable
{
    private readonly MemoryStream _output = new();
    private Image<Rgb24> _photo = null!;
    private Image<Rgba32> _transparent = null!;
    private Image<Rgb24> _indexed = null!;
    private Image<Rgb24> _sixteenColors = null!;
    private Image<Gray8> _bilevel = null!;
    private byte[] _bmp565 = null!;
    private byte[] _bmpIndexed4 = null!;

    [GlobalSetup]
    public void Setup()
    {
        _photo = Image.ImportPixelData<Rgb24>(BenchmarkInputs.LargeRgb24Source, BenchmarkInputs.LargeWidth, BenchmarkInputs.LargeHeight);
        _transparent = Image.ImportPixelData<Rgba32>(BenchmarkInputs.ServiceRgba32Source, BenchmarkInputs.ServiceWidth, BenchmarkInputs.ServiceHeight);
        _indexed = Image.ImportPixelData<Rgb24>(BenchmarkInputs.IndexedRgb24Source, BenchmarkInputs.LargeWidth, BenchmarkInputs.LargeHeight);

        // Untimed correctness gates: the format is lossless for these samples
        WorkloadChecks.RequireLossless(BenchmarkInputs.BmpRgb, BenchmarkInputs.LargeRgb24Source);
        WorkloadChecks.RequireLossless(BenchmarkInputs.BmpRgba, BenchmarkInputs.ServiceRgba32Source);
        WorkloadChecks.RequireLossless(BenchmarkInputs.BmpRunLength8, BenchmarkInputs.IndexedRgb24Source);

        // 16 colors: the red channel reduced to 4 levels, green and blue to 2
        var sixteenColors = BenchmarkInputs.LargeRgb24Source.Select(p => new Rgb24((byte)((p.R >> 6) * 85), (byte)((p.G >> 7) * 255), (byte)((p.B >> 7) * 255))).ToArray();
        _sixteenColors = Image.ImportPixelData<Rgb24>(sixteenColors, BenchmarkInputs.LargeWidth, BenchmarkInputs.LargeHeight);
        _bilevel = Image.ImportPixelData<Gray8>(BenchmarkInputs.LargeBilevelSource, BenchmarkInputs.LargeWidth, BenchmarkInputs.LargeHeight);
        _bmp565 = Encode(_photo, new BmpEncoder { PixelLayout = BmpPixelLayout.Bgr565, AllowBitDepthReduction = true });
        _bmpIndexed4 = Encode(_sixteenColors, new BmpEncoder { PixelLayout = BmpPixelLayout.Indexed4 });
        WorkloadChecks.RequireLossless(_bmpIndexed4, sixteenColors);
        WorkloadChecks.RequireLossless(Encode(_sixteenColors, new BmpEncoder { PixelLayout = BmpPixelLayout.Indexed4, Compression = BmpCompression.RunLength }), sixteenColors);
        WorkloadChecks.RequireLossless(Encode(_bilevel, new BmpEncoder { PixelLayout = BmpPixelLayout.Indexed1 }), BenchmarkInputs.LargeBilevelSource.Select(p => new Rgb24(p.Value, p.Value, p.Value)).ToArray());
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _photo?.Dispose();
        _transparent?.Dispose();
        _indexed?.Dispose();
        _sixteenColors?.Dispose();
        _bilevel?.Dispose();
    }

    [Benchmark]
    [Workload("2048x1536 BMP 24-bit -> Rgb24")]
    public int DecodeBmpRgb()
    {
        using var image = Image.Load<Rgb24>(BenchmarkInputs.BmpRgb);
        return image.Width;
    }

    [Benchmark]
    [Workload("2048x1536 BMP 8-bit indexed (256 colors) -> Rgb24")]
    public int DecodeBmpIndexed8()
    {
        using var image = Image.Load<Rgb24>(BenchmarkInputs.BmpIndexed8);
        return image.Width;
    }

    [Benchmark]
    [Workload("2048x1536 BMP 8-bit indexed run-length (256 colors) -> Rgb24")]
    public int DecodeBmpRunLength8()
    {
        using var image = Image.Load<Rgb24>(BenchmarkInputs.BmpRunLength8);
        return image.Width;
    }

    [Benchmark]
    [Workload("2048x1536 BMP 16-bit (5-6-5 bit fields) -> Rgb24")]
    public int DecodeBmpRgb565()
    {
        using var image = Image.Load<Rgb24>(_bmp565);
        return image.Width;
    }

    [Benchmark]
    [Workload("2048x1536 BMP 4-bit indexed (16 colors) -> Rgb24")]
    public int DecodeBmpIndexed4()
    {
        using var image = Image.Load<Rgb24>(_bmpIndexed4);
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
    [Workload("2048x1536 Rgb24 of 256 colors -> BMP 8-bit indexed", ReturnsOutputBytes = true)]
    public long EncodeBmpIndexed8() => ImageWorkloads.Encode(_indexed, new BmpEncoder { PixelLayout = BmpPixelLayout.Indexed8 }, _output);

    [Benchmark]
    [Workload("2048x1536 Rgb24 of 256 colors -> BMP 8-bit indexed run-length", ReturnsOutputBytes = true)]
    public long EncodeBmpRunLength8() => ImageWorkloads.Encode(_indexed, new BmpEncoder { PixelLayout = BmpPixelLayout.Indexed8, Compression = BmpCompression.RunLength }, _output);

    [Benchmark]
    [Workload("2048x1536 Rgb24 of 16 colors -> BMP 4-bit indexed", ReturnsOutputBytes = true)]
    public long EncodeBmpIndexed4() => ImageWorkloads.Encode(_sixteenColors, new BmpEncoder { PixelLayout = BmpPixelLayout.Indexed4 }, _output);

    [Benchmark]
    [Workload("2048x1536 Rgb24 of 16 colors -> BMP 4-bit indexed run-length", ReturnsOutputBytes = true)]
    public long EncodeBmpRunLength4() => ImageWorkloads.Encode(_sixteenColors, new BmpEncoder { PixelLayout = BmpPixelLayout.Indexed4, Compression = BmpCompression.RunLength }, _output);

    [Benchmark]
    [Workload("2048x1536 Gray8 bilevel (text page) -> BMP 1-bit indexed", ReturnsOutputBytes = true)]
    public long EncodeBmpIndexed1() => ImageWorkloads.Encode(_bilevel, new BmpEncoder { PixelLayout = BmpPixelLayout.Indexed1 }, _output);

    [Benchmark]
    [Workload("1024x768 Rgba32 -> BMP 32-bit", ReturnsOutputBytes = true)]
    public long EncodeBmpRgba() => ImageWorkloads.Encode(_transparent, new BmpEncoder(), _output);

    private byte[] Encode(Image image, BmpEncoder encoder)
    {
        ImageWorkloads.Encode(image, encoder, _output);
        return _output.ToArray();
    }

    public void Dispose()
    {
        Cleanup();
        _output.Dispose();
        GC.SuppressFinalize(this);
    }
}
