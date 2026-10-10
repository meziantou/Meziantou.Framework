namespace Meziantou.Framework.Imaging;

/// <summary>Immutable options for <see cref="Image.OpenReader{TPixel}(Stream, ImageReaderOptions?)"/>.</summary>
public sealed class ImageReaderOptions
{
    /// <summary>Gets the default options.</summary>
    public static ImageReaderOptions Default { get; } = new();

    /// <summary>
    /// Gets the configuration of the reader. Its resource limits apply to the whole input, and images returned by the reader
    /// are charged to the reader's allocation scope until they are disposed. Defaults to <see cref="ImageConfiguration.Default"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
    public ImageConfiguration Configuration
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = ImageConfiguration.Default;

    /// <summary>
    /// Gets the maximum number of displayed frames to read, or <see langword="null"/> to read all frames. Once the limit is
    /// reached, the reader reports a clean end of input without examining the remaining data.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public int? FrameLimit
    {
        get;
        init
        {
            if (value is not null)
            {
                ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value.Value);
            }

            field = value;
        }
    }

    /// <summary>Gets the options used to convert decoded samples to the reader's pixel type. Defaults to <see cref="PixelConversionOptions.Default"/>.</summary>
    /// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
    public PixelConversionOptions Conversion
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = PixelConversionOptions.Default;

    /// <summary>
    /// Gets a value indicating whether a caller-provided stream is left open when the reader is disposed. Defaults to
    /// <see langword="true"/>. Streams opened by the path overloads are always owned and closed by the reader.
    /// </summary>
    public bool LeaveOpen { get; init; } = true;
}
