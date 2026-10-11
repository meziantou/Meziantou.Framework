using System.IO.Compression;
using System.Numerics;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.TestHarness.Fixtures;
using Meziantou.Framework.Imaging.TestHarness.Pixels;
using Meziantou.Framework.Imaging.TestHarness.Png;
using Meziantou.Framework.Imaging.TestHarness.Streams;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// APNG encoding through the public save and writer APIs. Output is validated with the independent harness
/// reader (<see cref="ReferencePng"/>: strict chunk structure, APNG sequence and control rules, its own unfiltering and
/// Adam7, no compositor) through <see cref="ApngOutputVerifier"/>, and encoded delay/loop fields with the independent
/// container walker (<see cref="EncodedFieldInspector"/>). Rounded delays are checked against a brute-force search over
/// every 16-bit denominator. The library decoder is a supplementary round trip only; FFmpeg decodes the same kinds of output
/// in the interoperability tests.
/// </summary>
public sealed class ApngEncoderTests
{
    private static readonly PixelFormat[] AllFormats = [PixelFormat.Rgba32, PixelFormat.Bgra32, PixelFormat.Rgb24, PixelFormat.Rgba64, PixelFormat.Gray8, PixelFormat.Gray16];

    public static TheoryData<PixelFormat, bool, bool> LayoutCases()
    {
        var data = new TheoryData<PixelFormat, bool, bool>();
        foreach (var format in AllFormats)
        {
            data.Add(format, false, false);
            data.Add(format, true, false);
            data.Add(format, false, true);
            data.Add(format, true, true);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(LayoutCases))]
    public void EveryStorageTypeAndPosterLayoutEncodesFullCanvasSourceFrames(PixelFormat format, bool poster, bool interlaced)
    {
        using var image = CreateAnimation(format, 11, 7, frameCount: 3, poster, seed: (int)format * 3);
        image.Animation = new AnimationMetadata { TotalPlays = 3 };
        var png = Encode(image, new PngEncoder { Interlaced = interlaced });
        var reference = ApngOutputVerifier.Verify(png, image);
        Assert.Equal(interlaced ? 1 : 0, reference.InterlaceMethod);
        Assert.Equal(format is PixelFormat.Rgba64 or PixelFormat.Gray16 ? 16 : 8, reference.BitDepth);

        // Encoded control data, read again by the independent walker: delays and plays, in fcTL order
        var fields = EncodedFieldInspector.Inspect("png", png);
        Assert.Equal(3, fields.LoopValue);
        Assert.Equal(["1/10", "1/3", "0/1"], fields.FrameDelays.Select(delay => $"{delay!.Numerator}/{delay.Denominator}"));

        // Chunk layout: the poster (or frame zero) is the IDAT image, later frames are fcTL + fdAT
        var expectedTypes = poster
            ? new[] { "IHDR", "acTL", "IDAT", "fcTL", "fdAT", "fcTL", "fdAT", "fcTL", "fdAT", "IEND" }
            : ["IHDR", "acTL", "fcTL", "IDAT", "fcTL", "fdAT", "fcTL", "fdAT", "IEND"];
        Assert.Equal(expectedTypes, reference.ChunkTypes);
        Assert.Equal(poster ? [0u, 2u, 4u] : [0u, 1u, 3u], reference.Animation!.Frames.Select(frame => frame.SequenceNumber));

        AssertRoundTripsThroughLibraryDecoder(png, image);
    }

    [Theory]
    [InlineData(0, 1, 0, 1)]
    [InlineData(1, 10, 1, 10)]
    [InlineData(100, 1000, 1, 10)]
    [InlineData(1, 3, 1, 3)]
    [InlineData(1001, 30000, 1001, 30000)]
    [InlineData(65535, 1, 65535, 1)]
    [InlineData(1, 65535, 1, 65535)]
    [InlineData(65535, 65534, 65535, 65534)]
    [InlineData(131070, 131068, 65535, 65534)]
    public void ExactDurationsAreWrittenAsTheirNormalizedFraction(long numerator, long denominator, int expectedNumerator, int expectedDenominator)
    {
        using var image = CreateAnimation<Rgba32>(3, 2, frameCount: 2, poster: false, seed: 1);
        image.Frames[1].Metadata.Duration = new FrameDuration(numerator, denominator);
        var png = Encode(image, new PngEncoder()); // RequireExact (default)
        ApngOutputVerifier.Verify(png, image);
        var delay = EncodedFieldInspector.Inspect("png", png).FrameDelays[1]!;
        Assert.Equal(expectedNumerator, delay.Numerator);
        Assert.Equal(expectedDenominator, delay.Denominator);
    }

