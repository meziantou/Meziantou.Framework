using System.Collections.ObjectModel;

namespace Meziantou.Framework.Imaging.Metadata;

/// <summary>Mutable image-wide metadata: orientation, resolution, color profile, transfer function, EXIF, XMP and text entries.</summary>
/// <remarks>
/// <para>
/// <see cref="Clone"/> produces an independent container: mutable state (such as <see cref="TextEntries"/>) is copied,
/// while immutable values (profiles, blobs, text entries) are shared.
/// </para>
/// <para>
/// When an image is saved, metadata the encoder cannot represent causes an <see cref="UnsupportedImageFeatureException"/>
/// unless the encoder's <see cref="Formats.ImageEncoder.MetadataHandling"/> allows discarding it.
/// </para>
/// </remarks>
public sealed class ImageMetadata
{
    /// <summary>Gets or sets the format the image was decoded from. This is informational only and never selects an output format.</summary>
    public ImageFormat SourceFormat { get; set; }

    /// <summary>Gets or sets the orientation of the stored pixels. Defaults to <see cref="ExifOrientation.TopLeft"/>.</summary>
    /// <remarks>This typed value is authoritative: it overrides the orientation tag of <see cref="ExifProfile"/> when serialized.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="ExifOrientation"/>.</exception>
    public ExifOrientation Orientation
    {
        get;
        set
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The orientation must be between 1 and 8.");

            field = value;
        }
    } = ExifOrientation.TopLeft;

    /// <summary>Gets or sets the physical resolution, or <see langword="null"/> if unspecified.</summary>
    public ImageResolution? Resolution { get; set; }

    /// <summary>Gets or sets the ICC color profile, or <see langword="null"/> if the pixels are untagged (assumed sRGB or sGray).</summary>
    public IccProfile? IccProfile { get; set; }

    /// <summary>
    /// Gets or sets the transfer function of the stored color samples. Defaults to <see cref="ColorTransferFunction.Srgb"/>
    /// (sRGB encoded, or described by <see cref="IccProfile"/>).
    /// </summary>
    /// <remarks>
    /// This is a label: no operation converts the pixels. Linear-light resizing (<see cref="ResizeWorkingSpace.LinearSrgb"/>)
    /// and convolution (<see cref="ConvolutionWorkingSpace.LinearSrgb"/>) filter
    /// <see cref="ColorTransferFunction.Linear"/> samples directly, as they already are linear light. Only QOI stores
    /// <see cref="ColorTransferFunction.Linear"/>; other encoders treat it as metadata they cannot store
    /// (<see cref="Formats.ImageEncoder.MetadataHandling"/>).
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="ColorTransferFunction"/>.</exception>
    public ColorTransferFunction TransferFunction
    {
        get;
        set
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The transfer function is not valid.");

            field = value;
        }
    }

    /// <summary>Gets or sets the EXIF profile, or <see langword="null"/> if none.</summary>
    public ExifProfile? ExifProfile { get; set; }

    /// <summary>Gets or sets the XMP packet, or <see langword="null"/> if none.</summary>
    public XmpProfile? XmpProfile { get; set; }

    /// <summary>Gets the textual entries, in file order. <see langword="null"/> entries are rejected.</summary>
    public IList<ImageTextEntry> TextEntries { get; } = new NonNullCollection<ImageTextEntry>();

    /// <summary>Creates an independent copy of this metadata.</summary>
    /// <returns>A new instance; mutable state is copied and immutable values are shared.</returns>
    public ImageMetadata Clone()
    {
        var result = new ImageMetadata
        {
            SourceFormat = SourceFormat,
            Orientation = Orientation,
            Resolution = Resolution,
            IccProfile = IccProfile,
            TransferFunction = TransferFunction,
            ExifProfile = ExifProfile,
            XmpProfile = XmpProfile,
        };

        foreach (var entry in TextEntries)
        {
            result.TextEntries.Add(entry);
        }

        return result;
    }

    /// <summary>
    /// Reconciles the metadata after a geometry change (crop, resize, rotation) that produced a canvas of
    /// <paramref name="newSize"/>: existing EXIF pixel-dimension tags are updated and the EXIF thumbnail, now stale, is
    /// removed (its bytes are zeroed, never re-emitted). The typed <see cref="Orientation"/> is kept; it is written to the
    /// EXIF data when the image is serialized. A malformed EXIF profile is left unchanged (serializing it fails).
    /// </summary>
    /// <param name="newSize">The new canvas size.</param>
    internal void ReconcileGeometry(Size newSize) => ExifProfile = GetReconciledExifProfile(newSize, normalizeOrientation: false);

    /// <summary>
    /// Computes, without modifying this instance, the EXIF profile matching a canvas of <paramref name="newSize"/> after a
    /// geometry change (see <see cref="ReconcileGeometry(Size)"/>). Transactional operations compute it before their commit
    /// and assign it afterwards, so a failure leaves the metadata unchanged.
    /// </summary>
    /// <param name="newSize">The new canvas size.</param>
    /// <param name="normalizeOrientation">
    /// <see langword="true"/> when the pixels were transformed to the upright orientation (auto-orient): an existing
    /// orientation tag (whatever its value) is rewritten to <see cref="ExifOrientation.TopLeft"/>. No tag is added when there is none.
    /// </param>
    /// <returns>The reconciled profile, the current profile when it is malformed, or <see langword="null"/> when there is none.</returns>
    internal ExifProfile? GetReconciledExifProfile(Size newSize, bool normalizeOrientation)
    {
        if (ExifProfile is not { } exif)
            return null;

        var data = exif.Data.Span;
        if (!Internals.ExifTiff.IsValid(data))
            return exif;

        ExifOrientation? orientation = normalizeOrientation && Internals.ExifTiff.HasOrientationTag(data) ? ExifOrientation.TopLeft : null;
        var updated = Internals.ExifTiff.Rewrite(data, orientation, newSize, removeThumbnail: true);
        return new ExifProfile(MetadataBlob.FromOwnedArray(updated));
    }

    /// <summary>
    /// Removes the EXIF thumbnail after an in-place pixel edit (flip, grayscale, convolve) made it stale. The dimension tags and the
    /// orientation are unchanged. Nothing is done when there is no thumbnail or the profile is malformed.
    /// </summary>
    internal void RemoveStaleThumbnail()
    {
        if (ExifProfile is { } exif && Internals.ExifTiff.HasThumbnail(exif.Data.Span))
        {
            var updated = Internals.ExifTiff.Rewrite(exif.Data.Span, orientation: null, pixelSize: null, removeThumbnail: true);
            ExifProfile = new ExifProfile(MetadataBlob.FromOwnedArray(updated));
        }
    }

    private sealed class NonNullCollection<T> : Collection<T>
        where T : class
    {
        protected override void InsertItem(int index, T item)
        {
            ArgumentNullException.ThrowIfNull(item);
            base.InsertItem(index, item);
        }

        protected override void SetItem(int index, T item)
        {
            ArgumentNullException.ThrowIfNull(item);
            base.SetItem(index, item);
        }
    }
}
