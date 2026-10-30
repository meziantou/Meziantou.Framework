using BenchmarkDotNet.Attributes;
using Meziantou.Framework.Imaging.Benchmarks.Infrastructure;
using Meziantou.Framework.Imaging.Benchmarks.Workloads;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.FuzzTests;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Benchmarks;

/// <summary>
/// AVIF decoding and encoding. The decoded inputs are not written by the encoder of the library, which does not use every
/// tool that a decoder must be fast at: they are AV1 streams of the golden corpus, written by libaom,
/// in a minimal AVIF container: two 384x216 lossy stills that use every post-processing filter (deblocking, CDEF and
/// switchable loop restoration), 4:2:0 and 4:4:4, and a 176x144 animation of 5 images coded with inter frames, alternate
/// reference frames included. These inputs are small: the workloads measure the decoder on its own corpus and say nothing
/// about photographs.
/// </summary>
/// <remarks>
/// The encoder is far slower than the decoder, and than the encoders of the other formats: its inputs are a 512x384
/// photo-like still and the first 8 images of the 320x240 animation of <see cref="AnimationBenchmarks"/>, at the fastest
/// (0) and default (5) efforts. Quality and size trade-offs are measured by the <c>avif-quality</c> command, not here.
/// The image sequence written at effort 0 is also decoded: its luma motion vectors are whole samples, unlike the ones of libaom.
/// </remarks>
[Config(typeof(WorkloadConfig))]
public class AvifBenchmarks : IDisposable
{
    private const int EncodedWidth = 512;
    private const int EncodedHeight = 384;
    private const int EncodedFrames = 8;
    private readonly MemoryStream _output = new();
    private byte[] _still420 = null!;
    private byte[] _still444 = null!;
    private byte[] _animation = null!;
    private byte[] _encodedAnimation = null!;
    private Image<Rgb24> _photo = null!;
    private Image<Rgba32> _frames = null!;

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
        ImageWorkloads.Encode(_photo, new AvifEncoder { Effort = 0 }, _output);
        WorkloadChecks.RequireLossless(_output.ToArray(), source);
        ImageWorkloads.Encode(_frames, new AvifEncoder { Compression = AvifCompression.Lossy, Effort = 0 }, _output);
        _encodedAnimation = _output.ToArray();
        Require(_encodedAnimation, BenchmarkInputs.AnimationWidth, BenchmarkInputs.AnimationHeight, EncodedFrames);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _photo?.Dispose();
        _frames?.Dispose();
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
    [Workload("320x240x8 AVIF image sequence of the encoder (effort 0: whole-sample luma motion, lossless alpha) -> Rgba32", Frames = EncodedFrames)]
    public int DecodeEncodedAnimation()
    {
        using var image = Image.Load<Rgba32>(_encodedAnimation);
        return image.Frames.Count;
    }

