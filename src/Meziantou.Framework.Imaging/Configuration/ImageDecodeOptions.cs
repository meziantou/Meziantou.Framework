namespace Meziantou.Framework.Imaging;

/// <summary>Immutable options for the eager <c>Image.Load</c> and <c>Image.LoadAsync</c> methods.</summary>
public sealed class ImageDecodeOptions
{
    /// <summary>Gets the default options.</summary>
    public static ImageDecodeOptions Default { get; } = new();

    /// <summary>Gets the configuration of the loaded image (resource limits, parallelism). Defaults to <see cref="ImageConfiguration.Default"/>.</summary>
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
    /// Gets the maximum number of displayed frames to decode, or <see langword="null"/> to decode all frames.
    /// This is a deliberate prefix selection: decoding stops after the requested number of frames and the remaining data
    /// is not examined or validated. A separate poster frame is always preserved and is not counted.
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

    /// <summary>Gets the options used when a typed load converts decoded samples to the requested pixel type. Defaults to <see cref="PixelConversionOptions.Default"/>.</summary>
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
    /// Gets the color conversion applied to images whose samples are CMYK inks (four-component JPEG, separated TIFF), or
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
    /// Gets the area of the image to decode, in pixels of the stored image at full resolution (before its orientation is
    /// applied), or <see langword="null"/> (the default) to decode the whole image. The decoded image holds the part of
    /// the area that is in the image.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A JPEG 2000 file only decodes the tiles that the area meets, and in them the code-blocks whose coefficients reach
    /// it: the samples of the area are those of a full decode, for less time (the wavelet transformation of a tile that the
    /// area meets is still whole). With <see cref="ScaleDenominator"/> or <see cref="TargetSize"/>, the area is divided like
    /// the image: its edges are rounded up, as the reference grid of the format does, and an area narrower than a pixel of
    /// the reduced image keeps the pixel that covers it.
    /// </para>
    /// <para>
    /// The other formats cannot decode an area: a load with a region is an <see cref="UnsupportedImageFeatureException"/>, and
    /// so is an area that does not meet the image.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The area is empty or starts at a negative coordinate.</exception>
    public Rectangle? Region
    {
        get;
        init
        {
            if (value is { } region && (region.IsEmpty || region.X < 0 || region.Y < 0 || region.Width < 0 || region.Height < 0))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The region must not be empty nor start at a negative coordinate.");

            field = value;
        }
    }
}
