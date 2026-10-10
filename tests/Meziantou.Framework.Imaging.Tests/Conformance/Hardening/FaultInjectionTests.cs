using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.TestHarness.Fixtures;
using Meziantou.Framework.Imaging.TestHarness.Golden;
using Meziantou.Framework.Imaging.TestHarness.Streams;

namespace Meziantou.Framework.Imaging.Tests.Conformance.Hardening;

/// <summary>
/// Fault injection with the real codecs over every valid corpus input: allocation failure at
/// every rental, I/O failure at every input position (non-seekable short reads), cancellation at every rental and at stream
/// positions, failing and canceled outputs, uncompleted writers, and failures inside geometry transactions. Every case runs
/// under a <see cref="PoolAudit"/>: the injected exception must propagate unchanged (never reported as a clean end or wrapped
/// as content errors), readers and writers fault, no buffer leaks or is used after its return, scopes return to zero, prior
/// destination files survive, only the owned temporary file is removed, and failed geometry operations change nothing.
/// </summary>
public sealed class FaultInjectionTests
{
    private const int MaxPositions = 160;

    private static readonly PixelConversionOptions Conversion = new() { DiscardIncompatibleColorProfile = true };

    private static readonly ConvolutionOptions Sharpen = new(new ConvolutionKernel(3, 3, [0, -1, 0, -1, 5, -1, 0, -1, 0]));

    public static TheoryData<string, string> DecodeCases => Combine(ValidIds, ["load", "load-async", "reader", "reader-async", "reader-into", "identify-full"]);

    public static TheoryData<string, string> SaveCases => Combine(ValidIds, ["png", "gif", "jpeg", "webp", "qoi", "bmp", "tga", "pnm"]);

    public static TheoryData<string, string> GeometryCases => Combine(ValidIds, ["crop", "resize", "rotate", "auto-orient"]);

    private static IEnumerable<string> ValidIds => GoldenCorpus.Default.GetIds(kind: FixtureKinds.Valid).Where(id => GoldenCorpus.Default.Get(id).Entry.DecodeOptions is null);

    [Theory]
    [MemberData(nameof(DecodeCases))]
    public async Task AllocationFailureAtEveryRentalPropagatesAndReleasesEverything(string id, string operation)
    {
        var data = GoldenCorpus.Default.Get(id).ReadInput();
        var rentals = await CountRentalsAsync(() => DecodeAsync(operation, data, stream: null, CancellationToken.None));
        for (var rental = 1; rental <= rentals; rental++)
        {
            using var audit = new PoolAudit();
            var injected = new InsufficientMemoryException($"Injected allocation failure at rental {rental}.");
            audit.FailureInjector = attempt => attempt == rental ? injected : null;
            var exception = await CatchAsync(() => DecodeAsync(operation, data, stream: null, CancellationToken.None));
            Assert.Same(injected, exception);
            audit.AssertClean($"{operation} with rental {rental} of {rentals} failing");
        }
    }

    [Theory]
    [MemberData(nameof(DecodeCases))]
    public async Task IOFailureAtEveryInputPositionPropagatesAndReleasesEverything(string id, string operation)
    {
        var fixture = GoldenCorpus.Default.Get(id);
        var data = fixture.ReadInput();
        var asynchronous = operation.EndsWith("-async", StringComparison.Ordinal);

        // Only the bytes the decoder reads can fail: an input with unread trailing bytes (the TGA 2.0 trailer) stops earlier
        var consumed = await MeasureBytesReadAsync(operation, data, asynchronous);
        Assert.True(consumed == data.Length || FixtureTraits.HasUnreadTrailingBytes(fixture),
            $"{operation} read {consumed} of {data.Length} bytes; only inputs with unread trailing bytes may stop early.");
        foreach (var position in Positions(consumed))
        {
            using var audit = new PoolAudit();
            await using var stream = new TestInputStream(data)
            {
                Seekable = false,
                MaxBytesPerRead = 1 + (int)(position % 5),
                FailAtPosition = position,
                ForbidSynchronousReads = asynchronous,
                ForbidAsynchronousReads = !asynchronous,
            };
            var exception = await CatchAsync(() => DecodeAsync(operation, data, stream, CancellationToken.None));
            Assert.IsType<InjectedIOException>(exception);
            audit.AssertClean($"{operation} with a read failure at {position} of {data.Length}");
        }
    }

