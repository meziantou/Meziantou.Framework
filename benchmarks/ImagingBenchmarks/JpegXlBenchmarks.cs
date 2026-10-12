using BenchmarkDotNet.Attributes;
using Meziantou.Framework.Imaging.Benchmarks.Infrastructure;
using Meziantou.Framework.Imaging.Benchmarks.Workloads;
using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.Benchmarks;

/// <summary>
/// JPEG XL decoding, encoding and transcoding on 1024x768 photo-like stills: lossless and lossy (quality 75) RGB, a lossy
/// RGBA still whose alpha channel is lossless, their progressive forms read at once and refinement after refinement, and
/// the 1024x768 baseline JPEG of the service workload recompressed without decoding its pixels and rebuilt byte for byte.
/// Encoders run at the fastest (0) and default (5) efforts. Quality and size trade-offs are measured by the
/// <c>jxl-quality</c> command, not here.
/// </summary>
/// <remarks>
/// The decoded files are written by the encoder of the library, which only uses the 8x8 transform for lossy output: the
/// decoding workloads say nothing about files of other encoders, which use larger transforms, patches and splines.
/// </remarks>
[Config(typeof(WorkloadConfig))]
public class JpegXlBenchmarks : IDisposable
{
    private const int EncodedFrames = 8;
    private static readonly JpegXlEncoder Lossy = new() { Compression = JpegXlCompression.Lossy };
    private static readonly JpegXlEncoder ProgressiveLossy = new() { Compression = JpegXlCompression.Lossy, Progressive = true };
    private readonly MemoryStream _output = new();
    private Image<Rgb24> _photo = null!;
    private Image<Rgba32> _transparent = null!;
    private Image<Rgba32> _frames = null!;
    private Image<Rgb24> _destination = null!;
    private byte[] _lossless = null!;
    private byte[] _lossy = null!;
    private byte[] _progressiveLossy = null!;
    private byte[] _progressiveLossless = null!;
    private byte[] _lossyTransparent = null!;
    private byte[] _recompressed = null!;

    private static int Width => BenchmarkInputs.ServiceWidth;

    private static int Height => BenchmarkInputs.ServiceHeight;

    [GlobalSetup]
    public void Setup()
    {
        var source = unsafe(System.Runtime.InteropServices.MemoryMarshal.Cast<byte, Rgb24>(BenchmarkPixels.Rgb24(Width, Height, seed: 5))).ToArray();
        _photo = Image.ImportPixelData<Rgb24>(source, Width, Height);
        _transparent = Image.ImportPixelData<Rgba32>(BenchmarkInputs.ServiceRgba32Source, Width, Height);
        _destination = new Image<Rgb24>(Width, Height);
        var frames = BenchmarkPixels.AnimationFrames(BenchmarkInputs.AnimationWidth, BenchmarkInputs.AnimationHeight, EncodedFrames, seed: 6);
        _frames = Image.ImportPixelData<Rgba32>(frames[0], BenchmarkInputs.AnimationWidth, BenchmarkInputs.AnimationHeight);
        for (var i = 1; i < EncodedFrames; i++)
        {
            using var frame = Image.ImportPixelData<Rgba32>(frames[i], BenchmarkInputs.AnimationWidth, BenchmarkInputs.AnimationHeight);
            _frames.AppendFrame(frame.Frames[0]);
        }

        for (var i = 0; i < EncodedFrames; i++)
        {
            _frames.Frames[i].Metadata.Duration = FrameDuration.FromMilliseconds(40);
        }

        _lossless = Encode(_photo, new JpegXlEncoder());
        _lossy = Encode(_photo, Lossy);
        _progressiveLossy = Encode(_photo, ProgressiveLossy);
        _progressiveLossless = Encode(_photo, new JpegXlEncoder { Progressive = true });
        _lossyTransparent = Encode(_transparent, Lossy);
        ImageWorkloads.Recompress(BenchmarkInputs.ServiceJpeg, _output);
        _recompressed = _output.ToArray();

        // Untimed correctness gates: the inputs are what the workloads say
        WorkloadChecks.RequireLossless(_lossless, source);
        WorkloadChecks.RequireLossless(_progressiveLossless, source);
        WorkloadChecks.RequirePsnr(_lossy, source, minimum: 33);
        WorkloadChecks.RequireSamePixels<Rgb24>(_lossy, _progressiveLossy);
        WorkloadChecks.RequireRefinements<Rgb24>(_progressiveLossy, minimum: 3);
        WorkloadChecks.RequireAnimation(Encode(_frames, Lossy), BenchmarkInputs.AnimationWidth, BenchmarkInputs.AnimationHeight, EncodedFrames, FrameDuration.FromMilliseconds(40));
        ImageWorkloads.Reconstruct(_recompressed, _output);
        if (!_output.GetBuffer().AsSpan(0, (int)_output.Length).SequenceEqual(BenchmarkInputs.ServiceJpeg))
            throw new InvalidOperationException("The rebuilt JPEG file is not the JPEG file that was recompressed.");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _photo?.Dispose();
        _transparent?.Dispose();
        _frames?.Dispose();
        _destination?.Dispose();
    }

