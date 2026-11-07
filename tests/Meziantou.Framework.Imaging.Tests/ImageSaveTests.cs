using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Streams;
using Meziantou.Framework.Imaging.Tests.Codecs;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// Eager saves implemented on top of the writer, with test encoders registered for the
/// built-in output formats: every frame and the poster are written in one pass, static outputs reject animated images
/// instead of saving frame zero, and path saves infer the encoder from the extension and publish atomically.
/// </summary>
public sealed class ImageSaveTests
{
    private const int Width = 4;
    private const int Height = 3;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveWritesEveryFrameAndThePosterAndLeavesTheStreamOpen(bool asynchronous)
    {
        using var codecs = TestCodecs.UseAll(out _, out var png, out _, out _);
        using var image = StreamImages.CreateAnimation(Width, Height, 3, poster: true, totalPlays: 4);
        using var stream = new TestOutputStream { ForbidSynchronousWrites = asynchronous, ForbidAsynchronousWrites = !asynchronous };
        if (asynchronous)
        {
            await image.SaveAsync(stream, new PngEncoder(), XunitCancellationToken);
        }
        else
        {
            image.Save(stream, new PngEncoder());
        }

        Assert.False(stream.IsDisposed);
        Assert.Equal(["poster", "frame 0", "frame 1", "frame 2", "complete 3"], png.LastSession.Log);
        Assert.True(png.LastSession.Options.Capabilities.IsAnimated); // Auto: the image is animated
        Assert.Equal(3, png.LastSession.Options.ExpectedFrameCount);
        using var decoded = StreamImages.Decode(stream.ToArray());
        Assert.Equal(3, decoded.Frames.Count);
        Assert.Equal(4, decoded.Animation?.TotalPlays);
        Assert.Equal("saved", Assert.Single(decoded.Metadata.TextEntries).Value);
        Assert.Equal(StreamImages.GetBytes(image.PosterFrame!), StreamImages.GetBytes(decoded.PosterFrame!));
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(StreamImages.GetBytes(image.Frames[i]), StreamImages.GetBytes(decoded.Frames[i]));
            Assert.Equal(image.Frames[i].Metadata.Duration, decoded.Frames[i].Metadata.Duration);
        }
    }

    [Fact]
    public void FloatingPointImagesAreSavedOnlyWithAReduction()
    {
        using var image = new Image<RgbaFloat>(3, 2, new RgbaFloat(0.25f, 0.5f, 1, 0.75f));
        image.Frames[0][2, 1] = new RgbaFloat(1 / 3f, 0, 0.125f);

        // Nothing is written: PNG does not store floating-point samples (only TIFF does)
        using (var rejected = new MemoryStream())
        {
            var exception = Assert.Throws<UnsupportedImageFeatureException>(() => image.Save(rejected, new PngEncoder()));
            Assert.Contains("FloatingPointConversion", exception.Message);
            Assert.Equal(0, rejected.Length);
        }

        // Reduced to 16 bits, then encoded like Rgba64 pixels; the linear label follows the metadata policy
        var encoder = new PngEncoder { FloatingPointConversion = new PixelConversionOptions(), MetadataHandling = MetadataHandling.DiscardUnsupported };
        using var stream = new MemoryStream();
        image.Save(stream, encoder);
        using var decoded = Image.Load<Rgba64>(stream.ToArray());
        using var expected = image.CloneAs<Rgba64>();
        Assert.Equal(expected.Frames[0][0, 0], decoded.Frames[0][0, 0]);
        Assert.Equal(expected.Frames[0][2, 1], decoded.Frames[0][2, 1]);
        Assert.Equal(new Rgba64(16384, 32768, 65535, 49151), decoded.Frames[0][0, 0]);

        var strict = new PngEncoder { FloatingPointConversion = new PixelConversionOptions() };
        Assert.Throws<UnsupportedImageFeatureException>(() => image.Save(new MemoryStream(), strict));

        // Samples out of range need the policy of the encoder
        image.Frames[0][1, 0] = new RgbaFloat(2, 0, 0);
        using (var rejected = new MemoryStream())
        {
            Assert.Throws<UnsupportedImageFeatureException>(() => image.Save(rejected, encoder));
            Assert.Equal(0, rejected.Length);
        }

        using var clipped = new MemoryStream();
        image.Save(clipped, new PngEncoder { FloatingPointConversion = new PixelConversionOptions { OutOfRangeSamples = OutOfRangeSampleHandling.Clip }, MetadataHandling = MetadataHandling.DiscardUnsupported });
        using var decodedClip = Image.Load<Rgba64>(clipped.ToArray());
        Assert.Equal(new Rgba64(65535, 0, 0), decodedClip.Frames[0][1, 0]);
    }

    [Fact]
    public async Task FilesLoadIntoFloatingPointPixelsWithTheLabelOfTheFile()
    {
        using var source = new Image<Rgba64>(3, 2, new Rgba64(1, 32768, 65535, 40000));
        source.AppendFrame(source.Frames[0]).Metadata.Duration = new FrameDuration(1, 10);
        source.Frames[1][2, 1] = new Rgba64(65535, 0, 0);
        using var stream = new MemoryStream();
        source.Save(stream, new PngEncoder { AnimationMode = PngAnimationMode.Animated });
        var data = stream.ToArray();

        // Eager loads convert the decoded samples exactly: v / 65535
        using var floats = Image.Load<RgbaFloat>(data);
        using var expected = source.CloneAs<RgbaFloat>();
        Assert.Equal(2, floats.Frames.Count);
        Assert.Equal(expected.Frames[0][0, 0], floats.Frames[0][0, 0]);
        Assert.Equal(new RgbaFloat(1, 0, 0), floats.Frames[1][2, 1]);
        Assert.Equal(Metadata.ColorTransferFunction.Srgb, floats.Metadata.TransferFunction);
        Assert.Equal(PixelFormat.Rgba64, Image.Identify(data).PixelFormat);

        // So do sequential readers
        using var reader = Image.OpenReader<RgbaHalf>(new MemoryStream(data));
        using var first = await reader.ReadFrameAsync(XunitCancellationToken);
        using var second = reader.ReadFrame();
        Assert.Equal(new RgbaHalf((Half)(1 / 65535.0), (Half)(32768 / 65535.0), Half.One, (Half)(40000 / 65535.0)), first!.Frames[0][0, 0]);
        Assert.Equal(new RgbaHalf(Half.One, Half.Zero, Half.Zero), second!.Frames[0][2, 1]);
    }

    [Fact]
    public async Task SequentialWritersReduceEachFloatingPointFrame()
    {
        var options = new ImageWriterOptions(2, 1)
        {
            Encoder = new PngEncoder { AnimationMode = PngAnimationMode.Animated, FloatingPointConversion = new PixelConversionOptions { ToneMapping = new HdrToneMappingOptions { Operator = HdrToneMappingOperator.Clip } } },
            ExpectedFrameCount = 2,
            Animation = new AnimationMetadata(),
        };

        using var first = new Image<RgbaHalf>(2, 1, new RgbaHalf((Half)0.5, Half.Zero, Half.Zero));
        using var second = new Image<RgbaHalf>(2, 1, new RgbaHalf((Half)8, (Half)4, Half.Zero));
        using var stream = new MemoryStream();
        await using (var writer = Image.CreateWriter<RgbaHalf>(stream, options))
        {
            await writer.WriteFrameAsync(first.Frames[0], XunitCancellationToken);
            writer.WriteFrame(second.Frames[0]);
            await writer.CompleteAsync(XunitCancellationToken);
        }

        // Each frame has its own peak: 1 for the first one, 8 for the second one
        using var decoded = Image.Load<Rgba64>(stream.ToArray());
        Assert.Equal(new Rgba64(32768, 0, 0), decoded.Frames[0][0, 0]);
        Assert.Equal(new Rgba64(65535, 32768, 0), decoded.Frames[1][1, 0]);
    }

    [Fact]
    public void StaticOutputsRejectAnimatedImagesInsteadOfSavingFrameZero()
    {
        using var codecs = TestCodecs.UseAll(out _, out var png, out var gif, out var jpeg);
        using var animated = StreamImages.CreateAnimation(Width, Height, 2);
        using var withPoster = StreamImages.CreateAnimation(Width, Height, 1, poster: true);
        using var singleFrameAnimation = StreamImages.CreateAnimation(Width, Height, 1, totalPlays: 2);
        using var still = StreamImages.CreateAnimation(Width, Height, 1);

        // ThrowingStream fails the test on any I/O
        Assert.Equal("Animation", Assert.Throws<UnsupportedImageFeatureException>(() => animated.Save(new ThrowingStream(), new PngEncoder { AnimationMode = PngAnimationMode.Static })).Feature);
        Assert.Throws<UnsupportedImageFeatureException>(() => animated.Save(new ThrowingStream(), new JpegEncoder()));
        Assert.Throws<UnsupportedImageFeatureException>(() => singleFrameAnimation.Save(new ThrowingStream(), new JpegEncoder()));
        Assert.Throws<UnsupportedImageFeatureException>(() => withPoster.Save(new ThrowingStream(), new JpegEncoder()));
        Assert.Equal("Poster frame", Assert.Throws<UnsupportedImageFeatureException>(() => withPoster.Save(new ThrowingStream(), new GifEncoder())).Feature);
        Assert.Empty(png.Sessions);
        Assert.Empty(gif.Sessions);
        Assert.Empty(jpeg.Sessions);

        // Explicit choices: remove the animation, extract a frame, or select an animated output
        using var output = new TestOutputStream();
        singleFrameAnimation.Animation = null;
        singleFrameAnimation.Save(output, new JpegEncoder());
        using (var first = animated.CloneFrame(0))
        {
            first.Save(output, new PngEncoder { AnimationMode = PngAnimationMode.Static });
        }

        animated.Save(output, new GifEncoder());
        Assert.True(gif.LastSession.Options.Capabilities.IsAnimated);
        still.Save(output, new GifEncoder());
        Assert.False(gif.LastSession.Options.Capabilities.IsAnimated);
        still.Save(output, new PngEncoder());
        Assert.False(png.LastSession.Options.Capabilities.IsAnimated);
        still.Save(output, new PngEncoder { AnimationMode = PngAnimationMode.Animated });
        Assert.True(png.LastSession.Options.Capabilities.IsAnimated);
        Assert.NotNull(png.LastSession.Options.Animation);
    }

    [Fact]
    public void MetadataPolicyIsAppliedBeforeAnyOutput()
    {
        using var codecs = TestCodecs.UseAll(out _, out _, out var gif, out _);
        using var image = StreamImages.CreateAnimation(Width, Height, 1);
        image.Metadata.Orientation = ExifOrientation.RightTop;
        Assert.Throws<UnsupportedImageFeatureException>(() => image.Save(new ThrowingStream(), new GifEncoder()));
        Assert.Empty(gif.Sessions);
        using var output = new TestOutputStream();
        image.Save(output, new GifEncoder { MetadataHandling = MetadataHandling.DiscardUnsupported });
        Assert.Single(gif.Sessions);
    }

    [Theory]
    [InlineData("image.png", ImageFormat.Png, false)]
    [InlineData("image.PNG", ImageFormat.Png, false)]
    [InlineData("image.apng", ImageFormat.Png, true)]
    [InlineData("image.gif", ImageFormat.Gif, false)]
    [InlineData("image.jpg", ImageFormat.Jpeg, false)]
    [InlineData("image.jpeg", ImageFormat.Jpeg, false)]
    public async Task SaveToPathInfersTheEncoderFromTheExtension(string name, ImageFormat format, bool animated)
    {
        using var codecs = TestCodecs.UseAll(out _, out var png, out var gif, out var jpeg);
        var directory = TestFiles.CreateDirectory();
        try
        {
            using var image = StreamImages.CreateAnimation(Width, Height, 1);
            var path = directory / name;
            image.Save(path);
            var session = format switch { ImageFormat.Png => png.LastSession, ImageFormat.Gif => gif.LastSession, _ => jpeg.LastSession };
            Assert.Equal(animated, session.Options.Capabilities.IsAnimated);
            using (var decoded = StreamImages.Decode(await File.ReadAllBytesAsync(path, XunitCancellationToken)))
            {
                Assert.Equal(format, decoded.Metadata.SourceFormat);
            }

            // An explicit encoder overrides the extension
            await image.SaveAsync(path, new GifEncoder(), XunitCancellationToken);
            using (var decoded = StreamImages.Decode(await File.ReadAllBytesAsync(path, XunitCancellationToken)))
            {
                Assert.Equal(ImageFormat.Gif, decoded.Metadata.SourceFormat);
            }

            Assert.Equal([path], Directory.GetFiles(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task UnknownExtensionsRequireAnExplicitEncoder()
    {
        using var codecs = TestCodecs.UseAll(out _, out _, out _, out _);
        var directory = TestFiles.CreateDirectory();
        try
        {
            using var image = StreamImages.CreateAnimation(Width, Height, 1);
            Assert.Throws<ArgumentException>(() => image.Save(directory / "image.xyz"));
            Task? task = null;
            Assert.IsType<ArgumentException>(Record.Exception(() => { task = image.SaveAsync(directory / "image", cancellationToken: XunitCancellationToken); }));
            Assert.Null(task);
            Assert.Empty(Directory.GetFileSystemEntries(directory));
            await image.SaveAsync(directory / "image.xyz", new PngEncoder(), XunitCancellationToken);
            Assert.Single(Directory.GetFiles(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task FailedPathSavesPreserveTheExistingFile(bool asynchronous, bool cancel)
    {
        using var codecs = TestCodecs.UseAll(out _, out _, out var gif, out _);
        var directory = TestFiles.CreateDirectory();
        try
        {
            var path = directory / "animation.gif";
            await File.WriteAllTextAsync(path, "previous content", XunitCancellationToken);
            using var image = StreamImages.CreateAnimation(Width, Height, 3);
            using var cancellation = new CancellationTokenSource();
            if (cancel)
            {
                gif.BeforeStep = session =>
                {
                    if (session.Log.Count == 2)
                    {
                        cancellation.Cancel();
                    }
                };
            }
            else
            {
                gif.FailAt = (2, 1);
            }

            if (!asynchronous)
            {
                Assert.Throws<InjectedEncoderException>(() => image.Save(path));
            }
            else if (cancel)
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => image.SaveAsync(path, cancellationToken: cancellation.Token));
            }
            else
            {
                await Assert.ThrowsAsync<InjectedEncoderException>(() => image.SaveAsync(path, cancellationToken: XunitCancellationToken));
            }

            Assert.Equal("previous content", await File.ReadAllTextAsync(path, XunitCancellationToken));
            Assert.Equal([path], Directory.GetFiles(directory));

            // The image is unchanged and can be saved again
            gif.FailAt = null;
            gif.BeforeStep = null;
            image.Save(path);
            using var decoded = StreamImages.Decode(await File.ReadAllBytesAsync(path, XunitCancellationToken));
            Assert.Equal(3, decoded.Frames.Count);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task PreCanceledSavesCreateNoOutput()
    {
        using var codecs = TestCodecs.UseAll(out _, out var png, out _, out _);
        var directory = TestFiles.CreateDirectory();
        try
        {
            using var image = StreamImages.CreateAnimation(Width, Height, 1);
            using var canceled = new CancellationTokenSource();
            await canceled.CancelAsync();
            Task? task = null;
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task = image.SaveAsync(directory / "a.png", cancellationToken: canceled.Token));
            Assert.True(task!.IsCanceled);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => image.SaveAsync(new ThrowingStream(), new PngEncoder(), canceled.Token));
            Assert.Empty(Directory.GetFileSystemEntries(directory));
            Assert.Empty(png.Sessions);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("image.avif")]
    [InlineData("IMAGE.AVIF")]
    [InlineData("sequence.avifs")]
    public async Task AvifExtensionsOnlySelectTheDefaultEncoder(string name)
    {
        using var codecs = TestCodecs.UseAll(out _, out _, out _, out _);
        var directory = TestFiles.CreateDirectory();
        try
        {
            using var image = StreamImages.CreateAnimation(Width, Height, 1);
            var path = directory / name;
            Assert.IsType<AvifEncoder>(ImageEncoder.FromPath(path));

            // The extension only selects the default encoder: an explicit encoder writes its own format under that name
            await image.SaveAsync(path, new PngEncoder(), XunitCancellationToken);
            using var decoded = StreamImages.Decode(await File.ReadAllBytesAsync(path, XunitCancellationToken));
            Assert.Equal(ImageFormat.Png, decoded.Metadata.SourceFormat);
            Assert.Equal([path], Directory.GetFiles(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("image.heic")]
    [InlineData("IMAGE.HEIC")]
    [InlineData("image.heif")]
    [InlineData("sequence.heics")]
    [InlineData("sequence.heifs")]
    public async Task HeicExtensionsOnlySelectTheDefaultEncoder(string name)
    {
        using var codecs = TestCodecs.UseAll(out _, out _, out _, out _);
        var directory = TestFiles.CreateDirectory();
        try
        {
            using var image = StreamImages.CreateAnimation(Width, Height, 1);
            var path = directory / name;
            Assert.IsType<HeicEncoder>(ImageEncoder.FromPath(path));

            // The extension only selects the default encoder: an explicit encoder writes its own format under that name
            await image.SaveAsync(path, new PngEncoder(), XunitCancellationToken);
            using var decoded = StreamImages.Decode(await File.ReadAllBytesAsync(path, XunitCancellationToken));
            Assert.Equal(ImageFormat.Png, decoded.Metadata.SourceFormat);
            Assert.Equal([path], Directory.GetFiles(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void EveryBuiltInFormatCanBeSaved()
    {
        // Static PNG, APNG, GIF and JPEG all have their encoder
        using var animated = StreamImages.CreateAnimation(Width, Height, 2);
        using var output = new TestOutputStream();
        animated.Save(output, new GifEncoder());
        Assert.True(output.ToArray().AsSpan().StartsWith("GIF89a"u8));
        using var decoded = Image.Load(output.ToArray());
        Assert.Equal(2, decoded.Frames.Count);
    }

    public static TheoryData<ImageEncoder, PixelFormat> EncodersAndCompactFormats()
    {
        var data = new TheoryData<ImageEncoder, PixelFormat>();
        foreach (var encoder in CreateEncodersWithoutHotspot())
        {
            foreach (var format in (PixelFormat[])[PixelFormat.La16, PixelFormat.La32, PixelFormat.Rgb48, PixelFormat.A8])
            {
                data.Add(encoder, format);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EncodersAndCompactFormats))]
    public void EveryEncoderAcceptsTheCompactFormats(ImageEncoder encoder, PixelFormat format)
    {
        // Gray levels, opaque or not, so that every format stores them exactly; an alpha mask is black
        using var image = DrawingTestSupport.Create(format, Width, Height);
        image.Frames[0].ProcessPixelBytes(static pixels =>
        {
            for (var y = 0; y < pixels.Height; y++)
            {
                var row = pixels.GetRowSpan(y);
                for (var i = 0; i < row.Length; i++)
                {
                    row[i] = (byte)((y * 70) + (i * 13));
                }
            }
        });

        if (encoder is OpenExrEncoder)
        {
            // OpenEXR stores linear light only
            image.Metadata.TransferFunction = ColorTransferFunction.Linear;
        }

        if (encoder is PngEncoder or TiffEncoder)
        {
            // Stored as they are (an alpha mask as gray with alpha), and read back so
            using var stream = new MemoryStream();
            image.Save(stream, encoder);
            stream.Position = 0;
            using var reloaded = Image.Load(stream);
            var stored = format == PixelFormat.A8 ? PixelFormat.La16 : format;
            Assert.Equal(encoder is PngEncoder { AnimationMode: PngAnimationMode.Animated } ? PixelFormatWidening.GetWidenedFormat(stored) : stored, reloaded.PixelFormat);
            using var expected = image.CloneAs<Rgba64>();
            using var actual = reloaded.CloneAs<Rgba64>();
            Assert.Equal(StreamImages.GetBytes(expected.Frames[0]), StreamImages.GetBytes(actual.Frames[0]));
            return;
        }

        // The other encoders get the pixels widened without any loss: the output is the one of the widened image, or the
        // same exception before anything is written
        using var widened = image.CloneAs(PixelFormatWidening.GetWidenedFormat(format), options: null);
        using var widenedStream = new MemoryStream();
        using var compactStream = new MemoryStream();
        var widenedException = Record.Exception(() => widened.Save(widenedStream, encoder));
        var compactException = Record.Exception(() => image.Save(compactStream, encoder));
        Assert.Equal(widenedException?.GetType(), compactException?.GetType());
        Assert.Equal(widenedStream.ToArray(), compactStream.ToArray());
    }

    public static TheoryData<ImageEncoder, PixelFormat> EncodersAndInteropFormats()
    {
        var data = new TheoryData<ImageEncoder, PixelFormat>();
        foreach (var encoder in CreateEncodersWithoutHotspot())
        {
            foreach (var format in (PixelFormat[])[PixelFormat.Argb32, PixelFormat.Abgr32, PixelFormat.Bgr565, PixelFormat.Rgba1010102])
            {
                data.Add(encoder, format);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EncodersAndInteropFormats))]
    public void EveryEncoderWritesTheInteropFormatsAsTheirWidening(ImageEncoder encoder, PixelFormat format)
    {
        // Every bit pattern is a pixel of these formats
        using var image = DrawingTestSupport.Create(format, Width, Height);
        image.Frames[0].ProcessPixelBytes(static pixels =>
        {
            for (var y = 0; y < pixels.Height; y++)
            {
                var row = pixels.GetRowSpan(y);
                for (var i = 0; i < row.Length; i++)
                {
                    row[i] = (byte)((y * 70) + (i * 13));
                }
            }
        });

        if (encoder is OpenExrEncoder)
        {
            // OpenEXR stores linear light only
            image.Metadata.TransferFunction = ColorTransferFunction.Linear;
        }

        // No encoder stores them: the output is the one of the image widened without any loss, or the same exception
        // before anything is written
        Assert.False(encoder.StoresPixelFormat(format));
        using var widened = image.CloneAs(PixelFormatWidening.GetWidenedFormat(format), options: null);
        using var widenedStream = new MemoryStream();
        using var interopStream = new MemoryStream();
        var widenedException = Record.Exception(() => widened.Save(widenedStream, encoder));
        var interopException = Record.Exception(() => image.Save(interopStream, encoder));
        Assert.Equal(widenedException?.GetType(), interopException?.GetType());
        Assert.Equal(widenedStream.ToArray(), interopStream.ToArray());
    }

    public static TheoryData<ImageEncoder> EncodersWithoutHotspot() => [.. CreateEncodersWithoutHotspot()];

    private static ImageEncoder[] CreateEncodersWithoutHotspot() =>
    [
        new PngEncoder(),
        new PngEncoder { AnimationMode = PngAnimationMode.Animated },
        new GifEncoder(),
        new JpegEncoder(),
        new WebPEncoder(),
        new QoiEncoder(),
        new BmpEncoder(),
        new TgaEncoder(),
        new PnmEncoder(),
        new TiffEncoder(),
        new IcoEncoder(),
        new AvifEncoder(),
        new HeicEncoder(),
        new OpenExrEncoder { MetadataHandling = MetadataHandling.Strict },
    ];

    [Theory]
    [MemberData(nameof(EncodersWithoutHotspot))]
    public void OnlyCursorOutputsStoreAHotspotAndTheOthersFollowTheMetadataPolicy(ImageEncoder encoder)
    {
        Rgb24[] pixels = [new Rgb24(1, 2, 3), new Rgb24(4, 5, 6), new Rgb24(7, 8, 9), new Rgb24(10, 11, 12)];
        using var image = Image.ImportPixelData<Rgb24>(pixels, 2, 2);
        image.Frames[0].Metadata.Hotspot = new Point(1, 1);
        if (encoder is OpenExrEncoder)
        {
            // OpenEXR stores linear light only
            image.Metadata.TransferFunction = ColorTransferFunction.Linear;
        }

        using var stream = new MemoryStream();
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => image.Save(stream, encoder));
        Assert.Equal(encoder.Format, exception.Format);
        Assert.Equal("Metadata: cursor hotspot", exception.Feature);
        Assert.Equal(0, stream.Length);

        foreach (var handling in (MetadataHandling[])[MetadataHandling.DiscardUnsupported, MetadataHandling.Strip])
        {
            using var discarded = new MemoryStream();
            image.Save(discarded, WithHandling(encoder, handling));
            discarded.Position = 0;
            using var reloaded = Image.Load(discarded);
            Assert.Null(reloaded.Frames[0].Metadata.Hotspot);
        }

        // Without a hotspot there is nothing to reject
        image.Frames[0].Metadata.Hotspot = null;
        image.Save(stream, encoder);
        Assert.True(stream.Length > 0);

        static ImageEncoder WithHandling(ImageEncoder encoder, MetadataHandling handling) => encoder switch
        {
            PngEncoder png => new PngEncoder { AnimationMode = png.AnimationMode, MetadataHandling = handling },
            GifEncoder => new GifEncoder { MetadataHandling = handling },
            JpegEncoder => new JpegEncoder { MetadataHandling = handling },
            WebPEncoder => new WebPEncoder { MetadataHandling = handling },
            QoiEncoder => new QoiEncoder { MetadataHandling = handling },
            BmpEncoder => new BmpEncoder { MetadataHandling = handling },
            TgaEncoder => new TgaEncoder { MetadataHandling = handling },
            PnmEncoder => new PnmEncoder { MetadataHandling = handling },
            TiffEncoder => new TiffEncoder { MetadataHandling = handling },
            AvifEncoder => new AvifEncoder { MetadataHandling = handling },
            HeicEncoder => new HeicEncoder { MetadataHandling = handling },
            OpenExrEncoder => new OpenExrEncoder { MetadataHandling = handling },
            _ => new IcoEncoder { MetadataHandling = handling },
        };
    }

    [Fact]
    public void AHotspotOnALaterFrameFailsBeforeTheFirstByte()
    {
        using var image = StreamImages.CreateAnimation(Width, Height, 3);
        image.Frames[2].Metadata.Hotspot = new Point(0, 0);
        using var stream = new MemoryStream();
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => image.Save(stream, new GifEncoder()));
        Assert.Equal("Metadata: cursor hotspot", exception.Feature);
        Assert.Equal(0, stream.Length);

        // A sequential writer reports it for the frame that has it, and stays usable
        using var writer = Image.CreateWriter<Rgba32>(stream, new ImageWriterOptions(Width, Height) { Encoder = new GifEncoder(), LeaveOpen = true });
        writer.WriteFrame(image.Frames[0]);
        Assert.Throws<UnsupportedImageFeatureException>(() => writer.WriteFrame(image.Frames[2]));
        writer.WriteFrame(image.Frames[1]);
        writer.Complete();
        stream.Position = 0;
        using var decoded = Image.Load(stream);
        Assert.Equal(2, decoded.Frames.Count);
    }

    [Fact]
    public void SavingFramesLeasedByTheCallerFaultsTheSave()
    {
        using var codecs = TestCodecs.UseAll(out _, out _, out var gif, out _);
        using var image = StreamImages.CreateAnimation(Width, Height, 2);
        using var stream = new TestOutputStream();
        image.Frames[1].ProcessPixelBytes(_ => Assert.Throws<InvalidOperationException>(() => image.Save(stream, new GifEncoder())));
        Assert.Equal(["frame 0", "frame 1"], gif.LastSession.Log);
        Assert.True(gif.LastSession.IsDisposed);
        Assert.False(stream.IsDisposed);
    }

    public static TheoryData<ImageEncoder> FrameEncoders() =>
    [
        new PngEncoder(),
        new PngEncoder { AnimationMode = PngAnimationMode.Animated },
        new JpegEncoder { BackgroundColor = new Rgba64(65535, 65535, 65535) },
        new GifEncoder(),
        // The comment of the test image is metadata these formats cannot store
        new WebPEncoder { MetadataHandling = MetadataHandling.DiscardUnsupported },
        new WebPEncoder { Compression = WebPCompression.Lossy, MetadataHandling = MetadataHandling.DiscardUnsupported },
        new IcoEncoder { Kind = IconKind.Cursor, MetadataHandling = MetadataHandling.DiscardUnsupported },
        new AniEncoder { MetadataHandling = MetadataHandling.DiscardUnsupported },
    ];

    [Theory]
    [MemberData(nameof(FrameEncoders))]
    public async Task FrameSavesWriteTheBytesOfTheExtractedFrame(ImageEncoder encoder)
    {
        // An animation with a poster: the frame is written as a still image, as CloneFrame extracts it
        using var image = StreamImages.CreateAnimation(Width, Height, 3, poster: true, totalPlays: 2);
        var frame = image.Frames[1];
        frame.Metadata.Duration = new FrameDuration(1, 8);
        if (encoder.Format is ImageFormat.Cur or ImageFormat.Ani)
        {
            frame.Metadata.Hotspot = new Point(2, 1);
        }

        byte[] expected;
        using (var clone = image.CloneFrame(1))
        using (var stream = new MemoryStream())
        {
            clone.Save(stream, encoder);
            expected = stream.ToArray();
        }

        using (var stream = new MemoryStream())
        {
            frame.Save(stream, encoder);
            Assert.Equal(expected, stream.ToArray());
        }

        using (var stream = new MemoryStream())
        {
            await frame.SaveAsync(stream, encoder, XunitCancellationToken);
            Assert.Equal(expected, stream.ToArray());
        }

        var directory = TestFiles.CreateDirectory();
        try
        {
            var path = directory / "frame.bin";
            frame.Save(path, encoder);
            Assert.Equal(expected, await File.ReadAllBytesAsync(path, XunitCancellationToken));
            await frame.SaveAsync(path, encoder, XunitCancellationToken);
            Assert.Equal(expected, await File.ReadAllBytesAsync(path, XunitCancellationToken));
            Assert.Equal([path], Directory.GetFiles(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }

        // The image keeps its frames, its poster and its animation settings
        Assert.Equal(3, image.Frames.Count);
        Assert.NotNull(image.PosterFrame);
        Assert.Equal(2, image.Animation?.TotalPlays);
    }

    [Fact]
    public async Task ThePosterFrameIsSavedAsAStillImage()
    {
        using var image = StreamImages.CreateAnimation(Width, Height, 2, poster: true);
        var poster = image.PosterFrame!;
        var directory = TestFiles.CreateDirectory();
        try
        {
            // The encoder is inferred from the extension, as for an image
            using (var clone = image.ClonePosterFrame())
            {
                clone.Save(directory / "expected.png");
            }

            poster.Save(directory / "poster.png");
            await poster.SaveAsync(directory / "poster-async.png", cancellationToken: XunitCancellationToken);
            var expected = await File.ReadAllBytesAsync(directory / "expected.png", XunitCancellationToken);
            Assert.Equal(expected, await File.ReadAllBytesAsync(directory / "poster.png", XunitCancellationToken));
            Assert.Equal(expected, await File.ReadAllBytesAsync(directory / "poster-async.png", XunitCancellationToken));

            using var decoded = Image.Load<Rgba32>(expected);
            Assert.Single(decoded.Frames);
            Assert.Null(decoded.PosterFrame);
            Assert.Null(decoded.Animation);
            Assert.Equal(StreamImages.GetBytes(poster), StreamImages.GetBytes(decoded.Frames[0]));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }

        // The image itself still needs an output that stores its poster
        Assert.Equal("Animation", Assert.Throws<UnsupportedImageFeatureException>(() => image.Save(new ThrowingStream(), new PngEncoder { AnimationMode = PngAnimationMode.Static })).Feature);
    }

    [Fact]
    public async Task FrameSavesRejectWhatTheExtractedFrameCannotStoreBeforeAnyOutput()
    {
        // Alpha to JPEG without a background color
        using var image = StreamImages.CreateAnimation(Width, Height, 2);
        image.Frames[1][0, 0] = new Rgba32(1, 2, 3, 128);
        await AssertRejectedLikeTheExtractedFrame(image, 1, new JpegEncoder());

        // 16-bit samples to JPEG without a bit depth reduction
        using var deep = image.CloneAs<Rgba64>();
        await AssertRejectedLikeTheExtractedFrame(deep, 1, new JpegEncoder { BackgroundColor = new Rgba64(0, 0, 0) });

        // A cursor hotspot that the output cannot store
        image.Frames[1].Metadata.Hotspot = new Point(1, 1);
        await AssertRejectedLikeTheExtractedFrame(image, 1, new PngEncoder());
        image.Frames[1].Metadata.Hotspot = null;

        // The metadata of the image: GIF cannot store the orientation
        image.Metadata.Orientation = ExifOrientation.RightTop;
        await AssertRejectedLikeTheExtractedFrame(image, 0, new GifEncoder());

        // A failed path save publishes nothing and keeps the existing file
        var directory = TestFiles.CreateDirectory();
        try
        {
            var path = directory / "frame.gif";
            await File.WriteAllTextAsync(path, "previous content", XunitCancellationToken);
            Assert.Throws<UnsupportedImageFeatureException>(() => image.Frames[0].Save(path));
            await Assert.ThrowsAsync<UnsupportedImageFeatureException>(() => image.Frames[0].SaveAsync(path, cancellationToken: XunitCancellationToken));
            Assert.Equal("previous content", await File.ReadAllTextAsync(path, XunitCancellationToken));
            Assert.Equal([path], Directory.GetFiles(directory));

            // The policy of the encoder allows the loss explicitly
            using var stream = new MemoryStream();
            image.Frames[0].Save(stream, new GifEncoder { MetadataHandling = MetadataHandling.DiscardUnsupported });
            Assert.True(stream.ToArray().AsSpan().StartsWith("GIF89a"u8));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }

        static async Task AssertRejectedLikeTheExtractedFrame(Image image, int index, ImageEncoder encoder)
        {
            UnsupportedImageFeatureException expected;
            using (var clone = image.CloneFrame(index))
            {
                expected = Assert.Throws<UnsupportedImageFeatureException>(() => clone.Save(new ThrowingStream(), encoder));
            }

            // ThrowingStream fails the test on any I/O
            var frame = image.Frames[index];
            var actual = Assert.Throws<UnsupportedImageFeatureException>(() => frame.Save(new ThrowingStream(), encoder));
            var actualAsync = await Assert.ThrowsAsync<UnsupportedImageFeatureException>(() => frame.SaveAsync(new ThrowingStream(), encoder, XunitCancellationToken));
            foreach (var exception in (UnsupportedImageFeatureException[])[actual, actualAsync])
            {
                Assert.Equal(expected.Format, exception.Format);
                Assert.Equal(expected.Feature, exception.Feature);
                Assert.Equal(expected.Message, exception.Message);
            }
        }
    }

    [Fact]
    public async Task FrameSavesValidateTheirArgumentsBeforeAnyOutput()
    {
        using var image = StreamImages.CreateAnimation(Width, Height, 1);
        var frame = image.Frames[0];
        var directory = TestFiles.CreateDirectory();
        try
        {
            Assert.Throws<ArgumentNullException>(() => frame.Save((string)null!));
            Assert.Throws<ArgumentException>(() => frame.Save(""));
            Assert.Throws<ArgumentException>(() => frame.Save(directory / "frame.xyz"));
            Assert.IsType<ArgumentNullException>(RecordSynchronousException(() => frame.SaveAsync((string)null!, cancellationToken: XunitCancellationToken)));
            Assert.IsType<ArgumentException>(RecordSynchronousException(() => frame.SaveAsync(directory / "frame", cancellationToken: XunitCancellationToken)));

            Assert.Throws<ArgumentNullException>(() => frame.Save((Stream)null!, new PngEncoder()));
            Assert.Throws<ArgumentNullException>(() => frame.Save(new ThrowingStream(), null!));
            Assert.Throws<ArgumentException>(() => frame.Save(new MemoryStream([], writable: false), new PngEncoder()));
            Assert.IsType<ArgumentNullException>(RecordSynchronousException(() => frame.SaveAsync(new ThrowingStream(), null!, XunitCancellationToken)));
            Assert.IsType<ArgumentException>(RecordSynchronousException(() => frame.SaveAsync(new MemoryStream([], writable: false), new PngEncoder(), XunitCancellationToken)));

            // A canceled save creates nothing
            using var canceled = new CancellationTokenSource();
            await canceled.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => frame.SaveAsync(directory / "frame.png", cancellationToken: canceled.Token));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => frame.SaveAsync(new ThrowingStream(), new PngEncoder(), canceled.Token));
            Assert.Empty(Directory.GetFileSystemEntries(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void FrameSavesRequireAnAttachedFrameThatIsNotLeased()
    {
        using var image = StreamImages.CreateAnimation(Width, Height, 3, poster: true);
        var directory = TestFiles.CreateDirectory();
        try
        {
            var removed = image.Frames[2];
            image.RemoveFrame(2);
            var replacedPoster = image.PosterFrame!;
            image.SetPosterFrame(image.Frames[0]);
            AssertRejected<ObjectDisposedException>(removed, directory);
            AssertRejected<ObjectDisposedException>(replacedPoster, directory);

            // A lease of the frame conflicts with the save, as it does with CloneFrame; the other frames can be saved
            var leased = image.Frames[0];
            using var output = new MemoryStream();
            leased.ProcessPixelBytes(_ =>
            {
                Assert.Throws<InvalidOperationException>(() => image.CloneFrame(0));
                AssertRejected<InvalidOperationException>(leased, directory);
                image.Frames[1].Save(output, new PngEncoder());
            });

            Assert.True(output.Length > 0);
            leased.Save(output, new PngEncoder());

            image.Dispose();
            AssertRejected<ObjectDisposedException>(leased, directory);
            Assert.Empty(Directory.GetFileSystemEntries(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }

        static void AssertRejected<TException>(ImageFrame frame, FullPath directory)
            where TException : Exception
        {
            using var stream = new MemoryStream();
            Assert.Throws<TException>(() => frame.Save(stream, new PngEncoder()));
            Assert.Throws<TException>(() => frame.Save(directory / "frame.png"));
            Assert.IsType<TException>(RecordSynchronousException(() => frame.SaveAsync(new MemoryStream(), new PngEncoder(), XunitCancellationToken)));
            Assert.IsType<TException>(RecordSynchronousException(() => frame.SaveAsync(directory / "frame.png", cancellationToken: XunitCancellationToken)));
            Assert.Equal(0, stream.Length);
        }
    }

    /// <summary>Records the exception that an asynchronous method throws before it returns its task.</summary>
    private static Exception? RecordSynchronousException(Func<Task> action)
    {
        Task? task = null;
        var exception = Record.Exception(() => { task = action(); });
        Assert.Null(task);
        return exception;
    }
}