    [Benchmark]
    [Workload("176x144x5 AVIF animation: identification (full scan)")]
    public int? IdentifyAnimation() => Image.Identify(_animation, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan }).FrameCount;

    [Benchmark]
    [Workload("512x384 Rgb24 -> lossy AVIF q75 4:2:0, effort 0", ReturnsOutputBytes = true)]
    public long EncodeLossyFastest() => ImageWorkloads.Encode(_photo, new AvifEncoder { Compression = AvifCompression.Lossy, Effort = 0 }, _output);

    [Benchmark]
    [Workload("512x384 Rgb24 -> lossy AVIF q75 4:2:0, effort 5", ReturnsOutputBytes = true)]
    public long EncodeLossy() => ImageWorkloads.Encode(_photo, new AvifEncoder { Compression = AvifCompression.Lossy }, _output);

    [Benchmark]
    [Workload("512x384 Rgb24 -> lossless AVIF, effort 0", ReturnsOutputBytes = true)]
    public long EncodeLosslessFastest() => ImageWorkloads.Encode(_photo, new AvifEncoder { Effort = 0 }, _output);

    [Benchmark]
    [Workload("512x384 Rgb24 -> lossless AVIF, effort 5", ReturnsOutputBytes = true)]
    public long EncodeLossless() => ImageWorkloads.Encode(_photo, new AvifEncoder(), _output);

    [Benchmark]
    [Workload("Rgba32 320x240x8 -> lossy AVIF image sequence q75 (inter frames, lossless alpha), effort 0", Frames = EncodedFrames, ReturnsOutputBytes = true)]
    public long EncodeAnimation() => ImageWorkloads.Encode(_frames, new AvifEncoder { Compression = AvifCompression.Lossy, Effort = 0 }, _output);

    public void Dispose()
    {
        Cleanup();
        _output.Dispose();
        GC.SuppressFinalize(this);
    }

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

    /// <summary>
    /// The AV1 tiles of a frame decoded and encoded by <see cref="Workers"/> workers
    /// (<see cref="ImageConfiguration.MaxDegreeOfParallelism"/>). The decoded input is a 3840x2160 lossy 4:2:0 frame of the
    /// encoder of the library in 4x2 tiles, the layout that libavif's <c>--autotiling</c> chooses for that size, and the same
    /// frame in one tile (of which workers only share the post-processing filters). The encoded input is a
    /// 1920x1080 frame in 4x2 tiles: the encoder chooses the fewest tiles by itself, which is one tile up to 4096 samples of
    /// width, so the layout is set through the internal settings of the AV1 encoder.
    /// </summary>
    [Config(typeof(WorkloadConfig))]
    public class Tiles
    {
        private const int DecodedWidth = 3840;
        private const int DecodedHeight = 2160;
        private const int EncodedWidth = 1920;
        private const int EncodedHeight = 1080;
        private byte[] _tiled = null!;
        private byte[] _untiled = null!;
        private ImageDecodeOptions _options = null!;
        private Av1Encoder _encoder = null!;

        /// <summary>Gets or sets <see cref="ImageConfiguration.MaxDegreeOfParallelism"/> (1 is the default configuration).</summary>
        [Params(1, 8)]
        public int Workers { get; set; } = 1;

        [GlobalSetup]
        public void Setup()
        {
            _options = new ImageDecodeOptions { Configuration = new ImageConfiguration { MaxDegreeOfParallelism = Workers } };
            var pixels = BenchmarkPixels.Rgb24(DecodedWidth, DecodedHeight, seed: 23);
            _tiled = Encode(pixels, DecodedWidth, DecodedHeight, tileColsLog2: 2, tileRowsLog2: 1);
            _untiled = Encode(pixels, DecodedWidth, DecodedHeight, tileColsLog2: 0, tileRowsLog2: 0);
            Require(_tiled);
            Require(_untiled);

            _encoder = CreateEncoder(EncodedWidth, EncodedHeight, Workers, tileColsLog2: 2, tileRowsLog2: 1);
            Fill(_encoder, BenchmarkPixels.Rgb24(EncodedWidth, EncodedHeight, seed: 24), EncodedWidth, EncodedHeight);
        }

        [GlobalCleanup]
        public void Cleanup() => _encoder?.Dispose();

        [Benchmark]
        [Workload("3840x2160 lossy AVIF 4:2:0 in 4x2 AV1 tiles -> Rgb24")]
        public int DecodeTiled()
        {
            using var image = Image.Load<Rgb24>(_tiled, _options);
            return image.Width;
        }

        [Benchmark]
        [Workload("3840x2160 lossy AVIF 4:2:0 in one AV1 tile -> Rgb24")]
        public int DecodeUntiled()
        {
            using var image = Image.Load<Rgb24>(_untiled, _options);
            return image.Width;
        }

        [Benchmark]
        [Workload("1920x1080 4:2:0 -> lossy AV1 frame in 4x2 tiles q75, effort 0", ReturnsOutputBytes = true)]
        public long EncodeTiled()
        {
            _encoder.Reset();
            _encoder.BeginFrame();
            while (!_encoder.Encode(CancellationToken.None))
            {
            }

            return _encoder.Payload.Length;
        }

        // A lossy frame of the encoder of the library in the given layout of tiles, in a minimal AVIF container
        private static byte[] Encode(byte[] pixels, int width, int height, int tileColsLog2, int tileRowsLog2)
        {
            using var encoder = CreateEncoder(width, height, workers: 8, tileColsLog2, tileRowsLog2);
            Fill(encoder, pixels, width, height);
            encoder.BeginFrame();
            while (!encoder.Encode(CancellationToken.None))
            {
            }

            return FuzzAvifContainer.Wrap(encoder.Payload.Span, width, height, bitDepth: 8, "4:2:0");
        }

        private static Av1Encoder CreateEncoder(int width, int height, int workers, int tileColsLog2, int tileRowsLog2)
        {
            var settings = new Av1EncoderSettings(width, height, BitDepth: 8, Monochrome: false, SubsamplingX: 1, SubsamplingY: 1, ColorPrimaries: 1, TransferCharacteristics: 13, MatrixCoefficients: 6, FullRange: true, ChromaSamplePosition: 0, StillPicture: true, Lossless: false, Quality: 75, Effort: 0, KeyFrameInterval: 0, workers, tileColsLog2, tileRowsLog2);
            return new Av1Encoder(new AllocationScope(new ImageResourceLimits()), settings);
        }

        // The planes of the encoder: BT.601 full-range luma, and the chroma of the top left pixel of each 2x2 block
        private static void Fill(Av1Encoder encoder, byte[] pixels, int width, int height)
        {
            var y = encoder.GetSourcePlane(0);
            var u = encoder.GetSourcePlane(1);
            var v = encoder.GetSourcePlane(2);
            for (var row = 0; row < height; row++)
            {
                var luma = y.GetRow(row);
                for (var column = 0; column < width; column++)
                {
                    var offset = ((row * width) + column) * 3;
                    var (r, g, b) = (pixels[offset], pixels[offset + 1], pixels[offset + 2]);
                    luma[column] = (ushort)Math.Clamp(((77 * r) + (150 * g) + (29 * b) + 128) >> 8, 0, 255);
                    if ((row & 1) is 0 && (column & 1) is 0)
                    {
                        u.GetRow(row >> 1)[column >> 1] = (ushort)Math.Clamp((((-43 * r) - (85 * g) + (128 * b) + 128) >> 8) + 128, 0, 255);
                        v.GetRow(row >> 1)[column >> 1] = (ushort)Math.Clamp((((128 * r) - (107 * g) - (21 * b) + 128) >> 8) + 128, 0, 255);
                    }
                }
            }
        }

        private static void Require(byte[] encoded)
        {
            using var image = Image.Load(encoded);
            if (image.Width != DecodedWidth || image.Height != DecodedHeight)
                throw new InvalidOperationException($"Unexpected AVIF input: {image.Width}x{image.Height}.");
        }
    }
}
