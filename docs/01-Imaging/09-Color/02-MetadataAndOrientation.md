---
title: Metadata and orientation
description: Read and edit EXIF, XMP, ICC profiles and text entries, apply the EXIF orientation, and choose what saving does with metadata.
---

`image.Metadata` holds everything an image says about itself besides its pixels. Decoders fill it, encoders write it,
and you can read and change it in between.

| Member | What it is |
| --- | --- |
| `Orientation` | The EXIF orientation: which way is up |
| `IccProfile` | The ICC color profile that describes the samples |
| `ExifProfile`, `XmpProfile` | The EXIF and XMP payloads, kept byte for byte |
| `TextEntries` | Text entries, such as the `tEXt` chunks of PNG or the comments of GIF and JPEG |
| `Resolution` | The horizontal and vertical resolution, in dots per inch |
| `PixelAspectRatio` | The relative width and height of a pixel, for formats whose pixels are not square |
| `Cicp`, `TransferFunction`, `ContentLightLevel`, `MasteringDisplay` | Color descriptions of AVIF, HEIC, PNG, JPEG XL and QOI images: see [Color management](03-ColorManagement.md) |
| `SourceFormat` | The format the image was loaded from, for information only |

Each frame also has its own metadata (`frame.Metadata`): its duration in an animation, and the hotspot of a cursor.
Decoded metadata is bounded by `MaxMetadataBytes` (16 MiB by default), decompressed text included: see
[Resource limits](../05-LoadingAndSaving/07-ResourceLimits.md).

## Apply the EXIF orientation

A camera stores a photo as its sensor saw it, and records in its EXIF orientation how to turn it. Decoders report that
orientation and never move a pixel. Call `AutoOrient` to make the pixels upright:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/ColorExamples.cs;region=Imaging_ApplyExifOrientation;format=md-fenced-code-block;dedent=true -->
```csharp
public static ExifOrientation ApplyOrientation(string inputPath, string outputPath)
{
    using var image = Image.Load(inputPath);

    // The orientation the camera recorded: the decoder reports it and leaves the pixels as they are stored
    var orientation = image.Metadata.Orientation;

    // Turns or mirrors every frame so that the pixels are upright, and sets the orientation to TopLeft: a viewer
    // that ignores the orientation then shows the same picture as one that applies it
    image.AutoOrient();

    // The orientation tag of the EXIF block is rewritten to match; the other metadata are kept
    image.Save(outputPath);
    return orientation;
}
```
<!-- endref -->

- `AutoOrient` handles the eight orientations (turns and mirrors) of every frame, and sets `Orientation` to `TopLeft`.
  It is all or nothing, like the other geometry changes.
- Apply the orientation before you crop, resize or draw: the coordinates you give are those of the stored pixels.
- On save, `Orientation` is authoritative: the orientation and dimension tags of the EXIF block are rewritten, and a
  thumbnail that a geometry change made stale is removed.
- A format without EXIF (GIF, QOI, BMP, TGA, Netpbm) cannot store another orientation than `TopLeft`. Apply the
  orientation first: otherwise saving throws, or with `MetadataHandling.DiscardUnsupported` the orientation is dropped
  and the picture is shown as it is stored.

See [Handle phone photos](../03-Guides/05-HandlePhonePhotos.md) for a complete solution.

## Edit metadata

Change the members of `image.Metadata`, then save:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_EditMetadata;format=md-fenced-code-block;dedent=true -->
```csharp
public static void EditMetadata(string path)
{
    using var image = Image.Load(path);
    image.Metadata.TextEntries.Add(new ImageTextEntry("Title", "Sunset"));
    image.Metadata.Resolution = new ImageResolution(300, 300);
    image.Save(path, new PngEncoder { MetadataHandling = MetadataHandling.Strict });
}
```
<!-- endref -->

- Set a member to `null` to remove it: `image.Metadata.ExifProfile = null` removes the EXIF block.
- A malformed EXIF, ICC or XMP payload you set is rejected when the image is saved.
- Unknown chunks, segments and boxes of a file are skipped when it is read: they are not written back.

See [Strip or edit metadata](../03-Guides/08-StripOrEditMetadata.md) for a complete solution.

## Choose what saving does with metadata

Each format stores some metadata and not others. `ImageEncoder.MetadataHandling` says what happens to the metadata
the output format cannot store:

| `MetadataHandling` | Behavior |
| --- | --- |
| `Strict` (the default) | Throws an `UnsupportedImageFeatureException`: nothing is lost silently |
| `DiscardUnsupported` | Writes what the format can store and drops the rest |
| `Strip` | Writes no optional metadata |

Animation timing is never affected by this setting. What each format stores:

| Format | ICC profile | EXIF and orientation | XMP | Text entries |
| --- | --- | --- | --- | --- |
| PNG | Yes | Yes | Yes | Yes |
| JPEG | Yes | Yes (up to 65,527 bytes) | Standard XMP (up to 65,504 bytes) | Comments |
| WebP | RGB profiles | Yes | Yes | No |
| GIF | No | No | No | Comments |
| TIFF | Yes | Orientation only | Yes | No |
| AVIF, HEIC | Yes | Yes | Yes | No |
| JPEG XL | Yes | Yes | Yes | No |
| JPEG 2000 (JP2) | Gray and matrix RGB profiles | Yes | Yes | No |
| QOI, BMP, TGA, Netpbm | No | No | No | No |

ICO files store no metadata, and HEIC does not store the orientation of an image sequence yet. The pages of
[the formats](../10-Formats/01-Overview.md) give the details, such as the resolution, the color descriptions and the
limits of each container.

## Cursor hotspots

The hotspot of a cursor is the metadata of each frame (`frame.Metadata.Hotspot`). CUR and ANI files always store one per
image, the top-left corner when a frame has none. Every other format cannot store it: the hotspot follows
`MetadataHandling` like other metadata, so a cursor is never saved as a plain picture without an explicit choice. See
[ICO and CUR](../10-Formats/11-IcoAndCur.md) and [ANI](../10-Formats/12-Ani.md).
