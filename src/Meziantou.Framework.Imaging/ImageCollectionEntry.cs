using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging;

/// <summary>
/// One entry of an <see cref="ImageCollection"/>: a page of a document or an alternative representation of a drawing.
/// </summary>
/// <remarks>
/// <para>
/// An entry describes its image without decoding it: a loaded collection reads the directory structure of the file, not
/// its pixels, so <see cref="Size"/>, <see cref="PixelFormat"/> and <see cref="Metadata"/> are available for every entry
/// of a 400-page document at the cost of a few kilobytes of reads. <see cref="Decode(ImageDecodeOptions)"/> reads and decodes exactly one
/// entry, and returns a normal, independently owned <see cref="Image"/>.
/// </para>
/// <para>
/// An entry is <em>borrowed</em> from its collection: it is valid until the collection is disposed, or until the entry is
/// removed from it. The images it returns are not: they are owned by the caller, who disposes them.
/// </para>
/// </remarks>
public sealed class ImageCollectionEntry
{
    private readonly ImageCollection _collection;
    private readonly int _sourceIndex;
    private readonly ImageCollectionEntryDescriptor? _descriptor;
    private Image? _owned;

    /// <summary>Creates an entry backed by the encoded data of a loaded collection.</summary>
    internal ImageCollectionEntry(ImageCollection collection, int sourceIndex, ImageCollectionEntryDescriptor descriptor)
    {
        _collection = collection;
        _sourceIndex = sourceIndex;
        _descriptor = descriptor;
        Hotspot = descriptor.Hotspot;
    }

    /// <summary>Creates an entry that owns a copy of an image.</summary>
    internal ImageCollectionEntry(ImageCollection collection, Image owned, Point? hotspot)
    {
        _collection = collection;
        _sourceIndex = -1;
        _owned = owned;
        Hotspot = hotspot;
    }

    /// <summary>Gets the size of the entry, in pixels. Entries of one collection may differ in size.</summary>
    public Size Size => _descriptor?.Size ?? OwnedImage.Size;

    /// <summary>Gets the lossless working representation of the entry: the pixel format <see cref="Decode(ImageDecodeOptions)"/> produces by default.</summary>
    public PixelFormat PixelFormat => _descriptor?.PixelFormat ?? OwnedImage.PixelFormat;

    /// <summary>Gets the color model of the encoded samples.</summary>
    public ImageColorModel ColorModel => _descriptor?.ColorModel ?? GetColorModel(OwnedImage.PixelFormat);

    /// <summary>Gets the precision of one encoded component, in bits.</summary>
    public int BitsPerComponent => _descriptor?.BitsPerComponent ?? PixelFormats.GetBitsPerComponent(OwnedImage.PixelFormat);

    /// <summary>Gets a value indicating whether the entry can have transparent pixels.</summary>
    public bool MayHaveTransparency => _descriptor?.MayHaveTransparency ?? PixelFormats.HasAlpha(OwnedImage.PixelFormat);

    /// <summary>
    /// Gets the format of the stored payload of the entry (<see cref="ImageFormat.Png"/> or <see cref="ImageFormat.Bmp"/>
    /// inside an icon, <see cref="ImageFormat.Tiff"/> for a page), or <see cref="ImageFormat.Unknown"/> for an entry that
    /// was added to the collection and not encoded yet.
    /// </summary>
    public ImageFormat PayloadFormat => _descriptor?.PayloadFormat ?? ImageFormat.Unknown;

    /// <summary>
    /// Gets the hotspot of a cursor representation, in pixels from the top-left corner of this entry, or
    /// <see langword="null"/> when the entry is not a cursor representation.
    /// </summary>
    /// <remarks>
    /// A hotspot is cursor metadata, not a pixel offset: it never moves the image inside a canvas and is validated against
    /// the size of this entry only.
    /// </remarks>
    public Point? Hotspot { get; }

