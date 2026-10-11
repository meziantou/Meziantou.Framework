using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.TestHarness.Streams;
using Meziantou.Framework.Imaging.Tests.Codecs;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// The sequential reader lifecycle through the public API, with the test-only
/// <see cref="TestStreamCodec"/> (posters, delta frames on a private compositor, unknown counts): input variants, header
/// snapshots, owned results that outlive the reader and stay budgeted, <c>ReadFrameInto</c> reuse, poster rules, clean end
/// versus errors, limits, cancellation, overlapping calls, faults and disposal.
/// </summary>
public sealed class SequentialReaderTests
{
    private const int Width = 6;
    private const int Height = 4;
    private const PixelFormat Format = PixelFormat.Rgba32;

    public static TheoryData<InputVariant> ReaderVariants => [.. InputVariants.ReaderVariants];

    [Theory]
    [MemberData(nameof(ReaderVariants))]
    public async Task EveryReaderVariantReadsTheSameFrames(InputVariant variant)
    {
        var frames = TestStreamFormat.Frames(Width, Height, Format, 3);
        var poster = TestStreamFormat.Pattern(Width, Height, Format, 42);
        var data = TestStreamFormat.Encode(Width, Height, Format, frames, poster, totalPlays: 5, delta: true);
        using var codecs = TestCodecs.Use(new TestStreamCodec());
        var count = await InputVariants.ReadAsync<Rgba32, int>(variant, data, ImageFormat.Gif, options: null, async (reader, asynchronous) =>
        {
            var info = reader.Info;
            Assert.Equal(new Size(Width, Height), info.Size);
            Assert.Equal(3, info.FrameCount);
            Assert.True(info.HasPosterFrame);
            Assert.Equal(5, info.Animation?.TotalPlays);
            Assert.Equal(ImageIdentifyMode.Header, info.IdentifyMode);
            Assert.Equal("test stream", Assert.Single(info.Metadata.TextEntries).Value);

            using (var readPoster = asynchronous ? await reader.ReadPosterFrameAsync(XunitCancellationToken) : reader.ReadPosterFrame())
            {
                Assert.NotNull(readPoster);
                Assert.Equal(poster, GetBytes(readPoster.Frames[0]));
                Assert.Equal(0, reader.FramesRead);
            }

            for (var i = 0; i < frames.Length; i++)
            {
                using var frame = asynchronous ? await reader.ReadFrameAsync(XunitCancellationToken) : reader.ReadFrame();
                Assert.NotNull(frame);
                Assert.Equal(frames[i].Pixels, GetBytes(frame.Frames[0]));
                Assert.Equal(frames[i].Duration, frame.Frames[0].Metadata.Duration);
                Assert.Equal(i + 1, reader.FramesRead);
            }

            Assert.Null(asynchronous ? await reader.ReadFrameAsync(XunitCancellationToken) : reader.ReadFrame());
            Assert.Null(asynchronous ? await reader.ReadFrameAsync(XunitCancellationToken) : reader.ReadFrame());
            return reader.FramesRead;
        }, XunitCancellationToken);

        Assert.Equal(3, count);
    }

    [Fact]
    public async Task ReaderSnapshotsMatchTheEncodedFrames()
    {
        // The harness adapter used by the conformance hook, checked against the known frames of the test codec
        var frames = TestStreamFormat.Frames(Width, Height, Format, 3);
        var poster = TestStreamFormat.Pattern(Width, Height, Format, 7);
        var data = TestStreamFormat.Encode(Width, Height, Format, frames, poster, totalPlays: 2, delta: true);
        using var codecs = TestCodecs.Use(new TestStreamCodec());
        foreach (var into in new[] { false, true })
        {
            foreach (var asynchronous in new[] { false, true })
            {
                using var reader = Image.OpenReader<Rgba32>(new MemoryStream(data));
                var snapshot = into
                    ? await ReaderSnapshots.CaptureIntoAsync(reader, asynchronous, includeMetadata: true, XunitCancellationToken)
                    : await ReaderSnapshots.CaptureAsync(reader, asynchronous, includeMetadata: true, XunitCancellationToken);
                Assert.Equal(3, snapshot.Frames.Count);
                Assert.True(snapshot.HasAnimation);
                Assert.Equal(2, snapshot.TotalPlays);
                Assert.Equal(poster, snapshot.Poster!.ToArray());
                Assert.Equal("test stream", Assert.Single(snapshot.Metadata!.TextEntries).Value);
                for (var i = 0; i < frames.Length; i++)
                {
                    Assert.Equal(frames[i].Pixels, snapshot.Frames[i].Pixels.ToArray());
                    Assert.Equal(ImageSnapshots.ToRational(frames[i].Duration), snapshot.Frames[i].Duration);
                }
            }
        }
    }