    /// <summary>
    /// Counts the input bytes one decode needs, so that failures are only injected where a read happens. One byte per read
    /// gives the exact requirement: a larger read size can only fetch more bytes, never fewer.
    /// </summary>
    private static async Task<long> MeasureBytesReadAsync(string operation, byte[] data, bool asynchronous)
    {
        await using var stream = new TestInputStream(data)
        {
            Seekable = false,
            MaxBytesPerRead = 1,
            ForbidSynchronousReads = asynchronous,
            ForbidAsynchronousReads = !asynchronous,
        };
        await DecodeAsync(operation, data, stream, CancellationToken.None);
        return stream.BytesRead;
    }

    [Theory]
    [MemberData(nameof(DecodeCases))]
    public async Task CancellationAtEveryRentalAndReadReleasesEverything(string id, string operation)
    {
        var data = GoldenCorpus.Default.Get(id).ReadInput();
        if (!operation.EndsWith("-async", StringComparison.Ordinal) && operation != "identify-full")
            return; // the synchronous load and reader overloads take no token (covered through their asynchronous forms)

        var rentals = await CountRentalsAsync(() => DecodeAsync(operation, data, stream: null, CancellationToken.None));
        var canceled = 0;
        for (var rental = 1; rental <= rentals; rental++)
        {
            using var audit = new PoolAudit();
            using var cancellation = new CancellationTokenSource();
            audit.FailureInjector = attempt =>
            {
                if (attempt == rental)
                {
                    cancellation.Cancel();
                }

                return null;
            };
            var exception = await CatchAsync(() => DecodeAsync(operation, data, stream: null, cancellation.Token));
            if (exception is not null)
            {
                AssertCanceled(exception);
                canceled++;
            }

            audit.AssertClean($"{operation} canceled at rental {rental} of {rentals}");
        }

        // Cancellation observed during asynchronous reads (the stream cancels once a position was read)
        foreach (var position in Positions(data.Length).Where((_, index) => index % 4 == 0))
        {
            using var audit = new PoolAudit();
            using var cancellation = new CancellationTokenSource();
            await using var stream = new TestInputStream(data) { Seekable = false, MaxBytesPerRead = 3, ForbidSynchronousReads = true, CancellationSource = cancellation, CancelAtPosition = position + 1 };
            var exception = await CatchAsync(() => DecodeAsync(operation.Replace("identify-full", "identify-full-async", StringComparison.Ordinal), data, stream, cancellation.Token));
            if (exception is not null)
            {
                AssertCanceled(exception);
                canceled++;
            }

            audit.AssertClean($"{operation} canceled after reading {position + 1} bytes");
        }

        Assert.True(canceled > 0, "No cancellation was observed.");
    }

