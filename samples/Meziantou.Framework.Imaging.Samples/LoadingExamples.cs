using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.Samples;

/// <summary>Examples of the documentation site (docs/01-Imaging/04-Concepts and 05-LoadingAndSaving), run by DocumentationExampleTests.</summary>
internal static class LoadingExamples
{
    /// <summary>The same image loaded from a file, a stream and bytes in memory.</summary>
    #region Imaging_LoadFromEverySource
    public static Size[] LoadFromEverySource(string path)
    {
        // From a file: Load opens the file, reads it and closes it before returning
        using var fromFile = Image.Load(path);

        // From a stream: reading starts at the current position, and the stream is left open
        using var stream = File.OpenRead(path);
        using var fromStream = Image.Load(stream);

        // From bytes in memory: a byte[] converts to a ReadOnlySpan<byte>, which is not retained
        var bytes = File.ReadAllBytes(path);
        using var fromBytes = Image.Load(bytes);

        return [fromFile.Size, fromStream.Size, fromBytes.Size];
    }
    #endregion

    /// <summary>An asynchronous load from a stream, for example the body of an HTTP request.</summary>
    #region Imaging_LoadAsync
    public static async Task<Image> LoadUploadAsync(Stream upload, CancellationToken cancellationToken)
    {
        // The stream is read with asynchronous I/O; the caller keeps it and disposes it.
        // The returned image is owned by the caller, who disposes it too.
        return await Image.LoadAsync(upload, cancellationToken: cancellationToken);
    }
    #endregion

    /// <summary>A typed load converts the decoded pixels to the requested type, with an explicit policy for the alpha.</summary>
    #region Imaging_LoadTyped
    public static Image<Rgb24> LoadOpaque(string path)
    {
        // Rgb24 has no alpha: transparent pixels are composited over white instead of failing the load.
        // Without a background color, a source that is not opaque throws UnsupportedImageFeatureException
        return Image.Load<Rgb24>(path, new ImageDecodeOptions
        {
            Conversion = new PixelConversionOptions { BackgroundColor = new Rgba64(65535, 65535, 65535) },
        });
    }
    #endregion

    /// <summary>Detects the format of a file from its first bytes, without decoding anything.</summary>
    #region Imaging_DetectFormat
    public static ImageFormat DetectFormat(string path)
    {
        // The first 18 bytes are enough for every format; the extension of the file is never used
        var prefix = new byte[Image.FormatDetectionPrefixLength];
        using var stream = File.OpenRead(path);
        var length = stream.ReadAtLeast(prefix, prefix.Length, throwOnEndOfStream: false);
        return Image.DetectFormat(prefix.AsSpan(0, length)); // ImageFormat.Unknown when nothing matches
    }
    #endregion

    /// <summary>Encodes an image in memory: a stream has no extension, so the encoder is required.</summary>
    #region Imaging_SaveToStream
    public static async Task<byte[]> EncodeAsPngAsync(Image image, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream();

        // Writing starts at the current position of the stream, which is left open
        await image.SaveAsync(stream, new PngEncoder(), cancellationToken);
        return stream.ToArray();
    }
    #endregion

    /// <summary>A save is validated against the output before anything is written.</summary>
    #region Imaging_SaveValidation
    public static bool TrySaveAsJpeg(Image image, string path)
    {
        try
        {
            image.Save(path, new JpegEncoder { Quality = 90 });
            return true;
        }
        catch (UnsupportedImageFeatureException)
        {
            // JPEG cannot store this image as it is (an animation, transparency, 16-bit samples, metadata...).
            // Nothing was written: a file that already exists at path is left untouched
            return false;
        }
    }
    #endregion

    /// <summary>Stricter resource limits for images that come from untrusted users.</summary>
    #region Imaging_ConfigureLimits
    public static Image LoadAvatar(Stream upload)
    {
        // Create the configuration once and share it: it is immutable and thread-safe
        var configuration = new ImageConfiguration
        {
            Limits = new ImageResourceLimits
            {
                MaxEncodedBytes = 10 * 1024 * 1024, // 10 MiB of input
                MaxWidth = 4096,
                MaxHeight = 4096,
                MaxFramePixels = 16_000_000,
                MaxFrames = 100,
            },
        };

        // An input over a limit throws ImageResourceLimitException before the memory is allocated.
        // The image keeps the configuration: its later operations are bound by the same limits
        return Image.Load(upload, new ImageDecodeOptions { Configuration = configuration });
    }
    #endregion

    /// <summary>Distinct images can be processed concurrently, and the immutable settings shared.</summary>
    #region Imaging_ProcessInParallel
    public static void CreateThumbnails(IReadOnlyList<string> inputPaths, string outputDirectory, CancellationToken cancellationToken)
    {
        // Options and encoders are immutable: one instance serves every thread
        var resize = new ResizeOptions(128, 128);
        var encoder = new PngEncoder();

        Parallel.ForEach(inputPaths, new ParallelOptions { CancellationToken = cancellationToken }, inputPath =>
        {
            // Each thread works on its own image
            using var image = Image.Load(inputPath);
            image.Resize(resize, cancellationToken);
            image.Save(Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(inputPath) + ".png"), encoder);
        });
    }
    #endregion

    /// <summary>A geometry operation that fails leaves the image unchanged.</summary>
    #region Imaging_AtomicResize
    public static bool TryUpscale(Image image, int width, int height)
    {
        try
        {
            image.Resize(new ResizeOptions(width, height) { Mode = ResizeMode.Stretch, AllowUpscaling = true });
            return true;
        }
        catch (ImageResourceLimitException)
        {
            // Geometry is all or nothing: every frame keeps its size, its pixels and its metadata
            return false;
        }
    }
    #endregion
}
