using BenchmarkDotNet.Attributes;
using Meziantou.Framework.Imaging.Benchmarks.Infrastructure;
using Meziantou.Framework.Imaging.Benchmarks.Workloads;

namespace Meziantou.Framework.Imaging.Benchmarks;

/// <summary>
/// In-place convolution of 1920x1080 sources with transparency (premultiplied filtering). Each
/// operation clones the source first so it can be repeated; <see cref="Clone"/> measures that copy alone.
/// </summary>
[Config(typeof(WorkloadConfig))]
public class ConvolutionBenchmarks
{
    private static readonly ConvolutionKernel SharpenKernel = new(3, 3, [0, -1, 0, -1, 5, -1, 0, -1, 0]);

    private static readonly ConvolutionKernel BoxKernel = new(3, 3, [1 / 9d, 1 / 9d, 1 / 9d, 1 / 9d, 1 / 9d, 1 / 9d, 1 / 9d, 1 / 9d, 1 / 9d]);

    private static readonly ConvolutionKernel BinomialKernel = CreateBinomial5();

    private Image<Rgba32> _rgba32 = null!;
    private Image<Rgba64> _rgba64 = null!;

    /// <summary>Gets or sets <see cref="ImageConfiguration.MaxDegreeOfParallelism"/> (1 is the default configuration).</summary>
    [Params(1, 2, 4)]
    public int Workers { get; set; } = 1;

    [GlobalSetup]
    public void Setup()
    {
        var configuration = new ImageConfiguration { MaxDegreeOfParallelism = Workers };
        _rgba32 = Image.ImportPixelData<Rgba32>(BenchmarkInputs.ResizeRgba32Source, BenchmarkInputs.ResizeWidth, BenchmarkInputs.ResizeHeight, configuration: configuration);
        _rgba64 = Image.ImportPixelData<Rgba64>(BenchmarkInputs.ResizeRgba64Source, BenchmarkInputs.ResizeWidth, BenchmarkInputs.ResizeHeight, configuration: configuration);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _rgba32?.Dispose();
        _rgba64?.Dispose();
    }

    [Benchmark(Baseline = true)]
    [Workload("1920x1080 Rgba32 clone only (no convolution)")]
    public int Clone()
    {
        using var copy = _rgba32.Clone();
        return copy.Width;
    }

    [Benchmark]
    [Workload("1920x1080 Rgba32 3x3 sharpen (5 weights)")]
    public int Sharpen3() => Convolve(_rgba32, new ConvolutionOptions(SharpenKernel));

    [Benchmark]
    [Workload("1920x1080 Rgba32 3x3 box blur (9 weights)")]
    public int Box3() => Convolve(_rgba32, new ConvolutionOptions(BoxKernel));

    [Benchmark]
    [Workload("1920x1080 Rgba32 3x3 box blur, alpha preserved")]
    public int Box3PreserveAlpha() => Convolve(_rgba32, new ConvolutionOptions(BoxKernel) { PreserveAlpha = true });

    [Benchmark]
    [Workload("1920x1080 Rgba32 3x3 box blur, linear sRGB")]
    public int Box3Linear() => Convolve(_rgba32, new ConvolutionOptions(BoxKernel) { WorkingSpace = ConvolutionWorkingSpace.LinearSrgb });

    [Benchmark]
    [Workload("1920x1080 Rgba32 5x5 binomial blur (25 weights)")]
    public int Binomial5() => Convolve(_rgba32, new ConvolutionOptions(BinomialKernel));

    [Benchmark]
    [Workload("1920x1080 Rgba64 3x3 box blur (9 weights)")]
    public int Box3Rgba64() => Convolve(_rgba64, new ConvolutionOptions(BoxKernel));

    private static int Convolve(Image source, ConvolutionOptions options)
    {
        using var result = ImageWorkloads.ConvolveCopy(source, options);
        return result.Width;
    }

    private static ConvolutionKernel CreateBinomial5()
    {
        // The outer product of 1 4 6 4 1 with itself, divided by 256
        double[] row = [1, 4, 6, 4, 1];
        var weights = new double[25];
        for (var i = 0; i < weights.Length; i++)
        {
            weights[i] = row[i / 5] * row[i % 5] / 256;
        }

        return new ConvolutionKernel(5, 5, weights);
    }
}
