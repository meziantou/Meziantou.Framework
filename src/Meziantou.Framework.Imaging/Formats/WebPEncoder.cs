namespace Meziantou.Framework.Imaging.Formats;

/// <summary>Immutable settings for WebP encoding (still images and animations).</summary>
/// <remarks>
/// <para>
/// <see cref="Compression"/> selects the bitstream: <see cref="WebPCompression.Lossless"/> (the default) preserves every 8-bit
/// sample; <see cref="WebPCompression.Lossy"/> trades fidelity for size according to <see cref="Quality"/>. <see cref="Effort"/>
/// trades encoding time for size and never changes the decoded pixels of lossless output.
/// </para>
/// <para>
/// WebP stores 8-bit RGB(A): gray pixel formats are written as RGB (R = G = B, decoded as RGB), and 16-bit pixel formats require
/// <see cref="AllowBitDepthReduction"/>. Alpha is always stored when the pixel format has alpha (losslessly, also in lossy
/// mode).
/// </para>
/// <para>
/// An image with several frames or animation settings is written as an animation of full-canvas frames that replace the
/// canvas ("do not blend", "do not dispose"), with freshly computed control data. A still image (one frame, no animation
/// settings) can be written to any stream. Animations need a seekable destination: the RIFF size and the alpha flag are
/// patched once every frame is written. A writer with an unknown frame count produces an animation; set
/// <see cref="ImageWriterOptions.ExpectedFrameCount"/> to 1 to write a still image to a non-seekable stream. Path outputs are
/// always seekable.
/// </para>
/// <para>
/// Metadata (subject to <see cref="ImageEncoder.MetadataHandling"/>): ICC profile (<c>ICCP</c>, RGB profiles only), EXIF
/// (<c>EXIF</c>, orientation and dimensions reconciled) and XMP (<c>XMP </c>). WebP has no text or resolution chunk.
/// </para>
/// </remarks>
public sealed class WebPEncoder : ImageEncoder
{
    /// <inheritdoc />
    public override ImageFormat Format => ImageFormat.WebP;

    /// <summary>Gets the bitstream used for every frame. Defaults to <see cref="WebPCompression.Lossless"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="WebPCompression"/>.</exception>
    public WebPCompression Compression
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The compression is not valid.");

            field = value;
        }
    }

    /// <summary>
    /// Gets the quality of <see cref="WebPCompression.Lossy"/> output, from 0 (smallest) to 100 (best). Defaults to 75. Ignored by
    /// lossless output.
    /// </summary>
    /// <remarks>
    /// The quality selects the quantizer index (100 is the finest quantizer, still lossy: chroma subsampling, color conversion
    /// and rounding remain) and the loop-filter strength. It never changes the encoding time.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 0 and 100.</exception>
    public int Quality
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 0);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 100);
            field = value;
        }
    } = 75;

    /// <summary>
    /// Gets the compression effort, from 0 (fastest, largest output) to 9 (slowest, smallest output). Defaults to 5. The effort
    /// never changes the decoded pixels of lossless output; for lossy output it changes the mode decisions, not the quality setting.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 0 and 9.</exception>
    public int Effort
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 0);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 9);
            field = value;
        }
    } = 5;

    /// <summary>
    /// Gets a value indicating whether the color of fully transparent pixels is replaced with transparent black before encoding.
    /// Defaults to <see langword="false"/>: lossless output then preserves the color of fully transparent pixels exactly.
    /// </summary>
    /// <remarks>Setting it usually makes the output smaller; the visible pixels and every alpha value are unchanged.</remarks>
    public bool ClearTransparentColors { get; init; }

    /// <summary>Gets a value indicating whether 16-bit pixel formats may be reduced to 8 bits per component (nearest rounding). Defaults to <see langword="false"/>.</summary>
    public bool AllowBitDepthReduction { get; init; }

    /// <summary>
    /// Gets how frame durations are converted to WebP milliseconds (24-bit values). Defaults to
    /// <see cref="FrameDurationRounding.RoundToNearest"/>.
    /// </summary>
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
