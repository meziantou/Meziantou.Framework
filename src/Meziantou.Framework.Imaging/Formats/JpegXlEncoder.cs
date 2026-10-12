namespace Meziantou.Framework.Imaging.Formats;

/// <summary>
/// Immutable JPEG XL encoder settings. The output is lossless (Modular coding): every pixel format is stored as it is,
/// 16-bit samples and the color of fully transparent pixels included. An image is written as a bare codestream, or in the
/// JPEG XL container when it has EXIF or XMP metadata or when it needs level 10 of the format (16-bit samples, more than
/// 262,144 pixels per side or 268,435,456 pixels, an ICC profile above 4 MiB).
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
/// image header.
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
    /// visible at a normal viewing distance, and 100 is the finest quantization the encoder uses, still lossy. It never
    /// changes the encoding time.
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
    /// The decoded pixels are those of a file that is not progressive; the file is slightly larger.
    /// </remarks>
    public bool Progressive { get; init; }

    /// <summary>
    /// Gets the compression effort, from 0 (fastest, largest output) to 9 (slowest, smallest output). Defaults to 5. The effort
    /// never changes the decoded pixels.
    /// </summary>
    /// <remarks>
    /// Efforts 0 to 2 use prefix codes and fixed choices; from 3 on, the samples are coded with ANS and the color transform
    /// and the predictor are searched; from 7 on, the weighted predictor is also tried.
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
