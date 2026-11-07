namespace Meziantou.Framework.Imaging.Formats;

/// <summary>Immutable settings for GIF encoding.</summary>
/// <remarks>
/// <para>
/// GIF output is inherently lossy: colors are reduced to at most 256 entries per frame (local palettes, including the
/// transparent entry) with a deterministic quantizer, alpha is reduced to fully transparent or fully opaque, and 16-bit
/// precision is reduced to 8 bits. Selecting the GIF encoder is the explicit opt-in to these losses. A frame whose colors fit
/// its palette is reproduced exactly; other frames use a deterministic variance-based median-cut palette, or a fixed palette,
/// optionally dithered (<see cref="Quantization"/>). Only <c>Comment</c> text entries (Latin-1) are stored as metadata.
/// </para>
/// <para>
/// Frames are encoded from full displayed frames with freshly computed disposal/transparency control data, so pixels that
/// become transparent are correctly cleared: full-canvas frames by default, or the regions that change with
/// <see cref="OptimizeFrames"/>. Unknown frame counts are supported by the streaming writer without buffering.
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

    /// <summary>
    /// Gets how colors are reduced to the palette of each frame. Defaults to <see cref="QuantizeOptions.Default"/>: at most
    /// 256 entries per frame, the transparent entry included, without dithering.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A frame whose colors fit its palette is reproduced exactly; other frames use a deterministic variance-based median cut
    /// of their colors (see <see cref="PaletteBuilder"/>), with the dithering of <see cref="QuantizeOptions.Dithering"/>.
    /// </para>
    /// <para>
    /// With <see cref="QuantizeOptions.Palette"/>, every frame maps to that palette (each frame still has its own local color
    /// table, which holds the colors of the palette). The entries get the alpha reduction of the pixels first:
    /// <see cref="GifAlphaMode.Threshold"/> drops the entries whose alpha is below <see cref="AlphaThreshold"/> (transparent
    /// pixels take the transparent entry) and keeps the color of the others, <see cref="GifAlphaMode.Flatten"/> composites them
    /// over <see cref="BackgroundColor"/>. Saving fails with an <see cref="ArgumentException"/> when no entry is left, or when
    /// 256 colors are left for a pixel format with alpha and <see cref="GifAlphaMode.Threshold"/> (no room for the transparent
    /// entry).
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
    public QuantizeOptions Quantization
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = QuantizeOptions.Default;

    /// <summary>Gets the dithering mode: <see cref="Quantization"/>.<see cref="QuantizeOptions.Dithering"/>, which it sets.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="GifDithering"/>.</exception>
    [Obsolete("Use Quantization.Dithering, which also selects other dithering modes.")]
    public GifDithering Dithering
    {
        get => (GifDithering)Quantization.Dithering;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The dithering mode is not valid.");

            Quantization = Quantization.With(dithering: (DitheringMode)value);
        }
    }

    /// <summary>
    /// Gets the maximum number of palette entries per frame, including the transparent entry:
    /// <see cref="Quantization"/>.<see cref="QuantizeOptions.MaxColors"/>, which it sets.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 2 and 256.</exception>
    [Obsolete("Use Quantization.MaxColors.")]
    public int MaxColors
    {
        get => Quantization.MaxColors;
        init => Quantization = Quantization.With(maxColors: value);
    }

    /// <summary>Gets a value indicating whether frames are written interlaced. Defaults to <see langword="false"/>.</summary>
    public bool Interlaced { get; init; }

    /// <summary>
    /// Gets a value indicating whether the frames of an animation after the first one are written as the region of the
    /// canvas that changes. Defaults to <see langword="false"/>: every frame covers the canvas and is disposed with "restore
    /// to background".
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each frame stores the bounding box of the pixels that differ from the canvas it is drawn on, with the disposal of the
    /// previous frame ("do not dispose", "restore to background" or "restore to previous") that gives the smallest box; its
    /// unchanged pixels take the transparent entry, and its palette is built from the pixels of the box. A GIF frame cannot
    /// make a pixel transparent: the box of the previous frame grows to cover the pixels that become transparent, and its
    /// disposal clears them. A frame equal to the previous one is a 1x1 image. The first frame covers the canvas, and the
    /// durations, the play count and the number of frames are the same as without the setting.
    /// </para>
    /// <para>
    /// The pixels are compared after the alpha reduction, before the colors are reduced: when every frame fits its palette,
    /// the displayed frames are exactly those of the full-frame output. Otherwise a pixel that does not change keeps the
    /// color the frame that drew it was given, where a full frame would map it to its own palette again.
    /// </para>
    /// <para>
    /// The encoder keeps two copies of the canvas (4 bytes per pixel each), and writes each frame when the next one is
    /// given (or when the output completes): the control data of a frame holds the disposal chosen for the next one.
    /// </para>
    /// </remarks>
    public bool OptimizeFrames { get; init; }

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
