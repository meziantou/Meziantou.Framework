using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Streams;
using Meziantou.Framework.Imaging.Tests.Codecs;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// The writer lifecycle through the public API, with test encoders registered for the
/// built-in output formats (<see cref="TestStreamEncoder"/>): option snapshots, per-format container constraints enforced
/// in shared code, call ordering, preflight errors that keep the writer usable versus faults, repeated completion, abort on
/// disposal, cancellation, overlapping calls, non-seekable and asynchronous outputs, stream ownership and atomic path
/// publication.
/// </summary>
public sealed class ImageWriterTests
{
    private const int Width = 5;
    private const int Height = 4;

    [Theory]
    [InlineData(ImageFormat.Png, false)]
    [InlineData(ImageFormat.Png, true)]
    [InlineData(ImageFormat.Gif, false)]
    [InlineData(ImageFormat.Gif, true)]
    [InlineData(ImageFormat.Jpeg, false)]
    [InlineData(ImageFormat.Jpeg, true)]
    public async Task WritersProduceOnePassOutputOnNonSeekableStreams(ImageFormat format, bool asynchronous)
    {
        using var codecs = TestCodecs.UseAll(out _, out _, out _, out _);
        var count = format == ImageFormat.Jpeg ? 1 : 3;
        var frames = StreamImages.CreateFrames(Width, Height, count);
        using var poster = Image.ImportPixelBytes<Rgba32>(TestStreamFormat.Pattern(Width, Height, PixelFormat.Rgba32, 50), Width, Height);
        var options = new ImageWriterOptions(Width, Height)
        {
            Encoder = format switch
            {
                ImageFormat.Png => new PngEncoder { AnimationMode = PngAnimationMode.Animated },
                ImageFormat.Gif => new GifEncoder(),
                _ => new JpegEncoder(),
            },
            ExpectedFrameCount = format == ImageFormat.Gif ? null : count, // GIF: unknown count
            Animation = format == ImageFormat.Jpeg ? null : new AnimationMetadata { TotalPlays = 3 },
            Metadata = new ImageMetadata { TextEntries = { new ImageTextEntry(ImageTextEntry.CommentKeyword, "hello") } },
        };

        using var stream = new TestOutputStream { ForbidSynchronousWrites = asynchronous, ForbidAsynchronousWrites = !asynchronous };
        await using (var writer = Image.CreateWriter<Rgba32>(stream, options))
        {
            Assert.Equal(format, writer.Format);
            Assert.Equal(new Size(Width, Height), writer.CanvasSize);

            // Nothing is written before the first frame
            Assert.Equal(0, stream.BytesWritten);
            if (format == ImageFormat.Png)
            {
                if (asynchronous)
                {
                    await writer.WritePosterFrameAsync(poster.Frames[0], XunitCancellationToken);
                }
                else
                {
                    writer.WritePosterFrame(poster.Frames[0]);
                }
            }

            foreach (var frame in frames)
            {
                if (asynchronous)
                {
                    await writer.WriteFrameAsync(frame.Frames[0], XunitCancellationToken);
                }
                else
                {
                    writer.WriteFrame(frame.Frames[0]);
                }
            }

            Assert.Equal(count, writer.FramesWritten);
            if (asynchronous)
            {
                await writer.CompleteAsync(XunitCancellationToken);
            }
            else
            {
                writer.Complete();
            }

            Assert.True(stream.FlushCount >= 1);
        }

        Assert.False(stream.IsDisposed);
        using var decoded = StreamImages.Decode(stream.ToArray());
        Assert.Equal(count, decoded.Frames.Count);
        Assert.Equal(format, decoded.Metadata.SourceFormat);
        Assert.Equal("hello", Assert.Single(decoded.Metadata.TextEntries).Value);
        Assert.Equal(format == ImageFormat.Png, decoded.PosterFrame is not null);
        Assert.Equal(format == ImageFormat.Jpeg ? null : 3, decoded.Animation?.TotalPlays);
        for (var i = 0; i < count; i++)
        {
            Assert.Equal(StreamImages.GetBytes(frames[i].Frames[0]), StreamImages.GetBytes(decoded.Frames[i]));
            Assert.Equal(frames[i].Frames[0].Metadata.Duration, decoded.Frames[i].Metadata.Duration);
            frames[i].Dispose();
        }
    }

