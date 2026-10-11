using System.IO.Compression;

namespace Meziantou.Framework.Imaging.Formats;

/// <summary>Immutable settings for lossless PNG and APNG encoding.</summary>
/// <remarks>
/// <para>
/// The PNG color type and bit depth follow the pixel format: grayscale for <see cref="Gray8"/>/<see cref="Gray16"/>, RGB for
/// <see cref="Rgb24"/>, RGBA for <see cref="Rgba32"/>/<see cref="Bgra32"/>, and 16-bit samples for <see cref="Rgba64"/> and
/// <see cref="Gray16"/>. Encoding never reduces precision or drops alpha. Palette output is not produced in this version.
/// </para>
/// <para>
/// Animated output is written as full-canvas frames with freshly computed control data; delta information from a decoded
/// source is never reused. PNG and APNG writers require <see cref="ImageWriterOptions.ExpectedFrameCount"/>.
/// </para>
/// </remarks>
public sealed class PngEncoder : ImageEncoder
{
    /// <inheritdoc />
    public override ImageFormat Format => ImageFormat.Png;

    /// <summary>Gets the animation mode. Defaults to <see cref="PngAnimationMode.Auto"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="PngAnimationMode"/>.</exception>
    public PngAnimationMode AnimationMode
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The animation mode is not valid.");

            field = value;
        }
    }

    /// <summary>Gets the zlib compression level. Defaults to <see cref="System.IO.Compression.CompressionLevel.Optimal"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="System.IO.Compression.CompressionLevel"/>.</exception>
    public CompressionLevel CompressionLevel
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The compression level is not valid.");

            field = value;
        }
    } = CompressionLevel.Optimal;

    /// <summary>Gets the row filter strategy. Defaults to <see cref="PngFilter.Adaptive"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="PngFilter"/>.</exception>
    public PngFilter Filter
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The filter is not valid.");

            field = value;
        }
    }

    /// <summary>Gets a value indicating whether Adam7 interlacing is used. Defaults to <see langword="false"/>.</summary>
    public bool Interlaced { get; init; }

    /// <summary>
    /// Gets how frame durations are converted to APNG <c>delay_num / delay_den</c> (16-bit unsigned values). Defaults to
    /// <see cref="FrameDurationRounding.RequireExact"/>.
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
    }
}
