using BenchmarkDotNet.Attributes;
using Meziantou.Framework.Imaging.Benchmarks.Infrastructure;
using Meziantou.Framework.Imaging.Benchmarks.Workloads;
using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.Benchmarks;

/// <summary>
/// HEIC decoding and encoding. The decoded inputs are not written by the encoder of the library, which does not use every
/// tool that a decoder must be fast at: they are HEVC streams of the golden corpus, written by x265, in a minimal HEIC
/// container (<see cref="HevcStreamContainer"/>): two 384x216 lossy stills with both in-loop filters (deblocking and
/// sample adaptive offset), 4:2:0 and 4:4:4, and a 176x144 animation of 5 images coded with P and B pictures, which the
/// decoder outputs in another order than it decodes them, and a 3000x1280 grid of 8x6 copies of the 4:2:0 still, as phone
/// cameras write their photos (tiles decoded one by one, then assembled and converted). These inputs are small: the workloads measure the decoder on its
/// own corpus and say nothing about photographs. The corpus has no large 10-bit stream: the 10-bit still is the 512x384
/// photo-like still of the encoder workloads, written by the encoder of the library (lossy 4:2:0, effort 0), and decoded to
/// its 16-bit pixels, opaque and with the opaque, translucent and transparent areas of a 10-bit alpha plane. The 3840x2160 still with rows of the wavefront is written by the encoder of the library (lossy 4:2:0,
/// effort 0), whose large still images have them by default, as x265 writes its streams: the decoder decodes its rows, and
/// the tiles of the grid, with <see cref="Workers"/> workers.
/// </summary>
/// <remarks>
/// The encoder is far slower than the decoder, and than the encoders of the other formats: its inputs are a 512x384
/// photo-like still and the first 8 images of the 320x240 animation of <see cref="AnimationBenchmarks"/>, at the fastest
/// (0) and default (5) efforts. Quality and size trade-offs are measured by the <c>heic-quality</c> command, not here.
/// </remarks>
[Config(typeof(WorkloadConfig))]
public class HeicBenchmarks : IDisposable
{
    private const int EncodedWidth = 512;
    private const int EncodedHeight = 384;
    private const int EncodedFrames = 8;
    private const int GridColumns = 8;
    private const int GridRows = 6;
    private const int GridWidth = 3000;
    private const int GridHeight = 1280;
    private const int WavefrontWidth = 3840;
    private const int WavefrontHeight = 2160;
    private readonly MemoryStream _output = new();
    private byte[] _still420 = null!;
    private byte[] _still444 = null!;
    private byte[] _animation = null!;
    private byte[] _still10Bit = null!;
    private byte[] _still10BitAlpha = null!;
    private byte[] _grid = null!;
    private byte[] _wavefront = null!;
    private Image<Rgb24> _photo = null!;
    private Image<Rgba32> _frames = null!;

    /// <summary>Gets or sets <see cref="ImageConfiguration.MaxDegreeOfParallelism"/> of the decodings of the grid and of the wavefront still.</summary>
    [Params(1, 8)]
    public int Workers { get; set; } = 1;

