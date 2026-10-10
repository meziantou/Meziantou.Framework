using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.TestHarness.Streams;
using Meziantou.Framework.Imaging.Tests.Codecs;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// The per-frame decoder hook of the built-in container walkers: with a sequential
/// reader, the PNG/APNG, GIF and JPEG walkers report their header snapshot, yield after the header callback and after each
/// image end without beginning the next image, and resume where they stopped, over 1-byte non-seekable reads. The observers
/// below produce solid frames (one value per image) instead of decoding pixels, written exactly as the real codecs write
/// their decoders: the same observer serves eager loads and readers through <see cref="DecodedFrameSink.Create"/>.
/// </summary>
public sealed class SequentialWalkerTests
{
    [Fact]
    public void ApngWalkerYieldsAfterTheHeaderAndAfterEachImage()
    {
        var data = new SyntheticImages.PngBuilder()
            .Header(3, 2, 8, 6)
            .AnimationControl(2, 5)
            .Text("Title", "poster test")
            .ImageData(3, 2, 6)
            .FrameControl(3, 2)
            .FrameData(3, 2)
            .FrameControl(1, 1, x: 2, y: 1)
            .FrameData(1, 1)
            .Text("After", "frames")
            .End()
            .ToArray();
        var log = new List<string>();
        using var registry = ImageCodecRegistry.Override(new ImageCodecRegistry([new SolidPngCodec(log)]));
        using var stream = new TestInputStream(data) { Seekable = false, MaxBytesPerRead = 1 };
        using var reader = Image.OpenReader<Rgba32>(stream);

        // Opening parses the header only: no image is started
        Assert.Equal(["header"], log);
        Assert.Equal(2, reader.Info.FrameCount);
        Assert.True(reader.Info.HasPosterFrame);
        Assert.Equal(5, reader.Info.Animation?.TotalPlays);
        Assert.Equal(["Title"], reader.Info.Metadata.TextEntries.Select(entry => entry.Keyword));

        using (var poster = reader.ReadPosterFrame())
        {
            Assert.Equal(0x10, poster!.Frames[0][0, 0].R);
            Assert.Equal(["header", "start poster", "end"], log);
        }

        using (var frame = reader.ReadFrame())
        {
            Assert.Equal(0x20, frame!.Frames[0][2, 1].G);
            Assert.Equal(new FrameDuration(1, 10), frame.Frames[0].Metadata.Duration);
            Assert.Equal("end", log[^1]);
        }

        using (var frame = reader.ReadFrame())
        {
            Assert.Equal(0x30, frame!.Frames[0][0, 0].B);

            // Frames carry the image-wide metadata known when they are returned
            Assert.Equal(["Title"], frame.Metadata.TextEntries.Select(entry => entry.Keyword));
        }

        Assert.Equal(["header", "start poster", "end", "start frame", "end", "start frame", "end"], log);
        Assert.Null(reader.ReadFrame());
        Assert.Equal("end of file", log[^1]);
        Assert.Equal(2, reader.FramesRead);
    }

    [Fact]
    public void StaticPngFrameCanCarryTrailingMetadata()
    {
        // A still-image decoder may end its only frame at the end of the container, after the trailing metadata
        var data = new SyntheticImages.PngBuilder().Header(2, 2, 8, 6).Text("Before", "a").ImageData(2, 2, 6).Text("After", "b").End().ToArray();
        var log = new List<string>();
        using var registry = ImageCodecRegistry.Override(new ImageCodecRegistry([new SolidPngCodec(log)]));
        using var reader = Image.OpenReader<Rgba32>(new TestInputStream(data) { Seekable = false, MaxBytesPerRead = 3 });
        Assert.Equal(1, reader.Info.FrameCount);
        Assert.False(reader.Info.HasPosterFrame);
        Assert.Null(reader.ReadPosterFrame());
        using (var frame = reader.ReadFrame())
        {
            Assert.Equal(["Before", "After"], frame!.Metadata.TextEntries.Select(entry => entry.Keyword));
        }

        Assert.Null(reader.ReadFrame());

        // The same observer serves eager loads
        using var image = Image.Load(data);
        Assert.Equal(["Before", "After"], image.Metadata.TextEntries.Select(entry => entry.Keyword));
    }

