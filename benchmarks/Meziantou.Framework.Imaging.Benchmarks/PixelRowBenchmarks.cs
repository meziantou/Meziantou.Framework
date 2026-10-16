using BenchmarkDotNet.Attributes;
using Meziantou.Framework.Imaging.Benchmarks.Infrastructure;
using Meziantou.Framework.Imaging.Benchmarks.Workloads;

namespace Meziantou.Framework.Imaging.Benchmarks;

/// <summary>Typed row loops with static callbacks on a 1920x1080 Rgba32 frame: steady state must not allocate per row.</summary>
[Config(typeof(WorkloadConfig))]
public class PixelRowBenchmarks
{
    private readonly ImageWorkloads.SumState _state = new();
    private Image<Rgba32> _image = null!;

    [GlobalSetup]
    public void Setup() => _image = Image.ImportPixelData<Rgba32>(BenchmarkInputs.ResizeRgba32Source, BenchmarkInputs.ResizeWidth, BenchmarkInputs.ResizeHeight);

    [GlobalCleanup]
    public void Cleanup() => _image?.Dispose();

    [Benchmark]
    [Workload("1920x1080 Rgba32: invert colors, static ProcessPixelRows callback")]
    public void Invert() => ImageWorkloads.InvertRows(_image.Frames[0]);

    [Benchmark]
    [Workload("1920x1080 Rgba32: sum green, static callback with state")]
    public long SumGreen() => ImageWorkloads.SumGreen(_image.Frames[0], _state);
}
