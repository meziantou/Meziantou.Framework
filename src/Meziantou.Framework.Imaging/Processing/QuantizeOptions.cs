namespace Meziantou.Framework.Imaging;

/// <summary>
/// Immutable options of a color reduction to a palette: by
/// <see cref="ImageProcessingExtensions.Quantize(Image, QuantizeOptions?, CancellationToken)"/>, and by the encoders that store a
/// palette (<see cref="Formats.GifEncoder.Quantization"/>, <see cref="Formats.BmpEncoder.Quantization"/> and
/// <see cref="Formats.PngEncoder.Quantization"/>).
/// </summary>
/// <remarks>
/// <para>
/// Without <see cref="Palette"/>, each frame gets its own palette of at most <see cref="MaxColors"/> entries: the exact colors
/// of the frame when they fit, otherwise a deterministic variance-based median cut of its colors (see
/// <see cref="PaletteBuilder"/>). With <see cref="Palette"/>, every frame maps to that palette, which is how the frames of an
/// animation share one palette. A frame whose colors are all palette entries is reproduced exactly, whatever the dithering.
/// </para>
/// <para>
/// The reduction is deterministic: the same pixels and options give the same output on every platform.
/// </para>
/// </remarks>
public sealed class QuantizeOptions
{
    /// <summary>Gets the default options: at most 256 colors per frame, without dithering.</summary>
    public static QuantizeOptions Default { get; } = new();

    /// <summary>
    /// Gets the largest number of palette entries of a frame, including the transparent entry when the frame has transparent
    /// pixels. Defaults to 256. Ignored when <see cref="Palette"/> is set. An encoder may lower it further: the indexed BMP
    /// layouts hold 2, 16 or 256 entries.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 2 and 256.</exception>
    public int MaxColors
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 2);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, ImagePalette.MaxCount);
            field = value;
        }
    } = ImagePalette.MaxCount;

    /// <summary>
    /// Gets a fixed palette every frame maps to, or <see langword="null"/> (the default) to compute the palette of each frame.
    /// Build a palette shared by several frames or images with <see cref="PaletteBuilder"/>.
    /// </summary>
    public ImagePalette? Palette { get; init; }

    /// <summary>Gets the dithering applied when a frame has colors that are not palette entries. Defaults to <see cref="DitheringMode.None"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="DitheringMode"/>.</exception>
    public DitheringMode Dithering
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The dithering mode is not valid.");

            field = value;
        }
    }

    /// <summary>
    /// Gets the strength of the dithering, from 0 (no dithering) to 1 (the full error is diffused, or the full pattern added).
    /// Defaults to 1. It is applied in 256ths.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 0 and 1.</exception>
    public double DitherStrength
    {
        get;
        init
        {
            if (!double.IsFinite(value) || value is < 0 or > 1)
                throw new ArgumentOutOfRangeException(nameof(value), value, "The dither strength must be between 0 and 1.");

            field = value;
        }
    } = 1;

    /// <summary>Creates a copy of the options with other settings, for the obsolete encoder properties that forward to them.</summary>
    internal QuantizeOptions With(int? maxColors = null, DitheringMode? dithering = null) => new()
    {
        MaxColors = maxColors ?? MaxColors,
        Palette = Palette,
        Dithering = dithering ?? Dithering,
        DitherStrength = DitherStrength,
    };
}
