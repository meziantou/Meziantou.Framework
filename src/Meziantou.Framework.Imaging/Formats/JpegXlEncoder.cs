namespace Meziantou.Framework.Imaging.Formats;

/// <summary>
/// Immutable JPEG XL encoder settings. The output is lossless by default (Modular coding): every pixel format is stored
/// as it is, 16-bit samples and the color of fully transparent pixels included. <see cref="Compression"/> selects the
/// lossy coding (VarDCT), controlled by <see cref="Quality"/>; its alpha channel stays lossless. An image is written as a
/// bare codestream, or in the JPEG XL container when it has EXIF or XMP metadata or when it needs level 10 of the format
/// (16-bit samples, more than 262,144 pixels per side or 268,435,456 pixels, an ICC profile above 4 MiB).
/// </summary>
/// <remarks>
/// <para>
/// Animations are written as full frames that replace the image; durations are stored in milliseconds
/// (<see cref="DurationRounding"/>). A frame of an animation must have a duration: JPEG XL defines a frame without duration
/// as a layer of the next frame, not as an image that is shown.
/// </para>
/// <para>
/// The ICC profile is stored in the codestream, byte for byte; without a profile, the linear transfer function and a CICP
/// color description that the format can enumerate are stored as its color encoding. The orientation is stored in the
/// image header. Lossy output stores the samples in the XYB color space, which a decoder converts back to the color
/// encoding of the image: it requires an enumerated encoding or an sRGB profile, and throws
/// <see cref="UnsupportedImageFeatureException"/> for another profile and for the PQ and HLG transfer functions.
/// </para>
/// <para>
/// A JPEG file is recompressed without loss by <see cref="JpegXlTranscoder"/>, not by this encoder, which stores pixels.
/// </para>
/// </remarks>
public sealed class JpegXlEncoder : ImageEncoder
{
    /// <inheritdoc />
    public override ImageFormat Format => ImageFormat.JpegXl;

    /// <summary>Gets how every frame is coded. Defaults to <see cref="JpegXlCompression.Lossless"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="JpegXlCompression"/>.</exception>
    public JpegXlCompression Compression
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
    /// Gets the quality of <see cref="JpegXlCompression.Lossy"/> output, from 0 (smallest) to 100 (best). Defaults to 75.
    /// Ignored by lossless output.
    /// </summary>
    /// <remarks>
    /// The quality selects the quantization steps: 90 is a quality at which the difference with the original is barely
    /// visible at a normal viewing distance, and 100 is the finest quantization the encoder uses, still lossy. Up to 90,
    /// the decoder also smooths the image with the filter of the format that removes the edges between blocks, which the
    /// encoder prepares by sharpening it.
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
    /// Gets a value indicating whether the frames are written so that a reader of a part of the file can show the image at
    /// a lower resolution, then refine it. Defaults to <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// Lossy output is written in three passes after the image at one eighth of its resolution; lossless output is squeezed,
    /// each resolution in the sections of its pass. The decoded pixels are those of a file that is not progressive; the file
    /// is larger (a few percent for lossy output, more for lossless output). <see cref="ImageReader{TPixel}.ReadFrameRefinementInto"/>
    /// reads such a file stage by stage.
    /// </remarks>
    public bool Progressive { get; init; }

    /// <summary>
    /// Gets the compression effort, from 0 (fastest, largest output) to 9 (slowest, smallest output). Defaults to 5. The effort
    /// never changes the decoded pixels of lossless output; for lossy output it changes the decisions of the encoder, not
    /// the quantization that <see cref="Quality"/> sets.
    /// </summary>
    /// <remarks>
    /// Lossless output: efforts 0 to 2 use a fixed color transform and a fixed predictor; from 3 on, the color transform and
    /// the predictor are searched; from 7 on, the weighted predictor is also tried. At every effort, each group of samples
    /// is written with prefix codes or with ANS, whichever is smaller. Lossy output: efforts 0 to 2 only use the 8x8
    /// transform; from 3 on, each area has the 8x8 or the 16x16 transform, whichever costs less, each tile has its own
    /// factors of the chroma from luma prediction and the predictor of the alpha channel is searched; from 6 on, the
    /// contexts of the coefficients have more distributions; from 7 on, the 16x8 and 8x16 transforms are tried, and from
    /// 8 on the transforms of up to 64x64 samples.
    /// </remarks>
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
    /// Gets how frame durations are converted to JPEG XL ticks (milliseconds, 32-bit values). Defaults to
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
