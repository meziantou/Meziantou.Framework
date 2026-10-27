using System.Diagnostics;
using System.Security.Cryptography;
using Meziantou.Framework.Imaging.Tests.Conformance.Hardening;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.TestHarness.Streams;

namespace Meziantou.Framework.Imaging.FuzzTests;

/// <summary>
/// Runs one (possibly malformed) input through every public decoding entry point under a <see cref="PoolAudit"/> and checks
/// the properties of the library ("Fuzz and property testing"):
/// <list type="number">
/// <item><description>only documented exceptions escape (<see cref="ImageException"/> and its subclasses for content and
/// limits); anything else (index, argument, overflow, null reference, invalid operation...) is a bug;</description></item>
/// <item><description>no buffer leaks, double return or use after return, and every allocation scope returns to zero live bytes
/// once the results are disposed, after successes and failures alike;</description></item>
/// <item><description>identification is consistent with decoding: an unrecognized signature fails everywhere, a failed header
/// identification means no decode succeeds, a successful load implies a successful full identification reporting the same
/// canvas, pixel format, frame count and poster presence, and a failed full identification means no load succeeds;</description></item>
/// <item><description>eager and sequential decoding agree: the reader returns exactly the frames (pixels and durations) and the
/// poster of the eager load and then reports its clean end, and when the eager load fails the reader fails too (never a
/// clean end on malformed data); the only accepted difference is the live-allocation limit, since the reader does not keep
/// every frame alive. A reader that renders every refinement of a frame ends each frame with the same pixels;</description></item>
/// <item><description>transcoding is exact or refused: a JPEG file that is recompressed as a JPEG XL file is rebuilt byte for
/// byte, a refused input writes nothing, and JPEG reconstruction data that a mutation damaged never make anything else
/// than a documented exception or a file;</description></item>
/// <item><description>a collection of pages or representations (<see cref="ImageCollection"/>) describes and decodes the same
/// entries from a span and from a stream, rejects the formats that store one image, and its default entry (the first page,
/// or the largest representation) is the image of the eager load;</description></item>
/// <item><description>a lossless encoder, chosen by the variant among the encoders that have a lossless mode, gives back the
/// pixels of a corner of the first frame of a decoded image.</description></item>
/// </list>
/// </summary>
internal static class FuzzOracle
{
    /// <summary>
    /// Tight limits keep each case fast and bounded (mutated dimensions or counts are rejected early instead of allocating
    /// hundreds of megabytes), while staying far above the corpus inputs.
    /// </summary>
    public static ImageConfiguration Configuration { get; } = new()
    {
        Limits = new ImageResourceLimits
        {
            MaxWidth = 4096,
            MaxHeight = 4096,
            MaxFramePixels = 1 << 20,
            MaxFrames = 64,
            MaxTotalPixels = 1 << 23,
            MaxEncodedBytes = 1 << 20,
            MaxMetadataBytes = 1 << 20,
            MaxLiveAllocationBytes = 192L << 20,
        },
    };

    // Floating-point samples outside [0, 1] (OpenEXR) are clipped, so that the typed loads and the readers still compare them
    private static readonly PixelConversionOptions Conversion = new() { DiscardIncompatibleColorProfile = true, OutOfRangeSamples = OutOfRangeSampleHandling.Clip };

    // Four-component JPEG files are only loaded with a color conversion: with it, their mutations reach the decoding of
    // the inks and the embedded CMYK profile instead of stopping at the frame header
    private static readonly CmykConversionOptions CmykConversion = CmykConversionOptions.Default;

    /// <summary>Checks one input.</summary>
    /// <param name="input">The input.</param>
    /// <param name="variant">Selects the reader input (stream kind, read sizes) and the reader methods (whole frames, or every refinement of a frame); part of the case identity.</param>
    /// <returns>The first violated property, or <see langword="null"/>.</returns>
    public static FuzzFailure? Check(byte[] input, int variant) => Check(input, variant, out _);

