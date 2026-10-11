namespace Meziantou.Framework.Imaging.Formats;

/// <summary>Immutable settings for GIF encoding.</summary>
/// <remarks>
/// <para>
/// GIF output is inherently lossy: colors are reduced to at most 256 entries per frame (local palettes, including the
/// transparent entry) with a deterministic quantizer, alpha is reduced to fully transparent or fully opaque, and 16-bit
/// precision is reduced to 8 bits. Selecting the GIF encoder is the explicit opt-in to these losses. A frame whose colors fit
/// its palette is reproduced exactly; other frames use a deterministic variance-based median-cut palette, optionally with
/// Floyd–Steinberg dithering. Only <c>Comment</c> text entries (Latin-1) are stored as metadata.
/// </para>
/// <para>
/// Frames are encoded from full displayed frames with freshly computed disposal/transparency control data, so pixels that
/// become transparent are correctly cleared. Unknown frame counts are supported by the streaming writer without buffering.
/// </para>
/// </remarks>
public sealed class GifEncoder : ImageEncoder
{
    /// <inheritdoc />
    public override ImageFormat Format => ImageFormat.Gif;

    /// <summary>Gets how alpha is reduced. Defaults to <see cref="GifAlphaMode.Threshold"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="GifAlphaMode"/>.</exception>
    public GifAlphaMode AlphaMode
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The alpha mode is not valid.");

            field = value;
        }
    }

    /// <summary>
    /// Gets the alpha threshold used by <see cref="GifAlphaMode.Threshold"/>, on the 8-bit scale: pixels with a lower alpha
    /// become transparent. 16-bit alpha is compared after reduction to 8 bits. Defaults to 128.
    /// </summary>
    public byte AlphaThreshold { get; init; } = 128;

    /// <summary>
    /// Gets the opaque background color used by <see cref="GifAlphaMode.Flatten"/>, interpreted in the encoded color space of
    /// the image. Required when <see cref="AlphaMode"/> is <see cref="GifAlphaMode.Flatten"/>; saving fails with an
    /// <see cref="ArgumentException"/> otherwise.
    /// </summary>
    /// <exception cref="ArgumentException">The color is not fully opaque.</exception>
    public Rgba64? BackgroundColor
    {
        get;
        init
        {
            if (value is { A: not ushort.MaxValue })
                throw new ArgumentException("The background color must be fully opaque.", nameof(value));

            field = value;
        }
    }

    /// <summary>Gets the dithering mode. Defaults to <see cref="GifDithering.None"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="GifDithering"/>.</exception>
    public GifDithering Dithering
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The dithering mode is not valid.");

            field = value;
        }
    }

    /// <summary>Gets the maximum number of palette entries per frame, including the transparent entry. Defaults to 256.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 2 and 256.</exception>
    public int MaxColors
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 2);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 256);
            field = value;
        }
    } = 256;

    /// <summary>Gets a value indicating whether frames are written interlaced. Defaults to <see langword="false"/>.</summary>
    public bool Interlaced { get; init; }

    /// <summary>Gets how frame durations are converted to GIF hundredths of a second. Defaults to <see cref="FrameDurationRounding.RoundToNearest"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="FrameDurationRounding"/>.</exception>
    public FrameDurationRounding DurationRounding
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The duration rounding is not valid.");

            field = value;
        }
    } = FrameDurationRounding.RoundToNearest;
}