    [Fact]
    public void OptionsAreSnapshottedWhenTheWriterIsCreated()
    {
        using var codecs = TestCodecs.UseAll(out _, out _, out var gif, out _);
        var metadata = new ImageMetadata();
        metadata.TextEntries.Add(new ImageTextEntry(ImageTextEntry.CommentKeyword, "original"));
        var animation = new AnimationMetadata { TotalPlays = 2 };
        var options = new ImageWriterOptions(Width, Height) { Encoder = new GifEncoder(), Metadata = metadata, Animation = animation };
        using var stream = new TestOutputStream();
        using var writer = Image.CreateWriter<Rgba32>(stream, options);

        // Later caller edits never affect the running writer
        metadata.TextEntries.Clear();
        metadata.TextEntries.Add(new ImageTextEntry(ImageTextEntry.CommentKeyword, "changed"));
        animation.TotalPlays = 7;
        Assert.NotSame(options.Metadata, writer.Core.Options.Metadata);
        Assert.NotSame(options.Animation, writer.Core.Options.Animation);
        Assert.Equal(2, gif.LastSession.Options.Animation?.TotalPlays);

        var frames = StreamImages.CreateFrames(Width, Height, 2);
        writer.WriteFrame(frames[0].Frames[0]);
        writer.WriteFrame(frames[1].Frames[0]);
        writer.Complete();
        using var decoded = StreamImages.Decode(stream.ToArray());
        Assert.Equal("original", Assert.Single(decoded.Metadata.TextEntries).Value);
        Assert.Equal(2, decoded.Animation?.TotalPlays);
        Array.ForEach(frames, frame => frame.Dispose());
    }

    public static TheoryData<string> InvalidOptions => ["png-without-count", "static-png-count", "static-png-animation", "jpeg-count", "jpeg-animation", "gif-canvas", "gif-plays", "gif-icc-strict", "no-encoder"];

    [Theory]
    [MemberData(nameof(InvalidOptions))]
    public void ContainerConstraintsAreCheckedBeforeAnyOutput(string name)
    {
        using var codecs = TestCodecs.UseAll(out _, out var png, out var gif, out var jpeg);
        var (options, expected) = name switch
        {
            "png-without-count" => (new ImageWriterOptions(Width, Height) { Encoder = new PngEncoder() }, typeof(ArgumentException)),
            "static-png-count" => (new ImageWriterOptions(Width, Height) { Encoder = new PngEncoder { AnimationMode = PngAnimationMode.Static }, ExpectedFrameCount = 2 }, typeof(ArgumentException)),
            "static-png-animation" => (new ImageWriterOptions(Width, Height) { Encoder = new PngEncoder { AnimationMode = PngAnimationMode.Static }, ExpectedFrameCount = 1, Animation = new AnimationMetadata() }, typeof(ArgumentException)),
            "jpeg-count" => (new ImageWriterOptions(Width, Height) { Encoder = new JpegEncoder(), ExpectedFrameCount = 2 }, typeof(ArgumentException)),
            "jpeg-animation" => (new ImageWriterOptions(Width, Height) { Encoder = new JpegEncoder(), Animation = new AnimationMetadata() }, typeof(ArgumentException)),
            "gif-canvas" => (new ImageWriterOptions(70_000, 1) { Encoder = new GifEncoder() }, typeof(UnsupportedImageFeatureException)),
            "gif-plays" => (new ImageWriterOptions(Width, Height) { Encoder = new GifEncoder(), Animation = new AnimationMetadata { TotalPlays = 70_000 } }, typeof(UnsupportedImageFeatureException)),
            "gif-icc-strict" => (new ImageWriterOptions(Width, Height) { Encoder = new GifEncoder(), Metadata = new ImageMetadata { IccProfile = new IccProfile(new MetadataBlob(TestRawImage.CreateIccHeader("RGB "u8))) } }, typeof(UnsupportedImageFeatureException)),
            _ => (new ImageWriterOptions(Width, Height), typeof(ArgumentException)),
        };

        // ThrowingStream fails the test on any I/O; no encoder session is created
        Assert.Throws(expected, () => Image.CreateWriter<Rgba32>(new ThrowingStream(), options));
        Assert.Empty(png.Sessions);
        Assert.Empty(gif.Sessions);
        Assert.Empty(jpeg.Sessions);
    }

