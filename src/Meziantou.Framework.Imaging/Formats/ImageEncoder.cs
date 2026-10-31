using Meziantou.Framework.Imaging.Internals;

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

    /// <summary>
    /// Gets a value indicating whether the output stores pixels of a format that widens (<see cref="PixelFormatWidening.IsWidened"/>:
    /// <see cref="PixelFormat.La16"/>, <see cref="PixelFormat.La32"/>, <see cref="PixelFormat.Rgb48"/>, <see cref="PixelFormat.A8"/>
    /// and the interoperability formats) as they are; otherwise the writer gives the encoder a copy of the frames widened
    /// without any loss (<see cref="PixelFormatWidening"/>). The other formats always return <see langword="true"/>.
    /// </summary>
    /// <param name="format">The pixel format of the frames.</param>
    /// <returns><see langword="true"/> if the encoder receives the frames in <paramref name="format"/>.</returns>
    internal virtual bool StoresPixelFormat(PixelFormat format) => !PixelFormatWidening.IsWidened(format);

    /// <summary>Creates the encoder of a format with its default settings.</summary>
    /// <param name="format">The format to write.</param>
    /// <returns>
    /// A new encoder whose <see cref="Format"/> is <paramref name="format"/>, such as a <see cref="PngEncoder"/> for
    /// <see cref="ImageFormat.Png"/>. <see cref="ImageFormat.Ico"/> and <see cref="ImageFormat.Cur"/> both give an
    /// <see cref="IcoEncoder"/>, of the matching <see cref="IcoEncoder.Kind"/>.
    /// </returns>
    /// <remarks>This method is thread-safe. To change a setting, create the encoder type directly with an object initializer.</remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="format"/> is <see cref="ImageFormat.Unknown"/> or not a defined value.</exception>
    public static ImageEncoder Create(ImageFormat format) => format switch
    {
        ImageFormat.Png => new PngEncoder(),
        ImageFormat.Gif => new GifEncoder(),
        ImageFormat.Jpeg => new JpegEncoder(),
        ImageFormat.WebP => new WebPEncoder(),
        ImageFormat.Qoi => new QoiEncoder(),
        ImageFormat.Bmp => new BmpEncoder(),
        ImageFormat.Tga => new TgaEncoder(),
        ImageFormat.Pnm => new PnmEncoder(),
        ImageFormat.Tiff => new TiffEncoder(),
        ImageFormat.Ico => new IcoEncoder(),
        ImageFormat.Cur => new IcoEncoder { Kind = IconKind.Cursor },
        ImageFormat.Ani => new AniEncoder(),
        ImageFormat.Avif => new AvifEncoder(),
        ImageFormat.JpegXl => new JpegXlEncoder(),
        ImageFormat.Heic => new HeicEncoder(),
        ImageFormat.Jpeg2000 => new Jpeg2000Encoder(),
        ImageFormat.OpenExr => new OpenExrEncoder(),
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "The format is unknown or not a defined value."),
    };

    /// <summary>
    /// Creates the encoder a file extension selects: the one <c>Save(path)</c> uses when no encoder is given.
    /// </summary>
    /// <param name="extension">
    /// The extension, with or without its leading period (<c>.png</c> or <c>png</c>), compared without regard to ASCII case.
    /// A path is not an extension: use <see cref="Path.GetExtension(string)"/> first.
    /// </param>
    /// <returns>
    /// The default encoder of the format of the extension (<see cref="Create(ImageFormat)"/>), with the settings of the
    /// variant some extensions name: <c>.apng</c> gives an animated <see cref="PngEncoder"/>
    /// (<see cref="PngAnimationMode.Animated"/>), <c>.pbm</c> a <see cref="PnmEncoder"/> of
    /// <see cref="PnmPixelLayout.Bitmap"/> pixels, and <c>.j2k</c>, <c>.j2c</c> and <c>.jpc</c> a <see cref="Jpeg2000Encoder"/>
    /// of a bare codestream (<see cref="Jpeg2000Container.Codestream"/>).
    /// </returns>
    /// <remarks>
    /// The extensions are those of <see cref="ImageFormatInfo.FileExtensions"/>. This method is thread-safe.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="extension"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// The extension is not recognized, or names files the library reads but does not write (<c>.jpx</c>, <c>.jpf</c>,
    /// <c>.jph</c>, <c>.jhc</c>).
    /// </exception>
    public static ImageEncoder FromFileExtension(string extension)
    {
        ArgumentNullException.ThrowIfNull(extension);
        var normalized = extension.StartsWith('.', StringComparison.Ordinal) ? extension : "." + extension;
        return FromExtension(normalized, nameof(extension));
    }

    /// <summary>Selects the default encoder for a file name, from its extension (see <see cref="FromFileExtension(string)"/>).</summary>
    /// <param name="path">The file path.</param>
    /// <returns>The encoder.</returns>
    /// <exception cref="ArgumentException">The extension is not recognized.</exception>
    internal static ImageEncoder FromPath(string path) => FromExtension(Path.GetExtension(path), nameof(path));

    private static ImageEncoder FromExtension(string extension, string parameterName)
    {
        // The variants an extension names; every other extension selects the default encoder of its format
        if (extension.Equals(".apng", StringComparison.OrdinalIgnoreCase))
            return new PngEncoder { AnimationMode = PngAnimationMode.Animated };

        if (extension.Equals(".pbm", StringComparison.OrdinalIgnoreCase))
            return new PnmEncoder { PixelLayout = PnmPixelLayout.Bitmap };

        if (extension.Equals(".j2k", StringComparison.OrdinalIgnoreCase) || extension.Equals(".j2c", StringComparison.OrdinalIgnoreCase) || extension.Equals(".jpc", StringComparison.OrdinalIgnoreCase))
            return new Jpeg2000Encoder { Container = Jpeg2000Container.Codestream };

        // The files of these extensions are read, never written: the extended file format, and the high-throughput block coder
        if (extension.Equals(".jpx", StringComparison.OrdinalIgnoreCase) || extension.Equals(".jpf", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"JPEG 2000 files of the extended format ('{extension}') can be read but not written. Use the extension '.jp2', or specify an encoder explicitly.", parameterName);

        if (extension.Equals(".jph", StringComparison.OrdinalIgnoreCase) || extension.Equals(".jhc", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"High-throughput JPEG 2000 files ('{extension}') can be read but not written. Use the extension '.jp2' or '.j2k', or specify an encoder explicitly.", parameterName);

        if (extension.Length > 1 && ImageFormatInfo.TryGetByFileExtension(extension, out var info))
            return Create(info.Format);

        throw new ArgumentException($"Cannot select an encoder for the file extension '{extension}'. Specify an encoder explicitly.", parameterName);
    }
}
