---
title: Strip or edit metadata
description: Remove the GPS position and the other private data of a photo before publishing it, keep its colors, and edit its metadata.
---

A photo carries more than its pixels: EXIF data (the GPS position, the date, the camera and its settings), XMP data, text
comments, a color profile and an orientation. Some of it is private and should not be published; some of it changes how
the photo looks and must not be lost. [Image.Metadata](cref:Meziantou.Framework.Imaging.Image.Metadata) holds all of it, and the encoder decides what is written.

## Remove the private data and keep the colors

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/GuideExamples.cs;region=Imaging_RemovePrivateMetadata;format=md-fenced-code-block;dedent=true -->
```csharp
public static void RemovePrivateMetadata(string inputPath, string outputPath)
{
    using var image = Image.Load(inputPath);

    // The orientation is stored in the EXIF data: apply it to the pixels before removing EXIF
    image.AutoOrient();

    // EXIF holds the GPS position, the date and the camera; XMP can hold the same and more
    image.Metadata.ExifProfile = null;
    image.Metadata.XmpProfile = null;
    image.Metadata.TextEntries.Clear();

    // The ICC profile is kept: it describes the colors of the pixels, and holds nothing about the photographer
    image.Save(outputPath);
}
```
<!-- endref -->

- **The orientation first.** The orientation of a photo is an EXIF tag. [AutoOrient](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.AutoOrient) turns the pixels and resets
  `Metadata.Orientation` to [TopLeft](cref:Meziantou.Framework.Imaging.Metadata.ExifOrientation.TopLeft), so the photo stays upright without its EXIF data.
- **EXIF and XMP are removed whole.** [ExifProfile](cref:Meziantou.Framework.Imaging.Metadata.ExifProfile) and [XmpProfile](cref:Meziantou.Framework.Imaging.Metadata.XmpProfile) hold the raw bytes of the blocks: the library does
  not edit their tags one by one. Setting them to `null` removes the GPS position with the rest.
- **Text entries** are the comments and the text chunks of the file, such as the `Comment` of a JPEG or GIF file and
  the text chunks of a PNG file.
- **The ICC profile is kept.** It describes the colors of the pixels and nothing else: without it, a photo of a phone
  labeled Display P3 looks dull.

The format of the output follows the extension of its path, with the default [MetadataHandling.Strict](cref:Meziantou.Framework.Imaging.Formats.MetadataHandling.Strict): what is left is
written as it is, or [Save](cref:Meziantou.Framework.Imaging.Image.Save) throws when the format cannot store it.

## Remove all the metadata

[MetadataHandling.Strip](cref:Meziantou.Framework.Imaging.Formats.MetadataHandling.Strip) writes no optional metadata at all: no EXIF, XMP, text, resolution, ICC profile or orientation.
Since the orientation and the profile change how the pixels look, apply them to the pixels first:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/GuideExamples.cs;region=Imaging_StripAllMetadata;format=md-fenced-code-block;dedent=true -->
```csharp
public static void StripAllMetadata(string inputPath, string outputPath)
{
    using var image = Image.Load(inputPath);

    // Strip drops the orientation and the color profile too: apply them to the pixels first, so that the image
    // looks the same without them
    image.AutoOrient();
    image.ConvertColorProfile(PixelFormats.IsGrayscale(image.PixelFormat) ? IccProfile.SrgbGray : IccProfile.Srgb);

    image.Save(outputPath, new PngEncoder { MetadataHandling = MetadataHandling.Strip });
}
```
<!-- endref -->

The three policies of [ImageEncoder.MetadataHandling](cref:Meziantou.Framework.Imaging.Formats.ImageEncoder.MetadataHandling):

| Policy | Writes | When the format cannot store some metadata |
| --- | --- | --- |
| [Strict](cref:Meziantou.Framework.Imaging.Formats.MetadataHandling.Strict) (default) | Everything the image has | Throws an [UnsupportedImageFeatureException](cref:Meziantou.Framework.Imaging.UnsupportedImageFeatureException) before writing anything |
| [DiscardUnsupported](cref:Meziantou.Framework.Imaging.Formats.MetadataHandling.DiscardUnsupported) | What the format can store | Drops the rest |
| [Strip](cref:Meziantou.Framework.Imaging.Formats.MetadataHandling.Strip) | Nothing optional | Nothing to drop |