    /// <summary>Checks one input.</summary>
    /// <param name="input">The input.</param>
    /// <param name="variant">Selects the reader input (stream kind, read sizes) and the reader methods (whole frames, or every refinement of a frame); part of the case identity.</param>
    /// <param name="outcome">"decoded" when the eager load succeeded, otherwise the exception type of the load (statistics).</param>
    /// <returns>The first violated property, or <see langword="null"/>.</returns>
    public static FuzzFailure? Check(byte[] input, int variant, out string outcome)
    {
        outcome = "?";
        using var audit = new PoolAudit();
        var detected = Image.DetectFormat(input);

        var header = Run(audit, "identify-header", () => Image.Identify(input, new ImageIdentifyOptions { Configuration = Configuration, Mode = ImageIdentifyMode.Header }));
        if (header.Failure is not null)
            return header.Failure;

        var full = Run(audit, "identify-full", () => Image.Identify(input, new ImageIdentifyOptions { Configuration = Configuration, Mode = ImageIdentifyMode.FullScan }));
        if (full.Failure is not null)
            return full.Failure;

        var eager = Run(audit, "load", () =>
        {
            using var image = Image.Load(input, new ImageDecodeOptions { Configuration = Configuration, Conversion = Conversion, CmykConversion = CmykConversion });
            return DecodedSummary.Capture(image);
        });
        if (eager.Failure is not null)
            return eager.Failure;

        outcome = eager.Exception switch
        {
            null => "decoded",
            ImageResourceLimitException limit => "limit:" + limit.Kind.ToString(),
            _ => eager.Exception.GetType().Name,
        };

        var typed = Run(audit, "load-rgba64", () =>
        {
            using var image = Image.Load<Rgba64>(input, new ImageDecodeOptions { Configuration = Configuration, Conversion = Conversion, CmykConversion = CmykConversion });
            return DecodedSummary.Capture(image);
        });
        if (typed.Failure is not null)
            return typed.Failure;

        var sequential = Run(audit, "reader", () => ReadAllAsync(input, variant).GetAwaiter().GetResult());
        if (sequential.Failure is not null)
            return sequential.Failure;

        // Identification versus decoding
        if (detected == ImageFormat.Unknown)
        {
            foreach (var (name, exception) in new[] { ("identify-header", header.Exception), ("load", eager.Exception), ("reader", sequential.Exception) })
            {
                if (exception is not UnknownImageFormatException)
                    return FuzzFailure.Property("unknown-signature", $"DetectFormat returned Unknown but {name} {Describe(exception)}");
            }
        }

        if (header.Exception is not null)
        {
            foreach (var (name, succeeded) in new[] { ("identify-full", full.Succeeded), ("load", eager.Succeeded), ("load-rgba64", typed.Succeeded), ("reader", sequential.Succeeded) })
            {
                if (succeeded)
                    return FuzzFailure.Property("header-failure-but-" + name, $"Header identification failed ({Describe(header.Exception)}) but {name} succeeded");
            }
        }
        // The detection of a file of the HEIF family only sees the brands of its first bytes, which can name both AVIF and
        // HEIC (or be cut by the prefix): the identification, which reads the codec of the image, decides
        else if (header.Value!.Format != detected && !(IsHeifFamily(header.Value.Format) && IsHeifFamily(detected)))
        {
            return FuzzFailure.Property("identify-format", $"Identify reported {header.Value.Format}, DetectFormat {detected}");
        }

        // TIFF, ICO and CUR files are random-access containers, and the steps of an ANI file are located by a table: the
        // reader refuses them, whatever their content
        var isReadSequentially = detected is not (ImageFormat.Tiff or ImageFormat.Ico or ImageFormat.Cur or ImageFormat.Ani);
        if (!isReadSequentially && sequential.Exception is not UnsupportedImageFeatureException { Feature: "Sequential reading" })
            return FuzzFailure.Property("reader-random-access", $"The reader of a {detected} file {Describe(sequential.Exception)} instead of refusing the format");

        // The load of a TIFF document reads its first page, and its full identification every page
        var loadsFirstPageOnly = detected == ImageFormat.Tiff;
        if (full.Exception is not null && !IsLiveAllocationLimit(full.Exception) && !loadsFirstPageOnly)
        {
            foreach (var (name, succeeded) in new[] { ("load", eager.Succeeded), ("load-rgba64", typed.Succeeded) })
            {
                if (succeeded)
                    return FuzzFailure.Property("full-identify-failure-but-" + name, $"Full identification failed ({Describe(full.Exception)}) but {name} succeeded");
            }
        }

        if (eager.Succeeded)
        {
            var info = full.Value;
            if (info is null && !loadsFirstPageOnly)
                return FuzzFailure.Property("load-without-full-identify", $"Load succeeded but full identification failed: {Describe(full.Exception)}");

            var decoded = eager.Value!;
            if ((info is not null && (info.Width != decoded.Width || info.Height != decoded.Height || info.PixelFormat != decoded.PixelFormat
                || info.FrameCount != decoded.Frames.Count || (info.HasPosterFrame == true) != (decoded.Poster is not null)))
                || header.Value!.Width != decoded.Width || header.Value.Height != decoded.Height || header.Value.PixelFormat != decoded.PixelFormat)
            {
                return FuzzFailure.Property("identify-mismatch", $"Identify (header {Describe(header.Value)}, full {Describe(info)}) disagrees with the load ({decoded})");
            }
        }

        // Typed versus untyped, eager versus sequential
        // A conversion of floating-point samples to integers always refuses a sample that is not a number
        var refusedFloats = eager.Succeeded && PixelFormats.IsFloatingPoint(eager.Value!.PixelFormat) && typed.Exception is UnsupportedImageFeatureException;
        if (eager.Succeeded != typed.Succeeded && !IsLiveAllocationLimit(eager.Exception) && !IsLiveAllocationLimit(typed.Exception) && !refusedFloats)
            return FuzzFailure.Property("typed-untyped", $"Untyped load {Describe(eager)}, Rgba64 load {Describe(typed)}");

        if (eager.Succeeded && typed.Succeeded && eager.Value!.Frames.Count != typed.Value!.Frames.Count)
            return FuzzFailure.Property("typed-untyped-frames", $"Untyped load {eager.Value}, Rgba64 load {typed.Value}");

        if (!isReadSequentially)
        {
            // Refused above
        }
        else if (typed.Succeeded && sequential.Succeeded)
        {
            if (!typed.Value!.Matches(sequential.Value!))
                return FuzzFailure.Property("eager-sequential", $"Eager Rgba64 load {typed.Value} differs from the reader {sequential.Value}");
        }
        else if (typed.Succeeded && !IsLiveAllocationLimit(sequential.Exception))
        {
            return FuzzFailure.Property("eager-ok-reader-failed", $"Eager Rgba64 load succeeded ({typed.Value}) but the reader {Describe(sequential.Exception)}");
        }
        else if (sequential.Succeeded && !IsLiveAllocationLimit(typed.Exception))
        {
            return FuzzFailure.Property("clean-end-on-malformed-data", $"Eager Rgba64 load {Describe(typed.Exception)} but the reader ended cleanly after {sequential.Value}");
        }

        // Pages and representations: the collection reads the same entries from a span and from a stream, and its default
        // entry is the image of the eager load
        var collection = Run(audit, "collection", () => CheckCollection(input, variant, detected, eager, full));
        if (collection.Failure is not null)
            return collection.Failure;

        if (collection.Value is { Length: > 0 } collectionViolation)
            return FuzzFailure.Property(collectionViolation.Split(':')[0], collectionViolation);

        // HEIC: a decoding that is interrupted (a cancellation while the file is read, a reader that is disposed before the
        // last image) releases what it holds: the pictures that wait for their turn in the decoded picture buffer, the
        // reference pictures, the decoders of the color and of the alpha
        if (detected == ImageFormat.Heic)
        {
            var interrupted = Run(audit, "interrupted", () => CheckInterruptedDecodingAsync(input, variant, typed.Value?.Frames.Count ?? 0).GetAwaiter().GetResult());
            if (interrupted.Failure is not null)
                return interrupted.Failure;
        }

        // A JPEG file that is recompressed as a JPEG XL file comes back byte for byte, whatever the mutation; reconstruction
        // data that a mutation damaged are refused or give a file, never anything else
        if (detected is ImageFormat.Jpeg or ImageFormat.JpegXl)
        {
            var transcoded = Run(audit, "transcode", () => CheckTranscoding(input, detected));
            if (transcoded.Failure is not null)
                return transcoded.Failure;

            if (transcoded.Value is { Length: > 0 } violation)
                return FuzzFailure.Property(violation.Split(':')[0], violation);
        }

        // Decoded images (whatever metadata and geometry the mutation produced) can be processed and re-encoded, and every
        // encoder output decodes again (exactly for PNG)
        if (eager.Succeeded)
        {
            var processed = Run(audit, "post-decode", () => CheckDecodedImage(input, eager.Value!, variant));
            if (processed.Failure is not null)
                return processed.Failure;

            if (processed.Value is { Length: > 0 } violation)
                return FuzzFailure.Property(violation.Split(':')[0], violation);
        }

        return null;
    }

