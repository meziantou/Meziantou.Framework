using BenchmarkDotNet.Attributes;
using Meziantou.Framework.Imaging.Benchmarks.Infrastructure;
using Meziantou.Framework.Imaging.Benchmarks.Workloads;

namespace Meziantou.Framework.Imaging.Benchmarks;

/// <summary>
/// SSIM and hash distances of a 1920x1080 source with transparency against a copy with a 100x100 square of a different
/// color, as a visual regression test compares a screenshot with its reference. The 16-bit cases measure the conversion to
/// 8-bit rows.
/// </summary>
[Config(typeof(WorkloadConfig))]
public class ImageSimilarityBenchmarks
{
    private Image<Rgba32> _expected = null!;
    private Image<Rgba32> _actual = null!;
    private Image<Rgba64> _expected64 = null!;
    private Image<Rgba64> _actual64 = null!;

    [GlobalSetup]
    public void Setup()
    {
        _expected = Image.ImportPixelData<Rgba32>(BenchmarkInputs.ResizeRgba32Source, BenchmarkInputs.ResizeWidth, BenchmarkInputs.ResizeHeight);
        _actual = _expected.Clone();
        _actual.Frames[0].ProcessPixelRows(static pixels =>
        {
            for (var y = 490; y < 590; y++)
            {
                pixels.GetRowSpan(y).Slice(910, 100).Fill(Colors.Crimson);
            }
        });

        _expected64 = _expected.CloneAs<Rgba64>();
        _actual64 = _actual.CloneAs<Rgba64>();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _expected?.Dispose();
        _actual?.Dispose();
        _expected64?.Dispose();
        _actual64?.Dispose();
    }

    [Benchmark(Baseline = true)]
    [Workload("1920x1080 Rgba32 SSIM")]
    public double Ssim() => ImageSimilarity.ComputeSsim(_expected.Frames[0], _actual.Frames[0]);

    [Benchmark]
    [Workload("1920x1080 Rgba64 SSIM (8-bit conversion)")]
    public double Ssim64() => ImageSimilarity.ComputeSsim(_expected64.Frames[0], _actual64.Frames[0]);

    [Benchmark]
    [Workload("1920x1080 Rgba64 SSIM (16-bit samples)")]
    public double Ssim64SixteenBit() => ImageSimilarity.ComputeSsim(_expected64.Frames[0], _actual64.Frames[0], new ImageSimilarityOptions { Precision = SimilarityPrecision.SixteenBit });

    [Benchmark]
    [Workload("1920x1080 Rgba64 PSNR (16-bit samples)")]
    public double Psnr64SixteenBit() => ImageSimilarity.ComputePsnr(_expected64.Frames[0], _actual64.Frames[0], new ImageSimilarityOptions { Precision = SimilarityPrecision.SixteenBit });

    [Benchmark]
    [Workload("1920x1080 Rgba32 SSIM difference map")]
    public int SsimMap()
    {
        using var map = ImageSimilarity.CreateDifferenceMap(_expected.Frames[0], _actual.Frames[0], new DifferenceMapOptions { Kind = DifferenceMapKind.Ssim });
        return map.Width;
    }

    [Benchmark]
    [Workload("1920x1080 Rgba32 highlight difference map")]
    public int HighlightMap()
    {
        using var map = ImageSimilarity.CreateDifferenceMap(_expected.Frames[0], _actual.Frames[0]);
        return map.Width;
    }

    [Benchmark]
    [Workload("1920x1080 Rgba32 dHash distance (black and white backgrounds)")]
    public int DifferenceHashDistance() => ImageSimilarity.ComputeDifferenceHashDistance(_expected.Frames[0], _actual.Frames[0]);

    [Benchmark]
    [Workload("1920x1080 Rgba32 pHash distance (black and white backgrounds)")]
    public int PerceptualHashDistance() => ImageSimilarity.ComputePerceptualHashDistance(_expected.Frames[0], _actual.Frames[0]);

    [Benchmark]
    [Workload("1920x1080 Rgba32 pHash")]
    public ulong PerceptualHash() => ImageSimilarity.ComputePerceptualHash(_expected.Frames[0]);
}
