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

    // The 15x15 box as factors, and as a matrix whose middle weight is changed by 1e-12 so that it is not recognized as
    // separable: the same number of weights, applied as a matrix
    private static readonly ConvolutionKernel SeparableBox15 = ConvolutionKernel.CreateSeparable(Enumerable.Repeat(1 / 15d, 15).ToArray(), Enumerable.Repeat(1 / 15d, 15).ToArray());

    private static readonly ConvolutionKernel MatrixBox15 = CreateMatrixBox(15);

    private static readonly ConvolutionKernel SeparableBox31 = ConvolutionKernel.CreateSeparable(Enumerable.Repeat(1 / 31d, 31).ToArray(), Enumerable.Repeat(1 / 31d, 31).ToArray());

    private Image<Rgba32> _rgba32 = null!;
    private Image<Rgba64> _rgba64 = null!;
    private Image<Rgba32> _opaque32 = null!;
    private Image<RgbaFloat> _float = null!;

    /// <summary>Gets or sets <see cref="ImageConfiguration.MaxDegreeOfParallelism"/> (1 is the default configuration).</summary>
    [Params(1, 2, 4)]
    public int Workers { get; set; } = 1;

    [GlobalSetup]
    public void Setup()
    {
        var configuration = new ImageConfiguration { MaxDegreeOfParallelism = Workers };
        _rgba32 = Image.ImportPixelData<Rgba32>(BenchmarkInputs.ResizeRgba32Source, BenchmarkInputs.ResizeWidth, BenchmarkInputs.ResizeHeight, configuration: configuration);
        _rgba64 = Image.ImportPixelData<Rgba64>(BenchmarkInputs.ResizeRgba64Source, BenchmarkInputs.ResizeWidth, BenchmarkInputs.ResizeHeight, configuration: configuration);
        _opaque32 = Image.ImportPixelData<Rgba32>(BenchmarkInputs.ResizeRgba32OpaqueSource, BenchmarkInputs.ResizeWidth, BenchmarkInputs.ResizeHeight, configuration: configuration);
        _float = _rgba64.CloneAs<RgbaFloat>();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _rgba32?.Dispose();
        _rgba64?.Dispose();
        _opaque32?.Dispose();
        _float?.Dispose();
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

    [Benchmark]
    [Workload("1920x1080 Rgba32 Gaussian blur, deviation 2 (13 weights per direction)")]
    public int GaussianBlur2() => Blur(_rgba32, 2);

    [Benchmark]
    [Workload("1920x1080 Rgba32 Gaussian blur, deviation 8 (49 weights per direction)")]
    public int GaussianBlur8() => Blur(_rgba32, 8);

    [Benchmark]
    [Workload("1920x1080 Rgba32 Gaussian blur, deviation 32 (193 weights per direction)")]
    public int GaussianBlur32() => Blur(_rgba32, 32);

    [Benchmark]
    [Workload("1920x1080 opaque Rgba32 Gaussian blur, deviation 2 (13 weights per direction)")]
    public int GaussianBlur2Opaque() => Blur(_opaque32, 2);

    [Benchmark]
    [Workload("1920x1080 Rgba64 Gaussian blur, deviation 2 (13 weights per direction)")]
    public int GaussianBlur2Rgba64() => Blur(_rgba64, 2);

    [Benchmark]
    [Workload("1920x1080 Rgba32 15x15 box as a matrix (225 weights)")]
    public int Matrix15() => Convolve(_rgba32, new ConvolutionOptions(MatrixBox15));

    [Benchmark]
    [Workload("1920x1080 Rgba32 15x15 box as a separable matrix (15 + 15 weights)")]
    public int Separable15() => Convolve(_rgba32, new ConvolutionOptions(SeparableBox15));

    [Benchmark]
    [Workload("1920x1080 Rgba32 31x31 box as a separable matrix (31 + 31 weights)")]
    public int Separable31() => Convolve(_rgba32, new ConvolutionOptions(SeparableBox31));

    [Benchmark]
    [Workload("1920x1080 Rgba32 box blur, radius 7 (15x15, block sums)")]
    public int BoxBlur7() => Filter(_rgba32, image => image.BoxBlur(7));

    [Benchmark]
    [Workload("1920x1080 Rgba32 box blur, radius 15 (31x31, block sums)")]
    public int BoxBlur15() => Filter(_rgba32, image => image.BoxBlur(15));

    [Benchmark]
    [Workload("1920x1080 Rgba32 box blur, radius 100 (201x201, block sums)")]
    public int BoxBlur100() => Filter(_rgba32, image => image.BoxBlur(100));

    [Benchmark]
    [Workload("1920x1080 Rgba32 unsharp mask, deviation 2")]
    public int Sharpen2() => Filter(_rgba32, image => image.Sharpen(2, 1.5));

    [Benchmark]
    [Workload("1920x1080 Rgba32 median, radius 1 (3x3)")]
    public int Median1() => Filter(_rgba32, image => image.MedianBlur(1));

    [Benchmark]
    [Workload("1920x1080 Rgba32 median, radius 5 (11x11)")]
    public int Median5() => Filter(_rgba32, image => image.MedianBlur(5));

    [Benchmark]
    [Workload("1920x1080 Rgba64 median, radius 2 (5x5)")]
    public int Median2Rgba64() => Filter(_rgba64, image => image.MedianBlur(2));

    [Benchmark]
    [Workload("1920x1080 RgbaFloat median, radius 1 (3x3)")]
    public int Median1Float() => Filter(_float, image => image.MedianBlur(1));

    [Benchmark]
    [Workload("1920x1080 RgbaFloat median, radius 5 (11x11)")]
    public int Median5Float() => Filter(_float, image => image.MedianBlur(5));

    [Benchmark]
    [Workload("1920x1080 RgbaFloat median, radius 15 (31x31)")]
    public int Median15Float() => Filter(_float, image => image.MedianBlur(15));

    private static int Filter(Image source, Action<Image> filter)
    {
        using var result = ImageWorkloads.FilterCopy(source, filter);
        return result.Width;
    }

    private static ConvolutionKernel CreateMatrixBox(int size)
    {
        var weights = new double[size * size];
        Array.Fill(weights, 1d / (size * size));
        weights[weights.Length / 2] += 1e-12;
        return new ConvolutionKernel(size, size, weights);
    }

    private static int Convolve(Image source, ConvolutionOptions options)
    {
        using var result = ImageWorkloads.ConvolveCopy(source, options);
        return result.Width;
    }

    private static int Blur(Image source, double sigma)
    {
        using var result = ImageWorkloads.GaussianBlurCopy(source, sigma);
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