    [Fact]
    public void ReturnedImagesAreStillImagesWithoutAnimationState()
    {
        var data = TestStreamFormat.Encode(Width, Height, Format, TestStreamFormat.Frames(Width, Height, Format, 2), totalPlays: 3);
        using var codecs = TestCodecs.Use(new TestStreamCodec());
        using var reader = Image.OpenReader<Rgba32>(new MemoryStream(data));

        // Info is a detached snapshot: editing it changes neither the reader nor the frames it returns
        reader.Info.Metadata.TextEntries.Clear();
        reader.Info.Animation!.TotalPlays = 9;
        using var frame = reader.ReadFrame();
        Assert.NotNull(frame);
        Assert.Single(frame.Frames);
        Assert.Null(frame.PosterFrame);
        Assert.Null(frame.Animation);
        Assert.False(frame.IsAnimated);
        Assert.Equal(ImageFormat.Gif, frame.Metadata.SourceFormat);
        Assert.Equal("test stream", Assert.Single(frame.Metadata.TextEntries).Value);

        // Each returned image has its own metadata container
        frame.Metadata.TextEntries.Clear();
        using var second = reader.ReadFrame();
        Assert.Equal("test stream", Assert.Single(second!.Metadata.TextEntries).Value);
    }

    [Fact]
    public void ReturnedImagesOutliveTheReaderAndStayChargedUntilDisposed()
    {
        var frames = TestStreamFormat.Frames(Width, Height, Format, 3);
        var data = TestStreamFormat.Encode(Width, Height, Format, frames, delta: true);
        using var codecs = TestCodecs.Use(new TestStreamCodec());
        var path = WriteTemporaryFile(data);
        try
        {
            var reader = Image.OpenReader<Rgba32>(path);
            var scope = reader.Core.Scope;
            var images = new List<Image<Rgba32>>();
            while (reader.ReadFrame() is { } image)
            {
                images.Add(image);
            }

            reader.Dispose();

            // The file is closed, decoder state is released, the images remain valid and charged to the reader scope
            using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
            }

            File.Delete(path);
            Assert.Equal(0, scope.GetLiveBytes(AllocationKind.DecoderState));
            Assert.Equal(0, scope.GetLiveBytes(AllocationKind.CompositorState));
            Assert.True(scope.GetLiveBytes(AllocationKind.ImagePixels) >= 3L * Width * Height * 4);
            for (var i = 0; i < images.Count; i++)
            {
                Assert.Same(scope, images[i].Owner.Scope);
                Assert.Equal(frames[i].Pixels, GetBytes(images[i].Frames[0]));
            }

            foreach (var image in images)
            {
                image.Dispose();
            }

            Assert.Equal(0, scope.LiveBytes);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LiveAllocationLimitCountsImagesHeldByTheCaller()
    {
        const int Size = 32;
        var frames = TestStreamFormat.Frames(Size, Size, Format, 5);
        var data = TestStreamFormat.Encode(Size, Size, Format, frames);
        using var codecs = TestCodecs.Use(new TestStreamCodec());

        // Measure the reader state and the charge of one returned frame
        long baseline;
        long frameBytes;
        using (var probe = Image.OpenReader<Rgba32>(new MemoryStream(data)))
        {
            baseline = probe.Core.Scope.LiveBytes;
            using var frame = probe.ReadFrame();
            frameBytes = probe.Core.Scope.LiveBytes - baseline;
            Assert.True(frameBytes >= Size * Size * 4);
        }

        var options = new ImageReaderOptions { Configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxLiveAllocationBytes = baseline + (2 * frameBytes) } } };

        // Frames held by the caller stay budgeted: the third one does not fit, which is an error, not a clean end
        using (var reader = Image.OpenReader<Rgba32>(new MemoryStream(data), options))
        {
            using var first = reader.ReadFrame();
            using var second = reader.ReadFrame();
            var exception = Assert.Throws<ImageResourceLimitException>(() => reader.ReadFrame());
            Assert.Equal(ImageResourceLimitKind.LiveAllocationBytes, exception.Kind);
            Assert.Throws<InvalidOperationException>(() => reader.ReadFrame());
            Assert.Equal(2, reader.FramesRead);
        }

