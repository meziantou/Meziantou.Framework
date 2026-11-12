using BenchmarkDotNet.Attributes;
using Meziantou.Framework.Imaging.Benchmarks.Infrastructure;
using Meziantou.Framework.Imaging.Benchmarks.Workloads;

namespace Meziantou.Framework.Imaging.Benchmarks;

/// <summary>
/// <see cref="Image.Identify(ReadOnlySpan{byte}, ImageIdentifyOptions?)"/> of encoded files in memory, as an upload handler
/// checks the size, the format and the metadata before it decodes anything: the header only (the default), and the full
/// scan of the container. The photos are 2048x1536; the phone photo carries EXIF (orientation) and an ICC profile.
/// </summary>
[Config(typeof(WorkloadConfig))]
public class IdentifyBenchmarks
{
    private static readonly ImageIdentifyOptions FullScan = new() { Mode = ImageIdentifyMode.FullScan };

    private byte[] _jpeg = null!;
    private byte[] _png = null!;
    private byte[] _webP = null!;
    private byte[] _gif = null!;

    [GlobalSetup]
    public void Setup()
    {
        _jpeg = BenchmarkInputs.PhonePhotoJpeg;
        _png = BenchmarkInputs.LargePng8;
        _webP = BenchmarkInputs.LossyWebP;
        _gif = BenchmarkInputs.Gif;

        // Untimed correctness gate: the metadata of the photo is read
        if (Image.Identify(_jpeg).Metadata.IccProfile is null)
            throw new InvalidOperationException("The phone photo has no ICC profile.");
    }

    [Benchmark(Baseline = true)]
    [Workload("2048x1536 baseline JPEG with EXIF and ICC profile, header")]
    public int JpegHeader() => Image.Identify(_jpeg).Width;

    [Benchmark]
    [Workload("2048x1536 baseline JPEG with EXIF and ICC profile, full scan")]
    public int JpegFullScan() => Image.Identify(_jpeg, FullScan).Width;

    [Benchmark]
    [Workload("2048x1536 PNG 8-bit RGB, header")]
    public int PngHeader() => Image.Identify(_png).Width;

    [Benchmark]
    [Workload("2048x1536 PNG 8-bit RGB, full scan (every chunk)")]
    public int PngFullScan() => Image.Identify(_png, FullScan).Width;

    [Benchmark]
    [Workload("2048x1536 lossy WebP, header")]
    public int WebPHeader() => Image.Identify(_webP).Width;

    [Benchmark]
    [Workload("320x240 GIF of 48 frames, full scan (frame count)")]
    public int? GifFullScan() => Image.Identify(_gif, FullScan).FrameCount;
}