    /// <summary>Returns an empty string, or "name: description" for a violated property; throws for exceptions.</summary>
    private static string CheckTranscoding(byte[] input, ImageFormat format)
    {
        var options = new JpegXlTranscodeOptions { Configuration = Configuration };
        using var output = new MemoryStream();
        if (format == ImageFormat.JpegXl)
        {
            var hasData = JpegXlTranscoder.HasJpegReconstructionData(input, options);
            var rebuilt = JpegXlTranscoder.TryReconstructJpeg(input, output, options);
            if (rebuilt && !hasData)
                return "transcode-without-data: a JPEG file was rebuilt from a file that has no reconstruction data";

            if (!rebuilt && output.Length != 0)
                return $"transcode-partial-output: {output.Length} bytes were written for a file that is not rebuilt";

            return string.Empty;
        }

        try
        {
            JpegXlTranscoder.RecompressJpeg(input, output, options);
        }
        catch (ImageException)
        {
            // Refused: nothing is written
            return output.Length == 0 ? string.Empty : $"transcode-partial-output: {output.Length} bytes were written for a JPEG file that is refused";
        }

        var recompressed = output.ToArray();
        output.SetLength(0);
        if (!JpegXlTranscoder.TryReconstructJpeg(recompressed, output, options))
            return "transcode-round-trip: the recompressed file has no reconstruction data";

        if (!output.GetBuffer().AsSpan(0, (int)output.Length).SequenceEqual(input))
            return $"transcode-round-trip: the JPEG file rebuilt from the recompressed file has {output.Length} bytes that are not the {input.Length} bytes of the input";

        // The recompressed file is an image like any other
        using var image = Image.Load(recompressed, new ImageDecodeOptions { Configuration = Configuration, Conversion = Conversion });
        return string.Empty;
    }