    [Theory]
    [MemberData(nameof(SaveCases))]
    public async Task FailedSavesPreservePriorFilesAndReleaseEverything(string id, string encoderName)
    {
        var data = GoldenCorpus.Default.Get(id).ReadInput();
        var directory = FullPath.FromFileSystemInfo(Directory.CreateTempSubdirectory("meziantou-image-fault-"));
        try
        {
            using var audit = new PoolAudit();
            using (var image = LoadForEncoder(data, encoderName, out var encoder))
            {
                if (image is null)
                    return; // the encoder cannot represent this input (for example JPEG and an animation)

                var live = audit.LiveBytes;
                var extension = encoderName == "jpeg" ? ".jpg" : "." + encoderName;
                var destination = directory / ("existing" + extension);
                byte[] previous = [1, 2, 3, 4, 5];
                await File.WriteAllBytesAsync(destination, previous, XunitCancellationToken);

                // Baseline: the number of rentals and the output length
                audit.FailureInjector = _ => null;
                using var reference = new MemoryStream();
                image.Save(reference, encoder);
                var rentals = audit.Attempts;
                var length = reference.Length;
                Assert.Equal(live, audit.LiveBytes);

                for (var rental = 1; rental <= rentals; rental++)
                {
                    var injected = new InsufficientMemoryException($"Injected allocation failure at rental {rental}.");
                    audit.FailureInjector = attempt => attempt == rental ? injected : null;
                    var exception = rental % 2 == 0
                        ? Catch(() => image.Save(destination, encoder))
                        : await CatchAsync(() => image.SaveAsync(destination, encoder, XunitCancellationToken));
                    Assert.Same(injected, exception);
                    AssertDestinationPreserved(directory, destination, previous);
                    Assert.Equal(live, audit.LiveBytes);

                    using var cancellation = new CancellationTokenSource();
                    audit.FailureInjector = attempt =>
                    {
                        if (attempt == rental)
                        {
                            cancellation.Cancel();
                        }

                        return null;
                    };
                    exception = await CatchAsync(() => image.SaveAsync(destination, encoder, cancellation.Token));
                    if (exception is null)
                    {
                        Assert.Equal(reference.ToArray(), await File.ReadAllBytesAsync(destination, XunitCancellationToken));
                        await File.WriteAllBytesAsync(destination, previous, XunitCancellationToken);
                    }
                    else
                    {
                        AssertCanceled(exception);
                        AssertDestinationPreserved(directory, destination, previous);
                    }

                    Assert.Equal(live, audit.LiveBytes);
                }

                audit.FailureInjector = null;

                // Output failures at many positions of a non-seekable stream (a seekable stream for WebP animations, which
                // patch their RIFF size: they reject non-seekable output before writing anything)
                var patched = encoder is WebPEncoder && (image.Frames.Count > 1 || image.IsAnimated);
                if (patched)
                {
                    await using var rejected = new TestOutputStream();
                    Assert.IsType<ArgumentException>(Catch(() => image.Save(rejected, encoder)));
                    Assert.Equal(0, rejected.BytesWritten);
                    Assert.Equal(live, audit.LiveBytes);
                }

                foreach (var position in Positions(length))
                {
                    await using var output = new TestOutputStream { FailAtPosition = position, ForbidSynchronousWrites = position % 2 == 0, ForbidAsynchronousWrites = position % 2 != 0, Seekable = patched, AllowPatching = patched };
                    var exception = position % 2 == 0
                        ? await CatchAsync(() => image.SaveAsync(output, encoder, XunitCancellationToken))
                        : Catch(() => image.Save(output, encoder));
                    Assert.IsType<InjectedIOException>(exception);
                    Assert.Equal(live, audit.LiveBytes);
                }

                // Uncompleted writers abort: the destination is preserved and the temporary file removed
                using (var writer = CreateWriter(destination, image, encoder))
                {
                    WriteFrames(writer, image, count: Math.Max(1, image.Frames.Count - 1));
                }

                AssertDestinationPreserved(directory, destination, previous);
                Assert.Equal(live, audit.LiveBytes);

                // A writer faulted by an I/O failure stays faulted
                var patchedWriter = encoder is WebPEncoder && image.Frames.Count > 1;
                await using (var output = new TestOutputStream { FailAtPosition = 0, Seekable = patchedWriter, AllowPatching = patchedWriter })
                using (var writer = CreateWriter(output, image, encoder))
                {
                    var exception = Catch(() => WriteFrames(writer, image, image.Frames.Count));
                    if (exception is null)
                    {
                        exception = Catch(writer.Complete);
                    }

                    Assert.IsType<InjectedIOException>(exception);
                    Assert.Throws<InvalidOperationException>(writer.Complete);
                }

                Assert.Equal(live, audit.LiveBytes);
            }

            audit.AssertClean($"saves of {id} as {encoderName}");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [MemberData(nameof(GeometryCases))]
    public void FailedGeometryTransactionsChangeNothing(string id, string operation)
    {
        var data = GoldenCorpus.Default.Get(id).ReadInput();
        using var audit = new PoolAudit();
        using (var image = Load(data, operation))
        {
            var live = audit.LiveBytes;
            var original = ImageState.Capture(image);

            int rentals;
            using (var probe = Load(data, operation))
            {
                audit.FailureInjector = _ => null;
                Apply(probe, operation, CancellationToken.None);
                rentals = audit.Attempts;
            }

            Assert.Equal(live, audit.LiveBytes);
            for (var rental = 1; rental <= rentals; rental++)
            {
                var injected = new InsufficientMemoryException($"Injected allocation failure at rental {rental}.");
                audit.FailureInjector = attempt => attempt == rental ? injected : null;
                Assert.Same(injected, Catch(() => Apply(image, operation, CancellationToken.None)));
                Assert.Equal(original, ImageState.Capture(image));
                Assert.Equal(live, audit.LiveBytes);

                using var cancellation = new CancellationTokenSource();
                audit.FailureInjector = attempt =>
                {
                    if (attempt == rental)
                    {
                        cancellation.Cancel();
                    }

                    return null;
                };
                using var copy = Load(data, operation);
                var copyLive = audit.LiveBytes;
                var copyState = ImageState.Capture(copy);
                var exception = Catch(() => Apply(copy, operation, cancellation.Token));
                if (exception is not null)
                {
                    AssertCanceled(exception);
                    Assert.Equal(copyState, ImageState.Capture(copy));
                    Assert.Equal(copyLive, audit.LiveBytes);
                }
                else
                {
                    ImageState.AssertReadable(copy);
                }
            }

            audit.FailureInjector = null;

            // Pixel-only edits canceled before they start, and callbacks that throw, leave a valid, usable image and no lease
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            AssertCanceled(Catch(() => image.Flip(FlipMode.Vertical, canceled.Token)));
            AssertCanceled(Catch(() => image.Grayscale(canceled.Token)));
            AssertCanceled(Catch(() => image.Convolve(Sharpen, canceled.Token)));
            Assert.Equal(original, ImageState.Capture(image));

            // A convolution rents its scratch before it changes anything: a failed rental leaves the image as it was
            var failure = new InsufficientMemoryException("Injected allocation failure.");
            audit.FailureInjector = _ => failure;
            Assert.Same(failure, Catch(() => image.Convolve(Sharpen, XunitCancellationToken)));
            audit.FailureInjector = null;
            Assert.Equal(original, ImageState.Capture(image));
            Assert.Equal(live, audit.LiveBytes);
            Assert.IsType<InvalidTimeZoneException>(Catch(() => image.Frames[0].ProcessPixelBytes(static _ => throw new InvalidTimeZoneException("callback"))));
            ImageState.AssertReadable(image);
            image.Flip(FlipMode.Vertical, XunitCancellationToken);
            image.Flip(FlipMode.Vertical, XunitCancellationToken);
            Assert.Equal(original, ImageState.Capture(image));
        }

        audit.AssertClean($"{operation} on {id}");
    }

    private static void Apply(Image image, string operation, CancellationToken cancellationToken)
    {
        switch (operation)
        {
            case "crop":
                image.Crop(new Rectangle(image.Width / 3, image.Height / 4, Math.Max(1, image.Width - (image.Width / 3) - 1), Math.Max(1, image.Height / 2)), cancellationToken);
                break;
            case "resize":
                image.Resize(new ResizeOptions((image.Width * 3 / 2) + 1, Math.Max(1, image.Height / 2)) { Mode = ResizeMode.Stretch }, cancellationToken);
                break;
            case "rotate":
                image.Rotate(RotateMode.Rotate90, cancellationToken);
                break;
            default:
                image.AutoOrient(cancellationToken);
                break;
        }
    }

    private static Image Load(byte[] data, string operation)
    {
        var image = Image.Load(data, new ImageDecodeOptions { Conversion = Conversion });

        // Orientation 1 makes auto-orient a no-op: declare a transposing orientation so that a real transaction runs
        if (operation == "auto-orient" && image.Metadata.Orientation == Metadata.ExifOrientation.TopLeft)
        {
            image.Metadata.Orientation = Metadata.ExifOrientation.RightTop;
        }

        return image;
    }

    private static Image? LoadForEncoder(byte[] data, string encoderName, out ImageEncoder encoder)
    {
        var image = Image.Load(data, new ImageDecodeOptions { Conversion = Conversion });
        switch (encoderName)
        {
            case "png":
                encoder = new PngEncoder { MetadataHandling = MetadataHandling.Strip };
                return image;
            case "gif":
                encoder = new GifEncoder { MetadataHandling = MetadataHandling.Strip, AlphaMode = GifAlphaMode.Threshold };
                image.RemovePosterFrame();
                return image;
            case "webp":
                encoder = new WebPEncoder { MetadataHandling = MetadataHandling.Strip, AllowBitDepthReduction = true };
                image.RemovePosterFrame();
                if (image.Animation is { TotalPlays: > ushort.MaxValue } animation)
                {
                    animation.TotalPlays = null; // the WebP loop count is 16-bit
                }

                return image;
            case "qoi":
            case "bmp":
            case "tga":
            case "pnm":
                encoder = encoderName switch
                {
                    "qoi" => new QoiEncoder { MetadataHandling = MetadataHandling.Strip, AllowBitDepthReduction = true },
                    "bmp" => new BmpEncoder { MetadataHandling = MetadataHandling.Strip, AllowBitDepthReduction = true },
                    "tga" => new TgaEncoder { MetadataHandling = MetadataHandling.Strip, AllowBitDepthReduction = true, Compression = TgaCompression.RunLength },
                    _ => new PnmEncoder { MetadataHandling = MetadataHandling.Strip },
                };

                // These formats store one still image, and TGA stores at most 65,535 pixels per side
                if (image.Frames.Count == 1 && image.PosterFrame is null && !image.IsAnimated && (encoderName != "tga" || (image.Width <= ushort.MaxValue && image.Height <= ushort.MaxValue)))
                    return image;

                image.Dispose();
                return null;
            default:
                encoder = new JpegEncoder { MetadataHandling = MetadataHandling.Strip, BackgroundColor = new Rgba64(65535, 65535, 65535, 65535), AllowBitDepthReduction = true };
                if (image.Frames.Count == 1 && image.PosterFrame is null && !image.IsAnimated)
                    return image;

                image.Dispose();
                return null;
        }
    }

    private static ImageWriter<Rgba32> CreateWriter(string path, Image image, ImageEncoder encoder)
        => Image.CreateWriter<Rgba32>(path, WriterOptions(image, encoder));

    private static ImageWriter<Rgba32> CreateWriter(Stream stream, Image image, ImageEncoder encoder)
        => Image.CreateWriter<Rgba32>(stream, WriterOptions(image, encoder));

    private static ImageWriterOptions WriterOptions(Image image, ImageEncoder encoder) => new(image.Size)
    {
        Encoder = encoder is PngEncoder png && image.Frames.Count > 1 ? new PngEncoder { AnimationMode = PngAnimationMode.Animated, MetadataHandling = png.MetadataHandling } : encoder,
        ExpectedFrameCount = encoder is GifEncoder ? null : image.Frames.Count,
    };

    private static void WriteFrames(ImageWriter<Rgba32> writer, Image image, int count)
    {
        for (var i = 0; i < count; i++)
        {
            using var frame = image.Frames[i] is ImageFrame<Rgba32> ? null : Image.ImportPixelData<Rgba32>(new Rgba32[image.Width * image.Height], image.Width, image.Height);
            writer.WriteFrame(frame?.Frames[0] ?? (ImageFrame<Rgba32>)image.Frames[i]);
        }
    }

    private static void AssertDestinationPreserved(FullPath directory, FullPath destination, byte[] previous)
    {
        Assert.Equal(previous, File.ReadAllBytes(destination));
        Assert.Equal([destination.Name], Directory.EnumerateFiles(directory).Select(Path.GetFileName));
    }

    private static async Task<int> CountRentalsAsync(Func<Task> action)
    {
        using var audit = new PoolAudit();
        audit.FailureInjector = _ => null;
        await action();
        audit.AssertClean("baseline");
        Assert.True(audit.Attempts > 0, "The operation rents nothing.");
        return audit.Attempts;
    }

    /// <summary>Runs one decoding entry point to completion and disposes everything it returned. A reader that fails must stay faulted.</summary>
    private static async Task DecodeAsync(string operation, byte[] data, Stream? stream, CancellationToken cancellationToken)
    {
        stream ??= new MemoryStream(data, writable: false);
        var decodeOptions = new ImageDecodeOptions { Conversion = Conversion };
        switch (operation)
        {
            case "load":
                Image.Load(stream, decodeOptions).Dispose();
                break;
            case "load-async":
                (await Image.LoadAsync(stream, decodeOptions, cancellationToken)).Dispose();
                break;
            case "identify-full":
                Image.Identify(stream, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan });
                break;
            case "identify-full-async":
                await Image.IdentifyAsync(stream, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan }, cancellationToken);
                break;
            case "reader-into":
                using (var reader = Image.OpenReader<Rgba64>(stream, new ImageReaderOptions { Conversion = Conversion }))
                {
                    using var destination = reader.ReadFrame();
                    if (destination is null)
                        break;

                    try
                    {
                        while (reader.ReadFrameInto(destination))
                        {
                        }
                    }
                    catch (Exception exception) when (exception is not InvalidOperationException)
                    {
                        // The destination may be partially updated but stays structurally valid and usable
                        ImageState.AssertReadable(destination);
                        destination.Frames[0].Flip(FlipMode.Horizontal, CancellationToken.None);
                        Assert.Throws<InvalidOperationException>(() => reader.ReadFrameInto(destination));
                        throw;
                    }
                }

                break;
            default:
                var asynchronous = operation == "reader-async";
                var options = new ImageReaderOptions { Conversion = Conversion };
                using (var reader = await OpenReaderAsync(stream, options, asynchronous, cancellationToken))
                {
                    var preCanceled = false;
                    try
                    {
                        if (reader.Info.HasPosterFrame == true)
                        {
                            preCanceled = cancellationToken.IsCancellationRequested;
                            (asynchronous ? await reader.ReadPosterFrameAsync(cancellationToken) : reader.ReadPosterFrame())?.Dispose();
                        }

                        while (true)
                        {
                            preCanceled = cancellationToken.IsCancellationRequested;
                            if ((asynchronous ? await reader.ReadFrameAsync(cancellationToken) : reader.ReadFrame()) is not { } frame)
                                break;

                            frame.Dispose();
                        }
                    }
                    catch (Exception exception) when (exception is not InvalidOperationException && !(preCanceled && exception is OperationCanceledException))
                    {
                        // Malformed data, I/O failures, allocation failures and cancellation during a call fault the reader (a token
                        // canceled before the call is a preflight error that leaves it usable)
                        Assert.Throws<InvalidOperationException>(() => reader.ReadFrame());
                        throw;
                    }
                }

                break;
        }
    }