    [Fact]
    public void ApngDefectsAreErrorsAndFrameLimitsAreClean()
    {
        var valid = SyntheticImages.Apng(2, 2, frames: 3);
        var truncated = valid[..^30];
        using var registry = ImageCodecRegistry.Override(new ImageCodecRegistry([new SolidPngCodec([])]));

        using (var reader = Image.OpenReader<Rgba32>(new TestInputStream(truncated) { Seekable = false, MaxBytesPerRead = 1 }))
        {
            using var first = reader.ReadFrame();
            using var second = reader.ReadFrame();
            Assert.Throws<InvalidImageContentException>(() => reader.ReadFrame());
            Assert.Throws<InvalidOperationException>(() => reader.ReadFrame());
        }

        using (var reader = Image.OpenReader<Rgba32>(new TestInputStream(truncated), new ImageReaderOptions { FrameLimit = 2 }))
        {
            using var first = reader.ReadFrame();
            using var second = reader.ReadFrame();
            Assert.Null(reader.ReadFrame());
        }

        // A frame count different from acTL is detected at IEND: never a clean end
        var mismatch = new SyntheticImages.PngBuilder().Header(2, 2).AnimationControl(3, 0).FrameControl(2, 2).ImageData(2, 2).FrameControl(2, 2).FrameData(2, 2).End().ToArray();
        using (var reader = Image.OpenReader<Rgba32>(new MemoryStream(mismatch)))
        {
            using var first = reader.ReadFrame();
            using var second = reader.ReadFrame();
            Assert.Throws<InvalidImageContentException>(() => reader.ReadFrame());
        }
    }

    [Fact]
    public async Task GifWalkerYieldsAfterEachImage()
    {
        var data = new SyntheticImages.GifBuilder().Header(4, 3).Loop(2).GraphicControl(disposal: 1, delay: 7).Image(0, 0, 4, 3).Comment("between").GraphicControl(disposal: 1, delay: 3).Image(1, 1, 2, 2).GraphicControl(disposal: 1, delay: 5).Image(0, 0, 4, 3).Trailer().ToArray();
        var log = new List<string>();
        using var registry = ImageCodecRegistry.Override(new ImageCodecRegistry([new SolidGifCodec(log)]));
        using var stream = new TestInputStream(data) { Seekable = false, MaxBytesPerRead = 1, ForbidSynchronousReads = true };
        await using var reader = await Image.OpenReaderAsync<Rgba32>(stream, cancellationToken: XunitCancellationToken);
        Assert.Equal(["header"], log);
        Assert.Null(reader.Info.FrameCount);
        Assert.Equal(3, reader.Info.Animation?.TotalPlays);
        Assert.Null(await reader.ReadPosterFrameAsync(XunitCancellationToken));
        var durations = new List<FrameDuration>();
        while (await reader.ReadFrameAsync(XunitCancellationToken) is { } frame)
        {
            using (frame)
            {
                Assert.Equal("end", log[^1]);
                Assert.Equal((durations.Count + 1) * 0x10, frame.Frames[0][0, 0].R);
                durations.Add(frame.Frames[0].Metadata.Duration);
            }
        }

        Assert.Equal([new FrameDuration(7, 100), new FrameDuration(3, 100), new FrameDuration(5, 100)], durations);
        Assert.Equal("end of file", log[^1]);
    }

    [Fact]
    public void GifFrameLimitDoesNotExamineTheRest()
    {
        var data = SyntheticImages.Gif(2, 2, images: 3, loop: 0);
        var truncated = data[..^3];
        using var registry = ImageCodecRegistry.Override(new ImageCodecRegistry([new SolidGifCodec([])]));
        using (var reader = Image.OpenReader<Rgba32>(new MemoryStream(truncated), new ImageReaderOptions { FrameLimit = 1 }))
        {
            using var frame = reader.ReadFrame();
            Assert.NotNull(frame);
            Assert.Null(reader.ReadFrame());
        }

        using (var reader = Image.OpenReader<Rgba32>(new MemoryStream(truncated)))
        {
            using var first = reader.ReadFrame();
            using var second = reader.ReadFrame();
            Assert.Throws<InvalidImageContentException>(() => reader.ReadFrame());
        }
    }

    [Fact]
    public void JpegFrameMayBeginInTheHeaderCallback()
    {
        var data = SyntheticImages.Jpeg(0xC0, 1);
        var log = new List<string>();
        using var registry = ImageCodecRegistry.Override(new ImageCodecRegistry([new SolidJpegCodec(log)]));
        using var reader = Image.OpenReader<Gray8>(new TestInputStream(data) { Seekable = false, MaxBytesPerRead = 1 });
        Assert.Equal("header", log[^1]);
        Assert.Equal(1, reader.Info.FrameCount);
        using (var frame = reader.ReadFrame())
        {
            Assert.Equal(0x10, frame!.Frames[0][0, 0].Value);
        }

        Assert.Equal("end of file", log[^1]);
        Assert.Null(reader.ReadFrame());

        // The same observer serves eager loads
        using var image = Image.Load(data);
        Assert.IsType<Image<Gray8>>(image);
    }

    private static void Fill(DecodedFrameSink sink, Size size, byte value)
    {
        var row = new byte[size.Width * PixelFormats.GetBytesPerPixel(sink.SourcePixelFormat)];
        row.AsSpan().Fill(value);
        if (PixelFormats.HasAlpha(sink.SourcePixelFormat))
        {
            for (var i = 3; i < row.Length; i += 4)
            {
                row[i] = 0xFF;
            }
        }

        using var lease = sink.LeaseCurrentFrame();
        for (var y = 0; y < size.Height; y++)
        {
            sink.WriteRow(lease, y, row);
        }
    }

