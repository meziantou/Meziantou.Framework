namespace Meziantou.Framework.Imaging.Formats;

/// <summary>Immutable settings for Windows animated cursor (<c>.ani</c>) encoding.</summary>
/// <remarks>
/// <para>
/// An animated cursor is an animation whose frames are cursor images: every frame of the image is written as one step,
/// with its duration and its hotspot (<see cref="Metadata.FrameMetadata.Hotspot"/>, the top-left corner when the frame
/// has none). A still image is written as an animation of one step.
/// </para>
/// <para>
/// A step lasts a whole number of jiffies (sixtieths of a second): <see cref="DurationRounding"/> decides what happens to
/// a duration that is not one. The format has no play count, an animated cursor always loops: an image whose
/// <see cref="Metadata.AnimationMetadata.TotalPlays"/> is not <see langword="null"/> is rejected with an
/// <see cref="UnsupportedImageFeatureException"/>, never written as an endless animation silently.
/// </para>
/// <para>
/// Frames whose encoded cursor image and hotspot are identical are stored once and shown again through the sequence
/// table of the file, so an animation that repeats a drawing does not repeat its bytes. Each frame holds one
/// representation, written like a cursor file: a still PNG or a 32-bit DIB, selected by <see cref="PayloadFormat"/>.
/// </para>
/// <para>
/// The container stores at most 256 pixels per side; a larger image is rejected with an
/// <see cref="UnsupportedImageFeatureException"/>. <strong>The destination must be seekable</strong>: frames are written
/// as they arrive, with a memory use independent of their number, and the counts and sizes stored at the start of the
/// file are written once the last frame is known.
/// </para>
/// <para>
/// Metadata (subject to <see cref="ImageEncoder.MetadataHandling"/>): one <c>Title</c> and one <c>Author</c> text entry
/// (<see cref="Metadata.ImageMetadata.TextEntries"/>) without a language or a translated keyword, whose value is Latin-1
/// text without a NUL character, are stored. Other text entries, a resolution, an ICC profile, EXIF, XMP and an
/// orientation other than <see cref="Metadata.ExifOrientation.TopLeft"/> cannot be stored (rejected by default).
/// </para>
/// </remarks>
public sealed class AniEncoder : ImageEncoder
{
    /// <summary>The largest frame an animated cursor can store, per side (256 pixels).</summary>
    public const int MaxDimension = 256;

    /// <inheritdoc />
    public override ImageFormat Format => ImageFormat.Ani;

    /// <summary>Gets how the pixels of a frame are stored. Defaults to <see cref="IconPayloadFormat.Auto"/>.</summary>
    /// <remarks>
    /// <see cref="IconPayloadFormat.Auto"/> writes a PNG for frames larger than 64 pixels per side and for pixel formats
    /// with 16-bit samples, which a DIB cannot store, and a 32-bit DIB otherwise. <see cref="IconPayloadFormat.Dib"/> with
    /// 16-bit samples is rejected with an <see cref="UnsupportedImageFeatureException"/>: nothing is narrowed silently.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="IconPayloadFormat"/>.</exception>
    public IconPayloadFormat PayloadFormat
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The payload format is not valid.");

            field = value;
        }
    }

    /// <summary>Gets how frame durations are converted to jiffies (sixtieths of a second). Defaults to <see cref="FrameDurationRounding.RoundToNearest"/>.</summary>
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
