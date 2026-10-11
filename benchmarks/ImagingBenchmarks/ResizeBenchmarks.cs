using BenchmarkDotNet.Attributes;
using Meziantou.Framework.Imaging.Benchmarks.Infrastructure;
using Meziantou.Framework.Imaging.Benchmarks.Workloads;

namespace Meziantou.Framework.Imaging.Benchmarks;

/// <summary>
/// Alpha-aware resizing of 1920x1080 sources with transparency (premultiplied filtering). Each
/// operation clones the source first so it can be repeated; <see cref="Clone"/> measures that copy alone.
/// </summary>
[Config(typeof(WorkloadConfig))]
public class ResizeBenchmarks
{
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
    [Workload("1920x1080 Rgba32 clone only (no resize)")]
    public int Clone()
    {
        using var copy = _rgba32.Clone();
        return copy.Width;
    }

    [Benchmark]
    [Workload("1920x1080 -> 640x360 Rgba32 nearest neighbor")]
    public int Nearest() => Resize(_rgba32, ResamplingFilter.NearestNeighbor, 640, 360);

    [Benchmark]
    [Workload("1920x1080 -> 640x360 Rgba32 bilinear")]
    public int Bilinear() => Resize(_rgba32, ResamplingFilter.Bilinear, 640, 360);

    [Benchmark]
    [Workload("1920x1080 -> 640x360 Rgba32 Catmull-Rom")]
    public int CatmullRom() => Resize(_rgba32, ResamplingFilter.Bicubic, 640, 360);

    [Benchmark]
    [Workload("1920x1080 -> 640x360 Rgba32 Lanczos3")]
    public int Lanczos3() => Resize(_rgba32, ResamplingFilter.Lanczos3, 640, 360);

    [Benchmark]
    [Workload("1920x1080 -> 640x360 Rgba32 Catmull-Rom, linear sRGB")]
    public int CatmullRomLinear() => Resize(_rgba32, ResamplingFilter.Bicubic, 640, 360, ResizeWorkingSpace.LinearSrgb);

    [Benchmark]
    [Workload("1920x1080 -> 2880x1620 Rgba32 Catmull-Rom (upscale)")]
    public int CatmullRomUpscale() => Resize(_rgba32, ResamplingFilter.Bicubic, 2880, 1620);

    [Benchmark]
    [Workload("1920x1080 -> 640x360 Rgba64 Catmull-Rom")]
    public int CatmullRom16() => Resize(_rgba64, ResamplingFilter.Bicubic, 640, 360);

    private static int Resize(Image source, ResamplingFilter filter, int width, int height, ResizeWorkingSpace workingSpace = ResizeWorkingSpace.Encoded)
    {
        using var result = ImageWorkloads.ResizeCopy(source, new ResizeOptions(width, height) { Mode = ResizeMode.Stretch, Filter = filter, WorkingSpace = workingSpace, AllowUpscaling = true });
        return result.Width;
    }
}
