namespace Meziantou.Framework.Imaging.Formats;

/// <summary>Immutable settings for Windows icon (<c>.ico</c>) and cursor (<c>.cur</c>) encoding.</summary>
/// <remarks>
/// <para>
/// An icon file is a directory of alternative representations of one drawing, not an animation: use
/// <see cref="ImageCollection.Save(string, ImageEncoder?)"/> on a collection of
/// <see cref="ImageCollectionKind.Representations"/> to write several sizes, or
/// <see cref="Image.Save(string, ImageEncoder?)"/> to write a single one. Animated images are rejected.
/// </para>
/// <para>
/// Output is conservative: every representation is written either as a still PNG or as a 32-bit
/// <c>BITMAPINFOHEADER</c> DIB with straight alpha in the fourth byte and an all-zero AND mask (which is what the alpha
/// channel makes redundant), selected by <see cref="PayloadFormat"/>. Indexed payloads, 1/4/8/16/24-bit DIBs and
/// <c>BI_BITFIELDS</c> masks are decoded but never written.
/// </para>
/// <para>
/// The container stores at most 256 pixels per side; a larger image is rejected with an
/// <see cref="UnsupportedImageFeatureException"/>. The destination does not need to be seekable: a representation is at
/// most 256x256, so its payload is encoded in memory and the directory is written before it.
/// </para>
/// <para>
/// <see cref="IconKind.Cursor"/> writes the hotspot of every entry
/// (<see cref="ImageCollectionEntry.Hotspot"/>, defaulting to the top-left corner when the entry has none);
/// <see cref="IconKind.Icon"/> writes the conventional 1 color plane and the bit count instead, and an entry hotspot is
/// metadata the format cannot store (rejected by default, see <see cref="ImageEncoder.MetadataHandling"/>).
/// </para>
/// <para>
/// Metadata (subject to <see cref="ImageEncoder.MetadataHandling"/>): an icon directory stores none. A resolution, an
/// ICC profile, EXIF, XMP, text entries and an orientation other than
/// <see cref="Metadata.ExifOrientation.TopLeft"/> cannot be stored (rejected by default). A PNG payload is written
/// without ancillary chunks.
/// </para>
/// </remarks>
public sealed class IcoEncoder : ImageEncoder
{
    /// <summary>The largest representation an icon or cursor can store, per side (256 pixels).</summary>
    public const int MaxDimension = 256;

    /// <summary>The largest side written as a DIB by <see cref="IconPayloadFormat.Auto"/> (64 pixels).</summary>
    public const int AutoDibMaxDimension = 64;

    /// <inheritdoc />
    public override ImageFormat Format => Kind == IconKind.Cursor ? ImageFormat.Cur : ImageFormat.Ico;

    /// <summary>Gets the container to write. Defaults to <see cref="IconKind.Icon"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="IconKind"/>.</exception>
    public IconKind Kind
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The icon kind is not valid.");

            field = value;
        }
    } = IconKind.Icon;

    /// <summary>Gets how the pixels of a representation are stored. Defaults to <see cref="IconPayloadFormat.Auto"/>.</summary>
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
}
