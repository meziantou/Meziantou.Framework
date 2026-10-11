using BenchmarkDotNet.Attributes;
using Meziantou.Framework.Imaging.Benchmarks.Infrastructure;
using Meziantou.Framework.Imaging.FuzzTests;

namespace Meziantou.Framework.Imaging.Benchmarks;

/// <summary>
/// AVIF decoding. The inputs are not written by the encoder of the library, which does not use every tool that a
/// decoder must be fast at: they are AV1 streams of the golden corpus, written by libaom,
/// in a minimal AVIF container: two 384x216 lossy stills that use every post-processing filter (deblocking, CDEF and
/// switchable loop restoration), 4:2:0 and 4:4:4, and a 176x144 animation of 5 images coded with inter frames, alternate
/// reference frames included. These inputs are small: the workloads measure the decoder on its own corpus and say nothing
/// about photographs.
/// </summary>
[Config(typeof(WorkloadConfig))]
public class AvifBenchmarks
{
    private byte[] _still420 = null!;
    private byte[] _still444 = null!;
    private byte[] _animation = null!;

    [GlobalSetup]
    public void Setup()
    {
        _still420 = Wrap("av1/lr-switchable-luma-420-384x216.obu", 384, 216, "4:2:0");
        _still444 = Wrap("av1/lr-switchable-chroma-444-384x216.obu", 384, 216, "4:4:4");
        _animation = Wrap("av1/inter-alt-ref-420-8bit-176x144.obu", 176, 144, "4:2:0");

        // Untimed gates: the inputs are what the workloads say
        Require(_still420, 384, 216, frames: 1);
        Require(_still444, 384, 216, frames: 1);
        Require(_animation, 176, 144, frames: 5);
    }

    [Benchmark]
    [Workload("384x216 lossy AVIF 4:2:0 -> Rgb24")]
    public int DecodeStill420()
    {
        using var image = Image.Load<Rgb24>(_still420);
        return image.Width;
    }

    [Benchmark]
    [Workload("384x216 lossy AVIF 4:4:4 -> Rgb24")]
    public int DecodeStill444()
    {
        using var image = Image.Load<Rgb24>(_still444);
        return image.Width;
    }

    [Benchmark]
    [Workload("176x144x5 AVIF animation (inter frames) -> Rgb24")]
    public int DecodeAnimation()
    {
        using var image = Image.Load<Rgb24>(_animation);
        return image.Frames.Count;
    }

    [Benchmark]
    [Workload("176x144x5 AVIF animation (inter frames) -> Rgb24, frame by frame")]
    public int ReadAnimation()
    {
        using var reader = Image.OpenReader<Rgb24>(new MemoryStream(_animation));
        var count = 0;
        while (reader.ReadFrame() is { } frame)
        {
            frame.Dispose();
            count++;
        }

        return count;
    }

    [Benchmark]
    [Workload("176x144x5 AVIF animation: identification (full scan)")]
    public int? IdentifyAnimation() => Image.Identify(_animation, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan }).FrameCount;

    private static byte[] Wrap(string resourceName, int width, int height, string subsampling)
    {
        using var resource = typeof(AvifBenchmarks).Assembly.GetManifestResourceStream(resourceName) ?? throw new InvalidOperationException($"The resource '{resourceName}' is missing.");
        using var stream = new MemoryStream();
        resource.CopyTo(stream);
        return FuzzAvifContainer.Wrap(stream.ToArray(), width, height, bitDepth: 8, subsampling);
    }

    private static void Require(byte[] encoded, int width, int height, int frames)
    {
        using var image = Image.Load(encoded);
        if (image.Width != width || image.Height != height || image.Frames.Count != frames)
            throw new InvalidOperationException($"Unexpected AVIF input: {image.Width}x{image.Height}, {image.Frames.Count} frame(s).");
    }
}