    public static TheoryData<long, long> InexactDurations() => new()
    {
        { 1, 65536 },
        { 1, 1_000_000 },
        { 123_456_789, 1_000_000_007 },
        { 65_534_999, 1_000 },
        { 1_234_567, 10_000_000 }, // FrameDuration.FromTimeSpan(1,234,567 ticks)
        { 2, 131_071 },
        { 1_000_001, 3_000_000 },
        { long.MaxValue / 2, long.MaxValue - 4 },
        { 65_535L * 65_537, 65_537 + 1 },
    };

    [Theory]
    [MemberData(nameof(InexactDurations))]
    public void InexactDurationsFailBeforeAnyOutputUnlessRoundingIsExplicit(long numerator, long denominator)
    {
        var duration = new FrameDuration(numerator, denominator);
        using var image = CreateAnimation<Rgba32>(3, 2, frameCount: 3, poster: true, seed: 2);
        image.Frames[2].Metadata.Duration = duration;

        // Strict (default): the eager save preflights every frame, so even the last frame fails before the first byte
        using (var stream = new TestOutputStream())
        {
            var exception = Assert.Throws<UnsupportedImageFeatureException>(() => image.Save(stream, new PngEncoder()));
            Assert.Equal("APNG frame duration precision", exception.Feature);
            Assert.Equal(0, stream.BytesWritten);
        }

        // Explicit rounding: the nearest fraction with 16-bit numerator and denominator (ties toward the longer duration)
        var png = Encode(image, new PngEncoder { DurationRounding = FrameDurationRounding.RoundToNearest });
        ApngOutputVerifier.Verify(png, image, exactDurations: false);
        var delay = EncodedFieldInspector.Inspect("png", png).FrameDelays[2]!;
        var (expectedNumerator, expectedDenominator) = NearestBySearch(duration.Numerator, duration.Denominator);
        Assert.Equal((expectedNumerator, expectedDenominator), ((long)delay.Numerator!, (long)delay.Denominator!));
    }

    [Theory]
    [InlineData(65536, 1)]
    [InlineData(655351, 10)]
    [InlineData(long.MaxValue, 1)]
    public void OutOfRangeDurationsThrowInsteadOfClamping(long numerator, long denominator)
    {
        using var image = CreateAnimation<Rgba32>(3, 2, frameCount: 2, poster: false, seed: 3);
        image.Frames[1].Metadata.Duration = new FrameDuration(numerator, denominator);
        foreach (var rounding in Enum.GetValues<FrameDurationRounding>())
        {
            using var stream = new TestOutputStream();
            var exception = Assert.Throws<UnsupportedImageFeatureException>(() => image.Save(stream, new PngEncoder { DurationRounding = rounding }));
            Assert.Equal("APNG frame duration range", exception.Feature);
            Assert.Equal(0, stream.BytesWritten);
        }

        image.Frames[1].Metadata.Duration = new FrameDuration(65535, 1); // the largest delay
        ApngOutputVerifier.Verify(Encode(image, new PngEncoder()), image);
    }

