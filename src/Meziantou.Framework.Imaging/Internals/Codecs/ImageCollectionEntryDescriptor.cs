using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>What a container says about one entry of an <see cref="ImageCollection"/> before it is decoded.</summary>
/// <param name="Size">The size of the entry, read from the entry itself.</param>
/// <param name="PixelFormat">The lossless working representation of the entry.</param>
/// <param name="ColorModel">The color model of the encoded samples.</param>
/// <param name="BitsPerComponent">The precision of one encoded component.</param>
/// <param name="MayHaveTransparency">Whether the entry can have transparent pixels.</param>
/// <param name="PayloadFormat">The format of the stored payload (a PNG or a DIB inside an icon), or <see cref="ImageFormat.Unknown"/>.</param>
/// <param name="Hotspot">The hotspot of a cursor representation, or <see langword="null"/>.</param>
/// <param name="Metadata">The metadata of the entry.</param>
internal sealed record ImageCollectionEntryDescriptor(
    Size Size,
    PixelFormat PixelFormat,
    ImageColorModel ColorModel,
    int BitsPerComponent,
    bool MayHaveTransparency,
    ImageFormat PayloadFormat,
    Point? Hotspot,
    ImageMetadata Metadata);
