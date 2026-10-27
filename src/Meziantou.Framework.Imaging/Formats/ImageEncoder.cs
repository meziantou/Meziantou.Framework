namespace Meziantou.Framework.Imaging.Formats;

/// <summary>The base class of the immutable encoder settings accepted by the save and writer APIs.</summary>
/// <remarks>
/// Encoders are immutable and thread-safe; a single instance can be reused for any number of save operations.
/// The set of encoders is closed in this version: there is no codec plug-in interface.
/// </remarks>
public abstract class ImageEncoder
{
    private protected ImageEncoder()
    {
    }

    /// <summary>Gets the format produced by this encoder.</summary>
    public abstract ImageFormat Format { get; }

    /// <summary>Gets the policy applied to metadata the format cannot represent. Defaults to <see cref="MetadataHandling.Strict"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="Formats.MetadataHandling"/>.</exception>
    public MetadataHandling MetadataHandling
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The metadata handling is not valid.");

            field = value;
        }
    }

    /// <summary>
    /// Gets the conversion that reduces floating-point pixels (<see cref="RgbaFloat"/>, <see cref="RgbaHalf"/>) to 16-bit
    /// integer ones (<see cref="Rgba64"/>) before they are encoded, or <see langword="null"/> (the default) to reject them.
    /// It only applies to the outputs that do not store floating-point samples: every format but OpenEXR and TIFF, and the
    /// TIFF settings that cannot (<see cref="TiffEncoder"/>). <see cref="OpenExrEncoder"/> always stores them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The reduction is the one of <see cref="Image.CloneAs{TPixel}(PixelConversionOptions?)"/> with these options: samples
    /// that are not numbers are rejected, and samples outside [0, 1] are rejected unless
    /// <see cref="PixelConversionOptions.OutOfRangeSamples"/> clips them or <see cref="PixelConversionOptions.ToneMapping"/>
    /// compresses the highlights first. The peak of the tone mapping of an eager save is the one of all its frames; a
    /// sequential writer takes the one of each frame, unless the options give it. The reduced pixels are then encoded like
    /// any <see cref="Rgba64"/> pixels, with the rules of the encoder (an 8-bit format also needs its
    /// <c>AllowBitDepthReduction</c>), and the labels of the image (such as
    /// <see cref="Metadata.ColorTransferFunction.Linear"/>) follow <see cref="MetadataHandling"/>.
    /// </para>
    /// <para>
    /// Every frame is reduced before it is encoded, in memory charged to the writer: an eager save holds the reduced copy
    /// of all of its frames.
    /// </para>
    /// </remarks>
    public PixelConversionOptions? FloatingPointConversion { get; init; }

    /// <summary>
    /// Gets a value indicating whether the output stores floating-point pixels as they are, in which case
    /// <see cref="FloatingPointConversion"/> is not used.
    /// </summary>
    internal virtual bool StoresFloatingPointSamples => false;

    /// <summary>Selects the default encoder for a file name, from its extension.</summary>
    /// <param name="path">The file path.</param>
    /// <returns>The encoder.</returns>
    /// <exception cref="ArgumentException">The extension is not recognized.</exception>
    internal static ImageEncoder FromPath(string path)
    {
        var extension = Path.GetExtension(path);
        if (extension.Equals(".png", StringComparison.OrdinalIgnoreCase))
            return new PngEncoder();

        if (extension.Equals(".apng", StringComparison.OrdinalIgnoreCase))
            return new PngEncoder { AnimationMode = PngAnimationMode.Animated };

        if (extension.Equals(".gif", StringComparison.OrdinalIgnoreCase))
            return new GifEncoder();

        if (extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
            return new JpegEncoder();

        if (extension.Equals(".webp", StringComparison.OrdinalIgnoreCase))
            return new WebPEncoder();

        if (extension.Equals(".qoi", StringComparison.OrdinalIgnoreCase))
            return new QoiEncoder();

        if (extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase) || extension.Equals(".dib", StringComparison.OrdinalIgnoreCase))
            return new BmpEncoder();

        if (extension.Equals(".tga", StringComparison.OrdinalIgnoreCase) || extension.Equals(".icb", StringComparison.OrdinalIgnoreCase) || extension.Equals(".vda", StringComparison.OrdinalIgnoreCase) || extension.Equals(".vst", StringComparison.OrdinalIgnoreCase))
            return new TgaEncoder();

        if (extension.Equals(".pnm", StringComparison.OrdinalIgnoreCase) || extension.Equals(".pam", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".ppm", StringComparison.OrdinalIgnoreCase) || extension.Equals(".pgm", StringComparison.OrdinalIgnoreCase))
            return new PnmEncoder();

        if (extension.Equals(".pbm", StringComparison.OrdinalIgnoreCase))
            return new PnmEncoder { PixelLayout = PnmPixelLayout.Bitmap };

        if (extension.Equals(".tif", StringComparison.OrdinalIgnoreCase) || extension.Equals(".tiff", StringComparison.OrdinalIgnoreCase))
            return new TiffEncoder();

        if (extension.Equals(".ico", StringComparison.OrdinalIgnoreCase))
            return new IcoEncoder();

        if (extension.Equals(".cur", StringComparison.OrdinalIgnoreCase))
            return new IcoEncoder { Kind = IconKind.Cursor };

        if (extension.Equals(".ani", StringComparison.OrdinalIgnoreCase))
            return new AniEncoder();

        if (extension.Equals(".jxl", StringComparison.OrdinalIgnoreCase))
            return new JpegXlEncoder();

        if (extension.Equals(".avif", StringComparison.OrdinalIgnoreCase) || extension.Equals(".avifs", StringComparison.OrdinalIgnoreCase))
            return new AvifEncoder();

        if (extension.Equals(".heic", StringComparison.OrdinalIgnoreCase) || extension.Equals(".heif", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".heics", StringComparison.OrdinalIgnoreCase) || extension.Equals(".heifs", StringComparison.OrdinalIgnoreCase))
            return new HeicEncoder();

        if (extension.Equals(".jp2", StringComparison.OrdinalIgnoreCase))
            return new Jpeg2000Encoder();

        if (extension.Equals(".j2k", StringComparison.OrdinalIgnoreCase) || extension.Equals(".j2c", StringComparison.OrdinalIgnoreCase) || extension.Equals(".jpc", StringComparison.OrdinalIgnoreCase))
            return new Jpeg2000Encoder { Container = Jpeg2000Container.Codestream };

        if (extension.Equals(".exr", StringComparison.OrdinalIgnoreCase))
            return new OpenExrEncoder();

        // The files of these extensions are read, never written: the extended file format, and the high-throughput block coder
        if (extension.Equals(".jpx", StringComparison.OrdinalIgnoreCase) || extension.Equals(".jpf", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"JPEG 2000 files of the extended format ('{extension}') can be read but not written. Use the extension '.jp2', or specify an encoder explicitly.", nameof(path));

        if (extension.Equals(".jph", StringComparison.OrdinalIgnoreCase) || extension.Equals(".jhc", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"High-throughput JPEG 2000 files ('{extension}') can be read but not written. Use the extension '.jp2' or '.j2k', or specify an encoder explicitly.", nameof(path));

        throw new ArgumentException($"Cannot select an encoder for the file extension '{extension}'. Specify an encoder explicitly.", nameof(path));
    }
}
