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