    [Fact]
    public void OutputCapabilitiesFollowTheEncoderAndTheOptions()
    {
        using var codecs = TestCodecs.UseAll(out _, out var png, out var gif, out _);
        using var frame = StreamImages.CreateFrames(Width, Height, 1)[0];

        using (var writer = Image.CreateWriter<Rgba32>(new TestOutputStream(), new ImageWriterOptions(Width, Height) { Encoder = new PngEncoder(), ExpectedFrameCount = 1 }))
        {
            // Auto with one frame and no animation settings: a static PNG, which cannot store a poster
            Assert.False(writer.Core.Capabilities.IsAnimated);
            Assert.Null(png.LastSession.Options.Animation);
            Assert.Throws<InvalidOperationException>(() => writer.WritePosterFrame(frame.Frames[0]));
            writer.WriteFrame(frame.Frames[0]);
            Assert.Throws<InvalidOperationException>(() => writer.WriteFrame(frame.Frames[0]));
            writer.Complete();
        }

        using (var writer = Image.CreateWriter<Rgba32>(new TestOutputStream(), new ImageWriterOptions(Width, Height) { Encoder = new PngEncoder(), ExpectedFrameCount = 2 }))
        {
            Assert.True(writer.Core.Capabilities.IsAnimated);
            Assert.Null(png.LastSession.Options.Animation!.TotalPlays); // default: infinite loop
        }

        using (var writer = Image.CreateWriter<Rgba32>(new TestOutputStream(), new ImageWriterOptions(Width, Height) { Encoder = new PngEncoder { AnimationMode = PngAnimationMode.Animated }, ExpectedFrameCount = 1 }))
        {
            Assert.True(writer.Core.Capabilities.SupportsPosterFrame);
            writer.WritePosterFrame(frame.Frames[0]);
            writer.WriteFrame(frame.Frames[0]);
            writer.Complete();
        }

        using (var writer = Image.CreateWriter<Rgba32>(new TestOutputStream(), new ImageWriterOptions(Width, Height) { Encoder = new GifEncoder(), ExpectedFrameCount = 1 }))
        {
            Assert.False(writer.Core.Capabilities.IsAnimated);
            Assert.Throws<InvalidOperationException>(() => writer.WritePosterFrame(frame.Frames[0]));
        }

        using (var writer = Image.CreateWriter<Rgba32>(new TestOutputStream(), new ImageWriterOptions(Width, Height) { Encoder = new GifEncoder() }))
        {
            // Unknown count: may receive several frames
            Assert.True(writer.Core.Capabilities.IsAnimated);
            Assert.Null(gif.LastSession.Options.ExpectedFrameCount);
        }
    }

    [Fact]
    public void EveryBuiltInFormatHasAnEncoder()
    {
        // Static PNG, APNG, GIF and JPEG: PngEncoderTests, ApngEncoderTests, GifEncoderTests,
        // JpegEncoderTests. Creating a writer creates the codec session and writes nothing before the first frame.
        Assert.DoesNotContain(ImageEncoderRegistry.Default.Codecs, codec => codec.GetType().Name.Contains("Unavailable", StringComparison.Ordinal));
        ImageEncoder[] encoders = [new PngEncoder(), new PngEncoder { AnimationMode = PngAnimationMode.Animated }, new GifEncoder(), new JpegEncoder()];
        foreach (var encoder in encoders)
        {
            using var writer = Image.CreateWriter<Rgba32>(new ThrowingStream(), new ImageWriterOptions(Width, Height) { Encoder = encoder, ExpectedFrameCount = encoder is GifEncoder ? null : 1 });
            Assert.Equal(encoder.Format, writer.Format);
        }
    }

