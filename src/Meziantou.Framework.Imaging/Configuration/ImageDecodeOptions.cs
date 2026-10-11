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
}