The frame durations and the loop count of an animation are not metadata: no policy removes them. A cursor keeps its
hotspot in the formats that are made for it (CUR and ANI).

## Check what a file contains

[Image.Identify](cref:Meziantou.Framework.Imaging.Image.Identify) returns the metadata of a file without decoding its pixels: `info.Metadata.ExifProfile`,
[XmpProfile](cref:Meziantou.Framework.Imaging.Metadata.XmpProfile), [IccProfile](cref:Meziantou.Framework.Imaging.Metadata.IccProfile), [TextEntries](cref:Meziantou.Framework.Imaging.Metadata.ImageMetadata.TextEntries) and [Orientation](cref:Meziantou.Framework.Imaging.Metadata.ImageMetadata.Orientation). Use [ImageIdentifyMode.FullScan](cref:Meziantou.Framework.Imaging.ImageIdentifyMode.FullScan) to also read the metadata
stored after the pixels, which some formats allow. See [Identifying images](../05-LoadingAndSaving/03-IdentifyingImages.md).

## Edit the metadata

The properties of [Image.Metadata](cref:Meziantou.Framework.Imaging.Image.Metadata) can be set before saving:

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

| Property | Contains |
| --- | --- |
| [Orientation](cref:Meziantou.Framework.Imaging.Metadata.ImageMetadata.Orientation) | How viewers must turn the pixels ([ExifOrientation](cref:Meziantou.Framework.Imaging.Metadata.ExifOrientation)); [TopLeft](cref:Meziantou.Framework.Imaging.Metadata.ExifOrientation.TopLeft) means as stored |
| [Resolution](cref:Meziantou.Framework.Imaging.Metadata.ImageMetadata.Resolution) | The pixel density, in dots per inch ([ImageResolution](cref:Meziantou.Framework.Imaging.Metadata.ImageResolution)), used for printing |
| [TextEntries](cref:Meziantou.Framework.Imaging.Metadata.ImageMetadata.TextEntries) | Key and value pairs ([ImageTextEntry](cref:Meziantou.Framework.Imaging.Metadata.ImageTextEntry)): a title, a comment, a copyright |
| [IccProfile](cref:Meziantou.Framework.Imaging.Metadata.IccProfile) | The color profile that labels the pixels |
| [ExifProfile](cref:Meziantou.Framework.Imaging.Metadata.ExifProfile), [XmpProfile](cref:Meziantou.Framework.Imaging.Metadata.XmpProfile) | The raw EXIF and XMP blocks |

- **Orientation.** Setting [Orientation](cref:Meziantou.Framework.Imaging.Metadata.ImageMetadata.Orientation) changes how viewers display the image without moving a pixel; the EXIF
  orientation tag is rewritten from it when the file is saved. [AutoOrient](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.AutoOrient) does the opposite: it moves the pixels and
  resets the orientation.
- **The library keeps the EXIF data consistent.** On save, the dimension tags of the EXIF block are updated to the size
  of the image, and an EXIF thumbnail that a crop, a resize or a color conversion made stale is removed, so it can no
  longer show the uncropped photo.
- **Each format stores different metadata.** JPEG, PNG, WebP, AVIF and HEIC store EXIF, XMP and ICC profiles; GIF stores
  comments only; QOI, TGA and Netpbm store none. The full table is in
  [Metadata and orientation](../09-Color/02-MetadataAndOrientation.md).
- **Saving re-encodes the pixels.** The library writes a file from the image, so editing the metadata of a JPEG file and
  saving it as JPEG compresses its pixels again.
- **Colors are never converted by a label change.** Setting [IccProfile](cref:Meziantou.Framework.Imaging.Metadata.IccProfile) relabels the pixels without changing them; to
  convert the colors to another profile, use [ConvertColorProfile](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.ConvertColorProfile) (see
  [Color management](../09-Color/03-ColorManagement.md)).
