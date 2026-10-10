using Meziantou.Framework.Imaging.Formats;
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
        var directory = ImageWriterTests.CreateDirectory();
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
        var directory = ImageWriterTests.CreateDirectory();
        try
        {
            using var image = StreamImages.CreateAnimation(Width, Height, 1);
            Assert.Throws<ArgumentException>(() => image.Save(directory / "image.heic"));
            Task? task = null;
            Assert.IsType<ArgumentException>(Record.Exception(() => { task = image.SaveAsync(directory / "image", cancellationToken: XunitCancellationToken); }));
            Assert.Null(task);
            Assert.Empty(Directory.GetFileSystemEntries(directory));
            await image.SaveAsync(directory / "image.heic", new PngEncoder(), XunitCancellationToken);
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
        var directory = ImageWriterTests.CreateDirectory();
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
        var directory = ImageWriterTests.CreateDirectory();
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
}
