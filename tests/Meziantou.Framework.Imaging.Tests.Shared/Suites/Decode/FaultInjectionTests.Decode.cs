using Meziantou.Framework.Imaging.TestHarness.Fixtures;
using Meziantou.Framework.Imaging.TestHarness.Golden;
using Meziantou.Framework.Imaging.TestHarness.Streams;

namespace Meziantou.Framework.Imaging.Tests.Conformance.Hardening;

// Decoding and geometry cases: one row per fixture of the formats of the test project (CorpusScope)
public sealed partial class FaultInjectionTests
{
    private static readonly ConvolutionOptions Sharpen = new(new ConvolutionKernel(3, 3, [0, -1, 0, -1, 5, -1, 0, -1, 0]));

    public static TheoryData<string, string> DecodeCases => Combine(ValidIds, ["load", "load-async", "reader", "reader-async", "reader-into", "identify-full"]);

    public static TheoryData<string, string> GeometryCases => Combine(ValidIds, ["crop", "auto-crop", "resize", "rotate", "auto-orient"]);

    private static IEnumerable<string> ValidIds => CorpusScope.GetIds(FixtureKinds.Valid).Where(id => GoldenCorpus.Default.Get(id).Entry.DecodeOptions is null);

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

        // Every byte of the input is read, so a read can fail at every position; only an input with unread trailing bytes
        // (the boxes that follow the items of an AVIF image) stops earlier
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
            case "auto-crop":
                // A known analysis whose padded box reaches outside the canvas: every fixture runs the enlarging transaction,
                // with a background (opaque white) that every pixel format represents exactly
                var analysis = image.CreateAutoCropAnalysis(success: true, new Rectangle(0, 0, image.Width, image.Height), new Rgba64(ushort.MaxValue, ushort.MaxValue, ushort.MaxValue), weightX: 0, weightY: 0);
                Assert.True(image.AutoCrop(analysis, new AutoCropOptions { PaddingX = 1, PaddingY = 2 }, cancellationToken));
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
                if (SuiteReaders.ReadsFloats(data))
                {
                    ReadInto<RgbaFloat>(stream);
                }
                else
                {
                    ReadInto<Rgba64>(stream);
                }

                break;
            default:
                if (SuiteReaders.ReadsFloats(data))
                {
                    await ReadAllAsync<RgbaFloat>(stream, operation == "reader-async", cancellationToken);
                }
                else
                {
                    await ReadAllAsync<Rgba64>(stream, operation == "reader-async", cancellationToken);
                }

                break;
        }
    }

    private static void ReadInto<TPixel>(Stream stream)
        where TPixel : unmanaged
    {
        using var reader = Image.OpenReader<TPixel>(stream, new ImageReaderOptions { Conversion = Conversion });
        using var destination = reader.ReadFrame();
        if (destination is null)
            return;

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

    private static async Task ReadAllAsync<TPixel>(Stream stream, bool asynchronous, CancellationToken cancellationToken)
        where TPixel : unmanaged
    {
        var options = new ImageReaderOptions { Conversion = Conversion };
        using var reader = await OpenReaderAsync<TPixel>(stream, options, asynchronous, cancellationToken);
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

    private static async Task<ImageReader<TPixel>> OpenReaderAsync<TPixel>(Stream stream, ImageReaderOptions options, bool asynchronous, CancellationToken cancellationToken)
        where TPixel : unmanaged
    {
        if (asynchronous)
            return await Image.OpenReaderAsync<TPixel>(stream, options, cancellationToken);

        return Image.OpenReader<TPixel>(stream, options);
    }
}