    private sealed class SolidPngCodec(List<string> log) : PngCodec
    {
        protected override PngDecodeObserver CreateDecodeObserver(ImageDecodeRequest request, ImageCodecContext context) => new Observer(log, request, context);

        private sealed class Observer(List<string> log, ImageDecodeRequest request, ImageCodecContext context) : PngDecodeObserver
        {
            private DecodedFrameSink? _sink;
            private PngStructureParser? _structure;
            private int _images;

            public override void OnHeaderComplete(PngStructureParser structure)
            {
                _structure = structure;
                log.Add("header");
                _sink = DecodedFrameSink.Create(context, request, ImageFormat.Png, structure.Size, structure.DefaultPixelFormat, PixelFormat.Rgba32, structure.Metadata.IccProfile);
            }

            public override void OnImageStart(PngFrameControl? control)
            {
                if (control is null && _structure!.HasSeparatePoster)
                {
                    log.Add("start poster");
                    _sink!.BeginPoster();
                }
                else
                {
                    log.Add("start frame");
                    _sink!.BeginFrame(control?.Duration ?? FrameDuration.Zero);
                }
            }

            public override bool OnImageEnd()
            {
                log.Add("end");
                Fill(_sink!, _structure!.Size, (byte)(++_images * 0x10));

                // A still image ends at IEND, so that it carries the metadata that follows its image data
                return !_structure.IsAnimated || _sink!.EndImage();
            }

            public override void OnEnd(PngStructureParser structure)
            {
                log.Add("end of file");
                if (!structure.IsAnimated)
                {
                    _sink!.UpdateMetadata(structure.Metadata);
                    _sink.EndImage();
                }
            }

            public override Image GetResult() => _sink!.Build(_structure!.Metadata, _structure.Animation);

            protected override void Dispose(bool disposing)
            {
                _sink?.Dispose();
                base.Dispose(disposing);
            }
        }
    }

    private sealed class SolidGifCodec(List<string> log) : GifCodec
    {
        protected override GifDecodeObserver CreateDecodeObserver(ImageDecodeRequest request, ImageCodecContext context) => new Observer(log, request, context);

        private sealed class Observer(List<string> log, ImageDecodeRequest request, ImageCodecContext context) : GifDecodeObserver
        {
            private DecodedFrameSink? _sink;
            private GifStructureParser? _structure;
            private int _images;

            public override void OnHeaderComplete(GifStructureParser structure)
            {
                _structure = structure;
                log.Add("header");
                _sink = DecodedFrameSink.Create(context, request, ImageFormat.Gif, structure.Size, DefaultPixelFormats.Gif, PixelFormat.Rgba32, iccProfile: null);
            }

            public override void OnImageStart(GifImageDescriptor descriptor, ReadOnlySpan<byte> localColorTable, GifGraphicControl? control, int lzwMinimumCodeSize)
            {
                log.Add("image");
                _sink!.BeginFrame(control?.Duration ?? FrameDuration.Zero);
            }

            public override bool OnImageEnd()
            {
                log.Add("end");
                Fill(_sink!, _structure!.Size, (byte)(++_images * 0x10));
                return _sink!.EndImage();
            }

            public override void OnEnd(GifStructureParser structure) => log.Add("end of file");

            public override Image GetResult() => _sink!.Build(_structure!.Metadata, _structure.Animation);

            protected override void Dispose(bool disposing)
            {
                _sink?.Dispose();
                base.Dispose(disposing);
            }
        }
    }

    private sealed class SolidJpegCodec(List<string> log) : JpegCodec
    {
        protected override JpegDecodeObserver CreateDecodeObserver(ImageDecodeRequest request, ImageCodecContext context) => new Observer(log, request, context);

        private sealed class Observer(List<string> log, ImageDecodeRequest request, ImageCodecContext context) : JpegDecodeObserver
        {
            private DecodedFrameSink? _sink;
            private JpegStructureParser? _structure;

            public override void OnHeaderComplete(JpegStructureParser structure)
            {
                _structure = structure;
                log.Add("header");
                _sink = DecodedFrameSink.Create(context, request, ImageFormat.Jpeg, structure.Size, structure.DefaultPixelFormat, PixelFormat.Gray8, structure.Metadata.IccProfile);

                // Begun during OpenReader: the pixel target is bound only once the reader requests a frame
                _sink.BeginFrame(FrameDuration.Zero);
            }

            public override bool OnScanEnd()
            {
                log.Add("scan end");
                return true;
            }

            public override void OnEnd(JpegStructureParser structure)
            {
                log.Add("end of file");
                Fill(_sink!, structure.Size, 0x10);
                _sink!.UpdateMetadata(structure.Metadata);
                _sink.EndImage();
            }

            public override Image GetResult() => _sink!.Build(_structure!.Metadata, animation: null);

            protected override void Dispose(bool disposing)
            {
                _sink?.Dispose();
                base.Dispose(disposing);
            }
        }
    }
}