    private static async Task<ImageReader<Rgba64>> OpenReaderAsync(Stream stream, ImageReaderOptions options, bool asynchronous, CancellationToken cancellationToken)
    {
        if (asynchronous)
            return await Image.OpenReaderAsync<Rgba64>(stream, options, cancellationToken);

        return Image.OpenReader<Rgba64>(stream, options);
    }

    private static void AssertCanceled(Exception? exception)
        => Assert.IsAssignableTo<OperationCanceledException>(exception);

    private static IEnumerable<long> Positions(long length)
    {
        if (length <= MaxPositions)
            return Enumerable.Range(0, (int)length).Select(position => (long)position);

        // Every position near both ends, evenly spread positions in between
        return Enumerable.Range(0, 40).Select(position => (long)position)
            .Concat(Enumerable.Range(0, MaxPositions - 80).Select(i => 40 + ((length - 80) * i / (MaxPositions - 80))))
            .Concat(Enumerable.Range(0, 40).Select(i => length - 40 + i))
            .Distinct();
    }

    private static Exception? Catch(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static async Task<Exception?> CatchAsync(Func<Task> action)
    {
        try
        {
            await action();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static TheoryData<string, string> Combine(IEnumerable<string> ids, string[] values)
    {
        var data = new TheoryData<string, string>();
        foreach (var id in ids)
        {
            foreach (var value in values)
            {
                data.Add(id, value);
            }
        }

        return data;
    }
}