    [GlobalSetup]
    public void Setup()
    {
        _still420 = Wrap("hevc/x265-benchmark-intra-420-384x216.hevc", 384, 216, chromaFormatIdc: 1);
        _still444 = Wrap("hevc/x265-benchmark-intra-444-384x216.hevc", 384, 216, chromaFormatIdc: 3);
        _animation = Wrap("hevc/x265-benchmark-inter-420-176x144.hevc", 176, 144, chromaFormatIdc: 1);
        _grid = HevcStreamContainer.WrapGrid(ReadResource("hevc/x265-benchmark-intra-420-384x216.hevc"), 384, 216, chromaFormatIdc: 1, GridColumns, GridRows, GridWidth, GridHeight);

        // Untimed gates: the inputs are what the workloads say
        Require(_still420, 384, 216, frames: 1);
        Require(_still444, 384, 216, frames: 1);
        Require(_animation, 176, 144, frames: 5);
        Require(_grid, GridWidth, GridHeight, frames: 1);

        _photo = Image.ImportPixelBytes<Rgb24>(BenchmarkPixels.Rgb24(EncodedWidth, EncodedHeight, seed: 21), EncodedWidth, EncodedHeight);
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

        // The encoder writes what the workloads say: a lossless still that decodes to its source, and an image sequence
        var source = new Rgb24[EncodedWidth * EncodedHeight];
        _photo.Frames[0].CopyPixelDataTo(source);
        ImageWorkloads.Encode(_photo, new HeicEncoder { Effort = 0 }, _output);
        WorkloadChecks.RequireLossless(_output.ToArray(), source);
        ImageWorkloads.Encode(_frames, new HeicEncoder { Compression = HeicCompression.Lossy, Effort = 0 }, _output);
        Require(_output.ToArray(), BenchmarkInputs.AnimationWidth, BenchmarkInputs.AnimationHeight, EncodedFrames);
        using (var deep = _photo.CloneAs<Rgba64>())
        {
            ImageWorkloads.Encode(deep, new HeicEncoder { Compression = HeicCompression.Lossy, BitDepth = 10, Effort = 0 }, _output);
        }

        _still10Bit = _output.ToArray();
        Require(_still10Bit, EncodedWidth, EncodedHeight, frames: 1);

        // A large still whose rows are substreams of the wavefront: several workers decode it to the pixels of one
        using (var large = Image.ImportPixelBytes<Rgb24>(BenchmarkPixels.Rgb24(WavefrontWidth, WavefrontHeight, seed: 37), WavefrontWidth, WavefrontHeight))
        {
            ImageWorkloads.Encode(large, new HeicEncoder { Compression = HeicCompression.Lossy, Effort = 0 }, _output);
        }

        _wavefront = _output.ToArray();
        Require(_wavefront, WavefrontWidth, WavefrontHeight, frames: 1);
        using (var one = Image.Load<Rgb24>(_wavefront))
        using (var several = Image.Load<Rgb24>(_wavefront, WithWorkers(8)))
        {
            var expected = new Rgb24[WavefrontWidth * WavefrontHeight];
            var actual = new Rgb24[WavefrontWidth * WavefrontHeight];
            one.Frames[0].CopyPixelDataTo(expected);
            several.Frames[0].CopyPixelDataTo(actual);
            if (!expected.AsSpan().SequenceEqual(actual))
                throw new InvalidOperationException("The wavefront still decodes to other pixels with several workers.");
        }
        using (var decoded = Image.Load(_still10Bit))
        {
            if (decoded.PixelFormat is not (PixelFormat.Rgb48 or PixelFormat.Rgba64))
                throw new InvalidOperationException($"Unexpected 10-bit HEIC input: {decoded.PixelFormat}.");
        }

        using (var translucent = Image.ImportPixelData<Rgba64>(BenchmarkPixels.Rgba64(EncodedWidth, EncodedHeight, seed: 21, alpha: true), EncodedWidth, EncodedHeight))
        {
            ImageWorkloads.Encode(translucent, new HeicEncoder { Compression = HeicCompression.Lossy, BitDepth = 10, Effort = 0 }, _output);
        }

        _still10BitAlpha = _output.ToArray();
        Require(_still10BitAlpha, EncodedWidth, EncodedHeight, frames: 1);
        using (var decoded = Image.Load<Rgba64>(_still10BitAlpha))
        {
            var pixels = new Rgba64[EncodedWidth * EncodedHeight];
            decoded.Frames[0].CopyPixelDataTo(pixels);
            if (!pixels.Any(pixel => pixel.A is not (0 or ushort.MaxValue)) || !pixels.Any(pixel => pixel.A is 0) || !pixels.Any(pixel => pixel.A is ushort.MaxValue))
                throw new InvalidOperationException("Unexpected 10-bit HEIC input: the alpha plane is not opaque, translucent and transparent.");
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _photo?.Dispose();
        _frames?.Dispose();
    }

    [Benchmark]
    [Workload("384x216 lossy HEIC 4:2:0 -> Rgb24")]
    public int DecodeStill420()
    {
        using var image = Image.Load<Rgb24>(_still420);
        return image.Width;
    }

    [Benchmark]
    [Workload("384x216 lossy HEIC 4:4:4 -> Rgb24")]
    public int DecodeStill444()
    {
        using var image = Image.Load<Rgb24>(_still444);
        return image.Width;
    }

    [Benchmark]
    [Workload("3000x1280 HEIC grid of 8x6 lossy 4:2:0 tiles -> Rgb24")]
    public int DecodeGrid()
    {
        using var image = Image.Load<Rgb24>(_grid, WithWorkers(Workers));
        return image.Width;
    }

    [Benchmark]
    [Workload("3840x2160 lossy HEIC 4:2:0, rows of the wavefront -> Rgb24")]
    public int DecodeWavefront()
    {
        using var image = Image.Load<Rgb24>(_wavefront, WithWorkers(Workers));
        return image.Width;
    }

    [Benchmark]
    [Workload("176x144x5 HEIC animation (P and B pictures) -> Rgb24")]
    public int DecodeAnimation()
    {
        using var image = Image.Load<Rgb24>(_animation);
        return image.Frames.Count;
    }

    [Benchmark]
    [Workload("512x384 lossy 10-bit HEIC 4:2:0 -> Rgba64")]
    public int DecodeStill10Bit()
    {
        using var image = Image.Load<Rgba64>(_still10Bit);
        return image.Width;
    }

    [Benchmark]
    [Workload("512x384 lossy 10-bit HEIC 4:2:0 with a 10-bit alpha plane -> Rgba64")]
    public int DecodeStill10BitAlpha()
    {
        using var image = Image.Load<Rgba64>(_still10BitAlpha);
        return image.Width;
    }

    [Benchmark]
    [Workload("176x144x5 HEIC animation (P and B pictures) -> Rgb24, frame by frame")]
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
    [Workload("176x144x5 HEIC animation: identification (full scan)")]
    public int? IdentifyAnimation() => Image.Identify(_animation, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan }).FrameCount;