    /// <summary>Returns an empty string, or "name: description" for a violated property; throws for exceptions.</summary>
    private static string CheckDecodedImage(byte[] input, DecodedSummary decoded, int variant)
    {
        using var image = Image.Load(input, new ImageDecodeOptions { Configuration = Configuration, Conversion = Conversion, CmykConversion = CmykConversion });

        // A frame limit selects a prefix of a valid input
        using (var first = Image.Load(input, new ImageDecodeOptions { Configuration = Configuration, Conversion = Conversion, CmykConversion = CmykConversion, FrameLimit = 1 }))
        {
            if (first.Frames.Count != 1 || !FrameSummary.Capture(first.Frames[0]).Equals(decoded.Frames[0]))
                return $"frame-limit-prefix: FrameLimit = 1 returned {first.Frames.Count} frame(s) that differ from the first frame of {decoded}";
        }

        // Re-encode in the source format: metadata found in the input is kept (strict policy) when possible, else stripped
        var format = Image.DetectFormat(input);
        ImageEncoder[] encoders = format switch
        {
            ImageFormat.Png => [new PngEncoder { CompressionLevel = System.IO.Compression.CompressionLevel.Fastest, Interlaced = (variant & 16) != 0 }, new PngEncoder { MetadataHandling = MetadataHandling.Strip }],
            ImageFormat.Gif => [new GifEncoder { Interlaced = (variant & 16) != 0 }, new GifEncoder { MetadataHandling = MetadataHandling.Strip }],

            // Either process; the 16-bit pixels of a 12-bit file are reduced to the 8 bits the encoder writes
            _ => [new JpegEncoder { Quality = 1 + (variant % 100), Process = (variant & 16) != 0 ? JpegEncodingProcess.Progressive : JpegEncodingProcess.Baseline, AllowBitDepthReduction = true }, new JpegEncoder { MetadataHandling = MetadataHandling.Strip, AllowBitDepthReduction = true }],
        };
        byte[]? encoded = null;
        foreach (var encoder in encoders)
        {
            using var output = new MemoryStream();
            try
            {
                image.Save(output, encoder);
                encoded = output.ToArray();
                break;
            }
            catch (ImageException) when (encoder.MetadataHandling != MetadataHandling.Strip)
            {
                // Retry without metadata
            }
            catch (UnsupportedImageFeatureException)
            {
                // A documented loss (for example an animation duration that the output cannot represent exactly)
                break;
            }
        }

        if (encoded is not null)
        {
            using var reloaded = Image.Load(encoded, new ImageDecodeOptions { Configuration = Configuration, Conversion = Conversion, CmykConversion = CmykConversion });
            if (reloaded.Width != image.Width || reloaded.Height != image.Height || reloaded.Frames.Count != image.Frames.Count)
                return $"re-encode-structure: the re-encoded {format} file decodes as {reloaded.Width}x{reloaded.Height} with {reloaded.Frames.Count} frame(s), the source as {decoded}";

            if (format == ImageFormat.Png)
            {
                var again = DecodedSummary.Capture(reloaded);
                if (!again.Matches(decoded))
                    return $"re-encode-png-lossless: the re-encoded PNG decodes as {again}, the source as {decoded}";
            }
        }

        // A lossless encoder, chosen by the variant, gives back the pixels of a part of the first frame
        if (CheckLosslessRoundTrip(image, variant) is { Length: > 0 } lossless)
            return lossless;

        // Processing on whatever geometry and orientation the input declared
        image.AutoOrient();
        image.Resize(new ResizeOptions(Math.Max(1, image.Width / 2), Math.Max(1, (image.Height * 2) / 3)) { Mode = ResizeMode.Stretch });
        image.Rotate(RotateMode.Rotate90);
        image.Flip(FlipMode.Horizontal);
        image.Crop(new Rectangle(0, 0, Math.Max(1, image.Width - 1), image.Height));

        // Whatever the pixels, the detected box is a non-empty part of the canvas and a clamped auto-crop never enlarges it
        var size = image.Size;
        var analysis = image.AnalyzeAutoCrop(new AutoCropOptions { BucketThreshold = 0.9, AnalyzeWeights = true });
        if (analysis.CanvasSize != size || analysis.Bounds.IsEmpty || !new Rectangle(0, 0, size.Width, size.Height).Contains(analysis.Bounds) || !(Math.Abs(analysis.WeightX) <= 1) || !(Math.Abs(analysis.WeightY) <= 1))
            return $"auto-crop-analysis: the {size} canvas has the content box {analysis.Bounds} and the weights ({analysis.WeightX}, {analysis.WeightY})";

        var cropped = image.AutoCrop(analysis, new AutoCropOptions { PaddingX = 1, PaddingY = 2, PaddingMode = AutoCropPaddingMode.Contain });
        if ((cropped && !analysis.Success) || image.Width > size.Width || image.Height > size.Height || (!cropped && image.Size != size))
            return $"auto-crop-contain: the {size} canvas became {image.Size} (success: {analysis.Success}, changed: {cropped})";

        image.Convolve(new ConvolutionOptions(new ConvolutionKernel(3, 3, [0, -1, 0, -1, 5, -1, 0, -1, 0])) { EdgeMode = ConvolutionEdgeMode.Mirror });
        image.Grayscale();

        // Last, since enlarging the canvas may legitimately exceed the limits of the configuration
        // A canvas that keeps its size has still changed when the content box and its padding are not the canvas: one side
        // is cropped by what the opposite one is enlarged
        size = image.Size;
        var expand = new AutoCropOptions { PaddingX = 2, PaddingY = 1 };
        var box = image.AnalyzeAutoCrop(expand).Bounds;
        if (image.AutoCrop(expand) && image.Size == size && box.X == expand.PaddingX && box.Y == expand.PaddingY)
            return $"auto-crop-expand: the {size} canvas is reported as changed but the content box {box} and its padding are the canvas";

        return string.Empty;
    }

