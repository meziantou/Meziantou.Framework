using BenchmarkDotNet.Attributes;
using Meziantou.Framework.Imaging.Benchmarks.Infrastructure;
using Meziantou.Framework.Imaging.Benchmarks.Workloads;
using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.Benchmarks;

/// <summary>
/// WebP decoding and encoding: 2048x1536 photo-like RGB stills (lossy quality 75, lossless), a 1024x768 RGBA
/// still with opaque, translucent and transparent areas (lossy color with a lossless alpha plane), and the 320x240x48
/// animation of <see cref="AnimationBenchmarks"/>. Encoders run at the fastest (0), default (5) and slowest (9) efforts
/// where effort matters. Quality and size trade-offs are measured by the <c>webp-quality</c> command,
/// not here.
/// </summary>
[Config(typeof(WorkloadConfig))]
public class WebPBenchmarks : IDisposable
{
    private const int Frames = BenchmarkInputs.AnimationFrames;
    private readonly MemoryStream _output = new();
    private Image<Rgb24> _photo = null!;
    private Image<Rgb24> _service = null!;
    private Image<Rgba32> _transparent = null!;
    private Image<Rgba32> _animation = null!;

    [GlobalSetup]
    public void Setup()
    {
        _photo = Image.ImportPixelData<Rgb24>(BenchmarkInputs.LargeRgb24Source, BenchmarkInputs.LargeWidth, BenchmarkInputs.LargeHeight);
        _service = Image.ImportPixelBytes<Rgb24>(BenchmarkPixels.Rgb24(BenchmarkInputs.ServiceWidth, BenchmarkInputs.ServiceHeight, seed: 5), BenchmarkInputs.ServiceWidth, BenchmarkInputs.ServiceHeight);
        _transparent = Image.ImportPixelData<Rgba32>(BenchmarkInputs.ServiceRgba32Source, BenchmarkInputs.ServiceWidth, BenchmarkInputs.ServiceHeight);
        _animation = BenchmarkInputs.CreateAnimation();

        // Untimed correctness gates
        WorkloadChecks.RequireLossless(BenchmarkInputs.LosslessWebP, BenchmarkInputs.LargeRgb24Source);
        WorkloadChecks.RequirePsnr(BenchmarkInputs.LossyWebP, BenchmarkInputs.LargeRgb24Source, minimum: 33);
        WorkloadChecks.RequireAnimation(BenchmarkInputs.WebPAnimation, BenchmarkInputs.AnimationWidth, BenchmarkInputs.AnimationHeight, Frames, FrameDuration.FromMilliseconds(40));
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _photo?.Dispose();
        _service?.Dispose();
        _transparent?.Dispose();
        _animation?.Dispose();
    }

    [Benchmark]
    [Workload("2048x1536 lossy WebP (q75) -> Rgb24")]
    public int DecodeLossy()
    {
        using var image = Image.Load<Rgb24>(BenchmarkInputs.LossyWebP);
        return image.Width;
    }

    [Benchmark]
    [Workload("2048x1536 lossless WebP -> Rgb24")]
    public int DecodeLossless()
    {
        using var image = Image.Load<Rgb24>(BenchmarkInputs.LosslessWebP);
        return image.Width;
    }

    [Benchmark]
    [Workload("1024x768 lossy WebP with alpha (q75, ALPH lossless) -> Rgba32")]
    public int DecodeTransparent()
    {
        using var image = Image.Load<Rgba32>(BenchmarkInputs.TransparentWebP);
        return image.Width;
    }

    [Benchmark]
    [Workload("Animated WebP 320x240x48 (lossless frames) -> Rgba32 (eager, composited)", Frames = Frames)]
    public int DecodeAnimation()
    {
        using var image = Image.Load<Rgba32>(BenchmarkInputs.WebPAnimation);
        return image.Frames.Count;
    }

    [Benchmark]
    [Workload("2048x1536 Rgb24 -> lossy WebP q75, effort 0", ReturnsOutputBytes = true)]
    public long EncodeLossyFastest() => ImageWorkloads.Encode(_photo, new WebPEncoder { Compression = WebPCompression.Lossy, Quality = 75, Effort = 0 }, _output);

    [Benchmark]
    [Workload("2048x1536 Rgb24 -> lossy WebP q75, effort 5", ReturnsOutputBytes = true)]
    public long EncodeLossy() => ImageWorkloads.Encode(_photo, BenchmarkInputs.WebPLossyEncoder, _output);

    [Benchmark]
    [Workload("1024x768 Rgb24 -> lossless WebP, effort 0", ReturnsOutputBytes = true)]
    public long EncodeLosslessFastest() => ImageWorkloads.Encode(_service, new WebPEncoder { Effort = 0 }, _output);

    [Benchmark]
    [Workload("1024x768 Rgb24 -> lossless WebP, effort 5", ReturnsOutputBytes = true)]
    public long EncodeLossless() => ImageWorkloads.Encode(_service, new WebPEncoder(), _output);

    [Benchmark]
    [Workload("1024x768 Rgb24 -> lossless WebP, effort 9", ReturnsOutputBytes = true)]
    public long EncodeLosslessSlowest() => ImageWorkloads.Encode(_service, new WebPEncoder { Effort = 9 }, _output);

    [Benchmark]
    [Workload("1024x768 Rgba32 -> lossy WebP q75 with lossless alpha", ReturnsOutputBytes = true)]
    public long EncodeTransparent() => ImageWorkloads.Encode(_transparent, BenchmarkInputs.WebPLossyEncoder, _output);

    [Benchmark]
    [Workload("Rgba32 320x240x48 -> animated lossless WebP (effort 0, seek-and-patch)", Frames = Frames, ReturnsOutputBytes = true)]
    public long EncodeAnimation() => ImageWorkloads.Encode(_animation, new WebPEncoder { Effort = 0 }, _output);

    [Benchmark]
    [Workload("Animated WebP 320x240x48: reader -> resize 160x120 -> WebP writer (effort 0)", Frames = Frames)]
    public int SequentialAnimation() => ImageWorkloads.TransformAnimationSequentially(BenchmarkInputs.WebPAnimation, new WebPEncoder { Effort = 0 }, _output);

    public void Dispose()
    {
        Cleanup();
        _output.Dispose();
        GC.SuppressFinalize(this);
    }
}
