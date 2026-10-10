using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Collects the metadata of one input for identification and decoding, shared by every codec:
/// payloads are preserved byte for byte, structurally validated before adoption (an invalid payload is skipped like
/// unsupported ancillary data), the first occurrence wins, and every retained or decompressed byte is charged to
/// <see cref="ImageResourceLimits.MaxMetadataBytes"/> before it is retained.
/// </summary>
internal sealed class DecodedMetadataBuilder
{
    private readonly InputResourceTracker _tracker;

    public DecodedMetadataBuilder(InputResourceTracker tracker, ImageFormat format)
    {
        ArgumentNullException.ThrowIfNull(tracker);
        _tracker = tracker;
        Metadata = new ImageMetadata { SourceFormat = format };
    }

    /// <summary>Gets the metadata collected so far (owned by the builder; snapshots are cloned by <see cref="ImageInfo"/> and decoders).</summary>
    public ImageMetadata Metadata { get; }

    public InputResourceTracker Tracker => _tracker;

    /// <summary>Charges metadata bytes about to be retained or produced.</summary>
    /// <exception cref="ImageResourceLimitException">The metadata limit is exceeded.</exception>
    public void Charge(long bytes) => _tracker.ChargeMetadataBytes(bytes);

    /// <summary>Adopts an EXIF payload (from the TIFF header) and its orientation, unless one was already adopted or it is malformed.</summary>
    /// <returns><see langword="true"/> if the payload was adopted.</returns>
    public bool TryAdoptExif(ReadOnlySpan<byte> data)
    {
        if (Metadata.ExifProfile is not null || !MetadataValidation.TryValidateExif(data, out _))
            return false;

        Charge(data.Length);
        Metadata.ExifProfile = new ExifProfile(new MetadataBlob(data));
        Metadata.Orientation = ExifTiff.ReadOrientation(data) ?? ExifOrientation.TopLeft;
        return true;
    }

    /// <summary>Adopts an XMP packet, unless one was already adopted or it is not valid UTF-8.</summary>
    /// <returns><see langword="true"/> if the packet was adopted.</returns>
    /// <param name="data">The packet.</param>
    /// <param name="alreadyCharged"><see langword="true"/> when the bytes were already charged (decompressed).</param>
    public bool TryAdoptXmp(ReadOnlySpan<byte> data, bool alreadyCharged = false)
    {
        if (Metadata.XmpProfile is not null || !MetadataValidation.TryValidateXmpPacket(data, out _))
            return false;

        if (!alreadyCharged)
        {
            Charge(data.Length);
        }

        Metadata.XmpProfile = new XmpProfile(new MetadataBlob(data));
        return true;
    }

    /// <summary>
    /// Adopts an uncompressed ICC profile whose bytes were already charged (decompressed or reassembled), unless one was
    /// already adopted, it is malformed, or its color space cannot label the encoded samples (a grayscale profile for color
    /// samples, or the reverse: such a file violates its format specification).
    /// </summary>
    /// <param name="data">The profile bytes; ownership is transferred.</param>
    /// <param name="grayscaleSamples">Whether the encoded samples are grayscale.</param>
    /// <returns><see langword="true"/> if the profile was adopted.</returns>
    public bool TryAdoptIccProfile(byte[] data, bool grayscaleSamples)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (Metadata.IccProfile is not null || !MetadataValidation.TryValidateIccProfile(data, out _))
            return false;

        var profile = new IccProfile(MetadataBlob.FromOwnedArray(data));
        var expected = grayscaleSamples ? IccProfileColorSpace.Gray : IccProfileColorSpace.Rgb;
        if (profile.ColorSpace != expected)
            return false;

        Metadata.IccProfile = profile;
        return true;
    }

    /// <summary>Sets the resolution, unless one was already set.</summary>
    public void TrySetResolution(ImageResolution? resolution)
    {
        if (Metadata.Resolution is null && resolution is not null)
        {
            Metadata.Resolution = resolution;
        }
    }

    /// <summary>Adds a text entry, in file order. The caller charges its bytes first.</summary>
    public void AddText(string keyword, string value, string? languageTag = null, string? translatedKeyword = null)
        => Metadata.TextEntries.Add(new ImageTextEntry(keyword, value, string.IsNullOrEmpty(languageTag) ? null : languageTag, string.IsNullOrEmpty(translatedKeyword) ? null : translatedKeyword));
}