    [Fact]
    public void CallOrderAndCountsArePreflightErrorsThatKeepTheWriterUsable()
    {
        using var codecs = TestCodecs.UseAll(out _, out var png, out _, out var jpeg);
        var frames = StreamImages.CreateFrames(Width, Height, 3);
        using var stream = new TestOutputStream();
        using (var writer = Image.CreateWriter<Rgba32>(stream, new ImageWriterOptions(Width, Height) { Encoder = new PngEncoder { AnimationMode = PngAnimationMode.Animated }, ExpectedFrameCount = 2 }))
        {
            Assert.Throws<InvalidOperationException>(() => writer.Complete()); // no frame
            writer.WritePosterFrame(frames[2].Frames[0]);
            Assert.Throws<InvalidOperationException>(() => writer.WritePosterFrame(frames[2].Frames[0])); // poster twice
            writer.WriteFrame(frames[0].Frames[0]);
            Assert.Throws<InvalidOperationException>(() => writer.WritePosterFrame(frames[2].Frames[0])); // poster after frame zero
            Assert.Throws<InvalidOperationException>(() => writer.Complete()); // 1 of 2 frames
            writer.WriteFrame(frames[1].Frames[0]);
            Assert.Throws<InvalidOperationException>(() => writer.WriteFrame(frames[2].Frames[0])); // beyond the expected count
            Assert.Equal(2, writer.FramesWritten);
            writer.Complete();
            var length = stream.BytesWritten;

            // Completing again is harmless; writing after completion is not
            writer.Complete();
            Assert.Equal(length, stream.BytesWritten);
            Assert.Single(png.LastSession.Log, entry => entry.StartsWith("complete", StringComparison.Ordinal));
            Assert.Throws<InvalidOperationException>(() => writer.WriteFrame(frames[2].Frames[0]));
            Assert.True(png.LastSession.IsDisposed);
            Assert.Equal(0, writer.Core.Scope.LiveBytes);
        }

        using (var writer = Image.CreateWriter<Rgba32>(new TestOutputStream(), new ImageWriterOptions(Width, Height) { Encoder = new JpegEncoder() }))
        {
            writer.WriteFrame(frames[0].Frames[0]);
            Assert.Throws<InvalidOperationException>(() => writer.WriteFrame(frames[1].Frames[0])); // single-frame output
            Assert.Throws<InvalidOperationException>(() => writer.WritePosterFrame(frames[1].Frames[0]));
            writer.Complete();
            Assert.Equal(["frame 0", "complete 1"], jpeg.LastSession.Log);
        }

        Array.ForEach(frames, frame => frame.Dispose());
    }

