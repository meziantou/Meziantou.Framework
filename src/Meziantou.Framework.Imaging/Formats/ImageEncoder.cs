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

        if (extension.Equals(".tif", StringComparison.OrdinalIgnoreCase) || extension.Equals(".tiff", StringComparison.OrdinalIgnoreCase))
            return new TiffEncoder();

        if (extension.Equals(".ico", StringComparison.OrdinalIgnoreCase))
            return new IcoEncoder();

        if (extension.Equals(".cur", StringComparison.OrdinalIgnoreCase))
            return new IcoEncoder { Kind = IconKind.Cursor };

        if (extension.Equals(".ani", StringComparison.OrdinalIgnoreCase))
            return new AniEncoder();

        throw new ArgumentException($"Cannot select an encoder for the file extension '{extension}'. Specify an encoder explicitly.", nameof(path));
    }
}
