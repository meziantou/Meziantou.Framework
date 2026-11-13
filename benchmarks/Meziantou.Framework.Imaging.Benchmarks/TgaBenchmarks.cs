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
    private byte[] _tga15 = null!;

    [GlobalSetup]
    public void Setup()
    {
        _photo = Image.ImportPixelData<Rgb24>(BenchmarkInputs.LargeRgb24Source, BenchmarkInputs.LargeWidth, BenchmarkInputs.LargeHeight);
        _transparent = Image.ImportPixelData<Rgba32>(BenchmarkInputs.ServiceRgba32Source, BenchmarkInputs.ServiceWidth, BenchmarkInputs.ServiceHeight);

        // Untimed correctness gates: the format is lossless for these samples
        WorkloadChecks.RequireLossless(BenchmarkInputs.TgaRgb, BenchmarkInputs.LargeRgb24Source);
        WorkloadChecks.RequireLossless(BenchmarkInputs.TgaRgba, BenchmarkInputs.ServiceRgba32Source);
        WorkloadChecks.RequireLossless(BenchmarkInputs.TgaUncompressed, BenchmarkInputs.LargeRgb24Source);
        _tga15 = CreateTga15(BenchmarkInputs.LargeRgb24Source, BenchmarkInputs.LargeWidth, BenchmarkInputs.LargeHeight);
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
    [Workload("2048x1536 uncompressed TGA -> Rgb24")]
    public int DecodeTgaUncompressed()
    {
        using var image = Image.Load<Rgb24>(BenchmarkInputs.TgaUncompressed);
        return image.Width;
    }

    [Benchmark]
    [Workload("2048x1536 uncompressed 15-bit TGA (5-5-5) -> Rgb24")]
    public int DecodeTga15()
    {
        using var image = Image.Load<Rgb24>(_tga15);
        return image.Width;
    }

    [Benchmark]
    [Workload("2048x1536 Rgb24 -> run-length TGA", ReturnsOutputBytes = true)]
    public long EncodeTgaRgb() => ImageWorkloads.Encode(_photo, new TgaEncoder { Compression = TgaCompression.RunLength }, _output);

    [Benchmark]
    [Workload("1024x768 Rgba32 -> run-length TGA", ReturnsOutputBytes = true)]
    public long EncodeTgaRgba() => ImageWorkloads.Encode(_transparent, new TgaEncoder { Compression = TgaCompression.RunLength }, _output);

    /// <summary>Writes an uncompressed top-down TGA of 15-bit pixels (the encoder writes 24- and 32-bit pixels only).</summary>
    private static byte[] CreateTga15(Rgb24[] pixels, int width, int height)
    {
        var data = new byte[18 + (pixels.Length * 2)];
        data[2] = 2;
        data[12] = (byte)width;
        data[13] = (byte)(width >> 8);
        data[14] = (byte)height;
        data[15] = (byte)(height >> 8);
        data[16] = 16;
        data[17] = 0x20;
        for (var i = 0; i < pixels.Length; i++)
        {
            var value = ((pixels[i].R >> 3) << 10) | ((pixels[i].G >> 3) << 5) | (pixels[i].B >> 3);
            data[18 + (2 * i)] = (byte)value;
            data[19 + (2 * i)] = (byte)(value >> 8);
        }

        return data;
    }

    public void Dispose()
    {
        Cleanup();
        _output.Dispose();
        GC.SuppressFinalize(this);
    }
}