    [Fact]
    public void UnrepresentableDurationsKeepTheWriterUsable()
    {
        using var image = CreateAnimation<Rgba64>(4, 3, frameCount: 2, poster: false, seed: 4);
        using var stream = new TestOutputStream();
        using var writer = Image.CreateWriter<Rgba64>(stream, new ImageWriterOptions(4, 3) { Encoder = new PngEncoder { AnimationMode = PngAnimationMode.Animated }, ExpectedFrameCount = 2 });
        writer.WriteFrame(image.Frames[0]);
        var written = stream.BytesWritten;
        image.Frames[1].Metadata.Duration = new FrameDuration(1, 70000);
        Assert.Throws<UnsupportedImageFeatureException>(() => writer.WriteFrame(image.Frames[1]));
        Assert.Equal(written, stream.BytesWritten); // nothing of the rejected frame was written
        Assert.Equal(1, writer.FramesWritten);
        image.Frames[1].Metadata.Duration = new FrameDuration(1, 7);
        writer.WriteFrame(image.Frames[1]);
        writer.Complete();
        image.Animation = new AnimationMetadata(); // the writer default: infinite
        ApngOutputVerifier.Verify(stream.ToArray(), image);
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(int.MaxValue, int.MaxValue)]
    public void TotalPlaysMapToNumPlays(int? totalPlays, int numPlays)
    {
        using var image = CreateAnimation<Rgba32>(2, 2, frameCount: 2, poster: false, seed: 5);
        image.Animation = new AnimationMetadata { TotalPlays = totalPlays };
        var png = Encode(image, new PngEncoder());
        Assert.Equal(numPlays, ApngOutputVerifier.Verify(png, image).Animation!.NumPlays);
        Assert.Equal(numPlays, EncodedFieldInspector.Inspect("png", png).LoopValue);
        using var decoded = Image.Load(png);
        Assert.Equal(totalPlays, decoded.Animation!.TotalPlays);
    }

