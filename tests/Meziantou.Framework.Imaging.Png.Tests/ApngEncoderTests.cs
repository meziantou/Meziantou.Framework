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
        var directory = TestFiles.CreateDirectory();
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
        var directory = TestFiles.CreateDirectory();
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

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void IndexedAnimationsShareOnePaletteOfEveryFrameAndThePoster(bool poster, bool interlaced)
    {
        // Each image has its own colors; an eager save knows them all before it writes the palette
        using var image = CreateIndexedAnimation(9, 5, frameCount: 3, poster, colorsPerImage: 6, seed: 20);
        image.Animation = new AnimationMetadata { TotalPlays = 2 };
        var png = Encode(image, new PngEncoder { PixelLayout = PngPixelLayout.Indexed, Interlaced = interlaced });
        var reference = ApngOutputVerifier.Verify(png, image);

        var stills = Enumerable.Range(0, image.Frames.Count).Select(index => ImageSnapshots.CaptureFrame(image.Frames[index])).ToList();
        var firstImage = IndexedPngExpectation.Create(poster ? ImageSnapshots.CaptureFrame(image.PosterFrame!) : stills[0]);
        if (poster)
        {
            stills.Add(ImageSnapshots.CaptureFrame(image.PosterFrame!));
        }

        var expectation = IndexedPngExpectation.Create(stills);
        Assert.True(expectation.ColorCount > 16 && firstImage.ColorCount <= 16, "The union of the images must need a deeper palette than the first one.");
        Assert.Equal((byte)3, reference.ColorType);
        Assert.Equal(8, reference.BitDepth);
        Assert.Equal(expectation.Palette.ToArray(), reference.Palette.ToArray());
        Assert.Equal(expectation.Transparency.ToArray(), reference.Transparency.ToArray());

        // One palette for the whole file, after acTL and before the first image and its fcTL
        string[] expectedTypes = poster
            ? ["IHDR", "acTL", .. expectation.PaletteChunkTypes, "IDAT", "fcTL", "fdAT", "fcTL", "fdAT", "fcTL", "fdAT", "IEND"]
            : ["IHDR", "acTL", .. expectation.PaletteChunkTypes, "fcTL", "IDAT", "fcTL", "fdAT", "fcTL", "fdAT", "IEND"];
        Assert.Equal(expectedTypes, reference.ChunkTypes);

        // Supplementary: the library decodes the same frames
        using var decoded = Image.Load<Rgba32>(png);
        Assert.Equal(3, decoded.Frames.Count);
        for (var i = 0; i < 3; i++)
        {
            var result = PixelBufferComparer.Compare(ImageSnapshots.CaptureFrame(image.Frames[i]), ImageSnapshots.CaptureFrame(decoded.Frames[i]), ComparisonPolicy.Exact, $"library decoder round trip, frame {i}");
            Assert.True(result.IsMatch, result.Describe());
        }
    }

    [Fact]
    public void IndexedAnimationsNeverReduceTheColorsOfTheirImagesTogether()
    {
        // Every image fits a palette alone, their union does not: the eager save fails before the first byte
        using var image = CreateIndexedAnimation(20, 10, frameCount: 2, poster: true, colorsPerImage: 100, seed: 30);
        Assert.True(IndexedPngExpectation.Create(Enumerable.Range(0, image.Frames.Count).Select(index => ImageSnapshots.CaptureFrame(image.Frames[index])).Append(ImageSnapshots.CaptureFrame(image.PosterFrame!))).ColorCount > 256);
        using var stream = new TestOutputStream();
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => image.Save(stream, new PngEncoder { PixelLayout = PngPixelLayout.Indexed }));
        Assert.Equal("Color reduction", exception.Feature);
        Assert.Equal(0, stream.BytesWritten);

        // Without the poster the two frames fit
        image.RemovePosterFrame();
        ApngOutputVerifier.Verify(Encode(image, new PngEncoder { PixelLayout = PngPixelLayout.Indexed }), image);
    }

    [Fact]
    public void SequentialWritersTakeTheIndexedPaletteFromTheFirstImage()
    {
        var red = new Rgba32(255, 0, 0, 255);
        var green = new Rgba32(0, 255, 0, 255);
        var veil = new Rgba32(0, 0, 255, 100);
        using var first = CreateFrame(3, red, green, veil, red, red, green);
        using var second = CreateFrame(3, veil, veil, veil, green, green, green);
        using var third = CreateFrame(3, green, red, green, red, green, red);
        using var stream = new TestOutputStream();
        using var writer = Image.CreateWriter<Rgba32>(stream, new ImageWriterOptions(3, 2) { Encoder = new PngEncoder { AnimationMode = PngAnimationMode.Animated, PixelLayout = PngPixelLayout.Indexed }, ExpectedFrameCount = 3 });

        writer.WriteFrame(first.Frames[0]);
        var written = stream.BytesWritten;

        // A color that is not in the written palette: the same color with another alpha, then a new color
        foreach (var color in (Rgba32[])[new Rgba32(0, 0, 255, 101), new Rgba32(1, 0, 0, 255)])
        {
            using var other = CreateFrame(3, red, green, veil, red, color, green);
            var exception = Assert.Throws<UnsupportedImageFeatureException>(() => writer.WriteFrame(other.Frames[0]));
            Assert.Equal("Color reduction", exception.Feature);
            Assert.Contains("(1, 1)", exception.Message);
            Assert.Equal(written, stream.BytesWritten); // nothing of the rejected frame was written
            Assert.Equal(1, writer.FramesWritten);
        }

        writer.WriteFrame(second.Frames[0]);
        writer.WriteFrame(third.Frames[0]);
        writer.Complete();

        using var expected = first.Clone();
        expected.AppendFrame(second.Frames[0]);
        expected.AppendFrame(third.Frames[0]);
        expected.Animation = new AnimationMetadata(); // the writer default: infinite
        var reference = ApngOutputVerifier.Verify(stream.ToArray(), expected);
        Assert.Equal((3, 2), (reference.ColorType, reference.BitDepth));
        Assert.Equal([0, 0, 255, 0, 255, 0, 255, 0, 0], reference.Palette.ToArray());
        Assert.Equal([100], reference.Transparency.ToArray());
    }

    [Theory]
    [InlineData(false, DitheringMode.None)]
    [InlineData(true, DitheringMode.FloydSteinberg)]
    public void IndexedColorReductionSharesOnePaletteOfEveryFrameAndThePoster(bool poster, DitheringMode dithering)
    {
        // Every image has its own colors, too many for a palette
        using var image = CreateIndexedAnimation(30, 20, frameCount: 3, poster, colorsPerImage: 150, seed: 50);
        image.Animation = new AnimationMetadata { TotalPlays = 2 };
        var quantization = new QuantizeOptions { MaxColors = 64, Dithering = dithering };

        var png = Encode(image, new PngEncoder { PixelLayout = PngPixelLayout.Indexed, AllowColorReduction = true, Quantization = quantization });

        // The file of the animation reduced first to one palette built from every image
        using var expected = image.Clone();
        ImagePalette palette;
        using (var builder = new PaletteBuilder())
        {
            builder.Add(expected, TestContext.Current.CancellationToken);
            palette = builder.Build(64);
        }

        expected.Quantize(new QuantizeOptions { Palette = palette, Dithering = dithering }, TestContext.Current.CancellationToken);
        Assert.Equal(Encode(expected, new PngEncoder { PixelLayout = PngPixelLayout.Indexed }), png);
        var reference = ApngOutputVerifier.Verify(png, expected);
        Assert.InRange(reference.Palette.Length / 3, 2, 64);
    }

    [Fact]
    public void SequentialWritersReduceLaterFramesToThePaletteOfTheFirstImage()
    {
        var red = new Rgba32(255, 0, 0, 255);
        var green = new Rgba32(0, 255, 0, 255);
        var veil = new Rgba32(0, 0, 255, 100);
        using var first = CreateFrame(3, red, green, veil, red, red, green);
        using var second = CreateFrame(3, new Rgba32(250, 10, 0), new Rgba32(10, 240, 0), new Rgba32(0, 0, 250, 90), green, default, red);
        using var stream = new TestOutputStream();
        var encoder = new PngEncoder { AnimationMode = PngAnimationMode.Animated, PixelLayout = PngPixelLayout.Indexed, AllowColorReduction = true };
        using (var writer = Image.CreateWriter<Rgba32>(stream, new ImageWriterOptions(3, 2) { Encoder = encoder, ExpectedFrameCount = 2 }))
        {
            writer.WriteFrame(first.Frames[0]);
            writer.WriteFrame(second.Frames[0]);
            writer.Complete();
        }

        // The palette holds the colors of the first frame; the second frame gets its nearest entries (and a transparent
        // pixel the nearest entry of transparent black, the veil)
        using var expected = first.Clone();
        using var mapped = CreateFrame(3, red, green, veil, green, veil, red);
        expected.AppendFrame(mapped.Frames[0]);
        expected.Animation = new AnimationMetadata();
        var reference = ApngOutputVerifier.Verify(stream.ToArray(), expected);
        Assert.Equal([0, 0, 255, 0, 255, 0, 255, 0, 0], reference.Palette.ToArray());
    }

    [Fact]
    public void SequentialWritersReduceToThePaletteOfTheFirstAcceptedImage()
    {
        // The first frame is rejected (its duration does not fit APNG): its colors must not become the palette
        var red = new Rgba32(255, 0, 0, 255);
        var blue = new Rgba32(0, 0, 255, 255);
        using var rejected = CreateFrame(2, red, red, red, red);
        rejected.Frames[0].Metadata.Duration = new FrameDuration(65536, 1);
        using var first = CreateFrame(2, blue, blue, blue, new Rgba32(0, 0, 0, 255));
        using var second = CreateFrame(2, new Rgba32(250, 0, 0, 255), blue, blue, blue);
        using var stream = new TestOutputStream();
        var encoder = new PngEncoder { AnimationMode = PngAnimationMode.Animated, PixelLayout = PngPixelLayout.Indexed, AllowColorReduction = true };
        using (var writer = Image.CreateWriter<Rgba32>(stream, new ImageWriterOptions(2, 2) { Encoder = encoder, ExpectedFrameCount = 2 }))
        {
            Assert.Throws<UnsupportedImageFeatureException>(() => writer.WriteFrame(rejected.Frames[0]));
            Assert.Equal(0, stream.BytesWritten);
            writer.WriteFrame(first.Frames[0]);
            writer.WriteFrame(second.Frames[0]);
            writer.Complete();
        }

        // The red pixel of the second frame gets the nearest color of the first frame: black
        using var expected = first.Clone();
        using var mapped = CreateFrame(2, new Rgba32(0, 0, 0, 255), blue, blue, blue);
        expected.AppendFrame(mapped.Frames[0]);
        expected.Animation = new AnimationMetadata();
        var reference = ApngOutputVerifier.Verify(stream.ToArray(), expected);
        Assert.Equal([0, 0, 0, 0, 0, 255], reference.Palette.ToArray());
    }

    [Fact]
    public void SequentialWritersDropTheColorsOfARejectedFirstIndexedImage()
    {
        // The poster holds too many colors: rejected, and none of its colors stays in the palette of the file
        using var tooMany = PngEncoderTests.CreateWithColors<Rgba32>(20, 13, colors: 257, seed: 8);
        using var image = PngEncoderTests.CreateWithColors<Rgba32>(20, 13, colors: 2, seed: 40);
        using (var copy = image.Clone())
        {
            // The second frame uses the same two colors
            image.AppendFrame(copy.Frames[0]);
        }

        image.Frames[0].Metadata.Duration = new FrameDuration(1, 10);
        image.Frames[1].Metadata.Duration = new FrameDuration(1, 3);
        using var stream = new TestOutputStream();
        using var writer = Image.CreateWriter<Rgba32>(stream, new ImageWriterOptions(20, 13) { Encoder = new PngEncoder { AnimationMode = PngAnimationMode.Animated, PixelLayout = PngPixelLayout.Indexed }, ExpectedFrameCount = 2 });
        Assert.Throws<UnsupportedImageFeatureException>(() => writer.WritePosterFrame(tooMany.Frames[0]));
        Assert.Equal(0, stream.BytesWritten);

        writer.WriteFrame(image.Frames[0]);
        writer.WriteFrame(image.Frames[1]);
        writer.Complete();
        image.Animation = new AnimationMetadata();
        var reference = ApngOutputVerifier.Verify(stream.ToArray(), image);
        Assert.Equal((byte)1, reference.BitDepth);
        Assert.Equal(IndexedPngExpectation.Create(ImageSnapshots.CaptureFrame(image.Frames[0])).Palette.ToArray(), reference.Palette.ToArray());
    }

    [Theory]
    [MemberData(nameof(LayoutCases))]
    public void MinimumSizeComposesEveryFrameExactly(PixelFormat format, bool poster, bool interlaced)
    {
        using var image = CreateAnimation(format, 13, 9, frameCount: 4, poster, seed: (int)format * 7);
        image.Animation = new AnimationMetadata { TotalPlays = 2 };
        var png = Encode(image, new PngEncoder { Optimization = PngOptimization.MinimumSize, Interlaced = interlaced });
        var reference = OptimizedPngOutputVerifier.VerifyAnimation(png, image);
        Assert.Equal(interlaced ? 1 : 0, reference.InterlaceMethod);
        Assert.Equal(["1/10", "1/3", "0/1", "1/10"], EncodedFieldInspector.Inspect("png", png).FrameDelays.Select(delay => $"{delay!.Numerator}/{delay.Denominator}"));
        AssertRoundTripsThroughLibraryDecoder(png, image);
    }

    [Theory]
    [InlineData(PixelFormat.Rgba32)]
    [InlineData(PixelFormat.Rgb24)]
    [InlineData(PixelFormat.Rgba64)]
    [InlineData(PixelFormat.Gray8)]
    public void MinimumSizeWritesTheRegionsThatChange(PixelFormat format)
    {
        // A static scene with a small square that moves: the later frames store the part of the canvas that changed
        using var image = CreateScene(format, 48, 32, frameCount: 5, transparentBackground: false, seed: 3);
        var png = Encode(image, new PngEncoder { Optimization = PngOptimization.MinimumSize });
        var reference = OptimizedPngOutputVerifier.VerifyAnimation(png, image);
        var frames = reference.Animation!.Frames;
        Assert.True(frames[0].IsFullCanvasSource(48, 32));
        Assert.All(frames.Skip(1), frame => Assert.True(frame.Width * frame.Height < 48 * 32 / 2, $"region {frame.Width}x{frame.Height}"));
        Assert.HasCountLessThan(Encode(image, new PngEncoder()).Length / 2, png);
        AssertRoundTripsThroughLibraryDecoder(png, image);
    }

    [Fact]
    public void MinimumSizeAddsAColorKeySoThatOpaqueFramesSkipTheirUnchangedPixels()
    {
        // Every pixel is opaque: an unused color becomes the tRNS key, the transparent value of OVER frames
        using var image = CreateScene(PixelFormat.Rgb24, 64, 48, frameCount: 4, transparentBackground: false, seed: 9, changes: 2);
        var png = Encode(image, new PngEncoder { Optimization = PngOptimization.MinimumSize });
        var reference = OptimizedPngOutputVerifier.VerifyAnimation(png, image);
        Assert.Equal((byte)2, reference.ColorType);
        Assert.Contains("tRNS", reference.ChunkTypes);
        Assert.Contains((byte)1, reference.Animation!.Frames.Select(frame => frame.BlendOp).ToList());
        AssertRoundTripsThroughLibraryDecoder(png, image);
    }

    [Fact]
    public void MinimumSizeClearsTheRegionOfAFrameWhenTheNextOneDoesNotDrawThere()
    {
        // A square on a transparent background jumps across the canvas: BACKGROUND erases it, so the next frame only
        // draws the new square
        using var image = new Image<Rgba32>(40, 40);
        image.Frames[0].Metadata.Duration = new FrameDuration(1, 10);
        FillSquare(image.Frames[0], 2, 2, 6, new Rgba32(200, 30, 30, 255));
        using (var next = new Image<Rgba32>(40, 40))
        {
            FillSquare(next.Frames[0], 30, 30, 6, new Rgba32(200, 30, 30, 255));
            next.Frames[0].Metadata.Duration = new FrameDuration(1, 10);
            image.AppendFrame(next.Frames[0]);
        }

        var png = Encode(image, new PngEncoder { Optimization = PngOptimization.MinimumSize });
        var reference = OptimizedPngOutputVerifier.VerifyAnimation(png, image);
        var frames = reference.Animation!.Frames;
        Assert.Equal((byte)1, frames[0].DisposeOp);
        Assert.Equal((30, 30, 6, 6), (frames[1].XOffset, frames[1].YOffset, frames[1].Width, frames[1].Height));
        AssertRoundTripsThroughLibraryDecoder(png, image);
    }

    [Fact]
    public void MinimumSizeRestoresTheCanvasWhenAFrameIsAnOverlay()
    {
        // A background, then two frames that each add a square far from the other: PREVIOUS restores the background,
        // so the third frame only draws its own square
        var background = new Rgba32(10, 120, 200, 255);
        using var image = new Image<Rgba32>(40, 40);
        image.Frames[0].Metadata.Duration = new FrameDuration(1, 10);
        FillSquare(image.Frames[0], 0, 0, 40, background);
        foreach (var (x, y) in new[] { (2, 2), (30, 30) })
        {
            using var overlay = new Image<Rgba32>(40, 40);
            FillSquare(overlay.Frames[0], 0, 0, 40, background);
            FillSquare(overlay.Frames[0], x, y, 6, new Rgba32(250, 250, 10, 255));
            overlay.Frames[0].Metadata.Duration = new FrameDuration(1, 10);
            image.AppendFrame(overlay.Frames[0]);
        }

        var png = Encode(image, new PngEncoder { Optimization = PngOptimization.MinimumSize });
        var reference = OptimizedPngOutputVerifier.VerifyAnimation(png, image);
        var frames = reference.Animation!.Frames;
        Assert.Equal((byte)2, frames[1].DisposeOp);
        Assert.Equal((30, 30, 6, 6), (frames[2].XOffset, frames[2].YOffset, frames[2].Width, frames[2].Height));
        AssertRoundTripsThroughLibraryDecoder(png, image);
    }

    [Fact]
    public void MinimumSizeNeverBlendsWhatOverWouldRound()
    {
        // Partially transparent pixels that change on a visible canvas: OVER would depend on rounding, so SOURCE is used
        using var image = new Image<Rgba32>(16, 16);
        FillSquare(image.Frames[0], 0, 0, 16, new Rgba32(90, 90, 90, 255));
        image.Frames[0].Metadata.Duration = new FrameDuration(1, 10);
        for (var i = 1; i < 4; i++)
        {
            using var frame = new Image<Rgba32>(16, 16);
            FillSquare(frame.Frames[0], 0, 0, 16, new Rgba32(90, 90, 90, 255));
            FillSquare(frame.Frames[0], i * 3, 4, 5, new Rgba32(200, 10, 10, (byte)(60 * i)));
            frame.Frames[0].Metadata.Duration = new FrameDuration(1, 10);
            image.AppendFrame(frame.Frames[0]);
        }

        var png = Encode(image, new PngEncoder { Optimization = PngOptimization.MinimumSize });
        OptimizedPngOutputVerifier.VerifyAnimation(png, image);
        AssertRoundTripsThroughLibraryDecoder(png, image);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MinimumSizeStoresAnUnchangedFrameAsOnePixel(bool poster)
    {
        using var image = CreateScene(PixelFormat.Rgba32, 30, 20, frameCount: 1, transparentBackground: true, seed: 4);
        image.Frames[0].Metadata.Duration = new FrameDuration(1, 10);
        image.AppendFrame(image.Frames[0]);
        image.Frames[1].Metadata.Duration = new FrameDuration(1, 3);
        if (poster)
        {
            using var posterSource = PngEncoderTests.CreateRandom<Rgba32>(30, 20, seed: 5);
            image.SetPosterFrame(posterSource.Frames[0]);
        }

        var png = Encode(image, new PngEncoder { Optimization = PngOptimization.MinimumSize });
        var reference = OptimizedPngOutputVerifier.VerifyAnimation(png, image);
        Assert.Equal((1, 1), (reference.Animation!.Frames[1].Width, reference.Animation.Frames[1].Height));
        AssertRoundTripsThroughLibraryDecoder(png, image);
    }

    [Fact]
    public void MinimumSizeChoosesTheLayoutFromEveryFrameOfAnEagerSave()
    {
        // The first frame is opaque and the second has alpha: a save sees both, so the layout has alpha
        using var image = (Image<Rgba32>)CreateScene(PixelFormat.Rgba32, 20, 20, frameCount: 2, transparentBackground: false, seed: 6);
        image.Frames[1].ProcessPixelRows(rows => rows.GetRowSpan(3)[5] = new Rgba32(1, 2, 3, 128));
        var png = Encode(image, new PngEncoder { Optimization = PngOptimization.MinimumSize });
        var reference = OptimizedPngOutputVerifier.VerifyAnimation(png, image);
        Assert.Equal((byte)6, reference.ColorType);
    }

    [Fact]
    public void MinimumSizeSequentialWritersDecideTheLayoutFromTheFirstImageAndDeferEachFrame()
    {
        using var image = (Image<Rgba32>)CreateScene(PixelFormat.Rgba32, 20, 20, frameCount: 3, transparentBackground: false, seed: 6);
        using var alpha = image.CloneAs<Rgba32>();
        alpha.Frames[1].ProcessPixelRows(rows => rows.GetRowSpan(3)[5] = new Rgba32(1, 2, 3, 128));
        using var stream = new TestOutputStream();
        using (var writer = Image.CreateWriter<Rgba32>(stream, new ImageWriterOptions(20, 20) { Encoder = new PngEncoder { Optimization = PngOptimization.MinimumSize, AnimationMode = PngAnimationMode.Animated }, ExpectedFrameCount = 3 }))
        {
            writer.WriteFrame(image.Frames[0]);

            // The header is written, but frame zero waits for the disposal the next frame chooses
            var written = stream.ToArray();
            Assert.Equal(-1, written.AsSpan().IndexOf("fcTL"u8));

            // The layout of the first image (opaque RGB) cannot store alpha: the frame is rejected and nothing is written
            var length = stream.BytesWritten;
            var exception = Assert.Throws<UnsupportedImageFeatureException>(() => writer.WriteFrame(alpha.Frames[1]));
            Assert.Equal("Alpha removal", exception.Feature);
            Assert.Equal(length, stream.BytesWritten);

            writer.WriteFrame(image.Frames[1]);
            writer.WriteFrame(image.Frames[2]);
            writer.Complete();
            Assert.Equal(0, writer.Core.Scope.LiveBytes);
        }

        image.Animation = new AnimationMetadata();
        var reference = OptimizedPngOutputVerifier.VerifyAnimation(stream.ToArray(), image);
        Assert.Equal((byte)2, reference.ColorType);
    }

    [Fact]
    public void MinimumSizeSequentialWritersAddNoColorKey()
    {
        // A later frame could use any color: only an eager save, which sees every frame, adds an unused color key
        using var image = (Image<Rgb24>)CreateScene(PixelFormat.Rgb24, 24, 16, frameCount: 2, transparentBackground: false, seed: 2);
        using var stream = new TestOutputStream();
        using (var writer = Image.CreateWriter<Rgb24>(stream, new ImageWriterOptions(24, 16) { Encoder = new PngEncoder { Optimization = PngOptimization.MinimumSize, AnimationMode = PngAnimationMode.Animated }, ExpectedFrameCount = 2 }))
        {
            writer.WriteFrame(image.Frames[0]);
            writer.WriteFrame(image.Frames[1]);
            writer.Complete();
        }

        image.Animation = new AnimationMetadata();
        var reference = OptimizedPngOutputVerifier.VerifyAnimation(stream.ToArray(), image);
        Assert.DoesNotContain("tRNS", reference.ChunkTypes);
        Assert.All(reference.Animation!.Frames, frame => Assert.Equal((byte)0, frame.BlendOp));
    }

    [Fact]
    public void MinimumSizeIndexedAnimationsAddATransparentEntryWhenOverFramesPayForIt()
    {
        // A noisy background of 200 colors with two small squares that change in opposite corners: the region of each frame
        // covers the canvas, and OVER skips its unchanged pixels with a transparent palette entry
        using var image = PngEncoderTests.CreateWithColors<Rgba32>(40, 30, colors: 200, seed: 8);
        MakeOpaque(image.Frames[0]);
        image.Frames[0].Metadata.Duration = new FrameDuration(1, 10);
        Rgba32[] colors = [new(250, 0, 0, 255), new(0, 250, 0, 255), new(0, 0, 250, 255)];
        for (var i = 1; i < 4; i++)
        {
            using var frame = image.CloneAs<Rgba32>();
            FillSquare(frame.Frames[0], 0, 0, 2, colors[i % 3]);
            FillSquare(frame.Frames[0], 38, 28, 2, colors[(i + 1) % 3]);
            image.AppendFrame(frame.Frames[0]).Metadata.Duration = new FrameDuration(1, 10);
        }

        var png = Encode(image, new PngEncoder { Optimization = PngOptimization.MinimumSize, PixelLayout = PngPixelLayout.Indexed });
        var reference = OptimizedPngOutputVerifier.VerifyAnimation(png, image);
        Assert.Equal((byte)3, reference.ColorType);
        Assert.Equal((byte)8, reference.BitDepth); // the frame regions of an animation keep whole bytes per index
        Assert.Equal((byte)0, reference.Transparency.Span[0]);
        Assert.Contains((byte)1, reference.Animation!.Frames.Select(frame => frame.BlendOp).ToList());
        AssertRoundTripsThroughLibraryDecoder(png, image);

        // Frames that share no pixel: OVER cannot skip anything, so the palette gets no transparent entry
        using var random = CreateIndexedAnimation(18, 12, frameCount: 3, poster: true, colorsPerImage: 5, seed: 8);
        MakeOpaque(random.Frames[0]);
        MakeOpaque(random.Frames[1]);
        MakeOpaque(random.Frames[2]);
        MakeOpaque(random.PosterFrame!);
        png = Encode(random, new PngEncoder { Optimization = PngOptimization.MinimumSize, PixelLayout = PngPixelLayout.Indexed });
        reference = OptimizedPngOutputVerifier.VerifyAnimation(png, random);
        Assert.Equal(0, reference.Transparency.Length);
    }

    [Theory]
    [MemberData(nameof(LayoutCases))]
    public void OptimizedFramesComposeEveryFrameExactly(PixelFormat format, bool poster, bool interlaced)
    {
        using var image = CreateAnimation(format, 13, 9, frameCount: 4, poster, seed: (int)format * 7);
        image.Animation = new AnimationMetadata { TotalPlays = 2 };
        var png = Encode(image, new PngEncoder { OptimizeFrames = true, Interlaced = interlaced });
        var reference = OptimizedPngOutputVerifier.VerifyAnimation(png, image);

        // The layout of the pixel format, as without the setting
        var full = ReferencePng.Parse(Encode(image, new PngEncoder { Interlaced = interlaced }));
        Assert.Equal((full.ColorType, full.BitDepth, full.InterlaceMethod), (reference.ColorType, reference.BitDepth, reference.InterlaceMethod));
        Assert.True(reference.Animation!.Frames[0].IsFullCanvasSource(13, 9));
        Assert.Equal(["1/10", "1/3", "0/1", "1/10"], EncodedFieldInspector.Inspect("png", png).FrameDelays.Select(delay => $"{delay!.Numerator}/{delay.Denominator}"));
        AssertRoundTripsThroughLibraryDecoder(png, image);
    }

    public static TheoryData<string, bool> OptimizedFrameCases()
    {
        var data = new TheoryData<string, bool>();
        foreach (var name in DeltaFrameAnimations.Names)
        {
            data.Add(name, false);
            data.Add(name, true);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(OptimizedFrameCases))]
    public void OptimizedFramesReproduceEveryDisplayedFrame(string name, bool indexed)
    {
        using var image = DeltaFrameAnimations.Create(name);
        var layout = indexed ? PngPixelLayout.Indexed : PngPixelLayout.Auto;
        if (indexed && !TrySave(image, new PngEncoder { PixelLayout = layout }))
            return; // more than 256 colors in the animation: a palette cannot hold them, with or without the setting

        var png = Encode(image, new PngEncoder { OptimizeFrames = true, PixelLayout = layout });
        var reference = OptimizedPngOutputVerifier.VerifyAnimation(png, image, name);
        Assert.Equal(3u, reference.Animation!.NumPlays);
        Assert.Equal((byte)8, reference.BitDepth);
        AssertRoundTripsThroughLibraryDecoder(png, image);
        Assert.InRange(png.Length, 1, Encode(image, new PngEncoder { PixelLayout = layout }).Length);
    }

    [Theory]
    [InlineData(PixelFormat.Rgba32)]
    [InlineData(PixelFormat.Rgb24)]
    [InlineData(PixelFormat.Rgba64)]
    [InlineData(PixelFormat.Gray8)]
    public void OptimizedFramesWriteTheRegionsThatChange(PixelFormat format)
    {
        // A static scene with a small square that moves: the later frames store the part of the canvas that changed
        using var image = CreateScene(format, 48, 32, frameCount: 5, transparentBackground: false, seed: 3);
        var png = Encode(image, new PngEncoder { OptimizeFrames = true });
        var reference = OptimizedPngOutputVerifier.VerifyAnimation(png, image);
        var frames = reference.Animation!.Frames;
        Assert.True(frames[0].IsFullCanvasSource(48, 32));
        Assert.All(frames.Skip(1), frame => Assert.True(frame.Width * frame.Height < 48 * 32 / 2, $"region {frame.Width}x{frame.Height}"));

        // Opaque pixel formats have no transparent value: every region replaces the canvas
        Assert.All(frames, frame => Assert.True(frame.BlendOp == 0 || PixelFormats.HasAlpha(format)));
        Assert.HasCountLessThan(Encode(image, new PngEncoder()).Length / 2, png);
        AssertRoundTripsThroughLibraryDecoder(png, image);
    }

    [Fact]
    public void OptimizedFramesChooseTheDisposalOfThePreviousFrame()
    {
        using var transparent = DeltaFrameAnimations.Create("sprite-transparent");
        var frames = OptimizedPngOutputVerifier.VerifyAnimation(Encode(transparent, new PngEncoder { OptimizeFrames = true }), transparent).Animation!.Frames;
        for (var i = 1; i < frames.Count; i++)
        {
            // BACKGROUND erases the previous sprite: the frame is only its own sprite
            Assert.Equal(PngFrameControl.DisposeBackground, frames[i - 1].DisposeOp);
            Assert.Equal((3 + (3 * i), 5 + i, 6, 5), (frames[i].XOffset, frames[i].YOffset, frames[i].Width, frames[i].Height));
        }

        using var blink = DeltaFrameAnimations.Create("blink");
        frames = OptimizedPngOutputVerifier.VerifyAnimation(Encode(blink, new PngEncoder { OptimizeFrames = true }), blink).Animation!.Frames;
        for (var i = 1; i < frames.Count; i++)
        {
            if (i % 2 == 1)
            {
                // The square appears, then PREVIOUS removes it: the next frame is unchanged
                Assert.Equal((10, 5, 4, 4), (frames[i].XOffset, frames[i].YOffset, frames[i].Width, frames[i].Height));
                if (i < frames.Count - 1)
                {
                    Assert.Equal(PngFrameControl.DisposePrevious, frames[i].DisposeOp);
                }
            }
            else
            {
                Assert.Equal((0, 0, 1, 1), (frames[i].XOffset, frames[i].YOffset, frames[i].Width, frames[i].Height));
            }
        }
    }

    [Theory]
    [InlineData(PixelFormat.Rgba32, PngFrameControl.BlendOver)]
    [InlineData(PixelFormat.Rgb24, PngFrameControl.BlendSource)]
    public void OptimizedFramesWriteUnchangedFramesAsOnePixel(PixelFormat format, byte blendOp)
    {
        using var source = DeltaFrameAnimations.Create("unchanged");
        using Image image = format == PixelFormat.Rgb24 ? source.CloneAs<Rgb24>() : source.CloneAs<Rgba32>();
        var reference = OptimizedPngOutputVerifier.VerifyAnimation(Encode(image, new PngEncoder { OptimizeFrames = true }), image);
        var frames = reference.Animation!.Frames;
        Assert.Equal(5, frames.Count);
        foreach (var i in new[] { 2, 4 })
        {
            // An OVER pixel is transparent; without a transparent value, the pixel is written again
            Assert.Equal((0, 0, 1, 1, blendOp), (frames[i].XOffset, frames[i].YOffset, frames[i].Width, frames[i].Height, frames[i].BlendOp));
            Assert.Equal(new FrameDuration(i + 1, 20), new FrameDuration(frames[i].DelayNumerator, frames[i].DelayDenominator));
        }
    }

    [Fact]
    public void OptimizedFramesBlendTranslucentPixelsOverTransparentOnes()
    {
        // Each frame adds a translucent diagonal on a transparent canvas: OVER gives a translucent pixel exactly on a fully
        // transparent canvas pixel (the reference reader rejects any other translucent OVER pixel, whose result depends on
        // the rounding of a decoder), and the other pixels of the region are left unchanged
        using var image = new Image<Rgba32>(24, 24);
        image.Frames[0].Metadata.Duration = new FrameDuration(1, 10);
        for (var i = 1; i < 4; i++)
        {
            var frame = image.AppendFrame(image.Frames[i - 1]);
            frame.ProcessPixelRows(rows =>
            {
                for (var k = 0; k < 5; k++)
                {
                    rows.GetRowSpan((5 * i) + k)[(5 * i) + k] = new Rgba32(10, 200, (byte)(i * 60), (byte)(40 * i));
                }
            });
        }

        image.Animation = new AnimationMetadata();
        var frames = OptimizedPngOutputVerifier.VerifyAnimation(Encode(image, new PngEncoder { OptimizeFrames = true }), image).Animation!.Frames;
        for (var i = 1; i < frames.Count; i++)
        {
            Assert.Equal((5 * i, 5 * i, 5, 5, PngFrameControl.BlendOver), (frames[i].XOffset, frames[i].YOffset, frames[i].Width, frames[i].Height, frames[i].BlendOp));
        }
    }

    [Fact]
    public void OptimizedIndexedFramesHaveEightBitIndexesAndUseTheTransparentEntry()
    {
        // Some decoders misplace the packed pixels of a frame region: the 4 colors of this animation get 8-bit indexes
        using var image = DeltaFrameAnimations.Create("sprite-transparent");
        var png = Encode(image, new PngEncoder { OptimizeFrames = true, PixelLayout = PngPixelLayout.Indexed });
        var reference = OptimizedPngOutputVerifier.VerifyAnimation(png, image);
        Assert.Equal(((byte)3, (byte)8), (reference.ColorType, reference.BitDepth));
        Assert.Equal(ReferencePng.Parse(Encode(image, new PngEncoder { PixelLayout = PngPixelLayout.Indexed })).Palette.ToArray(), reference.Palette.ToArray());

        using var opaque = DeltaFrameAnimations.Create("sprite-opaque");
        var frames = OptimizedPngOutputVerifier.VerifyAnimation(Encode(opaque, new PngEncoder { OptimizeFrames = true, PixelLayout = PngPixelLayout.Indexed }), opaque).Animation!.Frames;
        Assert.All(frames, frame => Assert.Equal(PngFrameControl.BlendSource, frame.BlendOp));
        AssertRoundTripsThroughLibraryDecoder(png, image);
    }

    [Fact]
    public void OptimizedFramesAreWrittenWhenTheNextFrameIsGiven()
    {
        // The fcTL of a frame holds the disposal chosen by the next frame: a sequential writer writes each frame one frame late
        using var image = DeltaFrameAnimations.Create("sprite-transparent");
        var encoder = new PngEncoder { OptimizeFrames = true, AnimationMode = PngAnimationMode.Animated };
        using var stream = new TestOutputStream();
        using (var writer = Image.CreateWriter<Rgba32>(stream, new ImageWriterOptions(image.Width, image.Height) { Encoder = encoder, ExpectedFrameCount = image.Frames.Count, Animation = image.Animation }))
        {
            writer.WriteFrame(image.Frames[0]);
            Assert.Equal(-1, stream.ToArray().AsSpan().IndexOf("fcTL"u8));
            for (var i = 1; i < image.Frames.Count; i++)
            {
                writer.WriteFrame(image.Frames[i]);
                Assert.Equal(i == image.Frames.Count - 1 ? i + 1 : i, CountFrameControls(stream.ToArray()));
            }

            writer.Complete();
            Assert.Equal(0, writer.Core.Scope.LiveBytes);
        }

        Assert.Equal(Encode(image, encoder), stream.ToArray());
        OptimizedPngOutputVerifier.VerifyAnimation(stream.ToArray(), image);

        static int CountFrameControls(byte[] png)
        {
            var count = 0;
            for (var index = png.AsSpan().IndexOf("fcTL"u8); index >= 0; index = png.AsSpan(index + 4).IndexOf("fcTL"u8) is var next and >= 0 ? index + 4 + next : -1)
            {
                count++;
            }

            return count;
        }
    }

    [Fact]
    public void OptimizeFramesChangesNeitherStillImagesNorMinimumSizeOutput()
    {
        using var image = CreateScene(PixelFormat.Rgba32, 24, 16, frameCount: 3, transparentBackground: true, seed: 5);
        Assert.Equal(Encode(image, new PngEncoder { Optimization = PngOptimization.MinimumSize }), Encode(image, new PngEncoder { Optimization = PngOptimization.MinimumSize, OptimizeFrames = true }));
        while (image.Frames.Count > 1)
        {
            image.RemoveFrame(1);
        }

        Assert.Equal(Encode(image, new PngEncoder()), Encode(image, new PngEncoder { OptimizeFrames = true }));
    }

    private static bool TrySave(Image image, PngEncoder encoder)
    {
        try
        {
            _ = Encode(image, encoder);
            return true;
        }
        catch (UnsupportedImageFeatureException)
        {
            return false;
        }
    }

    /// <summary>
    /// Creates an animation of a seeded background (transparent, or random opaque colors) with small squares that move
    /// between frames (durations of 1/10).
    /// </summary>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    private static Image CreateScene(PixelFormat format, int width, int height, int frameCount, bool transparentBackground, int seed, int changes = 1)
    {
        var random = new Random(seed);
        using var background = transparentBackground ? new Image<Rgba32>(width, height) : PngEncoderTests.CreateRandom<Rgba32>(width, height, seed);
        MakeOpaque(background.Frames[0]);
        var frames = new List<Image<Rgba32>>();
        try
        {
            for (var i = 0; i < frameCount; i++)
            {
                var frame = background.CloneAs<Rgba32>();
                frames.Add(frame);
                for (var j = 0; j < changes; j++)
                {
                    var color = new Rgba32((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), 255);
                    FillSquare(frame.Frames[0], random.Next(width - 4), random.Next(height - 4), 4, color);
                }

                frame.Frames[0].Metadata.Duration = new FrameDuration(1, 10);
            }

            var result = Convert(frames[0], format);
            for (var i = 1; i < frameCount; i++)
            {
                using var converted = Convert(frames[i], format);
                result.AppendFrame(converted.Frames[0]);
                result.Frames[i].Metadata.Duration = new FrameDuration(1, 10);
            }

            result.Frames[0].Metadata.Duration = new FrameDuration(1, 10);
            return result;
        }
        finally
        {
            foreach (var frame in frames)
            {
                frame.Dispose();
            }
        }

        static Image Convert(Image<Rgba32> image, PixelFormat format) => format switch
        {
            PixelFormat.Rgba32 => image.CloneAs<Rgba32>(),
            PixelFormat.Rgb24 => image.CloneAs<Rgb24>(),
            PixelFormat.Rgba64 => image.CloneAs<Rgba64>(),
            _ => image.CloneAs<Gray8>(),
        };
    }

    private static void FillSquare(ImageFrame<Rgba32> frame, int x, int y, int size, Rgba32 color)
        => frame.ProcessPixelRows(rows =>
        {
            for (var row = y; row < y + size; row++)
            {
                rows.GetRowSpan(row).Slice(x, size).Fill(color);
            }
        });

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

    /// <summary>Creates an animation whose images each have their own seeded colors (durations 1/10, 1/3, 0, 1/10...).</summary>
    private static Image<Rgba32> CreateIndexedAnimation(int width, int height, int frameCount, bool poster, int colorsPerImage, int seed)
    {
        var image = PngEncoderTests.CreateWithColors<Rgba32>(width, height, colorsPerImage, seed);
        for (var i = 1; i < frameCount; i++)
        {
            using var frame = PngEncoderTests.CreateWithColors<Rgba32>(width, height, colorsPerImage, seed + (i * 1000));
            image.AppendFrame(frame.Frames[0]);
        }

        FrameDuration[] durations = [new(1, 10), new(1, 3), FrameDuration.Zero];
        for (var i = 0; i < frameCount; i++)
        {
            image.Frames[i].Metadata.Duration = durations[i % durations.Length];
        }

        if (poster)
        {
            using var posterSource = PngEncoderTests.CreateWithColors<Rgba32>(width, height, colorsPerImage, seed + 999_999);
            image.SetPosterFrame(posterSource.Frames[0]);
        }

        return image;
    }

    private static Image<Rgba32> CreateFrame(int width, params Rgba32[] pixels)
    {
        var image = new Image<Rgba32>(width, pixels.Length / width);
        image.Frames[0].ProcessPixelRows(rows =>
        {
            for (var y = 0; y < rows.Height; y++)
            {
                pixels.AsSpan(y * width, width).CopyTo(rows.GetRowSpan(y));
            }
        });

        image.Frames[0].Metadata.Duration = new FrameDuration(1, 10);
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
