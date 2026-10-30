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
/// <see cref="AllowBitDepthReduction"/>. Alpha is always stored when the pixel format has alpha: exactly, unless
/// <see cref="AlphaQuality"/> (lossy output) is below 100. <see cref="NearLossless"/> makes lossless output smaller by
/// allowing small color errors.
/// </para>
/// <para>
/// The modes of lossy output (VP8 prediction modes) are stored in a first partition of at most 524,287 bytes. When the modes
/// chosen freely do not fit, which only happens for very large detailed images (photographs of about 40 megapixels and more
/// at high quality), the frame is encoded once more with a budget for the mode bits: the macroblocks where 4x4 prediction
/// gains least switch to cheaper modes, spread over the whole image. The quantizer stays the same; the decoded quality
/// decreases gradually with the image size, and every size up to the 16,383x16,383 limit fits.
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
    /// and rounding remain) and the loop-filter strength. It is not a target distortion or file size. Its scale is not
    /// libwebp's: measured on photographs, <c>cwebp -q 75</c> matches a quality of about 80, <c>-q 50</c> about 70 and
    /// <c>-q 25</c> about 54; lower values of this scale are coarser than the same <c>cwebp</c> values.
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
    /// Gets the quality of the alpha channel of <see cref="WebPCompression.Lossy"/> output, from 0 (2 alpha levels) to 100 (exact
    /// alpha). Defaults to 100. Ignored by lossless output and by images without transparency.
    /// </summary>
    /// <remarks>
    /// Below 100, the alpha values are reduced to fewer levels (the levels of smallest squared error) before they are
    /// compressed losslessly: 2 + quality / 5 levels up to 70, then 8 more levels per point, as <c>cwebp -alpha_q</c>. The
    /// reduced alpha is kept only when it compresses smaller than the exact alpha (fewer levels can break smooth ramps). Fully
    /// transparent and fully opaque pixels stay exact.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 0 and 100.</exception>
    public int AlphaQuality
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 0);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 100);
            field = value;
        }
    } = 100;

    /// <summary>
    /// Gets the near-lossless level of <see cref="WebPCompression.Lossless"/> output, from 0 (largest color errors, smallest
    /// output) to 100 (exact). Defaults to 100. Ignored by lossy output.
    /// </summary>
    /// <remarks>
    /// Below 100, the color of the pixels that differ from a neighbor (edges and texture) is rounded before lossless
    /// compression, which makes it repeat; smooth areas keep their colors. Each color component stays within 1 (levels 80 to 99),
    /// 2 (60 to 79), 4 (40 to 59), 8 (20 to 39) or 16 (0 to 19) of the source. Alpha, and the pixels of the image border, stay
    /// exact.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 0 and 100.</exception>
    public int NearLossless
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 0);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 100);
            field = value;
        }
    } = 100;

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
