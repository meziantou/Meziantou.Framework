using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.TestHarness.Streams;
using Meziantou.Framework.Imaging.Tests.Codecs;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// The decoder plug-in points of the built-in container walkers: a decode observer receives the parsed header,
/// the image boundaries and the raw payloads in file order, builds the image with <see cref="DecodedImageBuilder"/>, and
/// may stop the walk (frame limit) without the rest of the data being examined. The observers below produce solid frames
/// instead of decoding pixels; the real codecs plug real decoders into the same points.
/// </summary>
public sealed class DecodeObserverTests
{
    [Fact]
    public void PngObserverReceivesImagesInFileOrder()
    {
        var data = new SyntheticImages.PngBuilder()
            .Header(3, 2, 8, 2)
            .AnimationControl(2, 5)
            .Text("Title", "poster test")
            .ImageData(3, 2, 2)
            .FrameControl(3, 2)
            .FrameData(3, 2)
            .FrameControl(1, 1, x: 2, y: 1)
            .FrameData(1, 1)
            .Text("After", "frames")
            .End()
            .ToArray();
        var log = new List<string>();
        var codec = new RecordingPngCodec(log);
        using var registry = ImageCodecRegistry.Override(new ImageCodecRegistry([codec]));
        using var stream = new TestInputStream(data) { Seekable = false, MaxBytesPerRead = 5 };
        using var image = Image.Load(stream);
        Assert.Equal(["header 3x2 poster=True", "start poster", "end", "start frame 3x2", "end", "start frame 1x1", "end", "end of file"], log);
        Assert.Equal(2, image.Frames.Count);
        Assert.NotNull(image.PosterFrame);
        Assert.Equal(5, image.Animation?.TotalPlays);
        Assert.Equal(new FrameDuration(1, 10), image.Frames[1].Metadata.Duration);
        Assert.Equal(["Title", "After"], image.Metadata.TextEntries.Select(entry => entry.Keyword));
        Assert.True(codec.CompressedBytes > 0);
        Assert.IsType<Image<Rgba32>>(image);
    }

    [Fact]
    public void PngObserverCanStopAtTheFrameLimitWithoutExaminingTheRest()
    {
        var valid = SyntheticImages.Apng(2, 2, frames: 3);

        // Cut the file inside the last frame: with a frame limit of one, the rest is never examined
        var truncated = valid[..^30];
        var log = new List<string>();
        using var registry = ImageCodecRegistry.Override(new ImageCodecRegistry([new RecordingPngCodec(log)]));
        using var image = Image.Load(truncated, new ImageDecodeOptions { FrameLimit = 1 });
        Assert.Single(image.Frames);
        Assert.NotNull(image.Animation);
        Assert.Throws<InvalidImageContentException>(() => Image.Load(truncated));
    }

    [Fact]
    public void GifObserverReceivesImagesAndSubBlocks()
    {
        var data = new SyntheticImages.GifBuilder().Header(4, 3).Loop(0).GraphicControl(disposal: 2, delay: 7).Image(0, 0, 4, 3).Comment("between").GraphicControl(disposal: 1, delay: 3).Image(1, 1, 2, 2).Trailer().ToArray();
        var log = new List<string>();
        using var registry = ImageCodecRegistry.Override(new ImageCodecRegistry([new RecordingGifCodec(log)]));
        using var image = Image.Load<Rgba64>(data);
        Assert.Equal(["header 4x3", "image 4x3 at 0,0 delay 7", "end", "image 2x2 at 1,1 delay 3", "end", "end of file"], log);
        Assert.Equal(2, image.Frames.Count);
        Assert.Null(image.Animation?.TotalPlays);
        Assert.Equal(new FrameDuration(7, 100), image.Frames[0].Metadata.Duration);
        Assert.Equal("between", Assert.Single(image.Metadata.TextEntries).Value);
    }

    [Fact]
    public void JpegObserverReceivesSegmentsAndRawEntropyData()
    {
        var entropy = new byte[] { 0x12, 0xFF, 0x00, 0x34, 0xFF, 0xD0, 0x56 };
        var data = SyntheticImages.Jpeg(0xC0, 1);
        var log = new List<string>();
        var codec = new RecordingJpegCodec(log);
        using var registry = ImageCodecRegistry.Override(new ImageCodecRegistry([codec]));
        using var image = Image.Load(data);
        Assert.Equal(["segment E0", "segment DB", "segment C0", "segment C4", "header 8x8", "segment DA", "scan end", "end of file"], log);
        Assert.Equal(entropy, codec.EntropyData.ToArray());
        Assert.IsType<Image<Gray8>>(image);
    }

    private sealed class RecordingPngCodec(List<string> log) : PngCodec
    {
        public long CompressedBytes { get; set; }

        protected override PngDecodeObserver CreateDecodeObserver(ImageDecodeRequest request, ImageCodecContext context) => new Observer(this, log, request, context);

        private sealed class Observer(RecordingPngCodec codec, List<string> log, ImageDecodeRequest request, ImageCodecContext context) : PngDecodeObserver
        {
            private DecodedFrameSink? _builder;
            private PngStructureParser? _structure;

