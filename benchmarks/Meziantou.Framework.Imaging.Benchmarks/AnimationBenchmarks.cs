using BenchmarkDotNet.Attributes;
using Meziantou.Framework.Imaging.Benchmarks.Infrastructure;
using Meziantou.Framework.Imaging.Benchmarks.Workloads;
using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.Benchmarks;

/// <summary>
/// GIF and APNG animations (320x240, 48 frames of 40 ms): eager decoding and encoding, eager edits (remove, move, resize
/// every frame, save), and sequential transforms (read one frame, resize it, write it) whose live pixel storage must not
/// depend on the frame count.
/// </summary>
[Config(typeof(WorkloadConfig))]
public class AnimationBenchmarks : IDisposable
{
    private const int Frames = BenchmarkInputs.AnimationFrames;
    private readonly MemoryStream _output = new();
    private Image<Rgba32> _animation = null!;

    [GlobalSetup]
    public void Setup()
    {
        _animation = BenchmarkInputs.CreateAnimation();
        var duration = FrameDuration.FromMilliseconds(40);
        WorkloadChecks.RequireAnimation(BenchmarkInputs.Gif, BenchmarkInputs.AnimationWidth, BenchmarkInputs.AnimationHeight, Frames, duration);
        WorkloadChecks.RequireAnimation(BenchmarkInputs.Apng, BenchmarkInputs.AnimationWidth, BenchmarkInputs.AnimationHeight, Frames, duration);
    }

    [GlobalCleanup]
    public void Cleanup() => _animation?.Dispose();

    [Benchmark]
    [Workload("GIF 320x240x48 -> Rgba32 (eager, composited)", Frames = Frames)]
    public int LoadGif()
    {
        using var image = Image.Load<Rgba32>(BenchmarkInputs.Gif);
        return image.Frames.Count;
    }

    [Benchmark]
    [Workload("APNG 320x240x48 -> Rgba32 (eager, composited)", Frames = Frames)]
    public int LoadApng()
    {
        using var image = Image.Load<Rgba32>(BenchmarkInputs.Apng);
        return image.Frames.Count;
    }

    [Benchmark]
    [Workload("Rgba32 320x240x48 -> GIF (256 colors, no dithering)", Frames = Frames, ReturnsOutputBytes = true)]
    public long SaveGif() => ImageWorkloads.Encode(_animation, new GifEncoder(), _output);

    [Benchmark]
    [Workload("Rgba32 320x240x48 -> APNG (adaptive filters, optimal zlib)", Frames = Frames, ReturnsOutputBytes = true)]
    public long SaveApng() => ImageWorkloads.Encode(_animation, new PngEncoder { AnimationMode = PngAnimationMode.Animated }, _output);

    [Benchmark]
    [Workload("GIF 320x240x48: load, remove, move, resize 160x120, save GIF", Frames = Frames, ReturnsOutputBytes = true)]
    public long EditGif() => ImageWorkloads.EditAnimation(BenchmarkInputs.Gif, new GifEncoder(), _output);

    [Benchmark]
    [Workload("APNG 320x240x48: load, remove, move, resize 160x120, save APNG", Frames = Frames, ReturnsOutputBytes = true)]
    public long EditApng() => ImageWorkloads.EditAnimation(BenchmarkInputs.Apng, new PngEncoder(), _output);

    [Benchmark]
    [Workload("GIF 320x240x48: reader -> resize 160x120 -> GIF writer", Frames = Frames)]
    public int SequentialGif() => ImageWorkloads.TransformAnimationSequentially(BenchmarkInputs.Gif, new GifEncoder(), _output);

    [Benchmark]
    [Workload("APNG 320x240x48: reader -> resize 160x120 -> APNG writer", Frames = Frames)]
    public int SequentialApng() => ImageWorkloads.TransformAnimationSequentially(BenchmarkInputs.Apng, new PngEncoder { AnimationMode = PngAnimationMode.Animated }, _output);

    public void Dispose()
    {
        Cleanup();
        _output.Dispose();
        GC.SuppressFinalize(this);
    }
}
