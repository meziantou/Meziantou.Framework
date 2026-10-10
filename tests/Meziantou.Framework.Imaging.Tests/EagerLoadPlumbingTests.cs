using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.TestHarness.Streams;
using Meziantou.Framework.Imaging.Tests.Codecs;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// The eager load plumbing exercised through the public API with a test-only decoder registered in the codec
/// registry: input variants, ownership, asynchronous I/O, cancellation, injected failures, limits, frame-limit selection,
/// typed dispatch with the conversion/alpha/profile policy, and resource release.
/// </summary>
public sealed class EagerLoadPlumbingTests
{
    private const int Width = 5;
    private const int Height = 3;

    public static TheoryData<InputVariant> Variants => [.. InputVariants.All];

    [Theory]
    [MemberData(nameof(Variants))]
    public async Task EveryInputVariantDecodesTheSameImage(InputVariant variant)
    {
        var codec = new TestRawCodec();
        var frames = CreateFrames(PixelFormat.Rgba32, 3);
        var data = TestRawImage.Encode(Width, Height, PixelFormat.Rgba32, PixelFormat.Rgba32, frames, totalPlays: 4);
        using (ImageCodecRegistry.Override(TestRawImage.CreateRegistry(codec)))
        {
            using var image = await InputVariants.LoadAsync(variant, data, ImageFormat.Png, options: null, XunitCancellationToken);
            Assert.IsType<Image<Rgba32>>(image);
            Assert.Equal(new Size(Width, Height), image.Size);
            Assert.Equal(3, image.Frames.Count);
            Assert.Equal(4, image.Animation?.TotalPlays);
            Assert.Equal(ImageFormat.Png, image.Metadata.SourceFormat);
            Assert.Equal("test codec", Assert.Single(image.Metadata.TextEntries).Value);
            for (var i = 0; i < frames.Length; i++)
            {
                Assert.Equal(frames[i].Duration, image.Frames[i].Metadata.Duration);
                AssertPixels(frames[i].Pixels, image.Frames[i]);
            }
        }

        Assert.Equal(0, codec.LastContext!.Scope.LiveBytes);
    }

