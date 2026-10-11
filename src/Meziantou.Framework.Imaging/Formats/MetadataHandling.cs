namespace Meziantou.Framework.Imaging.Formats;

/// <summary>Controls what an encoder does with image metadata it cannot represent.</summary>
/// <remarks>
/// Animation settings and frame durations are structural data, not metadata: they are never affected by this policy.
/// A cursor hotspot (<see cref="Metadata.FrameMetadata.Hotspot"/>) is metadata for every output but cursors and animated
/// cursors, which always store one.
/// Note that dropping <see cref="Metadata.ImageMetadata.Orientation"/> or <see cref="Metadata.ImageMetadata.IccProfile"/>
/// changes how viewers display the pixels; consider <c>AutoOrient</c> before stripping metadata.
/// Payloads the format can store are validated when written: a malformed EXIF, ICC or XMP payload throws an
/// <see cref="InvalidImageContentException"/> with <see cref="Strict"/> and <see cref="DiscardUnsupported"/>.
/// </remarks>
public enum MetadataHandling
{
    /// <summary>Writes all supported metadata and throws an <see cref="UnsupportedImageFeatureException"/> if any metadata cannot be represented. This is the default.</summary>
    Strict = 0,

    /// <summary>Writes all supported metadata and silently discards metadata the format cannot represent.</summary>
    DiscardUnsupported = 1,

    /// <summary>Writes no optional metadata (profiles, EXIF, XMP, text, resolution, orientation, a cursor hotspot the format has no field for).</summary>
    Strip = 2,
}