        // Disposing each frame keeps the reader within the same budget for any number of frames
        using (var reader = Image.OpenReader<Rgba32>(new MemoryStream(data), options))
        {
            var count = 0;
            while (reader.ReadFrame() is { } frame)
            {
                using (frame)
                {
                    Assert.Equal(frames[count].Pixels, GetBytes(frame.Frames[0]));
                    count++;
                }
            }

            Assert.Equal(5, count);
        }
    }

    [Fact]
    public void CompositorStateIsIndependentOfCallerEdits()
    {
        var frames = TestStreamFormat.Frames(Width, Height, Format, 4);
        var data = TestStreamFormat.Encode(Width, Height, Format, frames, delta: true);
        using var codecs = TestCodecs.Use(new TestStreamCodec());
        using var reader = Image.OpenReader<Rgba32>(new MemoryStream(data));
        var kept = new List<Image>();
        for (var i = 0; i < frames.Length; i++)
        {
            var frame = reader.ReadFrame()!;
            kept.Add(frame);
            Assert.Equal(frames[i].Pixels, GetBytes(frame.Frames[0]));

            // Delta frames are composited on the reader's private canvas, never on returned images
            frame.Frames[0].ProcessPixelRows(static pixels =>
            {
                for (var y = 0; y < pixels.Height; y++)
                {
                    pixels.GetRowSpan(y).Fill(new Rgba32(1, 2, 3, 4));
                }
            });

            if (i % 2 == 0)
            {
                frame.Dispose();
            }
        }

        kept.ForEach(image => image.Dispose());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadFrameIntoReusesTheDestinationStorage(bool asynchronous)
    {
        var frames = TestStreamFormat.Frames(Width, Height, Format, 3);
        var data = TestStreamFormat.Encode(Width, Height, Format, frames, delta: true);
        using var codecs = TestCodecs.Use(new TestStreamCodec());
        await using var reader = await Image.OpenReaderAsync<Rgba32>(new MemoryStream(data), cancellationToken: XunitCancellationToken);
        using var destination = new Image<Rgba32>(Width, Height, new Rgba32(9, 9, 9, 9));
        destination.Metadata.TextEntries.Add(new ImageTextEntry("Comment", "caller"));
        destination.Metadata.Orientation = ExifOrientation.RightTop;
        var frame = destination.Frames[0];
        var storage = frame.Storage;
        var ownScope = destination.Owner.Scope;
        var ownBytes = ownScope.LiveBytes;
        var readerBytes = reader.Core.Scope.LiveBytes;
        for (var i = 0; i < frames.Length; i++)
        {
            Assert.True(asynchronous ? await reader.ReadFrameIntoAsync(destination, XunitCancellationToken) : reader.ReadFrameInto(destination));
            Assert.Same(frame, destination.Frames[0]);
            Assert.Same(storage, frame.Storage);
            Assert.Equal(frames[i].Pixels, GetBytes(frame));
            Assert.Equal(frames[i].Duration, frame.Metadata.Duration);

            // The duration and the image-wide metadata are refreshed; nothing is charged to the reader
            Assert.Equal("test stream", Assert.Single(destination.Metadata.TextEntries).Value);
            Assert.Equal(ExifOrientation.TopLeft, destination.Metadata.Orientation);
            Assert.Equal(ImageFormat.Gif, destination.Metadata.SourceFormat);
            Assert.Null(destination.Animation);
            Assert.Equal(ownBytes, ownScope.LiveBytes);
            Assert.Equal(readerBytes, reader.Core.Scope.LiveBytes);
            Assert.Equal(i + 1, reader.FramesRead);

            // Caller edits of the destination never affect the next frame
            frame.ProcessPixelRows(static pixels => pixels.GetRowSpan(0).Clear());
        }

        // The clean end leaves the destination unchanged
        var before = GetBytes(frame);
        Assert.False(asynchronous ? await reader.ReadFrameIntoAsync(destination, XunitCancellationToken) : reader.ReadFrameInto(destination));
        Assert.Equal(before, GetBytes(frame));
        Assert.Equal(frames[^1].Duration, frame.Metadata.Duration);
    }

    [Fact]
    public void ReadFrameIntoReplacesTheHotspotOfTheDestination()
    {
        var frames = TestStreamFormat.Frames(Width, Height, Format, 1);
        var data = TestStreamFormat.Encode(Width, Height, Format, frames);
        using var codecs = TestCodecs.Use(new TestStreamCodec());
        using var reader = Image.OpenReader<Rgba32>(new MemoryStream(data));
        using var destination = new Image<Rgba32>(Width, Height);

        // The hotspot belongs to the previous content of the destination, like its duration
        destination.Frames[0].Metadata.Hotspot = new Point(1, 1);
        Assert.True(reader.ReadFrameInto(destination));
        Assert.Null(destination.Frames[0].Metadata.Hotspot);
    }

    [Fact]
    public void ReadFrameIntoRejectsIncompatibleDestinationsAndStaysUsable()
    {
        var frames = TestStreamFormat.Frames(Width, Height, Format, 1);
        var data = TestStreamFormat.Encode(Width, Height, Format, frames);
        using var codecs = TestCodecs.Use(new TestStreamCodec());
        using var reader = Image.OpenReader<Rgba32>(new MemoryStream(data));
        Assert.Throws<ArgumentNullException>(() => reader.ReadFrameInto(null!));

        using (var wrongSize = new Image<Rgba32>(Width + 1, Height))
        {
            Assert.Throws<ArgumentException>(() => reader.ReadFrameInto(wrongSize));

            // Argument errors of the asynchronous overload are thrown synchronously
            Task<bool>? task = null;
            Assert.IsType<ArgumentException>(Record.Exception(() => { task = reader.ReadFrameIntoAsync(wrongSize, XunitCancellationToken).AsTask(); }));
            Assert.Null(task);
        }

        using (var twoFrames = new Image<Rgba32>(Width, Height))
        {
            twoFrames.AppendFrame();
            Assert.Throws<ArgumentException>(() => reader.ReadFrameInto(twoFrames));
        }

        using (var withPoster = new Image<Rgba32>(Width, Height))
        {
            withPoster.SetPosterFrame(withPoster.Frames[0]);
            Assert.Throws<ArgumentException>(() => reader.ReadFrameInto(withPoster));
        }

        using (var animated = new Image<Rgba32>(Width, Height) { Animation = new AnimationMetadata() })
        {
            Assert.Throws<ArgumentException>(() => reader.ReadFrameInto(animated));
        }

        var disposed = new Image<Rgba32>(Width, Height);
        disposed.Dispose();
        Assert.Throws<ObjectDisposedException>(() => reader.ReadFrameInto(disposed));

        using var destination = new Image<Rgba32>(Width, Height);
        destination.Frames[0].ProcessPixelRows(pixels => Assert.Throws<InvalidOperationException>(() => reader.ReadFrameInto(destination)));

        // Preflight failures never fault the reader
        Assert.True(reader.ReadFrameInto(destination));
        Assert.Equal(frames[0].Pixels, GetBytes(destination.Frames[0]));
        Assert.False(reader.ReadFrameInto(destination));
    }

    [Fact]
    public void PosterRules()
    {
        var frames = TestStreamFormat.Frames(Width, Height, Format, 2);
        var poster = TestStreamFormat.Pattern(Width, Height, Format, 3);
        var withPoster = TestStreamFormat.Encode(Width, Height, Format, frames, poster);
        var withoutPoster = TestStreamFormat.Encode(Width, Height, Format, frames);
        using var codecs = TestCodecs.Use(new TestStreamCodec());

        using (var reader = Image.OpenReader<Rgba32>(new MemoryStream(withPoster)))
        {
            using var image = reader.ReadPosterFrame();
            Assert.NotNull(image);
            Assert.Null(image.Animation);
            Assert.Equal(FrameDuration.Zero, image.Frames[0].Metadata.Duration);

            // At most once; the reader stays usable
            Assert.Throws<InvalidOperationException>(() => reader.ReadPosterFrame());
            using var first = reader.ReadFrame();
            Assert.Equal(frames[0].Pixels, GetBytes(first!.Frames[0]));
        }

        using (var reader = Image.OpenReader<Rgba32>(new MemoryStream(withoutPoster)))
        {
            Assert.False(reader.Info.HasPosterFrame);
            Assert.Null(reader.ReadPosterFrame());
            Assert.Throws<InvalidOperationException>(() => reader.ReadPosterFrame());
            using var first = reader.ReadFrame();
            Assert.NotNull(first);
        }

        // Skipping the poster: it is decoded (validated), discarded and never charged afterward
        using (var reader = Image.OpenReader<Rgba32>(new MemoryStream(withPoster)))
        {
            var scope = reader.Core.Scope;
            var before = scope.GetLiveBytes(AllocationKind.ImagePixels);
            using (var first = reader.ReadFrame())
            {
                Assert.Equal(frames[0].Pixels, GetBytes(first!.Frames[0]));
            }

            Assert.Equal(before, scope.GetLiveBytes(AllocationKind.ImagePixels));
            Assert.Throws<InvalidOperationException>(() => reader.ReadPosterFrame());
            using var second = reader.ReadFrame();
            Assert.Equal(frames[1].Pixels, GetBytes(second!.Frames[0]));
            Assert.Null(reader.ReadFrame());
        }
    }

    public static TheoryData<string, bool> Corruptions => new()
    {
        { "truncated-row", false },
        { "truncated-row", true },
        { "missing-end", false },
        { "missing-end", true },
        { "count-mismatch", false },
        { "unknown-record", true },
        { "trailing-header", false },
    };

    [Theory]
    [MemberData(nameof(Corruptions))]
    public async Task MalformedDataIsNeverACleanEnd(string corruption, bool asynchronous)
    {
        var frames = TestStreamFormat.Frames(Width, Height, Format, 3);
        var valid = TestStreamFormat.Encode(Width, Height, Format, frames);
        var data = corruption switch
        {
            "truncated-row" => valid[..^20],
            "missing-end" => TestStreamFormat.Encode(Width, Height, Format, frames, endCount: -1),
            "count-mismatch" => TestStreamFormat.Encode(Width, Height, Format, frames, endCount: 2, declaredFrames: 0),
            "unknown-record" => [.. valid[..^5], (byte)'X'],
            _ => valid[..^3],
        };

        using var codecs = TestCodecs.Use(new TestStreamCodec());
        using var stream = new TestInputStream(data) { Seekable = false, MaxBytesPerRead = 7, ForbidSynchronousReads = asynchronous, ForbidAsynchronousReads = !asynchronous };
        await using var reader = await OpenAsync(stream, asynchronous);
        var read = new List<Image<Rgba32>>();
        try
        {
            var exception = await Assert.ThrowsAsync<InvalidImageContentException>(async () =>
            {
                while (true)
                {
                    var frame = asynchronous ? await reader.ReadFrameAsync(XunitCancellationToken) : reader.ReadFrame();
                    Assert.NotNull(frame); // a defect must never be reported as the end of input
                    read.Add(frame);
                }
            });
            Assert.Equal(ImageFormat.Gif, exception.Format);

            // The reader is faulted; its decoder state is released at once
            var faulted = reader.FramesRead;
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await reader.ReadFrameAsync(XunitCancellationToken));
            Assert.Throws<InvalidOperationException>(() => reader.ReadFrame());
            Assert.Equal(faulted, reader.FramesRead);
            Assert.Equal(0, reader.Core.Scope.GetLiveBytes(AllocationKind.CompositorState));
            Assert.Equal(read.Count, reader.FramesRead);
        }
        finally
        {
            read.ForEach(image => image.Dispose());
        }

        Assert.False(stream.IsDisposed);
    }

    [Fact]
    public void LimitsAreErrorsNotTruncation()
    {
        var frames = TestStreamFormat.Frames(Width, Height, Format, 3);
        var data = TestStreamFormat.Encode(Width, Height, Format, frames, TestStreamFormat.Pattern(Width, Height, Format, 9));
        using var codecs = TestCodecs.Use(new TestStreamCodec());

        static ImageReaderOptions Limits(ImageResourceLimits limits) => new() { Configuration = new ImageConfiguration { Limits = limits } };

        // The poster counts toward MaxFrames even when it is skipped
        using (var reader = Image.OpenReader<Rgba32>(new MemoryStream(data), Limits(new ImageResourceLimits { MaxFrames = 3 })))
        {
            using var first = reader.ReadFrame();
            using var second = reader.ReadFrame();
            Assert.Equal(ImageResourceLimitKind.Frames, Assert.Throws<ImageResourceLimitException>(() => reader.ReadFrame()).Kind);
            Assert.Throws<InvalidOperationException>(() => reader.ReadFrame());
        }

        using (var reader = Image.OpenReader<Rgba32>(new MemoryStream(data), Limits(new ImageResourceLimits { MaxTotalPixels = (2 * Width * Height) + 1 })))
        {
            using var first = reader.ReadFrame();
            using var second = reader.ReadFrame();
            Assert.Equal(ImageResourceLimitKind.TotalPixels, Assert.Throws<ImageResourceLimitException>(() => reader.ReadFrame()).Kind);
        }

        // The last frame is fully examined within the limit; the end record is not
        using (var reader = Image.OpenReader<Rgba32>(new MemoryStream(data), Limits(new ImageResourceLimits { MaxEncodedBytes = data.Length - 1 })))
        {
            using var first = reader.ReadFrame();
            using var second = reader.ReadFrame();
            using var third = reader.ReadFrame();
            Assert.NotNull(third);
            Assert.Equal(ImageResourceLimitKind.EncodedBytes, Assert.Throws<ImageResourceLimitException>(() => reader.ReadFrame()).Kind);
        }

        using (var exact = Image.OpenReader<Rgba32>(new MemoryStream(data), Limits(new ImageResourceLimits { MaxFrames = 4, MaxTotalPixels = 3 * Width * Height, MaxEncodedBytes = data.Length })))
        {
            var count = 0;
            while (exact.ReadFrame() is { } frame)
            {
                frame.Dispose();
                count++;
            }

            Assert.Equal(3, count);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IOErrorsPropagateAndFaultTheReader(bool asynchronous)
    {
        var data = TestStreamFormat.Encode(Width, Height, Format, TestStreamFormat.Frames(Width, Height, Format, 3));
        using var codecs = TestCodecs.Use(new TestStreamCodec());
        using var stream = new TestInputStream(data) { MaxBytesPerRead = 10, FailAtPosition = data.Length - 30 };
        await using var reader = await OpenAsync(stream, asynchronous);
        using var first = asynchronous ? await reader.ReadFrameAsync(XunitCancellationToken) : reader.ReadFrame();
        using var second = asynchronous ? await reader.ReadFrameAsync(XunitCancellationToken) : reader.ReadFrame();
        if (asynchronous)
        {
            await Assert.ThrowsAsync<InjectedIOException>(async () => await reader.ReadFrameAsync(XunitCancellationToken));
        }
        else
        {
            Assert.Throws<InjectedIOException>(() => reader.ReadFrame());
        }

        Assert.Throws<InvalidOperationException>(() => reader.ReadFrame());
        Assert.False(stream.IsDisposed);
    }

    [Fact]
    public void FrameLimitIsAPrefixSelectionThatDoesNotExamineTheRest()
    {
        var frames = TestStreamFormat.Frames(Width, Height, Format, 4);
        var data = TestStreamFormat.Encode(Width, Height, Format, frames);
        var truncated = data[..^((Width * Height * 4) + 20)];
        using var codecs = TestCodecs.Use(new TestStreamCodec());
        using var stream = new TestInputStream(truncated) { Seekable = false, MaxBytesPerRead = 16 };
        using var reader = Image.OpenReader<Rgba32>(stream, new ImageReaderOptions { FrameLimit = 2 });
        using var first = reader.ReadFrame();
        using var second = reader.ReadFrame();
        Assert.Equal(frames[1].Pixels, GetBytes(second!.Frames[0]));
        Assert.Null(reader.ReadFrame());
        Assert.Null(reader.ReadFrame());
        using var destination = new Image<Rgba32>(Width, Height);
        Assert.False(reader.ReadFrameInto(destination));
        Assert.True(stream.BytesRead < truncated.Length);
        Assert.Equal(0, reader.Core.Scope.GetLiveBytes(AllocationKind.CompositorState));
    }

    [Fact]
    public async Task PreCanceledTokensLeaveTheReaderUsable()
    {
        var frames = TestStreamFormat.Frames(Width, Height, Format, 2);
        var data = TestStreamFormat.Encode(Width, Height, Format, frames, TestStreamFormat.Pattern(Width, Height, Format, 1));
        using var codecs = TestCodecs.Use(new TestStreamCodec());
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();

        using (var stream = new TestInputStream(data))
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Image.OpenReaderAsync<Rgba32>(stream, cancellationToken: canceled.Token));
            Assert.Equal(0, stream.BytesRead);
            Assert.False(stream.IsDisposed);
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Image.OpenReaderAsync<Rgba32>("missing-file.png", cancellationToken: canceled.Token));

        await using var reader = await Image.OpenReaderAsync<Rgba32>(new MemoryStream(data), cancellationToken: XunitCancellationToken);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await reader.ReadPosterFrameAsync(canceled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await reader.ReadFrameAsync(canceled.Token));
        using var destination = new Image<Rgba32>(Width, Height);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await reader.ReadFrameIntoAsync(destination, canceled.Token));

        // Nothing was decoded: the poster window is still open and every frame is still available
        using var poster = await reader.ReadPosterFrameAsync(XunitCancellationToken);
        Assert.NotNull(poster);
        Assert.True(await reader.ReadFrameIntoAsync(destination, XunitCancellationToken));
        Assert.Equal(frames[0].Pixels, GetBytes(destination.Frames[0]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationDuringDecodingFaultsTheReader(bool into)
    {
        var frames = TestStreamFormat.Frames(Width, Height, Format, 3);
        var data = TestStreamFormat.Encode(Width, Height, Format, frames);
        var codec = new TestStreamCodec();
        using var codecs = TestCodecs.Use(codec);
        using var cancellation = new CancellationTokenSource();
        codec.BeforeRow = (frame, row) =>
        {
            if (frame == 1 && row == 2)
            {
                cancellation.Cancel();
            }
        };

        await using var reader = await Image.OpenReaderAsync<Rgba32>(new MemoryStream(data), cancellationToken: XunitCancellationToken);
        using var destination = new Image<Rgba32>(Width, Height);
        Assert.True(await reader.ReadFrameIntoAsync(destination, cancellation.Token));
        if (into)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await reader.ReadFrameIntoAsync(destination, cancellation.Token));

            // Partially updated (the first rows of frame 1), structurally valid and usable
            var pixels = GetBytes(destination.Frames[0]);
            var rowBytes = Width * 4;
            Assert.Equal(frames[1].Pixels.AsSpan(0, 2 * rowBytes).ToArray(), pixels.AsSpan(0, 2 * rowBytes).ToArray());
            Assert.Equal(frames[0].Pixels.AsSpan(2 * rowBytes).ToArray(), pixels.AsSpan(2 * rowBytes).ToArray());
            destination.Grayscale(XunitCancellationToken);
        }
        else
        {
            var scope = reader.Core.Scope;
            var before = scope.GetLiveBytes(AllocationKind.ImagePixels);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await reader.ReadFrameAsync(cancellation.Token));

            // The partially decoded image is released
            Assert.Equal(before, scope.GetLiveBytes(AllocationKind.ImagePixels));
        }

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await reader.ReadFrameAsync(XunitCancellationToken));
        Assert.Equal(1, reader.FramesRead);
    }

    [Fact]
    public async Task CancellationDuringAsynchronousReadsFaultsTheReader()
    {
        var data = TestStreamFormat.Encode(32, 32, Format, TestStreamFormat.Frames(32, 32, Format, 3));
        using var codecs = TestCodecs.Use(new TestStreamCodec());
        using var cancellation = new CancellationTokenSource();
        using var stream = new TestInputStream(data) { MaxBytesPerRead = 100, CancellationSource = cancellation, CancelAtPosition = data.Length / 2, ForbidSynchronousReads = true };
        await using var reader = await Image.OpenReaderAsync<Rgba32>(stream, cancellationToken: XunitCancellationToken);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            while (await reader.ReadFrameAsync(cancellation.Token) is { } frame)
            {
                frame.Dispose();
            }
        });
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await reader.ReadFrameAsync(XunitCancellationToken));
        Assert.False(stream.IsDisposed);
    }

    [Fact]
    public async Task OverlappingCallsAreRejectedWithoutDisturbingTheCallInProgress()
    {
        var frames = TestStreamFormat.Frames(32, 32, Format, 2);
        var data = TestStreamFormat.Encode(32, 32, Format, frames);
        using var codecs = TestCodecs.Use(new TestStreamCodec());
        var gate = new Gate();
        using var stream = new TestInputStream(data) { MaxBytesPerRead = 100, BeforeAsyncRead = gate.WaitAsync };
        var reader = await Image.OpenReaderAsync<Rgba32>(stream, cancellationToken: XunitCancellationToken);
        try
        {
            gate.Close();
            var pending = reader.ReadFrameAsync(XunitCancellationToken).AsTask();
            await gate.WaitUntilBlockedAsync();
            Assert.False(pending.IsCompleted);

            using var destination = new Image<Rgba32>(32, 32);
            Assert.Throws<InvalidOperationException>(() => reader.ReadFrame());
            Assert.Throws<InvalidOperationException>(() => reader.ReadFrameInto(destination));
            Assert.Throws<InvalidOperationException>(() => reader.ReadPosterFrame());
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await reader.ReadFrameAsync(XunitCancellationToken));
            Assert.Throws<InvalidOperationException>(() => reader.Dispose());

            gate.Open();
            using var frame = await pending;
            Assert.Equal(frames[0].Pixels, GetBytes(frame!.Frames[0]));

            // The rejected calls did not fault the reader
            using var second = await reader.ReadFrameAsync(XunitCancellationToken);
            Assert.Equal(frames[1].Pixels, GetBytes(second!.Frames[0]));
        }
        finally
        {
            gate.Open();
            await reader.DisposeAsync();
        }
    }

    [Fact]
    public async Task AsynchronousReadersUseAsynchronousReadsOnly()
    {
        var frames = TestStreamFormat.Frames(Width, Height, Format, 2);
        var data = TestStreamFormat.Encode(Width, Height, Format, frames, TestStreamFormat.Pattern(Width, Height, Format, 2));
        using var codecs = TestCodecs.Use(new TestStreamCodec());
        using var stream = new TestInputStream(data) { Seekable = false, MaxBytesPerRead = 5, ForbidSynchronousReads = true };
        await using (var reader = await Image.OpenReaderAsync<Rgba32>(stream, cancellationToken: XunitCancellationToken))
        {
            using var poster = await reader.ReadPosterFrameAsync(XunitCancellationToken);
            using var destination = new Image<Rgba32>(Width, Height);
            Assert.True(await reader.ReadFrameIntoAsync(destination, XunitCancellationToken));
            using var frame = await reader.ReadFrameAsync(XunitCancellationToken);
            Assert.Null(await reader.ReadFrameAsync(XunitCancellationToken));
        }

        Assert.Equal(0, stream.SynchronousReadCount);
        Assert.True(stream.AsynchronousReadCount > 1);
    }

    [Fact]
    public async Task DisposalRules()
    {
        var data = TestStreamFormat.Encode(Width, Height, Format, TestStreamFormat.Frames(Width, Height, Format, 2));
        using var codecs = TestCodecs.Use(new TestStreamCodec());

        // Caller streams stay open by default
        using (var stream = new TestInputStream(data))
        {
            var reader = Image.OpenReader<Rgba32>(stream);
            var frame = reader.ReadFrame();
            reader.Dispose();
            reader.Dispose();
            await reader.DisposeAsync();
            Assert.False(stream.IsDisposed);
            Assert.Throws<ObjectDisposedException>(() => reader.ReadFrame());
            Assert.Throws<ObjectDisposedException>(() => reader.ReadPosterFrame());
            await Assert.ThrowsAsync<ObjectDisposedException>(async () => await reader.ReadFrameAsync(XunitCancellationToken));
            using var destination = new Image<Rgba32>(Width, Height);
            Assert.Throws<ObjectDisposedException>(() => reader.ReadFrameInto(destination));

            // Info and the returned frames remain available
            Assert.Equal(2, reader.Info.FrameCount);
            Assert.Equal(1, reader.FramesRead);
            Assert.Equal(Width, frame!.Width);
            frame.Dispose();
        }

        // Opting out of LeaveOpen transfers ownership, also when opening fails
        using (var stream = new TestInputStream(data))
        {
            await using (var reader = await Image.OpenReaderAsync<Rgba32>(stream, new ImageReaderOptions { LeaveOpen = false }, XunitCancellationToken))
            {
                Assert.False(stream.IsDisposed);
            }

            Assert.True(stream.IsDisposed);
        }

        using (var stream = new TestInputStream("not an image"u8.ToArray()))
        {
            Assert.Throws<UnknownImageFormatException>(() => Image.OpenReader<Rgba32>(stream, new ImageReaderOptions { LeaveOpen = false }));
            Assert.True(stream.IsDisposed);
        }

        using (var stream = new TestInputStream("not an image"u8.ToArray()))
        {
            Assert.Throws<UnknownImageFormatException>(() => Image.OpenReader<Rgba32>(stream));
            Assert.False(stream.IsDisposed);
        }
    }

    [Fact]
    public void HeaderErrorsAreReportedWhenOpening()
    {
        var unsupported = TestStreamFormat.Encode(Width, Height, Format, TestStreamFormat.Frames(Width, Height, Format, 1));
        unsupported[8 + 10] |= TestStreamFormat.FlagUnsupported;
        var codec = new TestStreamCodec();
        using var codecs = TestCodecs.Use(codec);
        Assert.Equal("Test feature", Assert.Throws<UnsupportedImageFeatureException>(() => Image.OpenReader<Rgba32>(new MemoryStream(unsupported))).Feature);
        Assert.Equal(0, codec.RowsDecoded);
        Assert.Throws<InvalidImageContentException>(() => Image.OpenReader<Rgba32>(new MemoryStream(unsupported[..20])));
        Assert.Equal(0, codec.LastContext!.Scope.LiveBytes);
        var valid = TestStreamFormat.Encode(Width, Height, Format, TestStreamFormat.Frames(Width, Height, Format, 1));
        Assert.Throws<ImageResourceLimitException>(() => Image.OpenReader<Rgba32>(new MemoryStream(valid), new ImageReaderOptions { Configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxWidth = Width - 1 } } }));
    }

    [Fact]
    public void OpeningDecodesNoPixel()
    {
        var data = TestStreamFormat.Encode(Width, Height, Format, TestStreamFormat.Frames(Width, Height, Format, 2));
        var codec = new TestStreamCodec();
        using var codecs = TestCodecs.Use(codec);
        using var reader = Image.OpenReader<Rgba32>(new MemoryStream(data));
        Assert.Equal(0, codec.RowsDecoded);
        Assert.Equal(0, reader.FramesRead);
        Assert.Equal(0, reader.Core.Scope.GetLiveBytes(AllocationKind.ImagePixels));
        using var frame = reader.ReadFrame();
        Assert.Equal(Height, codec.RowsDecoded);
    }

    [Fact]
    public void TypedReadersApplyTheConversionPolicy()
    {
        var opaque = TestStreamFormat.Frames(Width, Height, Format, 1);
        var translucent = TestStreamFormat.Pattern(Width, Height, Format, 5);
        translucent[3] = 0x80;
        using var codecs = TestCodecs.Use(new TestStreamCodec());

        using (var reader = Image.OpenReader<Rgba64>(new MemoryStream(TestStreamFormat.Encode(Width, Height, Format, opaque))))
        {
            using var frame = reader.ReadFrame();
            Assert.Equal(opaque[0].Pixels[0] * 257, frame!.Frames[0][0, 0].R);
        }

        var data = TestStreamFormat.Encode(Width, Height, Format, [(FrameDuration.Zero, translucent)]);
        using (var reader = Image.OpenReader<Rgb24>(new MemoryStream(data)))
        {
            Assert.Throws<UnsupportedImageFeatureException>(() => reader.ReadFrame());
            Assert.Throws<InvalidOperationException>(() => reader.ReadFrame());
        }

        using (var reader = Image.OpenReader<Rgb24>(new MemoryStream(data), new ImageReaderOptions { Conversion = new PixelConversionOptions { BackgroundColor = new Rgba32(255, 255, 255, 255) } }))
        {
            using var frame = reader.ReadFrame();
            Assert.NotNull(frame);
        }
    }

    [Fact]
    public void EveryBuiltInDecoderIsAvailable()
    {
        // Static PNG and APNG are implemented
        using (var reader = Image.OpenReader<Rgba32>(new MemoryStream(SyntheticImages.Png(2, 2))))
        {
            using var frame = reader.ReadFrame();
            Assert.NotNull(frame);
        }

        using (var reader = Image.OpenReader<Rgba32>(new MemoryStream(SyntheticImages.Apng(2, 2, frames: 2))))
        {
            using var first = reader.ReadFrame();
            using var second = reader.ReadFrame();
            Assert.NotNull(second);
            Assert.Null(reader.ReadFrame());
        }

        using (var reader = Image.OpenReader<Rgba32>(new MemoryStream(SyntheticImages.Gif(2, 2, images: 2))))
        {
            using var first = reader.ReadFrame();
            using var second = reader.ReadFrame();
            Assert.NotNull(second);
            Assert.Null(reader.ReadFrame());
        }

        using (var reader = Image.OpenReader<Rgb24>(new MemoryStream(JpegTestImage.CreateRandom(9, 7, [(2, 2), (1, 1), (1, 1)], seed: 1).Encode())))
        {
            using var frame = reader.ReadFrame();
            Assert.NotNull(frame);
            Assert.Null(reader.ReadFrame());
        }

        var progressive = JpegTestImage.CreateRandom(9, 7, [(2, 2), (1, 1), (1, 1)], seed: 1).Encode(new JpegTestEncodeOptions { Progression = JpegTestProgressiveScan.ParseScript("0,1,2: 0 0 0 0; 0: 1 63 0 0; 1: 1 63 0 0; 2: 1 63 0 0") });
        using (var reader = Image.OpenReader<Rgb24>(new MemoryStream(progressive)))
        {
            using var frame = reader.ReadFrame();
            Assert.NotNull(frame);
            Assert.Null(reader.ReadFrame());
        }

        // Header defects keep their real category
        var corrupt = SyntheticImages.Png(2, 2);
        corrupt[29] ^= 0xFF; // IHDR CRC
        Assert.Throws<InvalidImageContentException>(() => Image.OpenReader<Rgba32>(new MemoryStream(corrupt)));
    }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The caller disposes the reader.")]
    private static async Task<ImageReader<Rgba32>> OpenAsync(Stream stream, bool asynchronous)
        => asynchronous ? await Image.OpenReaderAsync<Rgba32>(stream, cancellationToken: XunitCancellationToken) : Image.OpenReader<Rgba32>(stream);

    private static byte[] GetBytes(ImageFrame frame)
    {
        var bytes = new byte[frame.Width * frame.Height * PixelFormats.GetBytesPerPixel(frame.PixelFormat)];
        frame.CopyPixelBytesTo(bytes);
        return bytes;
    }

    private static string WriteTemporaryFile(byte[] data)
    {
        var directory = FullPath.GetTempPath() / "Meziantou.Framework.Imaging.Tests";
        Directory.CreateDirectory(directory);
        var path = directory / (Guid.NewGuid().ToString("N") + ".bin");
        File.WriteAllBytes(path, data);
        return path;
    }

    /// <summary>Holds asynchronous I/O calls while closed, to observe an operation in progress.</summary>
    internal sealed class Gate
    {
        private readonly Lock _lock = new();
        private TaskCompletionSource _open = CreateOpen();
        private TaskCompletionSource _blocked = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Close()
        {
            lock (_lock)
            {
                _open = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _blocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        public void Open()
        {
            lock (_lock)
            {
                _open.TrySetResult();
            }
        }

        public Task WaitAsync()
        {
            lock (_lock)
            {
                _blocked.TrySetResult();
                return _open.Task;
            }
        }

        public Task WaitUntilBlockedAsync()
        {
            lock (_lock)
            {
                return _blocked.Task.WaitAsync(TimeSpan.FromSeconds(30));
            }
        }

        private static TaskCompletionSource CreateOpen()
        {
            var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            source.SetResult();
            return source;
        }
    }
}
