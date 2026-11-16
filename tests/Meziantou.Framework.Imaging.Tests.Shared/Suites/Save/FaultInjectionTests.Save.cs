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

                // Output failures at many positions of a non-seekable stream (a seekable stream for AVIF and HEIC image sequences,
                // which write their moov box last, for compressed OpenEXR files, which patch the offsets of their chunks, and for
                // compressed TIFF files, which patch the pointer to their directory, and for DNG files, which patch the offset of
                // their image file directory: they reject non-seekable output before writing anything). WebP animations, which
                // patch their RIFF size, keep their whole output in memory for a non-seekable stream and write it at completion:
                // the failures happen there
                var patched = (encoder is AvifEncoder or HeicEncoder && image.Frames.Count > 1) || encoder is OpenExrEncoder { Compression: not OpenExrCompression.None } or TiffEncoder { Compression: not TiffCompression.None } or DngEncoder;
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
                    WriteFrames(writer, image, count: Math.Max(1, image.Frames.Count - 1), opaque: RejectsTransparency(encoder));
                }

                AssertDestinationPreserved(directory, destination, previous);
                Assert.Equal(live, audit.LiveBytes);

                // A writer faulted by an I/O failure stays faulted
                var patchedWriter = (encoder is WebPEncoder or AvifEncoder or HeicEncoder && image.Frames.Count > 1) || encoder is OpenExrEncoder { Compression: not OpenExrCompression.None } or TiffEncoder { Compression: not TiffCompression.None } or DngEncoder;
                await using (var output = new TestOutputStream { FailAtPosition = 0, Seekable = patchedWriter, AllowPatching = patchedWriter })
                using (var writer = CreateWriter(output, image, encoder))
                {
                    var exception = Catch(() => WriteFrames(writer, image, image.Frames.Count, opaque: RejectsTransparency(encoder)));
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

    /// <summary>Keeps an animation, the only input that the frame regions of an encoder change.</summary>
    private static Image? KeepAnimation(Image image)
    {
        if (image.Frames.Count > 1)
            return image;

        image.Dispose();
        return null;
    }

    private static Image? LoadForEncoder(byte[] data, string encoderName, out ImageEncoder encoder)
    {
        // The integer encoders reject floating-point pixels: an OpenEXR source is reduced to 16-bit integers first, its
        // samples clipped to [0, 1] (no conversion gives a value to a NaN sample: such a source is skipped)
        Image image;
        if (encoderName is not ("exr" or "exr-tiled" or "exr-dwa" or "hdr" or "dpx") && SuiteReaders.ReadsFloats(data))
        {
            using var floats = Image.Load<RgbaFloat>(data, new ImageDecodeOptions { Conversion = Conversion });
            if (!HasOnlyNumbers(floats))
            {
                encoder = new PngEncoder();
                return null;
            }

            image = floats.CloneAs<Rgba64>(new PixelConversionOptions { OutOfRangeSamples = OutOfRangeSampleHandling.Clip });
        }
        else
        {
            image = Image.Load(data, new ImageDecodeOptions { Conversion = Conversion });
        }

        switch (encoderName)
        {
            case "png":
                encoder = new PngEncoder { MetadataHandling = MetadataHandling.Strip };
                return image;
            case "png-frames":
                // The canvases of the regions, the region scanlines, and the deferred data of each frame
                encoder = new PngEncoder { MetadataHandling = MetadataHandling.Strip, OptimizeFrames = true };
                return KeepAnimation(image);
            case "png-minimum-size":
                // The analysis, the candidate encodings compressed to a counter, and the canvas copies of the animations
                encoder = new PngEncoder { MetadataHandling = MetadataHandling.Strip, Optimization = PngOptimization.MinimumSize };
                return image;
            case "png-layout":
                // The analysis of the still images, then the rows converted to the chosen layout
                encoder = new PngEncoder { MetadataHandling = MetadataHandling.Strip, Optimization = PngOptimization.Layout };
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
            case "gif-frames":
                // The canvases of the regions, and each frame written from them once the next one is compared
                encoder = new GifEncoder { MetadataHandling = MetadataHandling.Strip, AlphaMode = GifAlphaMode.Threshold, OptimizeFrames = true };
                image.RemovePosterFrame();
                return KeepAnimation(image);
            case "webp":
                encoder = new WebPEncoder { MetadataHandling = MetadataHandling.Strip, AllowBitDepthReduction = true };
                image.RemovePosterFrame();
                if (image.Animation is { TotalPlays: > ushort.MaxValue } animation)
                {
                    animation.TotalPlays = null; // the WebP loop count is 16-bit
                }

                return image;
            case "webp-frames":
                // The displayed frame, and the region copied out of each frame
                encoder = new WebPEncoder { MetadataHandling = MetadataHandling.Strip, AllowBitDepthReduction = true, OptimizeFrames = true };
                image.RemovePosterFrame();
                if (image.Animation is { TotalPlays: > ushort.MaxValue } frameAnimation)
                {
                    frameAnimation.TotalPlays = null; // the WebP loop count is 16-bit
                }

                return KeepAnimation(image);
            case "avif":
            case "heic":
                // The fastest search: the test encodes each input once per rental of the encoder
                encoder = encoderName is "avif"
                    ? new AvifEncoder { MetadataHandling = MetadataHandling.Strip, AllowBitDepthReduction = true, Effort = 0 }
                    : new HeicEncoder { MetadataHandling = MetadataHandling.Strip, AllowBitDepthReduction = true, Effort = 0 };
                image.RemovePosterFrame();
                if (image.Frames.Count == 1)
                {
                    image.Animation = null; // an image sequence of one image stores no animation settings
                }

                return image;
            case "jxl":
            case "jxl-lossy":
            case "jxl-progressive":
                encoder = encoderName switch
                {
                    "jxl" => new JpegXlEncoder { MetadataHandling = MetadataHandling.Strip },

                    // The fastest search of the lossy coding, in three passes: the test encodes each input once per rental
                    "jxl-lossy" => new JpegXlEncoder { MetadataHandling = MetadataHandling.Strip, Compression = JpegXlCompression.Lossy, Progressive = true, Effort = 0 },

                    // The lossless coding of a squeezed image, whose resolutions are spread over the sections of the frame
                    _ => new JpegXlEncoder { MetadataHandling = MetadataHandling.Strip, Progressive = true, Effort = 0 },
                };
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
            case "jp2":
            case "jp2-structured":
                // The whole encoder runs for every code-block: transformation, block coder, packet headers; the structured
                // output adds the rate control of two layers, precincts in a position progression, tile-parts and the TLM
                // and PLT marker segments, coded by the high-throughput block coder with its refinement passes
                encoder = encoderName is "jp2"
                    ? new Jpeg2000Encoder { MetadataHandling = MetadataHandling.Strip }
                    : new Jpeg2000Encoder
                    {
                        MetadataHandling = MetadataHandling.Strip,
                        BlockCoder = Jpeg2000BlockCoder.HighThroughput,
                        PrecinctSizes = [new Size(32, 32), new Size(16, 16)],
                        ProgressionOrder = Jpeg2000ProgressionOrder.Rpcl,
                        TilePartDivision = Jpeg2000TilePartDivision.Resolution,
                        WriteTilePartLengths = true,
                        WritePacketLengths = true,
                        QualityLayers = [Jpeg2000QualityLayer.FromBitsPerPixel(1), Jpeg2000QualityLayer.Complete],
                    };
                if (image.Frames.Count == 1 && image.PosterFrame is null && !image.IsAnimated)
                    return image;

                image.Dispose();
                return null;
            case "exr":
            case "exr-tiled":
            case "exr-dwa":
                encoder = encoderName switch
                {
                    // PIZ runs every stage of the encoder: range compression, wavelet and Huffman coding
                    "exr" => new OpenExrEncoder { MetadataHandling = MetadataHandling.Strip, Compression = OpenExrCompression.Piz },

                    // Tiles, and the buffers of the generated levels of a ripmap
                    "exr-tiled" => new OpenExrEncoder { MetadataHandling = MetadataHandling.Strip, Tiling = new OpenExrTiling { TileWidth = 16, TileHeight = 8, LevelMode = OpenExrLevelMode.RipmapLevels } },

                    // The lossy coder: DCT blocks, Huffman coded AC symbols, zlib sections and the run-length coded alpha
                    _ => new OpenExrEncoder { MetadataHandling = MetadataHandling.Strip, Compression = OpenExrCompression.Dwab },
                };
                if (image.Frames.Count == 1 && image.PosterFrame is null && !image.IsAnimated)
                    return image;

                image.Dispose();
                return null;
            case "tiff-planar":
                // Separate planes: every block of every plane serialized, cut from the chunky rows, predicted and compressed
                encoder = new TiffEncoder { MetadataHandling = MetadataHandling.Strip, SeparatePlanes = true, Predictor = TiffPredictor.HorizontalDifferencing, TileWidth = 16, TileHeight = 16 };
                if (image.Frames.Count == 1 && image.PosterFrame is null && !image.IsAnimated)
                    return image;

                image.Dispose();
                return null;
            case "dpx":
                // Every packing of the default depths: 8, 16 or 32-bit components, as the source
                encoder = new DpxEncoder { MetadataHandling = MetadataHandling.Strip };
                if (image.Frames.Count == 1 && image.PosterFrame is null && !image.IsAnimated)
                    return image;

                image.Dispose();
                return null;
            case "psd":
                // The image resources, then the planes of the composite image, PackBits rows and their table of lengths
                encoder = new PsdEncoder { MetadataHandling = MetadataHandling.Strip };
                if (image.Frames.Count == 1 && image.PosterFrame is null && !image.IsAnimated)
                    return image;

                image.Dispose();
                return null;
            case "dds":
            case "ktx2":
                // The uncompressed texels of the pixels, reduced to the formats of the container when they cannot store them
                encoder = encoderName is "dds"
                    ? new DdsEncoder { MetadataHandling = MetadataHandling.Strip, AllowBitDepthReduction = true, AllowChannelReduction = true }
                    : new Ktx2Encoder { MetadataHandling = MetadataHandling.Strip, AllowBitDepthReduction = true, AllowChannelReduction = true };
                if (image.Frames.Count == 1 && image.PosterFrame is null && !image.IsAnimated && !PixelFormats.IsCmyk(image.PixelFormat))
                    return image;

                image.Dispose();
                return null;
            case "dng":
            case "cin":
                // A DNG file stores lossless JPEG tiles of linear raw data, a Cineon file 10-bit film scans: neither stores
                // transparency or CMYK samples
                encoder = encoderName is "dng"
                    ? new DngEncoder { MetadataHandling = MetadataHandling.Strip }
                    : new CineonEncoder { MetadataHandling = MetadataHandling.Strip, AllowBitDepthReduction = true };
                if (image.Frames.Count == 1 && image.PosterFrame is null && !image.IsAnimated && !PixelFormats.IsCmyk(image.PixelFormat) && IsOpaque(image))
                    return image;

                image.Dispose();
                return null;
            case "hdr":
                // The run-length encoding of every scanline; RGBE stores no alpha (composited over black), no negative color
                // and no sample that is not a number (such a source is skipped)
                encoder = new RadianceEncoder { MetadataHandling = MetadataHandling.Strip, BackgroundColor = new Rgba64(0, 0, 0, 65535) };
                if (image.Frames.Count == 1 && image.PosterFrame is null && !image.IsAnimated && CanStoreAsRgbe(image))
                    return image;

                image.Dispose();
                return null;
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
                    "bmp-indexed" => new BmpEncoder { MetadataHandling = MetadataHandling.Strip, AllowBitDepthReduction = true, PixelLayout = BmpPixelLayout.Indexed8, Compression = BmpCompression.RunLength, AllowColorReduction = true, BackgroundColor = new Rgba64(0, 0, 0), Quantization = new QuantizeOptions { Dithering = DitheringMode.FloydSteinberg } },
                    "tga" => new TgaEncoder { MetadataHandling = MetadataHandling.Strip, AllowBitDepthReduction = true, Compression = TgaCompression.RunLength },
                    _ => new PnmEncoder { MetadataHandling = MetadataHandling.Strip },
                };

                // These formats store one still image, and TGA stores at most 65,535 pixels per side
                if (image.Frames.Count == 1 && image.PosterFrame is null && !image.IsAnimated && (encoderName != "tga" || (image.Width <= ushort.MaxValue && image.Height <= ushort.MaxValue)))
                    return image;

                image.Dispose();
                return null;
            default:
                // The progressive process keeps the coefficients of the image and codes each scan in two passes
                encoder = new JpegEncoder { MetadataHandling = MetadataHandling.Strip, BackgroundColor = new Rgba64(65535, 65535, 65535, 65535), AllowBitDepthReduction = true, Process = encoderName == "jpeg-progressive" ? JpegEncodingProcess.Progressive : JpegEncodingProcess.Baseline };
                if (image.Frames.Count == 1 && image.PosterFrame is null && !image.IsAnimated)
                    return image;

                image.Dispose();
                return null;
        }
    }

    private static bool IsOpaque(Image image)
    {
        if (!PixelFormats.HasAlpha(image.PixelFormat))
            return true;

        using var pixels = image.CloneAs<Rgba64>();
        var frame = pixels.Frames[0];
        for (var y = 0; y < frame.Height; y++)
        {
            for (var x = 0; x < frame.Width; x++)
            {
                if (frame[x, y].A != ushort.MaxValue)
                    return false;
            }
        }

        return true;
    }

    private static bool CanStoreAsRgbe(Image image)
    {
        if (!PixelFormats.IsFloatingPoint(image.PixelFormat))
            return true;

        using var floats = image.CloneAs<RgbaFloat>();
        foreach (var frame in floats.Frames)
        {
            for (var y = 0; y < frame.Height; y++)
            {
                for (var x = 0; x < frame.Width; x++)
                {
                    var pixel = frame[x, y];
                    if (!IsRgbeColor(pixel.R) || !IsRgbeColor(pixel.G) || !IsRgbeColor(pixel.B) || !float.IsFinite(pixel.A))
                        return false;
                }
            }
        }

        return true;

        // From 0 to 2^127, excluded
        static bool IsRgbeColor(float value) => value is >= 0 and < 1.7014118346046923E+38f;
    }

    private static bool HasOnlyNumbers(Image<RgbaFloat> image)
    {
        foreach (var frame in image.Frames)
        {
            for (var y = 0; y < frame.Height; y++)
            {
                for (var x = 0; x < frame.Width; x++)
                {
                    var pixel = frame[x, y];
                    if (float.IsNaN(pixel.R) || float.IsNaN(pixel.G) || float.IsNaN(pixel.B) || float.IsNaN(pixel.A))
                        return false;
                }
            }
        }

        return true;
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

    /// <summary>Whether the encoder rejects transparent pixels instead of storing or compositing them (DNG, Cineon).</summary>
    private static bool RejectsTransparency(ImageEncoder encoder) => encoder is DngEncoder or CineonEncoder;

    private static void WriteFrames(ImageWriter<Rgba32> writer, Image image, int count, bool opaque)
    {
        for (var i = 0; i < count; i++)
        {
            // The substitute of a frame of another pixel type: transparent black, or opaque black for an encoder that rejects
            // transparency
            using var frame = image.Frames[i] is ImageFrame<Rgba32> ? null : CreateSubstitute(image.Width, image.Height, opaque);
            if (frame is not null)
            {
                // The substitute keeps the timing of the frame: an encoder may reject a duration (JPEG XL frames without one)
                frame.Frames[0].Metadata.Duration = image.Frames[i].Metadata.Duration;
            }

            writer.WriteFrame(frame?.Frames[0] ?? (ImageFrame<Rgba32>)image.Frames[i]);
        }
    }

    private static Image<Rgba32> CreateSubstitute(int width, int height, bool opaque)
    {
        var pixels = new Rgba32[width * height];
        if (opaque)
        {
            pixels.AsSpan().Fill(new Rgba32(0, 0, 0, 255));
        }

        return Image.ImportPixelData<Rgba32>(pixels, width, height);
    }

    private static void AssertDestinationPreserved(FullPath directory, FullPath destination, byte[] previous)
    {
        Assert.Equal(previous, File.ReadAllBytes(destination));
        Assert.Equal([destination.Name], Directory.EnumerateFiles(directory).Select(Path.GetFileName));
    }
}