    [Benchmark]
    [Workload("512x384 Rgb24 -> lossy HEIC q75 4:2:0, effort 0", ReturnsOutputBytes = true)]
    public long EncodeLossyFastest() => ImageWorkloads.Encode(_photo, new HeicEncoder { Compression = HeicCompression.Lossy, Effort = 0 }, _output);

    [Benchmark]
    [Workload("512x384 Rgb24 -> lossy HEIC q75 4:2:0, effort 5", ReturnsOutputBytes = true)]
    public long EncodeLossy() => ImageWorkloads.Encode(_photo, new HeicEncoder { Compression = HeicCompression.Lossy }, _output);

    [Benchmark]
    [Workload("512x384 Rgb24 -> lossless HEIC, effort 0", ReturnsOutputBytes = true)]
    public long EncodeLosslessFastest() => ImageWorkloads.Encode(_photo, new HeicEncoder { Effort = 0 }, _output);

    [Benchmark]
    [Workload("512x384 Rgb24 -> lossless HEIC, effort 5", ReturnsOutputBytes = true)]
    public long EncodeLossless() => ImageWorkloads.Encode(_photo, new HeicEncoder(), _output);

    [Benchmark]
    [Workload("Rgba32 320x240x8 -> lossy HEIC image sequence q75 (inter frames, lossless alpha), effort 0", Frames = EncodedFrames, ReturnsOutputBytes = true)]
    public long EncodeAnimation() => ImageWorkloads.Encode(_frames, new HeicEncoder { Compression = HeicCompression.Lossy, Effort = 0 }, _output);

    public void Dispose()
    {
        Cleanup();
        _output.Dispose();
        GC.SuppressFinalize(this);
    }

    private static ImageDecodeOptions WithWorkers(int workers) => new() { Configuration = new ImageConfiguration { MaxDegreeOfParallelism = workers } };

    private static byte[] Wrap(string resourceName, int width, int height, int chromaFormatIdc) => HevcStreamContainer.Wrap(ReadResource(resourceName), width, height, bitDepth: 8, chromaFormatIdc);

    private static byte[] ReadResource(string resourceName)
    {
        using var resource = typeof(HeicBenchmarks).Assembly.GetManifestResourceStream(resourceName) ?? throw new InvalidOperationException($"The resource '{resourceName}' is missing.");
        using var stream = new MemoryStream();
        resource.CopyTo(stream);
        return stream.ToArray();
    }

    private static void Require(byte[] encoded, int width, int height, int frames)
    {
        using var image = Image.Load(encoded);
        if (image.Width != width || image.Height != height || image.Frames.Count != frames)
            throw new InvalidOperationException($"Unexpected HEIC input: {image.Width}x{image.Height}, {image.Frames.Count} frame(s).");
    }
}
