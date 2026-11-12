using BenchmarkDotNet.Attributes;
using Meziantou.Framework.Imaging.Benchmarks.Infrastructure;
using Meziantou.Framework.Imaging.Benchmarks.Workloads;
using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.Benchmarks;

/// <summary>
/// JPEG decoding (baseline, progressive, arithmetic, lossless, 12-bit, reduced and partial), encoding, and the
/// decode-resize-encode pipelines, in memory. The lossy 8-bit inputs encode the same 2048x1536 pixels at quality 85 with
/// 4:2:0 chroma subsampling. The four-component input (YCCK inks) has the same size: its decoding adds the ICC conversion of
/// every pixel from a CMYK profile to sRGB. The phone photo is stored sideways with a Display P3 profile. <see cref="Workers"/> is the
/// <see cref="ImageConfiguration.MaxDegreeOfParallelism"/> of the decodings and of the saved image: several workers
/// reconstruct the decoded MCU rows and transform the encoded ones.
/// </summary>
[Config(typeof(WorkloadConfig))]
public class JpegBenchmarks : IDisposable
{
    private static readonly JpegEncoder ProgressiveEncoder = new() { Quality = BenchmarkInputs.JpegQuality, ChromaSubsampling = JpegChromaSubsampling.Ratio420, Process = JpegEncodingProcess.Progressive };
    private static readonly JpegEncoder OptimizedEncoder = new() { Quality = BenchmarkInputs.JpegQuality, ChromaSubsampling = JpegChromaSubsampling.Ratio420, OptimizeHuffmanTables = true };
    private static readonly JpegEncoder Encoder444 = new() { Quality = BenchmarkInputs.JpegQuality, ChromaSubsampling = JpegChromaSubsampling.Ratio444 };
    private static readonly JpegEncoder ArithmeticEncoder = new() { Quality = BenchmarkInputs.JpegQuality, ChromaSubsampling = JpegChromaSubsampling.Ratio420, EntropyCoding = JpegEntropyCoding.Arithmetic };
    private static readonly JpegEncoder LosslessEncoder = new() { Process = JpegEncodingProcess.Lossless };
    private static readonly JpegEncoder Encoder12Bit = new() { Quality = BenchmarkInputs.JpegQuality, ChromaSubsampling = JpegChromaSubsampling.Ratio420, BitDepth = 12 };

    private readonly MemoryStream _output = new();
    private Image<Rgb24> _source = null!;
    private Image<Rgb48> _source48 = null!;
    private ImageConfiguration _configuration = null!;
    private ImageDecodeOptions _options = null!;
    private ImageDecodeOptions _inkOptions = null!;
    private ImageDecodeOptions _scaledOptions = null!;
    private ImageDecodeOptions _regionOptions = null!;

    /// <summary>Gets or sets <see cref="ImageConfiguration.MaxDegreeOfParallelism"/> (1 is the default configuration).</summary>
    [Params(1, 8)]
    public int Workers { get; set; } = 1;

    [GlobalSetup]
    public void Setup()
    {
        var pixels = BenchmarkInputs.LargeRgb24Source;
        _configuration = new ImageConfiguration { MaxDegreeOfParallelism = Workers };
        _options = new ImageDecodeOptions { Configuration = _configuration };
        _inkOptions = new ImageDecodeOptions { Configuration = _configuration, CmykConversion = BenchmarkInputs.InkJpegOptions.CmykConversion };
        _scaledOptions = new ImageDecodeOptions { Configuration = _configuration, ScaleDenominator = 8 };
        _regionOptions = new ImageDecodeOptions { Configuration = _configuration, Region = new Rectangle(0, 0, BenchmarkInputs.LargeWidth, 384) };
        _source = Image.ImportPixelData<Rgb24>(pixels, BenchmarkInputs.LargeWidth, BenchmarkInputs.LargeHeight, configuration: _configuration);
        using (var source64 = Image.ImportPixelData<Rgba64>(BenchmarkInputs.Rgb16Pixels, BenchmarkInputs.Large16Width, BenchmarkInputs.Large16Height, configuration: _configuration))
        {
            _source48 = source64.CloneAs<Rgb48>();
        }

        // Untimed correctness gate: the inputs must decode close to the independent source
        WorkloadChecks.RequirePsnr(BenchmarkInputs.BaselineJpeg, pixels, minimum: 35);
        WorkloadChecks.RequirePsnr(BenchmarkInputs.ProgressiveJpeg, pixels, minimum: 35);
        WorkloadChecks.RequirePsnr(BenchmarkInputs.ArithmeticJpeg, pixels, minimum: 35);
        WorkloadChecks.RequireLossless(BenchmarkInputs.LosslessJpeg, pixels);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _source?.Dispose();
        _source48?.Dispose();
    }

    [Benchmark]
    [Workload("2048x1536 baseline JPEG q85 4:2:0 -> Rgb24")]
    public int DecodeBaseline()
    {
        using var image = Image.Load<Rgb24>(BenchmarkInputs.BaselineJpeg, _options);
        return image.Width;
    }

    [Benchmark]
    [Workload("2048x1536 progressive JPEG q85 4:2:0 (10 scans) -> Rgb24")]
    public int DecodeProgressive()
    {
        using var image = Image.Load<Rgb24>(BenchmarkInputs.ProgressiveJpeg, _options);
        return image.Width;
    }

