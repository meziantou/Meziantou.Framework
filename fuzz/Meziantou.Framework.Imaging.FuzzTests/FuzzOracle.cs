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
/// every frame alive.</description></item>
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

    private static readonly PixelConversionOptions Conversion = new() { DiscardIncompatibleColorProfile = true };

    /// <summary>Checks one input.</summary>
    /// <param name="input">The input.</param>
    /// <param name="variant">Selects the reader input (stream kind, read sizes) and the reader methods; part of the case identity.</param>
    /// <returns>The first violated property, or <see langword="null"/>.</returns>
    public static FuzzFailure? Check(byte[] input, int variant) => Check(input, variant, out _);

    /// <summary>Checks one input.</summary>
    /// <param name="input">The input.</param>
    /// <param name="variant">Selects the reader input (stream kind, read sizes) and the reader methods; part of the case identity.</param>
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
            using var image = Image.Load(input, new ImageDecodeOptions { Configuration = Configuration, Conversion = Conversion });
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
            using var image = Image.Load<Rgba64>(input, new ImageDecodeOptions { Configuration = Configuration, Conversion = Conversion });
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
        else if (header.Value!.Format != detected)
        {
            return FuzzFailure.Property("identify-format", $"Identify reported {header.Value.Format}, DetectFormat {detected}");
        }

        if (full.Exception is not null && !IsLiveAllocationLimit(full.Exception))
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
            if (info is null)
                return FuzzFailure.Property("load-without-full-identify", $"Load succeeded but full identification failed: {Describe(full.Exception)}");

            var decoded = eager.Value!;
            if (info.Width != decoded.Width || info.Height != decoded.Height || info.PixelFormat != decoded.PixelFormat
                || info.FrameCount != decoded.Frames.Count || (info.HasPosterFrame == true) != (decoded.Poster is not null)
                || header.Value!.Width != decoded.Width || header.Value.Height != decoded.Height || header.Value.PixelFormat != decoded.PixelFormat)
            {
                return FuzzFailure.Property("identify-mismatch", $"Identify (header {Describe(header.Value)}, full {Describe(info)}) disagrees with the load ({decoded})");
            }
        }

        // Typed versus untyped, eager versus sequential
        if (eager.Succeeded != typed.Succeeded && !IsLiveAllocationLimit(eager.Exception) && !IsLiveAllocationLimit(typed.Exception))
            return FuzzFailure.Property("typed-untyped", $"Untyped load {Describe(eager)}, Rgba64 load {Describe(typed)}");

        if (eager.Succeeded && typed.Succeeded && eager.Value!.Frames.Count != typed.Value!.Frames.Count)
            return FuzzFailure.Property("typed-untyped-frames", $"Untyped load {eager.Value}, Rgba64 load {typed.Value}");

        if (typed.Succeeded && sequential.Succeeded)
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
    private static string CheckDecodedImage(byte[] input, DecodedSummary decoded, int variant)
    {
        using var image = Image.Load(input, new ImageDecodeOptions { Configuration = Configuration, Conversion = Conversion });

        // A frame limit selects a prefix of a valid input
        using (var first = Image.Load(input, new ImageDecodeOptions { Configuration = Configuration, Conversion = Conversion, FrameLimit = 1 }))
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
            _ => [new JpegEncoder { Quality = 1 + (variant % 100) }, new JpegEncoder { MetadataHandling = MetadataHandling.Strip }],
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
            using var reloaded = Image.Load(encoded, new ImageDecodeOptions { Configuration = Configuration, Conversion = Conversion });
            if (reloaded.Width != image.Width || reloaded.Height != image.Height || reloaded.Frames.Count != image.Frames.Count)
                return $"re-encode-structure: the re-encoded {format} file decodes as {reloaded.Width}x{reloaded.Height} with {reloaded.Frames.Count} frame(s), the source as {decoded}";

            if (format == ImageFormat.Png)
            {
                var again = DecodedSummary.Capture(reloaded);
                if (!again.Matches(decoded))
                    return $"re-encode-png-lossless: the re-encoded PNG decodes as {again}, the source as {decoded}";
            }
        }

        // Processing on whatever geometry and orientation the input declared
        image.AutoOrient();
        image.Resize(new ResizeOptions(Math.Max(1, image.Width / 2), Math.Max(1, (image.Height * 2) / 3)) { Mode = ResizeMode.Stretch });
        image.Rotate(RotateMode.Rotate90);
        image.Flip(FlipMode.Horizontal);
        image.Crop(new Rectangle(0, 0, Math.Max(1, image.Width - 1), image.Height));
        image.Convolve(new ConvolutionOptions(new ConvolutionKernel(3, 3, [0, -1, 0, -1, 5, -1, 0, -1, 0])) { EdgeMode = ConvolutionEdgeMode.Mirror });
        image.Grayscale();
        return string.Empty;
    }

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
        await using var stream = new TestInputStream(input)
        {
            Seekable = !shortReads,
            MaxBytesPerRead = shortReads ? 1 + (variant >> 4) % 7 : 0,
            ForbidSynchronousReads = asynchronous,
            ForbidAsynchronousReads = !asynchronous,
        };
        var options = new ImageReaderOptions { Configuration = Configuration, Conversion = Conversion };
        using var reader = await OpenReaderAsync(stream, options, asynchronous).ConfigureAwait(false);
        var info = reader.Info;
        FrameSummary? poster = null;
        if (readPoster && info.HasPosterFrame == true)
        {
            using var posterImage = asynchronous ? await reader.ReadPosterFrameAsync().ConfigureAwait(false) : reader.ReadPosterFrame();
            poster = posterImage is null ? null : FrameSummary.Capture(posterImage.Frames[0]);
        }

        var frames = new List<FrameSummary>();
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
