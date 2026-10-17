using Meziantou.Framework.Imaging;

namespace Meziantou.Framework.SnapshotTesting.Imaging;

/// <summary>
/// Every frame of an image file: the displayed frames of an animation, the pages of a TIFF file, or the representations of
/// an ICO or CUR file, in file order.
/// </summary>
internal sealed class DecodedSnapshot : IDisposable
{
    private static readonly ImageDecodeOptions DecodeOptions = new() { CmykConversion = CmykConversionOptions.Default };

    // The pixels are compared, not the colors they mean: a gray profile on an image converted to RGBA is dropped
    private static readonly PixelConversionOptions ConversionOptions = new() { DiscardIncompatibleColorProfile = true };

    private readonly List<Image> _images;

    private DecodedSnapshot(List<Image> images)
    {
        _images = images;
        var frames = new List<ImageFrame>();
        foreach (var image in images)
        {
            foreach (var frame in image.Frames)
            {
                frames.Add(frame);
            }
        }

        Frames = frames;
    }

    /// <summary>Gets the decoded images: one, or one per page or representation.</summary>
    public IReadOnlyList<Image> Images => _images;

    /// <summary>Gets the frames of every image, in order.</summary>
    public IReadOnlyList<ImageFrame> Frames { get; }

    /// <summary>Gets a value indicating whether a frame has samples of more than 8 bits.</summary>
    public bool HasHighPrecisionSamples => _images.Exists(static image => image.PixelFormat is PixelFormat.Rgba64 or PixelFormat.Gray16);

    /// <summary>Decodes an image file. CMYK images are converted to RGB.</summary>
    /// <exception cref="ImageException">The data is not an image, or the library cannot decode it.</exception>
    /// <exception cref="NotSupportedException">The library cannot decode the data.</exception>
    public static DecodedSnapshot Load(byte[] data)
    {
        var images = new List<Image>();
        try
        {
            var format = Image.DetectFormat(data.AsSpan(0, Math.Min(data.Length, Image.FormatDetectionPrefixLength)));
            if (format is ImageFormat.Tiff or ImageFormat.Ico or ImageFormat.Cur)
            {
                using var collection = ImageCollection.Load(data);
                foreach (var entry in collection.Entries)
                {
                    images.Add(entry.Decode(DecodeOptions));
                }
            }
            else
            {
                images.Add(Image.Load(data, DecodeOptions));
            }

            return new DecodedSnapshot(images);
        }
        catch
        {
            DisposeAll(images);
            throw;
        }
    }

    /// <summary>Copies every frame to <typeparamref name="TPixel"/> pixels; the frames of the copy are <see cref="ImageFrame{TPixel}"/>.</summary>
    public DecodedSnapshot ConvertTo<TPixel>()
        where TPixel : unmanaged
    {
        var images = new List<Image>(_images.Count);
        try
        {
            foreach (var image in _images)
            {
                images.Add(image.CloneAs<TPixel>(ConversionOptions));
            }

            return new DecodedSnapshot(images);
        }
        catch
        {
            DisposeAll(images);
            throw;
        }
    }

    public void Dispose() => DisposeAll(_images);

    private static void DisposeAll(List<Image> images)
    {
        foreach (var image in images)
        {
            image.Dispose();
        }
    }
}