            public override void OnHeaderComplete(PngStructureParser structure)
            {
                _structure = structure;
                log.Add($"header {structure.Width}x{structure.Height} poster={structure.HasSeparatePoster}");
                _builder = DecodedFrameSink.Create(context, request, ImageFormat.Png, structure.Size, structure.DefaultPixelFormat, PixelFormat.Rgba32, structure.Metadata.IccProfile);
            }

            public override void OnImageStart(PngFrameControl? control)
            {
                if (control is null && _structure!.HasSeparatePoster)
                {
                    log.Add("start poster");
                    _builder!.BeginPoster();
                }
                else
                {
                    log.Add(control is { } frame ? $"start frame {frame.Width}x{frame.Height}" : "start image");
                    _builder!.BeginFrame(control?.Duration ?? FrameDuration.Zero);
                }

                FillCurrentFrame(_builder, _structure!.Size, 0x40);
            }

            public override void OnImageData(ReadOnlySpan<byte> compressed) => codec.CompressedBytes += compressed.Length;

            public override bool OnImageEnd()
            {
                log.Add("end");
                return _builder!.EndImage();
            }

            public override void OnEnd(PngStructureParser structure) => log.Add("end of file");

            public override Image GetResult() => _builder!.Build(_structure!.Metadata, _structure.Animation);

            protected override void Dispose(bool disposing)
            {
                _builder?.Dispose();
                base.Dispose(disposing);
            }
        }
    }

    private sealed class RecordingGifCodec(List<string> log) : GifCodec
    {
        protected override GifDecodeObserver CreateDecodeObserver(ImageDecodeRequest request, ImageCodecContext context) => new Observer(log, request, context);

        private sealed class Observer(List<string> log, ImageDecodeRequest request, ImageCodecContext context) : GifDecodeObserver
        {
            private DecodedFrameSink? _builder;
            private GifStructureParser? _structure;

            public override void OnHeaderComplete(GifStructureParser structure)
            {
                _structure = structure;
                log.Add($"header {structure.Width}x{structure.Height}");
                _builder = DecodedFrameSink.Create(context, request, ImageFormat.Gif, structure.Size, DefaultPixelFormats.Gif, PixelFormat.Rgba32, iccProfile: null);
            }

            public override void OnImageStart(GifImageDescriptor descriptor, ReadOnlySpan<byte> localColorTable, GifGraphicControl? control, int lzwMinimumCodeSize)
            {
                log.Add($"image {descriptor.Width}x{descriptor.Height} at {descriptor.Left},{descriptor.Top} delay {control?.DelayHundredths}");
                _builder!.BeginFrame(control?.Duration ?? FrameDuration.Zero);
                FillCurrentFrame(_builder, _structure!.Size, 0x80);
            }

            public override bool OnImageEnd()
            {
                log.Add("end");
                return _builder!.EndImage();
            }

            public override void OnEnd(GifStructureParser structure) => log.Add("end of file");

            public override Image GetResult() => _builder!.Build(_structure!.Metadata, _structure.Animation);

            protected override void Dispose(bool disposing)
            {
                _builder?.Dispose();
                base.Dispose(disposing);
            }
        }
    }

    private sealed class RecordingJpegCodec(List<string> log) : JpegCodec
    {
        public MemoryStream EntropyData { get; } = new();

        protected override JpegDecodeObserver CreateDecodeObserver(ImageDecodeRequest request, ImageCodecContext context) => new Observer(this, log, request, context);

        private sealed class Observer(RecordingJpegCodec codec, List<string> log, ImageDecodeRequest request, ImageCodecContext context) : JpegDecodeObserver
        {
            private DecodedFrameSink? _builder;
            private JpegStructureParser? _structure;

            public override void OnSegment(byte marker, ReadOnlySpan<byte> payload) => log.Add(string.Create(CultureInfo.InvariantCulture, $"segment {marker:X2}"));

            public override void OnHeaderComplete(JpegStructureParser structure)
            {
                _structure = structure;
                log.Add($"header {structure.Width}x{structure.Height}");
                _builder = DecodedFrameSink.Create(context, request, ImageFormat.Jpeg, structure.Size, structure.DefaultPixelFormat, PixelFormat.Gray8, structure.Metadata.IccProfile);
                _builder.BeginFrame(FrameDuration.Zero);
            }

            public override void OnEntropyData(ReadOnlySpan<byte> data) => codec.EntropyData.Write(data);

            public override bool OnScanEnd()
            {
                log.Add("scan end");
                return true;
            }

            public override void OnEnd(JpegStructureParser structure)
            {
                log.Add("end of file");
                _builder!.EndImage();
            }

            public override Image GetResult() => _builder!.Build(_structure!.Metadata, animation: null);

            protected override void Dispose(bool disposing)
            {
                _builder?.Dispose();
                base.Dispose(disposing);
            }
        }
    }

    private static void FillCurrentFrame(DecodedFrameSink builder, Size size, byte value)
    {
        var row = new byte[size.Width * PixelFormats.GetBytesPerPixel(builder.SourcePixelFormat)];
        row.AsSpan().Fill(value);
        if (PixelFormats.HasAlpha(builder.SourcePixelFormat))
        {
            for (var i = 3; i < row.Length; i += 4)
            {
                row[i] = 0xFF;
            }
        }

        using var lease = builder.LeaseCurrentFrame();
        for (var y = 0; y < size.Height; y++)
        {
            builder.WriteRow(lease, y, row);
        }
    }
}