    [Theory]
    [MemberData(nameof(Variants))]
    public async Task EveryInputVariantIdentifiesTheSameInformation(InputVariant variant)
    {
        var codec = new TestRawCodec();
        var data = TestRawImage.Encode(Width, Height, PixelFormat.Rgb24, PixelFormat.Rgb24, CreateFrames(PixelFormat.Rgb24, 2));
        using (ImageCodecRegistry.Override(TestRawImage.CreateRegistry(codec)))
        {
            var expected = ImageInfoSnapshots.Describe(Image.Identify(data, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan }));
            var info = await InputVariants.IdentifyAsync(variant, data, ImageFormat.Png, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan }, XunitCancellationToken);
            Assert.Equal(expected, ImageInfoSnapshots.Describe(info));
            Assert.Equal(2, info.FrameCount);
        }
    }

    [Fact]
    public void ResultIsIndependentOfTheInput()
    {
        var codec = new TestRawCodec();
        var frames = CreateFrames(PixelFormat.Gray8, 1);
        var data = TestRawImage.Encode(Width, Height, PixelFormat.Gray8, PixelFormat.Gray8, frames);
        using var registry = ImageCodecRegistry.Override(TestRawImage.CreateRegistry(codec));
        Image image;
        using (var stream = new TestInputStream(data))
        {
            image = Image.Load(stream);
            Assert.False(stream.IsDisposed);
            Assert.Equal(data.Length, stream.ReadPosition);
        }

        // The stream is disposed and the source bytes are overwritten: the image keeps its own copy
        Array.Clear(data);
        using (image)
        {
            AssertPixels(frames[0].Pixels, image.Frames[0]);
        }
    }

    [Fact]
    public void StreamIsReadFromItsCurrentPositionAndNotRewound()
    {
        var codec = new TestRawCodec();
        var frames = CreateFrames(PixelFormat.Rgba32, 1);
        var image = TestRawImage.Encode(Width, Height, PixelFormat.Rgba32, PixelFormat.Rgba32, frames);
        byte[] data = [.. "garbage"u8, .. image, .. "trailing data"u8];
        using var registry = ImageCodecRegistry.Override(TestRawImage.CreateRegistry(codec));
        using var stream = new TestInputStream(data, initialPosition: 7) { Seekable = false };
        var info = Image.Identify(stream, new ImageIdentifyOptions { Mode = ImageIdentifyMode.Header });
        Assert.Equal(new Size(Width, Height), info.Size);

        // Identify consumed bytes: a second call does not see the signature again
        Assert.True(stream.ReadPosition > 7);
        Assert.Throws<UnknownImageFormatException>(() => Image.Identify(stream));
        Assert.False(stream.IsDisposed);
    }

    [Fact]
    public async Task AsynchronousOverloadsUseAsynchronousReadsOnly()
    {
        var codec = new TestRawCodec();
        var data = TestRawImage.Encode(Width, Height, PixelFormat.Rgba64, PixelFormat.Rgba64, CreateFrames(PixelFormat.Rgba64, 2));
        using var registry = ImageCodecRegistry.Override(TestRawImage.CreateRegistry(codec));
        using var stream = new TestInputStream(data) { Seekable = false, MaxBytesPerRead = 7, ForbidSynchronousReads = true };
        using var image = await Image.LoadAsync(stream, cancellationToken: XunitCancellationToken);
        Assert.Equal(2, image.Frames.Count);
        Assert.True(stream.AsynchronousReadCount > 1);
        Assert.Equal(0, stream.SynchronousReadCount);

        using var identifyStream = new TestInputStream(data) { ForbidSynchronousReads = true };
        var info = await Image.IdentifyAsync(identifyStream, cancellationToken: XunitCancellationToken);
        Assert.Equal(PixelFormat.Rgba64, info.PixelFormat);
    }

    [Fact]
    public async Task PreCanceledTokenFailsWithoutReading()
    {
        var codec = new TestRawCodec();
        var data = TestRawImage.Encode(Width, Height, PixelFormat.Rgba32, PixelFormat.Rgba32, CreateFrames(PixelFormat.Rgba32, 1));
        using var registry = ImageCodecRegistry.Override(TestRawImage.CreateRegistry(codec));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        using var stream = new TestInputStream(data);
        Task? task = null;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task = Image.LoadAsync(stream, cancellationToken: cancellation.Token));
        Assert.True(task!.IsCanceled);
        Assert.Equal(0, stream.BytesRead);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Image.IdentifyAsync(stream, cancellationToken: cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Image.LoadAsync("missing-file.png", cancellationToken: cancellation.Token));
    }

    [Fact]
    public async Task CancellationDuringReadsFailsAndReleasesEverything()
    {
        var codec = new TestRawCodec();
        var data = TestRawImage.Encode(64, 64, PixelFormat.Rgba32, PixelFormat.Rgba32, CreateFrames(PixelFormat.Rgba32, 3, 64, 64));
        using var registry = ImageCodecRegistry.Override(TestRawImage.CreateRegistry(codec));
        using var cancellation = new CancellationTokenSource();
        using var stream = new TestInputStream(data) { MaxBytesPerRead = 1000, CancellationSource = cancellation, CancelAtPosition = data.Length / 2 };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Image.LoadAsync(stream, cancellationToken: cancellation.Token));
        Assert.False(stream.IsDisposed);
        Assert.Equal(0, codec.LastContext!.Scope.LiveBytes);
    }

    [Fact]
    public async Task CancellationDuringDecodingFailsAndReleasesEverything()
    {
        var codec = new TestRawCodec();
        var data = TestRawImage.Encode(Width, Height, PixelFormat.Rgba32, PixelFormat.Rgba32, CreateFrames(PixelFormat.Rgba32, 3));
        using var registry = ImageCodecRegistry.Override(TestRawImage.CreateRegistry(codec));
        using var cancellation = new CancellationTokenSource();
        codec.BeforeRow = row =>
        {
            if (row == 4)
            {
                cancellation.Cancel();
            }
        };

        using var stream = new TestInputStream(data);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Image.LoadAsync(stream, cancellationToken: cancellation.Token));
        Assert.Equal(0, codec.LastContext!.Scope.LiveBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InjectedIOFailuresPropagateUnchanged(bool asynchronous)
    {
        var codec = new TestRawCodec();
        var data = TestRawImage.Encode(Width, Height, PixelFormat.Rgba32, PixelFormat.Rgba32, CreateFrames(PixelFormat.Rgba32, 2));
        using var registry = ImageCodecRegistry.Override(TestRawImage.CreateRegistry(codec));
        using var stream = new TestInputStream(data) { FailAtPosition = 40, MaxBytesPerRead = 16 };
        var exception = asynchronous
            ? await Assert.ThrowsAsync<InjectedIOException>(() => Image.LoadAsync(stream, cancellationToken: XunitCancellationToken))
            : Assert.Throws<InjectedIOException>(() => Image.Load(stream));
        Assert.Contains("position 40", exception.Message, StringComparison.Ordinal);
        Assert.False(stream.IsDisposed);
        Assert.Equal(0, codec.LastContext!.Scope.LiveBytes);
    }

    [Theory]
    [MemberData(nameof(Variants))]
    public async Task TruncationIsInvalidContentNeverASuccess(InputVariant variant)
    {
        var codec = new TestRawCodec();
        var data = TestRawImage.Encode(Width, Height, PixelFormat.Rgba32, PixelFormat.Rgba32, CreateFrames(PixelFormat.Rgba32, 2));
        using var registry = ImageCodecRegistry.Override(TestRawImage.CreateRegistry(codec));
        var truncated = data[..^1];
        var exception = await Assert.ThrowsAsync<InvalidImageContentException>(() => InputVariants.LoadAsync(variant, truncated, ImageFormat.Png, options: null, XunitCancellationToken));
        Assert.Equal(ImageFormat.Png, exception.Format);
        Assert.Contains("truncated", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Variants))]
    public async Task UnknownSignaturesAreReportedConsistently(InputVariant variant)
    {
        foreach (var data in new[] { Array.Empty<byte>(), "GIF8"u8.ToArray(), "\x89PNG\r\n\x1A"u8.ToArray(), "not an image at all"u8.ToArray(), "GIF88a........"u8.ToArray() })
        {
            await Assert.ThrowsAsync<UnknownImageFormatException>(() => InputVariants.LoadAsync(variant, data, ImageFormat.Png, options: null, XunitCancellationToken));
            await Assert.ThrowsAsync<UnknownImageFormatException>(() => InputVariants.IdentifyAsync(variant, data, ImageFormat.Png, options: null, XunitCancellationToken));
        }
    }

    [Fact]
    public void UnsupportedFeaturesAreReportedBeforeAnyPixel()
    {
        var codec = new TestRawCodec();
        var data = TestRawImage.Encode(Width, Height, PixelFormat.Rgba32, PixelFormat.Rgba32, CreateFrames(PixelFormat.Rgba32, 1), unsupportedFeature: true);
        using var registry = ImageCodecRegistry.Override(TestRawImage.CreateRegistry(codec));
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(data));
        Assert.Equal("Test feature", exception.Feature);
        Assert.Equal(0, codec.RowsDecoded);
    }

    [Fact]
    public void EncodedByteLimitIsEnforcedIncrementally()
    {
        var codec = new TestRawCodec();
        var data = TestRawImage.Encode(Width, Height, PixelFormat.Rgba32, PixelFormat.Rgba32, CreateFrames(PixelFormat.Rgba32, 2));
        using var registry = ImageCodecRegistry.Override(TestRawImage.CreateRegistry(codec));
        using (var exact = Image.Load(data, CreateOptions(new ImageResourceLimits { MaxEncodedBytes = data.Length })))
        {
            Assert.Equal(2, exact.Frames.Count);
        }

        var options = CreateOptions(new ImageResourceLimits { MaxEncodedBytes = data.Length - 1 });
        var fromSpan = Assert.Throws<ImageResourceLimitException>(() => Image.Load(data, options));
        using var stream = new TestInputStream(data) { Seekable = false, MaxBytesPerRead = 5 };
        var fromStream = Assert.Throws<ImageResourceLimitException>(() => Image.Load(stream, options));
        Assert.Equal(ImageResourceLimitKind.EncodedBytes, fromSpan.Kind);
        Assert.Equal(data.Length - 1, fromSpan.Limit);
        Assert.Equal(fromSpan.Requested, fromStream.Requested);
        Assert.True(stream.BytesRead <= data.Length - 1, "No byte beyond the limit may be read.");
    }

    [Fact]
    public void FrameAndPixelLimitsAreSafetyBoundsNotTruncation()
    {
        var codec = new TestRawCodec();
        var data = TestRawImage.Encode(Width, Height, PixelFormat.Rgba32, PixelFormat.Rgba32, CreateFrames(PixelFormat.Rgba32, 3), poster: TestRawImage.Pattern(Width, Height, PixelFormat.Rgba32, 9));
        using var registry = ImageCodecRegistry.Override(TestRawImage.CreateRegistry(codec));

        // Frames count the poster: 3 frames + poster = 4
        using (var exact = Image.Load(data, CreateOptions(new ImageResourceLimits { MaxFrames = 4, MaxTotalPixels = 3 * Width * Height })))
        {
            Assert.Equal(3, exact.Frames.Count);
            Assert.NotNull(exact.PosterFrame);
        }

        var frames = Assert.Throws<ImageResourceLimitException>(() => Image.Load(data, CreateOptions(new ImageResourceLimits { MaxFrames = 3 })));
        Assert.Equal(ImageResourceLimitKind.Frames, frames.Kind);
        Assert.Equal(4, frames.Requested);

        // Cumulative displayed pixels exclude the poster
        var pixels = Assert.Throws<ImageResourceLimitException>(() => Image.Load(data, CreateOptions(new ImageResourceLimits { MaxTotalPixels = (3 * Width * Height) - 1 })));
        Assert.Equal(ImageResourceLimitKind.TotalPixels, pixels.Kind);

        var width = Assert.Throws<ImageResourceLimitException>(() => Image.Identify(data, new ImageIdentifyOptions { Configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxWidth = Width - 1 } } }));
        Assert.Equal(ImageResourceLimitKind.Width, width.Kind);
        Assert.Equal(0, codec.LastContext!.Scope.LiveBytes);
    }

    [Fact]
    public void AllocationLimitFailsWithoutLeaking()
    {
        var codec = new TestRawCodec();
        var data = TestRawImage.Encode(64, 64, PixelFormat.Rgba32, PixelFormat.Rgba32, CreateFrames(PixelFormat.Rgba32, 4, 64, 64));
        using var registry = ImageCodecRegistry.Override(TestRawImage.CreateRegistry(codec));
        var exception = Assert.Throws<ImageResourceLimitException>(() => Image.Load(data, CreateOptions(new ImageResourceLimits { MaxLiveAllocationBytes = 3 * 64 * 64 * 4 })));
        Assert.Equal(ImageResourceLimitKind.LiveAllocationBytes, exception.Kind);
        Assert.Equal(0, codec.LastContext!.Scope.LiveBytes);
    }

    [Fact]
    public void LoadedImageOwnsTheOperationScopeAndTheInputBufferIsReleased()
    {
        var codec = new TestRawCodec();
        var data = TestRawImage.Encode(Width, Height, PixelFormat.Rgba32, PixelFormat.Rgba32, CreateFrames(PixelFormat.Rgba32, 2));
        using var registry = ImageCodecRegistry.Override(TestRawImage.CreateRegistry(codec));
        using var stream = new MemoryStream(data);
        var image = Image.Load(stream);
        var scope = codec.LastContext!.Scope;
        Assert.Same(scope, image.Owner.Scope);
        Assert.Equal(0, scope.GetLiveBytes(AllocationKind.DecoderState));
        Assert.True(scope.GetLiveBytes(AllocationKind.ImagePixels) > 0);
        image.Dispose();
        Assert.Equal(0, scope.LiveBytes);
    }

    [Fact]
    public void FrameLimitSelectsAPrefixWithoutExaminingTheRest()
    {
        var codec = new TestRawCodec();
        var frames = CreateFrames(PixelFormat.Rgba32, 3);
        var data = TestRawImage.Encode(Width, Height, PixelFormat.Rgba32, PixelFormat.Rgba32, frames, poster: TestRawImage.Pattern(Width, Height, PixelFormat.Rgba32, 7), totalPlays: 2);

        // Corrupt the third frame: it is never examined
        var corrupted = data.AsSpan(0, data.Length - (Width * Height * 4) - 8).ToArray();
        using var registry = ImageCodecRegistry.Override(TestRawImage.CreateRegistry(codec));
        using var stream = new TestInputStream(corrupted) { Seekable = false };
        using var image = Image.Load(stream, new ImageDecodeOptions { FrameLimit = 2 });
        Assert.Equal(2, image.Frames.Count);
        Assert.NotNull(image.PosterFrame);
        Assert.Equal(2, image.Animation?.TotalPlays);
        AssertPixels(frames[1].Pixels, image.Frames[1]);

        // Without the selection, the truncated third frame is an error
        Assert.Throws<InvalidImageContentException>(() => Image.Load(corrupted));
    }

    [Fact]
    public void TypedLoadsConvertDirectlyAndPreservePrecision()
    {
        var codec = new TestRawCodec();
        var frames = CreateFrames(PixelFormat.Rgb24, 1);
        var data = TestRawImage.Encode(Width, Height, PixelFormat.Rgb24, PixelFormat.Rgb24, frames);
        using var registry = ImageCodecRegistry.Override(TestRawImage.CreateRegistry(codec));
        using var wide = Image.Load<Rgba64>(data);
        var source = frames[0].Pixels;
        Assert.Equal(new Rgba64((ushort)(source[0] * 257), (ushort)(source[1] * 257), (ushort)(source[2] * 257), ushort.MaxValue), wide.Frames[0][0, 0]);

        using var bgra = Image.Load<Bgra32>(data);
        Assert.Equal(new Bgra32(source[3], source[4], source[5], 255), bgra.Frames[0][1, 0]);

        // Untyped loads use the default representation of the file
        using var untyped = Image.Load(data);
        Assert.IsType<Image<Rgb24>>(untyped);
    }

    [Fact]
    public async Task TypedLoadsNeverDiscardAlphaSilently()
    {
        var codec = new TestRawCodec();
        var pixels = TestRawImage.Pattern(Width, Height, PixelFormat.Rgba32, 3);
        pixels[3] = 128;
        var data = TestRawImage.Encode(Width, Height, PixelFormat.Rgba32, PixelFormat.Rgba32, [(FrameDuration.Zero, pixels)]);
        using var registry = ImageCodecRegistry.Override(TestRawImage.CreateRegistry(codec));
        Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load<Rgb24>(data));
        using (var stream = new MemoryStream(data))
        {
            await Assert.ThrowsAsync<UnsupportedImageFeatureException>(() => Image.LoadAsync<Gray8>(stream, cancellationToken: XunitCancellationToken));
        }

        Assert.Equal(0, codec.LastContext!.Scope.LiveBytes);

        // An explicit background flattens: out = round((src * a + bg * (255 - a)) / 255)
        var options = new ImageDecodeOptions { Conversion = new PixelConversionOptions { BackgroundColor = new Rgba32(255, 255, 255) } };
        using var flattened = Image.Load<Rgb24>(data, options);
        static byte Flatten(byte value) => (byte)(((value * 128) + (255 * 127) + 127) / 255);
        Assert.Equal(new Rgb24(Flatten(pixels[0]), Flatten(pixels[1]), Flatten(pixels[2])), flattened.Frames[0][0, 0]);
        Assert.Equal(new Rgb24(pixels[4], pixels[5], pixels[6]), flattened.Frames[0][1, 0]);
    }

    [Fact]
    public void ColorProfilePolicyAppliesToTypedLoads()
    {
        var codec = new TestRawCodec();
        var data = TestRawImage.Encode(Width, Height, PixelFormat.Rgb24, PixelFormat.Rgb24, CreateFrames(PixelFormat.Rgb24, 1), iccColorSpace: 2);
        using var registry = ImageCodecRegistry.Override(TestRawImage.CreateRegistry(codec));
        using (var color = Image.Load(data))
        {
            Assert.NotNull(color.Metadata.IccProfile);
        }

        codec.RowsDecoded = 0;
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load<Gray8>(data));
        Assert.Equal("Incompatible color profile", exception.Feature);
        Assert.Equal(0, codec.RowsDecoded);
        using var gray = Image.Load<Gray8>(data, new ImageDecodeOptions { Conversion = new PixelConversionOptions { DiscardIncompatibleColorProfile = true } });
        Assert.Null(gray.Metadata.IccProfile);
    }

    [Fact]
    public void DefaultRepresentationDropsAProfileItCannotCarry()
    {
        // A grayscale profile on samples whose default representation is RGBA (gray + alpha) is not adopted
        var codec = new TestRawCodec();
        var data = TestRawImage.Encode(Width, Height, PixelFormat.Rgba32, PixelFormat.Rgba32, CreateFrames(PixelFormat.Rgba32, 1), iccColorSpace: 1);
        using var registry = ImageCodecRegistry.Override(TestRawImage.CreateRegistry(codec));
        using var image = Image.Load(data);
        Assert.Null(image.Metadata.IccProfile);
        using var gray = Image.Load<Gray8>(data);
        Assert.Equal(IccProfileColorSpace.Gray, gray.Metadata.IccProfile?.ColorSpace);
    }

    [Fact]
    public void RegistryOverrideIsScopedToTheCurrentFlow()
    {
        var codec = new TestRawCodec();
        var data = TestRawImage.Encode(Width, Height, PixelFormat.Rgba32, PixelFormat.Rgba32, CreateFrames(PixelFormat.Rgba32, 1));
        using (ImageCodecRegistry.Override(TestRawImage.CreateRegistry(codec)))
        {
            using var image = Image.Load(data);
            Assert.Equal(ImageFormat.Unknown, Image.DetectFormat(data));
        }

        Assert.Same(ImageCodecRegistry.Default, ImageCodecRegistry.Current);
        Assert.Throws<UnknownImageFormatException>(() => Image.Load(data));
    }

    [Fact]
    public void EveryBuiltInDecoderIsAvailable()
    {
        // Static PNG, APNG, GIF, baseline and progressive JPEG are implemented
        using (var png = Image.Load(SyntheticImages.Png(2, 2)))
        {
            Assert.Equal(ImageFormat.Png, png.Metadata.SourceFormat);
        }

        using (var apng = Image.Load(SyntheticImages.Apng(2, 2, frames: 2)))
        {
            Assert.Equal(2, apng.Frames.Count);
        }

        // GIF is implemented
        using (var gif = Image.Load(SyntheticImages.Gif(2, 2, images: 1)))
        {
            Assert.Equal(ImageFormat.Gif, gif.Metadata.SourceFormat);
            Assert.Equal(PixelFormat.Rgba32, gif.PixelFormat);
        }

        // Baseline JPEG is implemented: the synthetic file's header is valid, its tables are not (no AC table)
        using (var jpeg = Image.Load(JpegTestImage.CreateRandom(9, 7, [(2, 1), (1, 1), (1, 1)], seed: 1).Encode()))
        {
            Assert.Equal(ImageFormat.Jpeg, jpeg.Metadata.SourceFormat);
        }

        Assert.Throws<InvalidImageContentException>(() => Image.Load<Rgb24>(SyntheticImages.Jpeg(0xC0, 3)));

        // Progressive JPEG is implemented too
        var progressive = JpegTestImage.CreateRandom(9, 7, [(1, 1)], seed: 1).Encode(new JpegTestEncodeOptions { Progression = JpegTestProgressiveScan.ParseScript("0: 0 0 0 0; 0: 1 63 0 0") });
        using (var jpeg = Image.Load(progressive))
        {
            Assert.Equal(PixelFormat.Gray8, jpeg.PixelFormat);
        }
    }

    private static ImageDecodeOptions CreateOptions(ImageResourceLimits limits) => new() { Configuration = new ImageConfiguration { Limits = limits } };

    private static (FrameDuration Duration, byte[] Pixels)[] CreateFrames(PixelFormat format, int count, int width = Width, int height = Height)
        => [.. Enumerable.Range(0, count).Select(index => (new FrameDuration(index + 1, 25), TestRawImage.Pattern(width, height, format, index)))];

    private static void AssertPixels(byte[] expected, ImageFrame frame)
    {
        var actual = new byte[expected.Length];
        frame.CopyPixelBytesTo(actual);
        Assert.Equal(expected, actual);
    }
}
