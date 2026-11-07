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
    /// Gets the color conversion applied to images whose samples are CMYK inks (four-component JPEG), or
    /// <see langword="null"/> (the default) to reject them with an <see cref="UnsupportedImageFeatureException"/>.
    /// </summary>
    /// <remarks>
    /// No pixel format holds CMYK samples, so such an image can only be loaded by converting its colors. Like every color
    /// conversion it is explicit: set <c>new CmykConversionOptions()</c> to convert to sRGB. Other images are not affected.
    /// </remarks>
    public CmykConversionOptions? CmykConversion { get; init; }

    /// <summary>
    /// Gets the factor by which the decoder divides the width and the height of the image, a power of two. Defaults to 1:
    /// the image is decoded at full resolution.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The decoded image has <c>ceil(width / ScaleDenominator)</c> by <c>ceil(height / ScaleDenominator)</c> pixels,
    /// computed from fewer samples than a full decode, for less time and memory, by the formats that support it (see the
    /// format reference of the readme). The pixels are close to a resize of the full image, not equal to it. The metadata
    /// is the one of the file, like after a resize.
    /// </para>
    /// <para>
    /// A file that cannot be decoded at this scale (a format that only decodes at full resolution, or a scale that the file
    /// does not allow) is an <see cref="UnsupportedImageFeatureException"/>. Set <see cref="TargetSize"/> instead to get
    /// the smallest image the file allows without failing. The two properties cannot be set together.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a positive power of two.</exception>
    /// <exception cref="ArgumentException">The value is not 1 and <see cref="TargetSize"/> is set.</exception>
    public int ScaleDenominator
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            if (!int.IsPow2(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The scale denominator must be a power of two.");

            if (value != 1 && TargetSize is not null)
                throw new ArgumentException("The ScaleDenominator and TargetSize options cannot both be set.", nameof(value));

            field = value;
        }
    } = 1;

    /// <summary>
    /// Gets the smallest size the decoder needs to produce, or <see langword="null"/> (the default) to decode at full
    /// resolution. A file that can be decoded at a reduced resolution is decoded at the largest
    /// <see cref="ScaleDenominator"/> it allows whose size is still at least this size in both dimensions; any other file
    /// is decoded at full resolution.
    /// </summary>
    /// <remarks>
    /// The size is compared with the stored dimensions of the image, before its orientation is applied. The decoded image
    /// is usually larger than the target: resize it afterwards to get an exact size. For a thumbnail, this is much faster
    /// than resizing a full decode, with nearly the same result.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The width or the height of the value is zero.</exception>
    /// <exception cref="ArgumentException">The value is not <see langword="null"/> and <see cref="ScaleDenominator"/> is not 1.</exception>
    public Size? TargetSize
    {
        get;
        init
        {
            if (value is { IsEmpty: true })
                throw new ArgumentOutOfRangeException(nameof(value), value, "The target size must not be empty.");

            if (value is not null && ScaleDenominator != 1)
                throw new ArgumentException("The ScaleDenominator and TargetSize options cannot both be set.", nameof(value));

            field = value;
        }
    }

    /// <summary>
    /// Gets a value indicating whether a caller-provided stream is left open when the reader is disposed. Defaults to
    /// <see langword="true"/>. Streams opened by the path overloads are always owned and closed by the reader.
    /// </summary>
    public bool LeaveOpen { get; init; } = true;
}
