using BenchmarkDotNet.Attributes;
using Meziantou.Framework.Imaging.Benchmarks.Infrastructure;
using Meziantou.Framework.Imaging.Benchmarks.Workloads;
using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.Benchmarks;

/// <summary>
/// JPEG decoding (baseline and progressive), encoding, and the decode-resize-encode pipeline, in memory. Both inputs encode
/// the same 2048x1536 pixels at quality 85 with 4:2:0 chroma subsampling. The four-component input (YCCK inks) has the
/// same size: its decoding adds the ICC conversion of every pixel from a CMYK profile to sRGB. <see cref="Workers"/> is the
/// <see cref="ImageConfiguration.MaxDegreeOfParallelism"/> of the decodings and of the saved image: several workers
/// reconstruct the decoded MCU rows and transform the encoded ones.
/// </summary>
[Config(typeof(WorkloadConfig))]
public class JpegBenchmarks : IDisposable
{
    private static readonly JpegEncoder ProgressiveEncoder = new() { Quality = BenchmarkInputs.JpegQuality, ChromaSubsampling = JpegChromaSubsampling.Ratio420, Process = JpegEncodingProcess.Progressive };

    private readonly MemoryStream _output = new();
    private Image<Rgb24> _source = null!;
    private ImageConfiguration _configuration = null!;
    private ImageDecodeOptions _options = null!;
    private ImageDecodeOptions _inkOptions = null!;

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
        _source = Image.ImportPixelData<Rgb24>(pixels, BenchmarkInputs.LargeWidth, BenchmarkInputs.LargeHeight, configuration: _configuration);

        // Untimed correctness gate: both inputs must decode close to the independent source
        WorkloadChecks.RequirePsnr(BenchmarkInputs.BaselineJpeg, pixels, minimum: 35);
        WorkloadChecks.RequirePsnr(BenchmarkInputs.ProgressiveJpeg, pixels, minimum: 35);
    }

    [GlobalCleanup]
    public void Cleanup() => _source?.Dispose();

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
    [Workload("2048x1536 Rgb24 -> baseline JPEG q85 4:2:0", ReturnsOutputBytes = true)]
    public long Encode() => ImageWorkloads.Encode(_source, BenchmarkInputs.JpegEncoder, _output);

    [Benchmark]
    [Workload("2048x1536 Rgb24 -> progressive JPEG q85 4:2:0", ReturnsOutputBytes = true)]
    public long EncodeProgressive() => ImageWorkloads.Encode(_source, ProgressiveEncoder, _output);

    [Benchmark]
    [Workload("baseline 2048x1536 -> resize 1024x768 (Catmull-Rom) -> JPEG q85 4:2:0", ReturnsOutputBytes = true)]
    public long TranscodeBaseline() => ImageWorkloads.JpegDecodeResizeEncode(BenchmarkInputs.BaselineJpeg, new Size(1024, 768), _output, _configuration);

    [Benchmark]
    [Workload("progressive 2048x1536 -> resize 1024x768 (Catmull-Rom) -> JPEG q85 4:2:0", ReturnsOutputBytes = true)]
    public long TranscodeProgressive() => ImageWorkloads.JpegDecodeResizeEncode(BenchmarkInputs.ProgressiveJpeg, new Size(1024, 768), _output, _configuration);

    public void Dispose()
    {
        Cleanup();
        _output.Dispose();
        GC.SuppressFinalize(this);
    }
}