    [Benchmark]
    [Workload("2048x1536 baseline YCCK JPEG q85 4:2:0 -> sRGB Rgb24 (ICC conversion)")]
    public int DecodeInks()
    {
        using var image = Image.Load<Rgb24>(BenchmarkInputs.InkJpeg, _inkOptions);
        return image.Width;
    }

    [Benchmark]
    [Workload("2048x1536 sequential arithmetic-coded JPEG q85 4:2:0 -> Rgb24")]
    public int DecodeArithmetic()
    {
        using var image = Image.Load<Rgb24>(BenchmarkInputs.ArithmeticJpeg, _options);
        return image.Width;
    }

    [Benchmark]
    [Workload("2048x1536 lossless JPEG (predictive, 8-bit RGB) -> Rgb24")]
    public int DecodeLossless()
    {
        using var image = Image.Load<Rgb24>(BenchmarkInputs.LosslessJpeg, _options);
        return image.Width;
    }

    [Benchmark]
    [Workload("1024x768 12-bit JPEG q85 4:2:0 -> Rgb48")]
    public int Decode12Bit()
    {
        using var image = Image.Load<Rgb48>(BenchmarkInputs.Jpeg12Bit, _options);
        return image.Width;
    }

    [Benchmark]
    [Workload("2048x1536 baseline JPEG q85 4:2:0 -> 256x192 Rgb24 (1/8 scale from the DC coefficients)")]
    public int DecodeScaled()
    {
        using var image = Image.Load<Rgb24>(BenchmarkInputs.BaselineJpeg, _scaledOptions);
        return image.Width;
    }

    [Benchmark]
    [Workload("2048x1536 baseline JPEG q85 4:2:0 -> top 2048x384 area as Rgb24 (decoding stops after it)")]
    public int DecodeRegion()
    {
        using var image = Image.Load<Rgb24>(BenchmarkInputs.BaselineJpeg, _regionOptions);
        return image.Height;
    }

    [Benchmark]
    [Workload("2048x1536 Rgb24 -> baseline JPEG q85 4:2:0", ReturnsOutputBytes = true)]
    public long Encode() => ImageWorkloads.Encode(_source, BenchmarkInputs.JpegEncoder, _output);

    [Benchmark]
    [Workload("2048x1536 Rgb24 -> progressive JPEG q85 4:2:0", ReturnsOutputBytes = true)]
    public long EncodeProgressive() => ImageWorkloads.Encode(_source, ProgressiveEncoder, _output);

    [Benchmark]
    [Workload("2048x1536 Rgb24 -> baseline JPEG q85 4:2:0, optimized Huffman tables", ReturnsOutputBytes = true)]
    public long EncodeOptimized() => ImageWorkloads.Encode(_source, OptimizedEncoder, _output);

    [Benchmark]
    [Workload("2048x1536 Rgb24 -> baseline JPEG q85 4:4:4", ReturnsOutputBytes = true)]
    public long Encode444() => ImageWorkloads.Encode(_source, Encoder444, _output);

    [Benchmark]
    [Workload("2048x1536 Rgb24 -> sequential arithmetic-coded JPEG q85 4:2:0", ReturnsOutputBytes = true)]
    public long EncodeArithmetic() => ImageWorkloads.Encode(_source, ArithmeticEncoder, _output);

    [Benchmark]
    [Workload("2048x1536 Rgb24 -> lossless JPEG (predictor 1)", ReturnsOutputBytes = true)]
    public long EncodeLossless() => ImageWorkloads.Encode(_source, LosslessEncoder, _output);

    [Benchmark]
    [Workload("1024x768 Rgb48 -> 12-bit JPEG q85 4:2:0", ReturnsOutputBytes = true)]
    public long Encode12Bit() => ImageWorkloads.Encode(_source48, Encoder12Bit, _output);

    [Benchmark]
    [Workload("baseline 2048x1536 -> resize 1024x768 (Catmull-Rom) -> JPEG q85 4:2:0", ReturnsOutputBytes = true)]
    public long TranscodeBaseline() => ImageWorkloads.JpegDecodeResizeEncode(BenchmarkInputs.BaselineJpeg, new Size(1024, 768), _output, _configuration);

    [Benchmark]
    [Workload("progressive 2048x1536 -> resize 1024x768 (Catmull-Rom) -> JPEG q85 4:2:0", ReturnsOutputBytes = true)]
    public long TranscodeProgressive() => ImageWorkloads.JpegDecodeResizeEncode(BenchmarkInputs.ProgressiveJpeg, new Size(1024, 768), _output, _configuration);

    [Benchmark]
    [Workload("portrait phone photo 2048x1536 (EXIF 6, Display P3) -> upright -> sRGB -> fit 1024x1024 (Catmull-Rom) -> JPEG q85 4:2:0", ReturnsOutputBytes = true)]
    public long TranscodePhonePhoto() => ImageWorkloads.JpegPhotoToSrgbThumbnail(BenchmarkInputs.PhonePhotoJpeg, new Size(1024, 1024), _output, _configuration);

    public void Dispose()
    {
        Cleanup();
        _output.Dispose();
        GC.SuppressFinalize(this);
    }
}