    /// <summary>Returns an empty string, or "name: description" for a violated property; throws for exceptions.</summary>
    private static string CheckLosslessRoundTrip(Image image, int variant)
    {
        // A corner of the first frame, so that the slowest encoders stay fast whatever the size of the input
        using var still = image.CloneFrame(0);
        still.Crop(new Rectangle(0, 0, Math.Min(still.Width, 40), Math.Min(still.Height, 40)));
        var source = still.PixelFormat;
        var eightBit = source switch
        {
            PixelFormat.Rgba64 => PixelFormat.Rgba32,
            PixelFormat.Gray16 => PixelFormat.Gray8,
            _ => source,
        };
        var option = (variant >> 4) & 3;
        var alternative = (variant & 16) != 0;

        // An icon DIB whose alpha channel is entirely zero is read through its AND mask, which the encoder leaves clear
        // (IcoEntryDecoder: an all-transparent icon is never what the file means): such a corner is written as a PNG payload
        var dibPayload = alternative && !IsFullyTransparent(still);
        var (encoder, pixelFormat) = (variant % 12) switch
        {
            0 => ((ImageEncoder)new QoiEncoder { MetadataHandling = MetadataHandling.Strip }, eightBit),
            1 => (new BmpEncoder { MetadataHandling = MetadataHandling.Strip, RowOrder = alternative ? BmpRowOrder.TopDown : BmpRowOrder.BottomUp }, eightBit),
            2 => (new TgaEncoder { MetadataHandling = MetadataHandling.Strip, Compression = alternative ? TgaCompression.RunLength : TgaCompression.None }, eightBit),
            3 => (new PnmEncoder { MetadataHandling = MetadataHandling.Strip }, source),
            4 => (new TiffEncoder { MetadataHandling = MetadataHandling.Strip, Compression = option switch { 0 => TiffCompression.None, 1 => TiffCompression.Deflate, 2 => TiffCompression.Lzw, _ => TiffCompression.PackBits }, BigEndian = (variant & 64) != 0, BigTiff = (variant & 128) != 0, RowsPerStrip = 1 + (variant % 7) }, source),
            5 => (new WebPEncoder { MetadataHandling = MetadataHandling.Strip, Effort = option * 3 }, eightBit),
            6 => (new JpegXlEncoder { MetadataHandling = MetadataHandling.Strip }, source),
            7 => (new Jpeg2000Encoder { MetadataHandling = MetadataHandling.Strip }, source),
            8 => (new PngEncoder { MetadataHandling = MetadataHandling.Strip, Interlaced = alternative }, source),
            9 => (new IcoEncoder { MetadataHandling = MetadataHandling.Strip, Kind = (variant & 32) != 0 ? IconKind.Cursor : IconKind.Icon, PayloadFormat = dibPayload ? IconPayloadFormat.Dib : IconPayloadFormat.Png }, dibPayload ? eightBit : source),
            10 => (new AvifEncoder { MetadataHandling = MetadataHandling.Strip }, eightBit),
            _ => (new HeicEncoder { MetadataHandling = MetadataHandling.Strip }, eightBit),
        };

        using var converted = pixelFormat switch
        {
            PixelFormat.Rgba32 => (Image)still.CloneAs<Rgba32>(Conversion),
            PixelFormat.Bgra32 => still.CloneAs<Bgra32>(Conversion),
            PixelFormat.Rgb24 => still.CloneAs<Rgb24>(Conversion),
            PixelFormat.Rgba64 => still.CloneAs<Rgba64>(Conversion),
            PixelFormat.Gray8 => still.CloneAs<Gray8>(Conversion),
            _ => still.CloneAs<Gray16>(Conversion),
        };
        using var output = new MemoryStream();
        converted.Save(output, encoder);
        using var expected = converted.CloneAs<Rgba64>(Conversion);
        using var reloaded = Image.Load<Rgba64>(output.ToArray(), new ImageDecodeOptions { Configuration = Configuration, Conversion = Conversion });
        if (reloaded.Width != expected.Width || reloaded.Height != expected.Height || reloaded.Frames.Count != 1)
            return $"lossless-round-trip-structure: the {pixelFormat} image of {expected.Width}x{expected.Height} written as {encoder.Format} decodes as {reloaded.Width}x{reloaded.Height} with {reloaded.Frames.Count} frame(s)";

        if (FrameSummary.Capture(expected.Frames[0]).PixelHash != FrameSummary.Capture(reloaded.Frames[0]).PixelHash)
            return $"lossless-round-trip-pixels: the {pixelFormat} image of {expected.Width}x{expected.Height} written as {encoder.Format} ({encoder.GetType().Name}, variant {variant}) does not decode to the same pixels";

        return string.Empty;
    }

    private static bool IsFullyTransparent(Image image)
    {
        if (!PixelFormats.HasAlpha(image.PixelFormat))
            return false;

        using var pixels = image.CloneAs<Rgba64>(Conversion);
        var values = new Rgba64[pixels.Width * pixels.Height];
        pixels.Frames[0].CopyPixelDataTo(values);
        return Array.TrueForAll(values, pixel => pixel.A == 0);
    }