    [Fact]
    public void WritersRequireTheExactFrameCount()
    {
        using var image = CreateAnimation<Rgba32>(3, 2, frameCount: 3, poster: false, seed: 6);
        var encoder = new PngEncoder { AnimationMode = PngAnimationMode.Animated };
        using var stream = new TestOutputStream();
        Assert.Throws<ArgumentException>(() => Image.CreateWriter<Rgba32>(stream, new ImageWriterOptions(3, 2) { Encoder = encoder })); // non-seekable: still required
        Assert.Throws<ArgumentException>(() => Image.CreateWriter<Rgba32>(new TestOutputStream { Seekable = true }, new ImageWriterOptions(3, 2) { Encoder = encoder }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageWriterOptions(3, 2) { Encoder = encoder, ExpectedFrameCount = 0 });
        Assert.Equal(0, stream.BytesWritten);

        using (var writer = Image.CreateWriter<Rgba32>(stream, new ImageWriterOptions(3, 2) { Encoder = encoder, ExpectedFrameCount = 3, Animation = new AnimationMetadata { TotalPlays = 4 } }))
        {
            Assert.Throws<InvalidOperationException>(() => writer.Complete()); // no frame: usable
            writer.WriteFrame(image.Frames[0]);
            writer.WriteFrame(image.Frames[1]);
            Assert.Throws<InvalidOperationException>(() => writer.Complete()); // 2 of 3: usable
            writer.WriteFrame(image.Frames[2]);
            Assert.Throws<InvalidOperationException>(() => writer.WriteFrame(image.Frames[0])); // count reached: usable
            writer.Complete();
            Assert.Equal(0, writer.Core.Scope.LiveBytes);
        }

        image.Animation = new AnimationMetadata { TotalPlays = 4 };
        ApngOutputVerifier.Verify(stream.ToArray(), image);
    }

    [Fact]
    public void PostersAreWrittenOnceBeforeFrameZeroAndNotCounted()
    {
        using var image = CreateAnimation<Rgba32>(3, 2, frameCount: 1, poster: true, seed: 7);
        var options = new ImageWriterOptions(3, 2) { Encoder = new PngEncoder(), ExpectedFrameCount = 1, Animation = new AnimationMetadata() };
        using (var late = Image.CreateWriter<Rgba32>(new TestOutputStream(), options))
        {
            late.WriteFrame(image.Frames[0]);
            Assert.Throws<InvalidOperationException>(() => late.WritePosterFrame(image.PosterFrame!)); // after frame zero: usable
            late.Complete();
        }

        using var stream = new TestOutputStream();
        using (var writer = Image.CreateWriter<Rgba32>(stream, options))
        {
            Assert.True(writer.Core.Capabilities.SupportsPosterFrame);
            writer.WritePosterFrame(image.PosterFrame!);
            Assert.Throws<InvalidOperationException>(() => writer.WritePosterFrame(image.PosterFrame!)); // twice
            Assert.Throws<InvalidOperationException>(() => writer.Complete()); // a poster is not a displayed frame
            writer.WriteFrame(image.Frames[0]);
            writer.Complete();
        }

        // A single-frame animation with a separate poster: num_frames counts the displayed frame only
        var reference = ApngOutputVerifier.Verify(stream.ToArray(), image);
        Assert.Equal(1, reference.Animation!.NumFrames);
        Assert.True(reference.Animation.HasSeparatePoster);

        // Static PNG outputs have no poster
        using var still = Image.CreateWriter<Rgba32>(new TestOutputStream(), new ImageWriterOptions(3, 2) { Encoder = new PngEncoder(), ExpectedFrameCount = 1 });
        Assert.Throws<InvalidOperationException>(() => still.WritePosterFrame(image.PosterFrame!));
    }

    [Fact]
    public void AutomaticModeWritesAnApngExactlyForAnimatedImages()
    {
        using var still = CreateAnimation<Rgba32>(3, 2, frameCount: 1, poster: false, seed: 8);
        Assert.Null(ReferencePng.Parse(Encode(still, new PngEncoder())).Animation);

        // Animated mode: a one-frame APNG whose default image is frame zero
        var forced = ApngOutputVerifier.Verify(Encode(still, new PngEncoder { AnimationMode = PngAnimationMode.Animated }), still);
        Assert.True(forced.Animation!.Frames[0].UsesIdat);

        using var settingsOnly = CreateAnimation<Rgba32>(3, 2, frameCount: 1, poster: false, seed: 8);
        settingsOnly.Animation = new AnimationMetadata { TotalPlays = 2 };
        Assert.Equal(1, ApngOutputVerifier.Verify(Encode(settingsOnly, new PngEncoder()), settingsOnly).Animation!.NumFrames);

        using var posterOnly = CreateAnimation<Rgba32>(3, 2, frameCount: 1, poster: true, seed: 8);
        ApngOutputVerifier.Verify(Encode(posterOnly, new PngEncoder()), posterOnly);

        using var animated = CreateAnimation(PixelFormat.Gray16, 3, 2, frameCount: 4, poster: false, seed: 8);
        ApngOutputVerifier.Verify(Encode(animated, new PngEncoder()), animated);

        // Writers: Auto is animated with more than one expected frame or with animation settings
        using (var writer = Image.CreateWriter<Rgba32>(new TestOutputStream(), new ImageWriterOptions(3, 2) { Encoder = new PngEncoder(), ExpectedFrameCount = 2 }))
        {
            Assert.True(writer.Core.Capabilities.IsAnimated);
        }

        using (var writer = Image.CreateWriter<Rgba32>(new TestOutputStream(), new ImageWriterOptions(3, 2) { Encoder = new PngEncoder(), ExpectedFrameCount = 1, Animation = new AnimationMetadata() }))
        {
            Assert.True(writer.Core.Capabilities.IsAnimated);
        }

        using (var writer = Image.CreateWriter<Rgba32>(new TestOutputStream(), new ImageWriterOptions(3, 2) { Encoder = new PngEncoder(), ExpectedFrameCount = 1 }))
        {
            Assert.False(writer.Core.Capabilities.IsAnimated);
        }
    }

    [Fact]
    public void PathSavesInferApngFromTheExtension()
    {
        var directory = ImageWriterTests.CreateDirectory();
        try
        {
            using var still = CreateAnimation(PixelFormat.Rgb24, 4, 3, frameCount: 1, poster: false, seed: 9);
            using var animated = CreateAnimation(PixelFormat.Rgb24, 4, 3, frameCount: 2, poster: true, seed: 9);
            still.Save(directory / "still.apng");
            ApngOutputVerifier.Verify(File.ReadAllBytes(directory / "still.apng"), still);
            animated.Save(directory / "animated.png"); // .png: Auto
            ApngOutputVerifier.Verify(File.ReadAllBytes(directory / "animated.png"), animated);
            Assert.Throws<UnsupportedImageFeatureException>(() => animated.Save(directory / "static.png", new PngEncoder { AnimationMode = PngAnimationMode.Static }));
            Assert.Equal(["animated.png", "still.apng"], Directory.GetFileSystemEntries(directory).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void MetadataChunksFollowTheAnimationControlAndPrecedeTheImageData()
    {
        using var image = CreateAnimation<Rgba32>(4, 4, frameCount: 2, poster: true, seed: 10);
        image.Metadata.Resolution = new ImageResolution(72, 72);
        image.Metadata.TextEntries.Add(new ImageTextEntry("Title", "animated"));
        var png = Encode(image, new PngEncoder());
        Assert.Equal(["IHDR", "acTL", "pHYs", "tEXt", "IDAT", "fcTL", "fdAT", "fcTL", "fdAT", "IEND"], ApngOutputVerifier.Verify(png, image).ChunkTypes);
        Assert.Equal(["Title: animated"], EncodedFieldInspector.Inspect("png", png).Text.Select(entry => entry.ToString()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NonSeekableOutputIsStreamedFrameByFrameWithBoundedMemory(bool asynchronous)
    {
        // Incompressible 16-bit frames stored without compression: several fdAT chunks per frame
        const int FrameWidth = 160;
        const int FrameHeight = 120;
        var peaks = new List<long>();
        foreach (var count in new[] { 2, 24 })
        {
            using var frames = CreateAnimation<Rgba64>(FrameWidth, FrameHeight, frameCount: 2, poster: false, seed: 11);
            using var stream = new TestOutputStream { ForbidSynchronousWrites = asynchronous, ForbidAsynchronousWrites = !asynchronous };
            var options = new ImageWriterOptions(FrameWidth, FrameHeight) { Encoder = new PngEncoder { CompressionLevel = CompressionLevel.NoCompression, Filter = PngFilter.None }, ExpectedFrameCount = count };
            using (var writer = Image.CreateWriter<Rgba64>(stream, options))
            {
                var previous = 0L;
                for (var i = 0; i < count; i++)
                {
                    var frame = frames.Frames[i % 2];
                    if (asynchronous)
                    {
                        await writer.WriteFrameAsync(frame, XunitCancellationToken);
                    }
                    else
                    {
                        writer.WriteFrame(frame);
                    }

                    // Each frame reaches the destination when its call returns: nothing is held back for the end
                    Assert.True(stream.BytesWritten - previous > FrameWidth * FrameHeight * 8, $"frame {i}: {stream.BytesWritten - previous} bytes written");
                    previous = stream.BytesWritten;
                }

                if (asynchronous)
                {
                    await writer.CompleteAsync(XunitCancellationToken);
                }
                else
                {
                    writer.Complete();
                }

                peaks.Add(writer.Core.Scope.GetDiagnostics().PeakLiveBytes);
                Assert.Equal(0, writer.Core.Scope.LiveBytes);
            }

            var reference = ReferencePng.Parse(stream.ToArray());
            Assert.Equal(count, reference.Animation!.NumFrames);
            Assert.All(reference.Animation.Frames.Skip(1), frame => Assert.HasCountGreaterThan(4, frame.DataSequenceNumbers));
            Assert.All(reference.GetChunks("fdAT"), chunk => Assert.InRange(chunk.Data.Length, 5, 4 + PngImageDataEncoder.ChunkCapacity));
            for (var i = 0; i < count; i++)
            {
                var result = PixelBufferComparer.Compare(ImageSnapshots.CaptureFrame(frames.Frames[i % 2]), reference.DecodeDisplayedFrame(i), ComparisonPolicy.Exact, $"frame {i}");
                Assert.True(result.IsMatch, result.Describe());
            }
        }

        // The working memory (scanlines, one chunk, the output buffer) never depends on the number of frames, and is far
        // below the size of the animation: nothing is buffered for a seek-back or a final rewrite
        Assert.Equal(peaks[0], peaks[1]);
        Assert.True(peaks[1] < 24L * FrameWidth * FrameHeight * 8 / 4, $"Peak writer memory {peaks[1]} bytes.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(40)]
    [InlineData(70)]
    [InlineData(200)]
    [InlineData(20_000)]
    [InlineData(40_000)]
    public async Task FailingStreamsFaultTheWriterAndReleaseItsState(long failAt)
    {
        using var image = CreateAnimation<Rgba32>(64, 64, frameCount: 3, poster: true, seed: 12);
        foreach (var asynchronous in new[] { false, true })
        {
            using var stream = new TestOutputStream { FailAtPosition = failAt };
            var writer = Image.CreateWriter<Rgba32>(stream, new ImageWriterOptions(64, 64) { Encoder = new PngEncoder { CompressionLevel = CompressionLevel.NoCompression }, ExpectedFrameCount = 3 });
            try
            {
                if (asynchronous)
                {
                    await Assert.ThrowsAsync<InjectedIOException>(async () =>
                    {
                        await writer.WritePosterFrameAsync(image.PosterFrame!, XunitCancellationToken);
                        foreach (var frame in image.Frames)
                        {
                            await writer.WriteFrameAsync(frame, XunitCancellationToken);
                        }

                        await writer.CompleteAsync(XunitCancellationToken);
                    });
                }
                else
                {
                    Assert.Throws<InjectedIOException>(() =>
                    {
                        writer.WritePosterFrame(image.PosterFrame!);
                        foreach (var frame in image.Frames)
                        {
                            writer.WriteFrame(frame);
                        }

                        writer.Complete();
                    });
                }

                Assert.Equal(0, writer.Core.Scope.LiveBytes);
                Assert.Throws<InvalidOperationException>(() => writer.WriteFrame(image.Frames[0])); // faulted
                Assert.True(stream.BytesWritten <= failAt);
            }
            finally
            {
                writer.Dispose();
            }

            Assert.False(stream.IsDisposed);
        }
    }

    [Fact]
    public async Task CancellationDuringALaterFrameFaultsTheWriter()
    {
        using var image = CreateAnimation<Rgba64>(200, 100, frameCount: 3, poster: false, seed: 13);
        using var source = CancellationTokenSource.CreateLinkedTokenSource(XunitCancellationToken);
        using var stream = new TestOutputStream { CancellationSource = source, CancelAtPosition = 200 * 100 * 8 + 70_000 };
        await using var writer = Image.CreateWriter<Rgba64>(stream, new ImageWriterOptions(200, 100) { Encoder = new PngEncoder { CompressionLevel = CompressionLevel.NoCompression }, ExpectedFrameCount = 3 });
        await writer.WriteFrameAsync(image.Frames[0], source.Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => writer.WriteFrameAsync(image.Frames[1], source.Token).AsTask());
        Assert.Equal(0, writer.Core.Scope.LiveBytes);
        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.CompleteAsync(XunitCancellationToken).AsTask());
    }

    [Fact]
    public async Task FailedPathSavesPreserveTheDestinationAndPublishNothing()
    {
        var directory = ImageWriterTests.CreateDirectory();
        try
        {
            var path = directory / "out.apng";
            await File.WriteAllBytesAsync(path, "previous"u8.ToArray(), XunitCancellationToken);
            using var image = CreateAnimation<Rgba32>(8, 8, frameCount: 3, poster: true, seed: 14);

            // Late failures: the poster and frames 0-1 were written to the temporary file, then frame 2 cannot be leased (or,
            // asynchronously, the poster, which is leased before the first asynchronous write yields)
            image.Frames[2].ProcessPixelRows(_ => Assert.Throws<InvalidOperationException>(() => image.Save(path)));
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                Task? save = null;
                image.PosterFrame!.ProcessPixelRows(_ => save = image.SaveAsync(path, cancellationToken: XunitCancellationToken));
                await save!;
            });

            // Early failures: unrepresentable duration, pre-canceled token
            image.Frames[1].Metadata.Duration = new FrameDuration(1, 100_000);
            Assert.Throws<UnsupportedImageFeatureException>(() => image.Save(path));
            image.Frames[1].Metadata.Duration = new FrameDuration(1, 100);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => image.SaveAsync(path, cancellationToken: new CancellationToken(canceled: true)));
            Assert.Equal("previous"u8.ToArray(), await File.ReadAllBytesAsync(path, XunitCancellationToken));
            Assert.Equal(["out.apng"], Directory.GetFileSystemEntries(directory).Select(Path.GetFileName));

            // An uncompleted writer publishes nothing either
            using (var writer = Image.CreateWriter<Rgba32>(path, new ImageWriterOptions(8, 8) { ExpectedFrameCount = 3 }))
            {
                writer.WritePosterFrame(image.PosterFrame!);
                writer.WriteFrame(image.Frames[0]);
                writer.WriteFrame(image.Frames[1]);
            }

            Assert.Equal("previous"u8.ToArray(), await File.ReadAllBytesAsync(path, XunitCancellationToken));
            Assert.Equal(["out.apng"], Directory.GetFileSystemEntries(directory).Select(Path.GetFileName));

            await image.SaveAsync(path, cancellationToken: XunitCancellationToken);
            ApngOutputVerifier.Verify(await File.ReadAllBytesAsync(path, XunitCancellationToken), image);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(PixelFormat.Rgba32)]
    [InlineData(PixelFormat.Rgba64)]
    public void EditedAnimationsSaveTheIntendedDisplayedFrames(PixelFormat format)
    {
        // A decoded APNG with a poster and partial OVER/PREVIOUS frames: its delta data must not leak into the output
        using var image = CreateAnimation(format, 9, 6, frameCount: 4, poster: true, seed: 15);
        image.Animation = new AnimationMetadata { TotalPlays = 2 };
        using var decoded = Image.Load(Encode(image, new PngEncoder()));

        // Opaque frame 1 becomes transparent in a region of frame 2 (colors of transparent pixels kept by SOURCE)
        MakeOpaque(decoded.Frames[1]);
        decoded.Frames[2].ProcessPixelBytes(pixels =>
        {
            var bytesPerPixel = PixelFormats.GetBytesPerPixel(format);
            for (var y = 1; y < 4; y++)
            {
                var row = pixels.GetRowSpan(y);
                for (var x = 2; x < 7; x++)
                {
                    row.Slice((x * bytesPerPixel) + (3 * bytesPerPixel / 4), bytesPerPixel / 4).Clear(); // alpha = 0
                }
            }
        });
        decoded.MoveFrame(3, 0);
        decoded.RemoveFrame(2);
        decoded.Frames[0].Metadata.Duration = new FrameDuration(7, 9);
        var beforeResize = Encode(decoded, new PngEncoder());
        ApngOutputVerifier.Verify(beforeResize, decoded);
        decoded.Resize(new ResizeOptions(13, 5) { Mode = ResizeMode.Stretch }, XunitCancellationToken);
        var png = Encode(decoded, new PngEncoder { Interlaced = true });
        var reference = ApngOutputVerifier.Verify(png, decoded);
        Assert.Equal(3, reference.Animation!.NumFrames);
        Assert.Equal(2, reference.Animation.NumPlays);
        AssertRoundTripsThroughLibraryDecoder(png, decoded);
    }

    private static void MakeOpaque(ImageFrame frame)
    {
        var bytesPerPixel = PixelFormats.GetBytesPerPixel(frame.PixelFormat);
        frame.ProcessPixelBytes(pixels =>
        {
            for (var y = 0; y < pixels.Height; y++)
            {
                var row = pixels.GetRowSpan(y);
                for (var x = 0; x < pixels.Width; x++)
                {
                    row.Slice((x * bytesPerPixel) + (3 * bytesPerPixel / 4), bytesPerPixel / 4).Fill(0xFF);
                }
            }
        });
    }

    internal static Image<TPixel> CreateAnimation<TPixel>(int width, int height, int frameCount, bool poster, int seed)
        where TPixel : unmanaged
        => (Image<TPixel>)CreateAnimation(PixelFormats.GetPixelFormat<TPixel>(), width, height, frameCount, poster, seed);

    /// <summary>Creates an animation of seeded random frames (durations 1/10, 1/3, 0, 1/10...) with an optional separate poster.</summary>
    internal static Image CreateAnimation(PixelFormat format, int width, int height, int frameCount, bool poster, int seed)
    {
        var image = PngEncoderTests.CreateRandom(format, width, height, seed);
        for (var i = 1; i < frameCount; i++)
        {
            using var frame = PngEncoderTests.CreateRandom(format, width, height, seed + (i * 1000));
            image.AppendFrame(frame.Frames[0]);
        }

        FrameDuration[] durations = [new(1, 10), new(1, 3), FrameDuration.Zero];
        for (var i = 0; i < frameCount; i++)
        {
            image.Frames[i].Metadata.Duration = durations[i % durations.Length];
        }

        if (poster)
        {
            using var posterSource = PngEncoderTests.CreateRandom(format, width, height, seed + 999_999);
            image.SetPosterFrame(posterSource.Frames[0]);
        }

        return image;
    }

    private static byte[] Encode(Image image, PngEncoder encoder)
    {
        using var stream = new TestOutputStream();
        image.Save(stream, encoder);
        return stream.ToArray();
    }

    private static void AssertRoundTripsThroughLibraryDecoder(byte[] png, Image image)
    {
        // Supplementary only: both sides widened to Rgba64 (the APNG working layout for 16-bit data)
        using var expected = image.CloneAs<Rgba64>();
        using var decoded = Image.Load<Rgba64>(png);
        Assert.Equal(image.Frames.Count, decoded.Frames.Count);
        Assert.Equal(image.Animation?.TotalPlays, decoded.Animation?.TotalPlays);
        for (var i = 0; i < image.Frames.Count; i++)
        {
            Assert.Equal(image.Frames[i].Metadata.Duration, decoded.Frames[i].Metadata.Duration);
            var result = PixelBufferComparer.Compare(ImageSnapshots.CaptureFrame(expected.Frames[i]), ImageSnapshots.CaptureFrame(decoded.Frames[i]), ComparisonPolicy.Exact, $"library decoder round trip, frame {i}");
            Assert.True(result.IsMatch, result.Describe());
        }

        Assert.Equal(image.PosterFrame is null, decoded.PosterFrame is null);
        if (expected.PosterFrame is { } poster)
        {
            var result = PixelBufferComparer.Compare(ImageSnapshots.CaptureFrame(poster), ImageSnapshots.CaptureFrame(decoded.PosterFrame!), ComparisonPolicy.Exact, "library decoder round trip, poster");
            Assert.True(result.IsMatch, result.Describe());
        }
    }

    /// <summary>
    /// The nearest fraction to <c>p/q</c> with numerator and denominator in [0, 65535] / [1, 65535], ties to the larger one,
    /// by exhaustive search over the denominators (independent of the Stern–Brocot implementation).
    /// </summary>
    private static (long Numerator, long Denominator) NearestBySearch(long p, long q)
    {
        BigInteger bestNumerator = 0, bestDenominator = 1;
        for (BigInteger b = 1; b <= ushort.MaxValue; b++)
        {
            var floor = p * b / q;
            for (var a = floor; a <= floor + 1; a++)
            {
                if (a > ushort.MaxValue)
                    continue;

                // |a/b - p/q| compared with |best| by cross multiplication: |a q - p b| / (b q)
                var distance = BigInteger.Abs((a * q) - (p * b)) * bestDenominator;
                var bestDistance = BigInteger.Abs((bestNumerator * q) - (p * bestDenominator)) * b;
                if (distance < bestDistance || (distance == bestDistance && a * bestDenominator > bestNumerator * b))
                {
                    bestNumerator = a;
                    bestDenominator = b;
                }
            }
        }

        var gcd = BigInteger.GreatestCommonDivisor(bestNumerator, bestDenominator);
        return gcd.IsZero ? (0, 1) : ((long)(bestNumerator / gcd), (long)(bestDenominator / gcd));
    }
}