    [Benchmark]
    [Workload("1024x768 lossless JPEG XL -> Rgb24")]
    public int DecodeLossless()
    {
        using var image = Image.Load<Rgb24>(_lossless);
        return image.Width;
    }

    [Benchmark]
    [Workload("1024x768 lossy JPEG XL (q75) -> Rgb24")]
    public int DecodeLossy()
    {
        using var image = Image.Load<Rgb24>(_lossy);
        return image.Width;
    }

    [Benchmark]
    [Workload("1024x768 lossy JPEG XL with alpha (q75, lossless alpha) -> Rgba32")]
    public int DecodeTransparent()
    {
        using var image = Image.Load<Rgba32>(_lossyTransparent);
        return image.Width;
    }

    [Benchmark]
    [Workload("1024x768 progressive lossy JPEG XL (q75, 3 passes) -> Rgb24")]
    public int DecodeProgressiveLossy()
    {
        using var image = Image.Load<Rgb24>(_progressiveLossy);
        return image.Width;
    }

    [Benchmark]
    [Workload("1024x768 progressive lossless JPEG XL (squeeze) -> Rgb24")]
    public int DecodeProgressiveLossless()
    {
        using var image = Image.Load<Rgb24>(_progressiveLossless);
        return image.Width;
    }

    [Benchmark]
    [Workload("1024x768 progressive lossy JPEG XL (q75, 3 passes) -> Rgb24, every refinement rendered")]
    public int ReadRefinements() => ImageWorkloads.ReadRefinements(_progressiveLossy, _destination);

    [Benchmark]
    [Workload("1024x768 Rgb24 -> lossless JPEG XL, effort 0", ReturnsOutputBytes = true)]
    public long EncodeLosslessFastest() => ImageWorkloads.Encode(_photo, new JpegXlEncoder { Effort = 0 }, _output);

    [Benchmark]
    [Workload("1024x768 Rgb24 -> lossless JPEG XL, effort 5", ReturnsOutputBytes = true)]
    public long EncodeLossless() => ImageWorkloads.Encode(_photo, new JpegXlEncoder(), _output);

    [Benchmark]
    [Workload("1024x768 Rgb24 -> progressive lossless JPEG XL (squeeze), effort 5", ReturnsOutputBytes = true)]
    public long EncodeProgressiveLossless() => ImageWorkloads.Encode(_photo, new JpegXlEncoder { Progressive = true }, _output);

    [Benchmark]
    [Workload("1024x768 Rgb24 -> lossy JPEG XL q75, effort 0", ReturnsOutputBytes = true)]
    public long EncodeLossyFastest() => ImageWorkloads.Encode(_photo, new JpegXlEncoder { Compression = JpegXlCompression.Lossy, Effort = 0 }, _output);

    [Benchmark]
    [Workload("1024x768 Rgb24 -> lossy JPEG XL q75, effort 5", ReturnsOutputBytes = true)]
    public long EncodeLossy() => ImageWorkloads.Encode(_photo, Lossy, _output);

    [Benchmark]
    [Workload("1024x768 Rgb24 -> progressive lossy JPEG XL q75 (3 passes), effort 5", ReturnsOutputBytes = true)]
    public long EncodeProgressiveLossy() => ImageWorkloads.Encode(_photo, ProgressiveLossy, _output);

    [Benchmark]
    [Workload("1024x768 Rgba32 -> lossy JPEG XL q75 with lossless alpha", ReturnsOutputBytes = true)]
    public long EncodeTransparent() => ImageWorkloads.Encode(_transparent, Lossy, _output);

    [Benchmark]
    [Workload("Rgba32 320x240x8 -> lossy JPEG XL animation q75 (full frames, lossless alpha)", Frames = EncodedFrames, ReturnsOutputBytes = true)]
    public long EncodeAnimation() => ImageWorkloads.Encode(_frames, Lossy, _output);

    [Benchmark]
    [Workload("1024x768 baseline JPEG (q85, 4:2:0) -> JPEG XL with reconstruction data, pixels not decoded", ReturnsOutputBytes = true)]
    public long RecompressJpeg() => ImageWorkloads.Recompress(BenchmarkInputs.ServiceJpeg, _output);

    [Benchmark]
    [Workload("1024x768 recompressed JPEG XL -> the JPEG file, byte for byte", ReturnsOutputBytes = true)]
    public long ReconstructJpeg() => ImageWorkloads.Reconstruct(_recompressed, _output);

    public void Dispose()
    {
        Cleanup();
        _output.Dispose();
        GC.SuppressFinalize(this);
    }

    private byte[] Encode(Image image, ImageEncoder encoder)
    {
        ImageWorkloads.Encode(image, encoder, _output);
        return _output.ToArray();
    }
}
