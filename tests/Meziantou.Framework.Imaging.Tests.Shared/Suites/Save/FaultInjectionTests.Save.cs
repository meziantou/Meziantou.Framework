using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.TestHarness.Fixtures;
using Meziantou.Framework.Imaging.TestHarness.Golden;
using Meziantou.Framework.Imaging.TestHarness.Png;
using Meziantou.Framework.Imaging.TestHarness.Streams;

namespace Meziantou.Framework.Imaging.Tests.Conformance.Hardening;

// Saving cases: every valid fixture of the corpus, of any format, saved with the encoders of the test project (CorpusScope)
public sealed partial class FaultInjectionTests
{
    public static TheoryData<string, string> SaveCases => Combine(SourceIds, [.. CorpusScope.EncoderNames]);

    private static IEnumerable<string> SourceIds => GoldenCorpus.Default.GetIds(kind: FixtureKinds.Valid).Where(id => GoldenCorpus.Default.Get(id).Entry.DecodeOptions is null);

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
                // patch their RIFF size, and for AVIF image sequences, which write their moov box last: they reject
                // non-seekable output before writing anything)
                var patched = (encoder is WebPEncoder && (image.Frames.Count > 1 || image.IsAnimated)) || (encoder is AvifEncoder && image.Frames.Count > 1);
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
                var patchedWriter = encoder is WebPEncoder or AvifEncoder && image.Frames.Count > 1;
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

    private static Image? LoadForEncoder(byte[] data, string encoderName, out ImageEncoder encoder)
    {
        var image = Image.Load(data, new ImageDecodeOptions { Conversion = Conversion });
        switch (encoderName)
        {
            case "png":
                encoder = new PngEncoder { MetadataHandling = MetadataHandling.Strip };
                return image;
            case "png-indexed":
                // The palette pass of every image before the header, then the mapping and the packing of the rows
                encoder = new PngEncoder { MetadataHandling = MetadataHandling.Strip, PixelLayout = PngPixelLayout.Indexed, Interlaced = true };
                if (CanStoreIndexed(image))
                    return image;

                image.Dispose();
                return null;
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
            case "avif":
                // The fastest search: the test encodes each input once per rental of the encoder
                encoder = new AvifEncoder { MetadataHandling = MetadataHandling.Strip, AllowBitDepthReduction = true, Effort = 0 };
                image.RemovePosterFrame();
                if (image.Frames.Count == 1)
                {
                    image.Animation = null; // an image sequence of one image stores no animation settings
                }

                return image;
            case "jxl":
                encoder = new JpegXlEncoder { MetadataHandling = MetadataHandling.Strip };
                image.RemovePosterFrame();
                if (image.Frames.Count > 1)
                {
                    // A frame of a JPEG XL animation must have a duration, of at least half a millisecond
                    foreach (var frame in image.Frames.Where(frame => frame.Metadata.Duration < new FrameDuration(1, 1000)))
                    {
                        frame.Metadata.Duration = new FrameDuration(1, 100);
                    }
                }

                return image;
            case "qoi":
            case "bmp":
            case "bmp-indexed":
            case "tga":
            case "pnm":
                encoder = encoderName switch
                {
                    "qoi" => new QoiEncoder { MetadataHandling = MetadataHandling.Strip, AllowBitDepthReduction = true },
                    "bmp" => new BmpEncoder { MetadataHandling = MetadataHandling.Strip, AllowBitDepthReduction = true },

                    // Every pass of the indexed layouts: histogram, dithered mapping, run-length measurement, headers and rows
                    "bmp-indexed" => new BmpEncoder { MetadataHandling = MetadataHandling.Strip, AllowBitDepthReduction = true, PixelLayout = BmpPixelLayout.Indexed8, Compression = BmpCompression.RunLength, AllowColorReduction = true, Dithering = BmpDithering.FloydSteinberg, BackgroundColor = new Rgba64(0, 0, 0) },
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

    /// <summary>
    /// Whether an exact palette can hold the image, both for an eager save (one palette for every frame and the poster) and
    /// for the sequential writes of this suite (the palette of frame zero, which every later frame must then use).
    /// </summary>
    private static bool CanStoreIndexed(Image image)
    {
        var frames = Enumerable.Range(0, image.Frames.Count).Select(index => ImageSnapshots.CaptureFrame(image.Frames[index])).ToList();
        var stills = image.PosterFrame is { } poster ? frames.Append(ImageSnapshots.CaptureFrame(poster)) : frames;
        if (!IndexedPngExpectation.Create(stills).IsStorable)
            return false;

        var first = IndexedPngExpectation.Create(frames[0]).ColorCount;
        return frames.Skip(1).All(frame => IndexedPngExpectation.Create(frames[0], frame).ColorCount == first);
    }

    private static ImageWriter<Rgba32> CreateWriter(string path, Image image, ImageEncoder encoder)
        => Image.CreateWriter<Rgba32>(path, WriterOptions(image, encoder));

    private static ImageWriter<Rgba32> CreateWriter(Stream stream, Image image, ImageEncoder encoder)
        => Image.CreateWriter<Rgba32>(stream, WriterOptions(image, encoder));

    private static ImageWriterOptions WriterOptions(Image image, ImageEncoder encoder) => new(image.Size)
    {
        Encoder = encoder is PngEncoder png && image.Frames.Count > 1 ? new PngEncoder { AnimationMode = PngAnimationMode.Animated, MetadataHandling = png.MetadataHandling, PixelLayout = png.PixelLayout, Interlaced = png.Interlaced } : encoder,
        ExpectedFrameCount = encoder is GifEncoder ? null : image.Frames.Count,
    };

    private static void WriteFrames(ImageWriter<Rgba32> writer, Image image, int count)
    {
        for (var i = 0; i < count; i++)
        {
            using var frame = image.Frames[i] is ImageFrame<Rgba32> ? null : Image.ImportPixelData<Rgba32>(new Rgba32[image.Width * image.Height], image.Width, image.Height);
            if (frame is not null)
            {
                // The substitute keeps the timing of the frame: an encoder may reject a duration (JPEG XL frames without one)
                frame.Frames[0].Metadata.Duration = image.Frames[i].Metadata.Duration;
            }

            writer.WriteFrame(frame?.Frames[0] ?? (ImageFrame<Rgba32>)image.Frames[i]);
        }
    }

    private static void AssertDestinationPreserved(FullPath directory, FullPath destination, byte[] previous)
    {
        Assert.Equal(previous, File.ReadAllBytes(destination));
        Assert.Equal([destination.Name], Directory.EnumerateFiles(directory).Select(Path.GetFileName));
    }
}