    [Fact]
    public async Task FrameValidationErrorsKeepTheWriterUsable()
    {
        using var codecs = TestCodecs.UseAll(out _, out _, out var gif, out _);
        var frames = StreamImages.CreateFrames(Width, Height, 2);
        gif.RejectFrame = (frame, _) => frame.Metadata.Duration == FrameDuration.FromMilliseconds(999);
        using var stream = new TestOutputStream();
        await using var writer = Image.CreateWriter<Rgba32>(stream, new ImageWriterOptions(Width, Height) { Encoder = new GifEncoder() });
        Assert.Throws<ArgumentNullException>(() => writer.WriteFrame(null!));
        Task? task = null;
        Assert.IsType<ArgumentNullException>(Record.Exception(() => { task = writer.WriteFrameAsync(null!, XunitCancellationToken).AsTask(); }));

        using (var wrongSize = new Image<Rgba32>(Width + 1, Height))
        {
            Assert.Throws<ArgumentException>(() => writer.WriteFrame(wrongSize.Frames[0]));
            // Argument errors of the asynchronous overloads are thrown synchronously
            Assert.IsType<ArgumentException>(Record.Exception(() => { task = writer.WriteFrameAsync(wrongSize.Frames[0], XunitCancellationToken).AsTask(); }));
            Assert.Null(task);
        }

        var disposed = new Image<Rgba32>(Width, Height);
        var disposedFrame = disposed.Frames[0];
        disposed.Dispose();
        Assert.Throws<ObjectDisposedException>(() => writer.WriteFrame(disposedFrame));

        using (var multi = new Image<Rgba32>(Width, Height))
        {
            var removed = multi.AppendFrame();
            multi.RemoveFrame(1);
            Assert.Throws<ObjectDisposedException>(() => writer.WriteFrame(removed));
        }

        // Codec preflight (for example an alpha or timing policy) runs before anything of the frame is written
        using (var rejected = new Image<Rgba32>(Width, Height))
        {
            rejected.Frames[0].Metadata.Duration = FrameDuration.FromMilliseconds(999);
            Assert.Throws<UnsupportedImageFeatureException>(() => writer.WriteFrame(rejected.Frames[0]));
        }

        Assert.Equal(0, stream.BytesWritten);
        Assert.Equal(0, writer.FramesWritten);
        await writer.WriteFrameAsync(frames[0].Frames[0], XunitCancellationToken);
        await writer.WriteFrameAsync(frames[1].Frames[0], XunitCancellationToken);
        await writer.CompleteAsync(XunitCancellationToken);
        using var decoded = StreamImages.Decode(stream.ToArray());
        Assert.Equal(2, decoded.Frames.Count);
        Array.ForEach(frames, frame => frame.Dispose());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task LateFailuresFaultTheWriter(bool asynchronous, bool duringCompletion)
    {
        using var codecs = TestCodecs.UseAll(out _, out _, out var gif, out _);
        var frames = StreamImages.CreateFrames(Width, Height, 3);
        if (duringCompletion)
        {
            gif.FailOnComplete = true;
        }
        else
        {
            gif.FailAt = (1, 2);
        }

        using var stream = new TestOutputStream();
        await using var writer = Image.CreateWriter<Rgba32>(stream, new ImageWriterOptions(Width, Height) { Encoder = new GifEncoder() });
        await Write(writer, frames[0], asynchronous);
        if (duringCompletion)
        {
            await Write(writer, frames[1], asynchronous);
            await Assert.ThrowsAsync<InjectedEncoderException>(() => Complete(writer, asynchronous));
        }
        else
        {
            await Assert.ThrowsAsync<InjectedEncoderException>(() => Write(writer, frames[1], asynchronous));
        }

        // Faulted: the session and buffers are released, later calls fail, disposal aborts
        Assert.True(gif.LastSession.IsDisposed);
        Assert.Equal(0, writer.Core.Scope.LiveBytes);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Write(writer, frames[2], asynchronous));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Complete(writer, asynchronous));
        await writer.DisposeAsync();
        Assert.False(stream.IsDisposed);
        Array.ForEach(frames, frame => frame.Dispose());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IOFailuresPropagateAndFaultTheWriter(bool asynchronous)
    {
        using var codecs = TestCodecs.UseAll(out _, out _, out _, out _);
        var frames = StreamImages.CreateFrames(Width, Height, 2);
        using var stream = new TestOutputStream { FailAtPosition = 50 };
        await using var writer = Image.CreateWriter<Rgba32>(stream, new ImageWriterOptions(Width, Height) { Encoder = new GifEncoder() });
        await Assert.ThrowsAsync<InjectedIOException>(() => Write(writer, frames[0], asynchronous));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Write(writer, frames[1], asynchronous));
        Array.ForEach(frames, frame => frame.Dispose());
    }

    [Fact]
    public async Task CancellationBeforeWorkKeepsTheWriterUsableAndAfterwardFaultsIt()
    {
        using var codecs = TestCodecs.UseAll(out _, out _, out var gif, out _);
        var frames = StreamImages.CreateFrames(Width, Height, 3);
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();
        using var stream = new TestOutputStream();
        await using var writer = Image.CreateWriter<Rgba32>(stream, new ImageWriterOptions(Width, Height) { Encoder = new GifEncoder() });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await writer.WriteFrameAsync(frames[0].Frames[0], canceled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await writer.CompleteAsync(canceled.Token));
        Assert.Equal(0, stream.BytesWritten);

        await writer.WriteFrameAsync(frames[0].Frames[0], XunitCancellationToken);
        using var cancellation = new CancellationTokenSource();
        gif.BeforeStep = session =>
        {
            if (session.CurrentFrame == frames[1].Frames[0])
            {
                cancellation.Cancel();
            }
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await writer.WriteFrameAsync(frames[1].Frames[0], cancellation.Token));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await writer.WriteFrameAsync(frames[2].Frames[0], XunitCancellationToken));
        Assert.True(gif.LastSession.IsDisposed);
        Array.ForEach(frames, frame => frame.Dispose());
    }

    [Fact]
    public async Task CancellationDuringAsynchronousWritesFaultsTheWriter()
    {
        using var codecs = TestCodecs.UseAll(out _, out _, out _, out _);
        var frames = StreamImages.CreateFrames(200, 100, 2);
        using var cancellation = new CancellationTokenSource();
        using var stream = new TestOutputStream { CancellationSource = cancellation, CancelAtPosition = 1000, ForbidSynchronousWrites = true };
        await using var writer = Image.CreateWriter<Rgba32>(stream, new ImageWriterOptions(200, 100) { Encoder = new GifEncoder() });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await writer.WriteFrameAsync(frames[0].Frames[0], cancellation.Token);
            await writer.WriteFrameAsync(frames[1].Frames[0], cancellation.Token);
        });
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await writer.CompleteAsync(XunitCancellationToken));
        Array.ForEach(frames, frame => frame.Dispose());
    }

    [Fact]
    public async Task OverlappingCallsAreRejectedWithoutDisturbingTheCallInProgress()
    {
        using var codecs = TestCodecs.UseAll(out _, out _, out _, out _);
        var frames = StreamImages.CreateFrames(Width, Height, 2);
        var gate = new SequentialReaderTests.Gate();
        using var stream = new TestOutputStream { BeforeAsyncWrite = gate.WaitAsync };
        var writer = Image.CreateWriter<Rgba32>(stream, new ImageWriterOptions(Width, Height) { Encoder = new GifEncoder() });
        try
        {
            gate.Close();
            var pending = writer.WriteFrameAsync(frames[0].Frames[0], XunitCancellationToken).AsTask();
            await gate.WaitUntilBlockedAsync();
            Assert.Throws<InvalidOperationException>(() => writer.WriteFrame(frames[1].Frames[0]));
            Assert.Throws<InvalidOperationException>(() => writer.Complete());
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await writer.CompleteAsync(XunitCancellationToken));
            Assert.Throws<InvalidOperationException>(() => writer.Dispose());

            gate.Open();
            await pending;
            Assert.Equal(1, writer.FramesWritten);
            await writer.WriteFrameAsync(frames[1].Frames[0], XunitCancellationToken);
            await writer.CompleteAsync(XunitCancellationToken);
        }
        finally
        {
            gate.Open();
            await writer.DisposeAsync();
        }

        using var decoded = StreamImages.Decode(stream.ToArray());
        Assert.Equal(2, decoded.Frames.Count);
        Array.ForEach(frames, frame => frame.Dispose());
    }

    [Fact]
    public async Task AsynchronousWritersUseAsynchronousWritesOnlyAndFlushBetweenSteps()
    {
        using var codecs = TestCodecs.UseAll(out _, out _, out _, out _);
        const int LargeWidth = 200;
        const int LargeHeight = 100; // 80,000 bytes per frame: more than one flush threshold
        var frames = StreamImages.CreateFrames(LargeWidth, LargeHeight, 2);
        using var stream = new TestOutputStream { ForbidSynchronousWrites = true };
        await using (var writer = Image.CreateWriter<Rgba32>(stream, new ImageWriterOptions(LargeWidth, LargeHeight) { Encoder = new GifEncoder() }))
        {
            await writer.WriteFrameAsync(frames[0].Frames[0], XunitCancellationToken);
            Assert.True(stream.AsynchronousWriteCount >= 2);
            await writer.WriteFrameAsync(frames[1].Frames[0], XunitCancellationToken);
            await writer.CompleteAsync(XunitCancellationToken);

            // Bounded private state: the output buffer stays near the flush threshold, nothing is retained afterward
            Assert.Equal(0, writer.Core.Scope.LiveBytes);
        }

        Assert.Equal(0, stream.SynchronousWriteCount);
        Assert.True(stream.AsynchronousWriteCount >= 4);
        using var decoded = StreamImages.Decode(stream.ToArray());
        Assert.Equal(StreamImages.GetBytes(frames[1].Frames[0]), StreamImages.GetBytes(decoded.Frames[1]));
        Array.ForEach(frames, frame => frame.Dispose());
    }

    [Fact]
    public void FramesAreBorrowedOnlyDuringTheCall()
    {
        using var codecs = TestCodecs.UseAll(out _, out _, out var gif, out _);
        gif.DeltaFrames = true;
        var frames = StreamImages.CreateFrames(Width, Height, 3);
        var expected = frames.Select(frame => StreamImages.GetBytes(frame.Frames[0])).ToArray();
        using var stream = new TestOutputStream();
        using (var writer = Image.CreateWriter<Rgba32>(stream, new ImageWriterOptions(Width, Height) { Encoder = new GifEncoder() }))
        {
            foreach (var frame in frames)
            {
                writer.WriteFrame(frame.Frames[0]);
                Assert.Null(gif.LastSession.CurrentFrame);

                // The caller may modify or dispose the frame as soon as the call returns
                frame.Frames[0].ProcessPixelRows(static pixels => pixels.GetRowSpan(0).Clear());
                frame.Dispose();
            }

            writer.Complete();
        }

        using var decoded = StreamImages.Decode(stream.ToArray());
        for (var i = 0; i < frames.Length; i++)
        {
            Assert.Equal(expected[i], StreamImages.GetBytes(decoded.Frames[i]));
        }
    }

    [Fact]
    public async Task CallerStreamsStayOpenUnlessOwnershipIsTransferred()
    {
        using var codecs = TestCodecs.UseAll(out _, out _, out _, out _);
        using var frame = StreamImages.CreateFrames(Width, Height, 1)[0];
        var options = new ImageWriterOptions(Width, Height) { Encoder = new GifEncoder() };

        using (var stream = new TestOutputStream())
        {
            using (var writer = Image.CreateWriter<Rgba32>(stream, options))
            {
                writer.WriteFrame(frame.Frames[0]);
                writer.Complete();
            }

            Assert.False(stream.IsDisposed);
        }

        using (var stream = new TestOutputStream())
        {
            var writer = Image.CreateWriter<Rgba32>(stream, new ImageWriterOptions(Width, Height) { Encoder = new GifEncoder(), LeaveOpen = false });
            writer.WriteFrame(frame.Frames[0]);
            writer.Complete();
            Assert.False(stream.IsDisposed);
            await writer.DisposeAsync();
            Assert.True(stream.IsDisposed);
        }

        using (var stream = new TestOutputStream())
        {
            // Aborted output: the bytes already written stay, the stream is closed as requested, nothing is finalized
            var writer = Image.CreateWriter<Rgba32>(stream, new ImageWriterOptions(Width, Height) { Encoder = new GifEncoder(), LeaveOpen = false });
            writer.WriteFrame(frame.Frames[0]);
            var written = stream.BytesWritten;
            writer.Dispose();
            writer.Dispose();
            Assert.True(stream.IsDisposed);
            Assert.Equal(written, stream.BytesWritten);
            Assert.Throws<ObjectDisposedException>(() => writer.WriteFrame(frame.Frames[0]));
            Assert.Throws<ObjectDisposedException>(() => writer.Complete());
            await Assert.ThrowsAsync<ObjectDisposedException>(async () => await writer.CompleteAsync(XunitCancellationToken));
            Assert.Throws<InvalidImageContentException>(() => StreamImages.Decode(stream.ToArray()));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PathWritersPublishAtomicallyOnlyOnSuccessfulCompletion(bool asynchronous)
    {
        using var codecs = TestCodecs.UseAll(out _, out _, out _, out _);
        var directory = CreateDirectory();
        try
        {
            var path = directory / "animation.gif";
            await File.WriteAllTextAsync(path, "previous content", XunitCancellationToken);
            var frames = StreamImages.CreateFrames(Width, Height, 2);
            var writer = Image.CreateWriter<Rgba32>(path, new ImageWriterOptions(Width, Height));
            try
            {
                Assert.Equal(ImageFormat.Gif, writer.Format); // inferred from the extension
                var temporary = writer.Core.TemporaryPath!;
                Assert.Equal(directory, FullPath.FromPath(temporary).Parent);
                Assert.True(File.Exists(temporary));
                await Write(writer, frames[0], asynchronous);
                await Write(writer, frames[1], asynchronous);
                Assert.Equal("previous content", await File.ReadAllTextAsync(path, XunitCancellationToken));
                await Complete(writer, asynchronous);
                Assert.False(File.Exists(temporary));

                // A repeated successful completion is harmless
                await Complete(writer, asynchronous);
            }
            finally
            {
                await writer.DisposeAsync();
            }

            using (var decoded = StreamImages.Decode(await File.ReadAllBytesAsync(path, XunitCancellationToken)))
            {
                Assert.Equal(2, decoded.Frames.Count);
            }

            Assert.Equal([path], Directory.GetFiles(directory));
            Array.ForEach(frames, frame => frame.Dispose());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    public static TheoryData<string> PathFailures => ["dispose-without-complete", "late-failure", "complete-failure", "cancellation", "count-mismatch"];

    [Theory]
    [MemberData(nameof(PathFailures))]
    public async Task FailedPathWritersNeverReplaceTheDestination(string failure)
    {
        using var codecs = TestCodecs.UseAll(out _, out var png, out _, out _);
        var directory = CreateDirectory();
        try
        {
            var path = directory / "image.png";
            await File.WriteAllTextAsync(path, "previous content", XunitCancellationToken);

            // An unrelated file that looks like a temporary file is never touched
            var unrelated = directory / ".image.png.0123456789ab.tmp";
            await File.WriteAllTextAsync(unrelated, "not ours", XunitCancellationToken);
            var frames = StreamImages.CreateFrames(Width, Height, 2);
            using var cancellation = new CancellationTokenSource();
            png.FailAt = failure == "late-failure" ? (1, 1) : null;
            png.FailOnComplete = failure == "complete-failure";
            png.BeforeStep = failure == "cancellation" ? _ => cancellation.Cancel() : null;
            try
            {
                await using var writer = Image.CreateWriter<Rgba32>(path, new ImageWriterOptions(Width, Height) { ExpectedFrameCount = 2 });
                var temporary = writer.Core.TemporaryPath!;
                await writer.WriteFrameAsync(frames[0].Frames[0], failure == "cancellation" ? cancellation.Token : XunitCancellationToken);
                switch (failure)
                {
                    case "late-failure":
                        await Assert.ThrowsAsync<InjectedEncoderException>(async () => await writer.WriteFrameAsync(frames[1].Frames[0], XunitCancellationToken));
                        Assert.False(File.Exists(temporary)); // a faulted writer deletes its temporary file at once
                        break;

                    case "complete-failure":
                        await writer.WriteFrameAsync(frames[1].Frames[0], XunitCancellationToken);
                        await Assert.ThrowsAsync<InjectedEncoderException>(async () => await writer.CompleteAsync(XunitCancellationToken));
                        break;

                    case "count-mismatch":
                        Assert.Throws<InvalidOperationException>(() => writer.Complete());
                        break;
                }
            }
            catch (OperationCanceledException) when (failure == "cancellation")
            {
                // The first write observed the cancellation
            }

            Assert.Equal("previous content", await File.ReadAllTextAsync(path, XunitCancellationToken));
            Assert.Equal("not ours", await File.ReadAllTextAsync(unrelated, XunitCancellationToken));
            Assert.Equal([unrelated, path], Directory.GetFiles(directory).Order(StringComparer.Ordinal));
            Array.ForEach(frames, frame => frame.Dispose());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void PathWritersInferTheEncoderFromTheExtensionUnlessOneIsGiven()
    {
        using var codecs = TestCodecs.UseAll(out _, out var png, out var gif, out var jpeg);
        var directory = CreateDirectory();
        try
        {
            ImageWriterCore Create(string name, ImageEncoder? encoder = null, int? count = 1)
            {
                using var writer = Image.CreateWriter<Rgba32>(directory / name, new ImageWriterOptions(Width, Height) { Encoder = encoder, ExpectedFrameCount = count });
                return writer.Core;
            }

            Assert.False(Create("a.png").Capabilities.IsAnimated);
            Assert.IsType<PngEncoder>(png.LastSession.Options.Encoder);
            Assert.True(Create("a.apng").Capabilities.IsAnimated);
            Assert.Equal(ImageFormat.Gif, Create("a.GIF", count: null).Format);
            Assert.Equal(ImageFormat.Jpeg, Create("a.jpg").Format);
            Assert.Equal(ImageFormat.Jpeg, Create("a.jpeg").Format);
            Assert.Equal(ImageFormat.Gif, Create("a.png", new GifEncoder()).Format);
            Assert.Equal(ImageFormat.Png, Create("a.unknown", new PngEncoder()).Format);
            Assert.Throws<ArgumentException>(() => Create("a.heic"));
            Assert.Throws<ArgumentException>(() => Create("noextension"));
            Assert.Throws<DirectoryNotFoundException>(() => Create(Path.Combine("missing", "a.png")));

            // Aborted writers leave nothing behind
            Assert.Empty(Directory.GetFileSystemEntries(directory));
            Assert.Equal(2, jpeg.Sessions.Count);
            Assert.All(gif.Sessions, session => session.IsDisposed);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void EncoderCreationFailuresCreateNoOutput()
    {
        using var codecs = TestCodecs.UseAll(out _, out var png, out _, out _);
        png.CreationFailure = new UnsupportedImageFeatureException("The test encoder cannot encode this configuration.", ImageFormat.Png, "Test");
        var directory = CreateDirectory();
        try
        {
            Assert.Throws<UnsupportedImageFeatureException>(() => Image.CreateWriter<Rgba32>(directory / "a.png", new ImageWriterOptions(Width, Height) { ExpectedFrameCount = 1 }));
            Assert.Throws<UnsupportedImageFeatureException>(() => Image.CreateWriter<Rgba32>(new ThrowingStream(), new ImageWriterOptions(Width, Height) { Encoder = new PngEncoder(), ExpectedFrameCount = 1 }));
            Assert.Empty(Directory.GetFileSystemEntries(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    internal static FullPath CreateDirectory()
    {
        var directory = FullPath.GetTempPath() / "Meziantou.Framework.Imaging.Tests" / Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static async Task Write(ImageWriter<Rgba32> writer, Image<Rgba32> frame, bool asynchronous)
    {
        if (asynchronous)
        {
            await writer.WriteFrameAsync(frame.Frames[0], XunitCancellationToken);
        }
        else
        {
            writer.WriteFrame(frame.Frames[0]);
        }
    }

    private static async Task Complete(ImageWriter<Rgba32> writer, bool asynchronous)
    {
        if (asynchronous)
        {
            await writer.CompleteAsync(XunitCancellationToken);
        }
        else
        {
            writer.Complete();
        }
    }
}