    /// <summary>Gets the metadata of the entry. Entries of one collection have independent metadata.</summary>
    public ImageMetadata Metadata => _descriptor?.Metadata ?? OwnedImage.Metadata;

    /// <summary>Gets the image owned by this entry, or <see langword="null"/> for an entry backed by encoded data.</summary>
    internal Image? OwnedOrNull => _owned;

    private Image OwnedImage => _owned ?? throw new ObjectDisposedException(nameof(ImageCollectionEntry));

    /// <summary>Decodes the entry into a new image, in its default pixel format.</summary>
    /// <param name="options">The options, or <see langword="null"/> to decode with the configuration of the collection.</param>
    /// <returns>A new image owned by the caller. It is a still image: a page or a representation is never an animation.</returns>
    /// <exception cref="ObjectDisposedException">The collection is disposed.</exception>
    /// <exception cref="InvalidImageContentException">The data of the entry is malformed or truncated.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The entry uses a recognized but unsupported feature.</exception>
    /// <exception cref="ImageResourceLimitException">A configured limit is exceeded.</exception>
    /// <exception cref="IOException">An I/O error occurred.</exception>
    public Image Decode(ImageDecodeOptions? options = null) => DecodeCore(options, pixelFormat: null);

    /// <summary>Decodes the entry into a new image of the requested pixel type.</summary>
    /// <typeparam name="TPixel">The pixel type of the result.</typeparam>
    /// <param name="options">The options, or <see langword="null"/> to decode with the configuration of the collection.</param>
    /// <returns>A new image owned by the caller.</returns>
    /// <exception cref="NotSupportedException"><typeparamref name="TPixel"/> is not a built-in pixel struct.</exception>
    /// <exception cref="ObjectDisposedException">The collection is disposed.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The conversion would drop information without an explicit policy.</exception>
    public Image<TPixel> Decode<TPixel>(ImageDecodeOptions? options = null)
        where TPixel : unmanaged
        => (Image<TPixel>)DecodeCore(options, PixelFormats.GetPixelFormat<TPixel>());

    /// <summary>Releases the image an owned entry holds (called when the entry is removed or the collection is disposed).</summary>
    internal void DisposeOwned()
    {
        _owned?.Dispose();
        _owned = null;
    }

    private Image DecodeCore(ImageDecodeOptions? options, PixelFormat? pixelFormat)
    {
        _collection.ThrowIfDisposed();

        // Without explicit options, an entry is decoded with the configuration the collection was loaded with, so the
        // limits a caller set on Load also apply to every entry it extracts
        options ??= _collection.Configuration == ImageConfiguration.Default
            ? ImageDecodeOptions.Default
            : new ImageDecodeOptions { Configuration = _collection.Configuration };

        if (_sourceIndex >= 0)
            return _collection.DecodeSourceEntry(_sourceIndex, options, pixelFormat);

        var image = OwnedImage;
        if (pixelFormat is null || pixelFormat == image.PixelFormat)
            return image.Clone();

        return PixelFormats.GetPixelType(pixelFormat.Value) switch
        {
            var type when type == typeof(Rgba32) => image.CloneAs<Rgba32>(options.Conversion),
            var type when type == typeof(Bgra32) => image.CloneAs<Bgra32>(options.Conversion),
            var type when type == typeof(Rgb24) => image.CloneAs<Rgb24>(options.Conversion),
            var type when type == typeof(Rgba64) => image.CloneAs<Rgba64>(options.Conversion),
            var type when type == typeof(Gray8) => image.CloneAs<Gray8>(options.Conversion),
            _ => image.CloneAs<Gray16>(options.Conversion),
        };
    }

    private static ImageColorModel GetColorModel(PixelFormat format)
    {
        if (PixelFormats.IsGrayscale(format))
            return ImageColorModel.Grayscale;

        return PixelFormats.HasAlpha(format) ? ImageColorModel.Rgba : ImageColorModel.Rgb;
    }
}