    /// <summary>Returns an empty string, or "name: description" for a violated property; throws for exceptions.</summary>
    private static string CheckCollection(byte[] input, int variant, ImageFormat detected, Outcome<DecodedSummary> eager, Outcome<ImageInfo> full)
    {
        var fromSpan = CollectionSummary.Load(() => ImageCollection.Load(input, Configuration), variant);

        // From a stream that starts after a few unrelated bytes, read in short reads or not
        var prefix = (variant >> 5) & 3;
        var data = new byte[prefix + input.Length];
        data.AsSpan(0, prefix).Fill(0xA5);
        input.CopyTo(data, prefix);
        using var stream = new TestInputStream(data, prefix) { MaxBytesPerRead = (variant & 1) != 0 ? 1 + ((variant >> 4) % 7) : 0 };
        var fromStream = CollectionSummary.Load(() => ImageCollection.Load(stream, Configuration), variant);
        if (fromSpan.ToString() != fromStream.ToString() && fromSpan.Exception is not ImageResourceLimitException { Kind: ImageResourceLimitKind.EncodedBytes })
            return $"collection-span-stream: the collection loaded from a span is {fromSpan}, from a stream {fromStream}";

        // The collection tells the formats apart from the detection prefix: the detection of a file of the HEIF family
        // reads the brands that follow it when it has them, and reports a file type box the prefix cuts as AVIF
        var prefixFormat = Image.DetectFormat(input.AsSpan(0, Math.Min(input.Length, Image.FormatDetectionPrefixLength)));
        var isCollection = detected is ImageFormat.Tiff or ImageFormat.Ico or ImageFormat.Cur;
        switch (fromSpan.Exception)
        {
            case null when !isCollection:
                return $"collection-single-image: a {detected} input is loaded as a collection {fromSpan}";

            case not null and not UnknownImageFormatException when prefixFormat == ImageFormat.Unknown:
                return $"collection-unknown-signature: DetectFormat returned Unknown but the collection {Describe(fromSpan.Exception)}";

            case not null and not UnsupportedImageFeatureException when prefixFormat != ImageFormat.Unknown && !isCollection:
                return $"collection-single-image: a {prefixFormat} input is rejected with {Describe(fromSpan.Exception)} instead of UnsupportedImageFeatureException";

            case not null when eager.Succeeded && isCollection && full.Succeeded:
                return $"collection-failure-but-load: the eager load succeeded ({eager.Value}) but the collection {Describe(fromSpan.Exception)}";
        }

        // The collection and the full identification walk the same structure: every entry
        if (isCollection && fromSpan.Succeeded != full.Succeeded && fromSpan.Exception is not ImageResourceLimitException && full.Exception is not ImageResourceLimitException)
            return $"collection-full-identify: the full identification {Describe(full.Exception)} but the collection {Describe(fromSpan.Exception)}";

        if (fromSpan.Exception is not null)
            return string.Empty;

        if (fromSpan.Format != detected)
            return $"collection-format: the collection reports {fromSpan.Format}, DetectFormat {detected}";

        if (full.Value?.CollectionEntryCount is { } count && count != fromSpan.Entries.Count)
            return $"collection-count: the identification reports {count} entries, the collection {fromSpan}";

        if (fromSpan.Entries.Count == 0)
            return eager.Succeeded ? $"collection-empty: the eager load succeeded ({eager.Value}) but the collection has no entry" : string.Empty;

        // The default entry: the first page, or the largest representation (then the deepest, which the description of
        // an entry does not tell apart from the other representations of that size)
        var largest = fromSpan.Entries.Max(entry => (long)entry.Width * entry.Height);
        List<int> candidates = detected == ImageFormat.Tiff ? [0] : Enumerable.Range(0, fromSpan.Entries.Count).Where(index => (long)fromSpan.Entries[index].Width * fromSpan.Entries[index].Height == largest).ToList();
        if (candidates.Exists(index => index >= CollectionSummary.MaxDecodedEntries))
            return string.Empty;

        if (eager.Succeeded)
        {
            var decoded = eager.Value!;
            if (!candidates.Exists(index => fromSpan.Entries[index].Decoded is { } entry && entry.Width == decoded.Width && entry.Height == decoded.Height && entry.PixelFormat == decoded.PixelFormat && entry.Frame.PixelHash == decoded.Frames[0].PixelHash))
                return $"collection-default-entry: the eager load ({decoded}) is none of the entries {string.Join(", ", candidates)} of the collection {fromSpan}";
        }
        else if (candidates.Count == 1 && fromSpan.Entries[candidates[0]].Decoded is not null && !IsLiveAllocationLimit(eager.Exception))
        {
            return $"collection-default-entry-decoded: the eager load {Describe(eager.Exception)} but the entry {candidates[0]} of the collection decodes ({fromSpan})";
        }

        return string.Empty;
    }

