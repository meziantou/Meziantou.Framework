using BenchmarkDotNet.Attributes;
using Meziantou.Framework.Imaging.Benchmarks.Infrastructure;
using Meziantou.Framework.Imaging.Benchmarks.Workloads;

namespace Meziantou.Framework.Imaging.Benchmarks;

/// <summary>
/// JPEG decoding (baseline and progressive), encoding, and the decode-resize-encode pipeline, in memory. Both inputs encode
/// the same 2048x1536 pixels at quality 85 with 4:2:0 chroma subsampling.
/// </summary>
[Config(typeof(WorkloadConfig))]
public class JpegBenchmarks : IDisposable
{
    private readonly MemoryStream _output = new();
    private Image<Rgb24> _source = null!;

    [GlobalSetup]
    public void Setup()
    {
        var pixels = BenchmarkInputs.LargeRgb24Source;
        _source = Image.ImportPixelData<Rgb24>(pixels, BenchmarkInputs.LargeWidth, BenchmarkInputs.LargeHeight);

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
        using var image = Image.Load<Rgb24>(BenchmarkInputs.BaselineJpeg);
        return image.Width;
    }

    [Benchmark]
    [Workload("2048x1536 progressive JPEG q85 4:2:0 (10 scans) -> Rgb24")]
    public int DecodeProgressive()
    {
        using var image = Image.Load<Rgb24>(BenchmarkInputs.ProgressiveJpeg);
        return image.Width;
    }

    [Benchmark]
    [Workload("2048x1536 Rgb24 -> baseline JPEG q85 4:2:0", ReturnsOutputBytes = true)]
    public long Encode() => ImageWorkloads.Encode(_source, BenchmarkInputs.JpegEncoder, _output);

    [Benchmark]
    [Workload("baseline 2048x1536 -> resize 1024x768 (Catmull-Rom) -> JPEG q85 4:2:0", ReturnsOutputBytes = true)]
    public long TranscodeBaseline() => ImageWorkloads.JpegDecodeResizeEncode(BenchmarkInputs.BaselineJpeg, new Size(1024, 768), _output);

    [Benchmark]
    [Workload("progressive 2048x1536 -> resize 1024x768 (Catmull-Rom) -> JPEG q85 4:2:0", ReturnsOutputBytes = true)]
    public long TranscodeProgressive() => ImageWorkloads.JpegDecodeResizeEncode(BenchmarkInputs.ProgressiveJpeg, new Size(1024, 768), _output);

    public void Dispose()
    {
        Cleanup();
        _output.Dispose();
        GC.SuppressFinalize(this);
    }
}