    private static async Task<string> CheckInterruptedDecodingAsync(byte[] input, int variant, int frameCount)
    {
        var options = new ImageDecodeOptions { Configuration = Configuration, Conversion = Conversion, CmykConversion = CmykConversion };
        using (var cancellation = new CancellationTokenSource())
        {
            // The token is cancelled when the reading of the file reaches a position: an eighth to seven eighths of the file
            await using var stream = new TestInputStream(input)
            {
                Seekable = (variant & 1) is 0,
                MaxBytesPerRead = 1024,
                CancellationSource = cancellation,
                CancelAtPosition = Math.Max(1, (long)input.Length * (1 + ((variant >> 1) % 7)) / 8),
            };
            try
            {
                using var image = await Image.LoadAsync<Rgba64>(stream, options, cancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (ImageException)
            {
            }
        }

        if (frameCount >= 2)
        {
            // Some of the images of a sequence, then the reader is disposed
            await using var stream = new TestInputStream(input);
            using var reader = Image.OpenReader<Rgba64>(stream, new ImageReaderOptions { Configuration = Configuration, Conversion = Conversion, CmykConversion = CmykConversion });
            var count = 1 + ((variant >> 4) % (frameCount - 1));
            for (var i = 0; i < count; i++)
            {
                using var frame = reader.ReadFrame();
                if (frame is null)
                    break;
            }
        }

        return "";
    }

    private static bool IsHeifFamily(ImageFormat format) => format is ImageFormat.Avif or ImageFormat.Heic;

    private static bool IsLiveAllocationLimit(Exception? exception) => exception is ImageResourceLimitException { Kind: ImageResourceLimitKind.LiveAllocationBytes };

    private static string Describe(Exception? exception) => exception is null ? "succeeded" : $"threw {exception.GetType().Name}: {exception.Message}";

    private static string Describe<T>(Outcome<T> outcome)
        where T : class
        => outcome.Succeeded ? $"succeeded ({outcome.Value})" : Describe(outcome.Exception);

    private static string Describe(ImageInfo? info) => info is null ? "none" : FormattableString.Invariant($"{info.Format} {info.Width}x{info.Height} {info.PixelFormat} frames={info.FrameCount} poster={info.HasPosterFrame}");

    private static async Task<DecodedSummary> ReadAllAsync(byte[] input, int variant)
    {
        var shortReads = (variant & 1) != 0;
        var useInto = (variant & 2) != 0;
        var asynchronous = (variant & 4) != 0;
        var readPoster = (variant & 8) == 0;
        var readRefinements = (variant & 128) != 0;
        await using var stream = new TestInputStream(input)
        {
            Seekable = !shortReads,
            MaxBytesPerRead = shortReads ? 1 + (variant >> 4) % 7 : 0,
            ForbidSynchronousReads = asynchronous,
            ForbidAsynchronousReads = !asynchronous,
        };
        var options = new ImageReaderOptions { Configuration = Configuration, Conversion = Conversion, CmykConversion = CmykConversion };
        using var reader = await OpenReaderAsync(stream, options, asynchronous).ConfigureAwait(false);
        var info = reader.Info;
        FrameSummary? poster = null;
        if (readPoster && info.HasPosterFrame == true)
        {
            using var posterImage = asynchronous ? await reader.ReadPosterFrameAsync().ConfigureAwait(false) : reader.ReadPosterFrame();
            poster = posterImage is null ? null : FrameSummary.Capture(posterImage.Frames[0]);
        }

        var frames = new List<FrameSummary>();
        if (readRefinements)
        {
            await ReadRefinementsAsync(reader, frames, asynchronous).ConfigureAwait(false);
        }

        Image<Rgba64>? destination = null;
        try
        {
            while (true)
            {
                if (useInto && destination is not null)
                {
                    var read = asynchronous ? await reader.ReadFrameIntoAsync(destination).ConfigureAwait(false) : reader.ReadFrameInto(destination);
                    if (!read)
                        break;

                    frames.Add(FrameSummary.Capture(destination.Frames[0]));
                    continue;
                }

                var frame = asynchronous ? await reader.ReadFrameAsync().ConfigureAwait(false) : reader.ReadFrame();
                if (frame is null)
                    break;

                frames.Add(FrameSummary.Capture(frame.Frames[0]));
                if (useInto)
                {
                    destination = frame;
                }
                else
                {
                    frame.Dispose();
                }
            }
        }
        finally
        {
            destination?.Dispose();
        }

        if (reader.FramesRead != frames.Count)
            throw new UnreachableException($"FramesRead is {reader.FramesRead}, {frames.Count} frames were read");

        // When the poster is not requested, the eager load still reports it: the reader skipped it (and validated it)
        return new DecodedSummary(info.Width, info.Height, PixelFormat.Rgba64, frames, readPoster ? poster : null, comparePoster: readPoster);
    }

    /// <summary>Reads every frame through every rendering of it, coarse ones included: the last one is the frame of the eager load.</summary>
    private static async Task ReadRefinementsAsync(ImageReader<Rgba64> reader, List<FrameSummary> frames, bool asynchronous)
    {
        using var destination = new Image<Rgba64>(reader.Info.Width, reader.Info.Height);
        while (true)
        {
            FrameRefinement? refinement;
            var renderings = 0;
            do
            {
                refinement = asynchronous ? await reader.ReadFrameRefinementIntoAsync(destination).ConfigureAwait(false) : reader.ReadFrameRefinementInto(destination);
                if (refinement is not null && (refinement.Downsampling < 1 || refinement.CompletedPasses < 0 || ++renderings > 64))
                    throw new UnreachableException($"Refinement {renderings} has the downsampling {refinement.Downsampling} and {refinement.CompletedPasses} passes");
            }
            while (refinement is { IsFinal: false });

            if (refinement is null)
            {
                if (renderings != 0)
                    throw new UnreachableException("The reader ended a frame without its final refinement");

                return;
            }

            frames.Add(FrameSummary.Capture(destination.Frames[0]));
        }
    }

    private static async Task<ImageReader<Rgba64>> OpenReaderAsync(Stream stream, ImageReaderOptions options, bool asynchronous)
    {
        if (asynchronous)
            return await Image.OpenReaderAsync<Rgba64>(stream, options).ConfigureAwait(false);

        return Image.OpenReader<Rgba64>(stream, options);
    }

    private static Outcome<T> Run<T>(PoolAudit audit, string operation, Func<T> action)
        where T : class
    {
        T? value = null;
        Exception? exception = null;
        try
        {
            value = action();
        }
        catch (Exception ex)
        {
            exception = ex;
        }

        var leak = audit.Verify();
        if (leak is not null)
            return new Outcome<T>(value, exception, FuzzFailure.Property(operation + "-resources", $"{operation} {Describe(exception)}; {leak}"));

        if (exception is not null and not ImageException)
            return new Outcome<T>(value, exception, FuzzFailure.FromException(operation, exception));

        return new Outcome<T>(value, exception, Failure: null);
    }

    private sealed record Outcome<T>(T? Value, Exception? Exception, FuzzFailure? Failure)
        where T : class
    {
        public bool Succeeded => Exception is null;
    }

    /// <summary>What a collection describes and decodes, comparable as text; throws only for undocumented exceptions.</summary>
    private sealed class CollectionSummary
    {
        public const int MaxDecodedEntries = 8;

        public Exception? Exception { get; private init; }

        public ImageFormat Format { get; private init; }

        public bool Succeeded => Exception is null;

        public List<EntrySummary> Entries { get; } = [];

        public static CollectionSummary Load(Func<ImageCollection> load, int variant)
        {
            ImageCollection collection;
            try
            {
                collection = load();
            }
            catch (ImageException exception)
            {
                return new CollectionSummary { Exception = exception };
            }

            using (collection)
            {
                // The first entries are decoded in an order the variant chooses: each entry is also decoded after the others
                var count = collection.Count;
                var decodedCount = Math.Min(count, MaxDecodedEntries);
                var outcomes = new (DecodedEntry? Decoded, string? Error)[decodedCount];
                for (var i = 0; i < decodedCount; i++)
                {
                    var index = (variant & 2) != 0 ? decodedCount - 1 - i : i;
                    try
                    {
                        using var image = collection[index].Decode(new ImageDecodeOptions { Configuration = Configuration, Conversion = Conversion, CmykConversion = CmykConversion });
                        if (image.Frames.Count != 1 || image.PosterFrame is not null || image.IsAnimated)
                            throw new UnreachableException($"The entry {index} decodes as {image.Frames.Count} frame(s), animated: {image.IsAnimated}");

                        outcomes[index] = (new DecodedEntry(image.Width, image.Height, image.PixelFormat, FrameSummary.Capture(image.Frames[0])), null);
                    }
                    catch (ImageException exception)
                    {
                        outcomes[index] = (null, exception.GetType().Name);
                    }
                }

                var result = new CollectionSummary { Format = collection.Format };
                for (var i = 0; i < count; i++)
                {
                    var entry = collection[i];
                    var description = FormattableString.Invariant($"{entry.Size.Width}x{entry.Size.Height} {entry.PixelFormat} {entry.ColorModel} {entry.BitsPerComponent} {entry.MayHaveTransparency} {entry.PayloadFormat} {entry.Hotspot}");
                    var (decoded, error) = i < decodedCount ? outcomes[i] : (null, "not decoded");
                    result.Entries.Add(new EntrySummary(entry.Size.Width, entry.Size.Height, description, decoded, error));
                }

                return result;
            }
        }

        public override string ToString()
            => Exception is not null ? Exception.GetType().Name : $"{Format} [{string.Join("; ", Entries)}]";
    }

    private sealed record EntrySummary(int Width, int Height, string Description, DecodedEntry? Decoded, string? Error)
    {
        public override string ToString() => $"{Description} -> {(Decoded is { } decoded ? $"{decoded.Width}x{decoded.Height} {decoded.PixelFormat} {decoded.Frame.PixelHash}" : Error)}";
    }

    private sealed record DecodedEntry(int Width, int Height, PixelFormat PixelFormat, FrameSummary Frame);

    private sealed record FrameSummary(int Width, int Height, FrameDuration Duration, string PixelHash)
    {
        public static FrameSummary Capture(ImageFrame frame)
        {
            var bytes = new byte[frame.Width * frame.Height * PixelFormats.GetBytesPerPixel(frame.PixelFormat)];
            frame.CopyPixelBytesTo(bytes);
            return new FrameSummary(frame.Width, frame.Height, frame.Metadata.Duration, Convert.ToHexString(SHA256.HashData(bytes))[..16]);
        }
    }

    private sealed class DecodedSummary(int width, int height, PixelFormat pixelFormat, IReadOnlyList<FrameSummary> frames, FrameSummary? poster, bool comparePoster)
    {
        public int Width { get; } = width;

        public int Height { get; } = height;

        public PixelFormat PixelFormat { get; } = pixelFormat;

        public IReadOnlyList<FrameSummary> Frames { get; } = frames;

        public FrameSummary? Poster { get; } = poster;

        public bool ComparePoster { get; } = comparePoster;

        public static DecodedSummary Capture(Image image)
        {
            var frames = new List<FrameSummary>();
            foreach (var frame in image.Frames)
            {
                frames.Add(FrameSummary.Capture(frame));
            }

            return new DecodedSummary(image.Width, image.Height, image.PixelFormat, frames, image.PosterFrame is { } poster ? FrameSummary.Capture(poster) : null, comparePoster: true);
        }

        public bool Matches(DecodedSummary other)
        {
            if (Width != other.Width || Height != other.Height || PixelFormat != other.PixelFormat || !Frames.SequenceEqual(other.Frames))
                return false;

            return !ComparePoster || !other.ComparePoster || Equals(Poster, other.Poster);
        }

        public override string ToString()
            => FormattableString.Invariant($"{Width}x{Height} {PixelFormat}, {Frames.Count} frame(s) [{string.Join(", ", Frames.Select(frame => $"{frame.Duration}:{frame.PixelHash}"))}], poster {(ComparePoster ? Poster?.PixelHash ?? "none" : "not read")}");
    }
}
