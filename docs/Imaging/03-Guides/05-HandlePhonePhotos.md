---
title: Handle photos from phones and cameras
description: Load HEIC, AVIF and JPEG photos, turn them upright, convert their colors to sRGB, and publish them as JPEG or WebP.
---

Photos from phones and cameras come as HEIC (the default of Apple devices), AVIF or JPEG files. Three properties of
these files surprise people who publish them as they are: the pixels are often stored sideways, the colors are labeled
with a color space wider than sRGB, and the metadata holds the GPS position of the photo. This guide loads such a photo
and turns it into a file that every browser shows correctly.

## Load a HEIC photo

HEIC files load like any other image, without a native codec or an operating system component:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_LoadHeic;format=md-fenced-code-block;dedent=true -->
```csharp
public static string ConvertHeic(string heicPath, string jpegPath)
{
    using var image = Image.Load(heicPath);
    var description = $"{image.Width}x{image.Height} {image.PixelFormat}, orientation: {image.Metadata.Orientation}";

    // A camera stores the image as its sensor reads it, and the rotation to apply as a property of the file: the
    // decoder reports it as the orientation of the image and moves no pixel. AutoOrient applies it, so that the
    // JPEG file needs no orientation.
    image.AutoOrient();

    // The ICC profile of the photo (Display P3 for a recent phone) and its EXIF block are stored in the JPEG file
    image.Save(jpegPath, new JpegEncoder { Quality = 90 });
    return description;
}
```
<!-- endref -->

- The photo of a phone is a grid of tiles in the file; it loads as one image.
- 8-bit photos load as [Rgb24](cref:Meziantou.Framework.Imaging.Rgb24), 10-bit and 12-bit ones as [Rgb48](cref:Meziantou.Framework.Imaging.Rgb48), so that no precision is lost.
- The rotation of the camera is reported as `Metadata.Orientation`, never applied by the decoder. [AutoOrient](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.AutoOrient) applies
  it.
- Only the main image is read: the thumbnails, the depth maps, the gain maps and the other photos of a burst stored in
  the same file are ignored.

## Prepare a photo for the web

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/GuideExamples.cs;region=Imaging_PreparePhotoForWeb;format=md-fenced-code-block;dedent=true -->
```csharp
public static void PreparePhotoForWeb(string inputPath, string outputPath)
{
    // HEIC, AVIF or JPEG: the format comes from the content of the file
    using var photo = Image.Load(inputPath);

    // The decoder reports the orientation of the camera and moves no pixel: turn the pixels upright
    photo.AutoOrient();

    // At most 2048 pixels on the long side; a smaller photo keeps its size
    photo.Resize(new ResizeOptions(2048, 2048) { AllowUpscaling = false });

    // Phones label their photos with a wider color space (Display P3), as an ICC profile or a color description.
    // Converting to sRGB gives the colors every browser shows. The tone mapping only applies to high dynamic range
    // samples (PQ or HLG), which it compresses into the range of sRGB; it is ignored for the other photos
    var options = new IccColorTransformOptions { ToneMapping = HdrToneMappingOptions.Default };
    photo.ConvertColorProfile(PixelFormats.IsGrayscale(photo.PixelFormat) ? IccProfile.SrgbGray : IccProfile.Srgb, options);

    // The pixels are upright and sRGB: the metadata (EXIF with the GPS position, XMP, the profile) can go
    photo.Save(outputPath, new JpegEncoder
    {
        Quality = 85,
        AllowBitDepthReduction = true, // 10-bit and 12-bit photos load with 16-bit samples
        MetadataHandling = MetadataHandling.Strip,
    });
}
```
<!-- endref -->

The same code handles HEIC, AVIF, JPEG and the other formats, since the format is detected from the content of the file.

### Turn the photo upright

A camera writes the pixels as its sensor reads them, and stores the rotation to apply as metadata: the EXIF orientation
of a JPEG file, or the rotation and mirror properties of a HEIC or AVIF file. Viewers that read it show the photo
upright; the others show it sideways. [AutoOrient](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.AutoOrient) turns the pixels for the eight possible orientations and resets
`Metadata.Orientation` to [TopLeft](cref:Meziantou.Framework.Imaging.Metadata.ExifOrientation.TopLeft), so the result looks the same everywhere, with or without metadata.

Apply it before any operation that depends on the layout of the image: resizing to a box, cropping, drawing a
watermark in a corner. See [Metadata and orientation](../09-Color/02-MetadataAndOrientation.md).

### Convert the colors to sRGB

Recent phones capture colors that sRGB cannot show, and label their photos with an ICC profile such as Display P3, or,
in AVIF and HEIC files, with a color description (`Metadata.Cicp`). The library keeps these labels and never converts
the colors on its own. [ConvertColorProfile](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.ConvertColorProfile) converts the pixels and replaces the label:

- Browsers show an image without profile as sRGB. Converting to sRGB lets you remove the profile and still get the right
  colors.
- The tone mapping only matters for high dynamic range photos (PQ or HLG samples, from some AVIF files): converting them
  requires [IccColorTransformOptions.ToneMapping](cref:Meziantou.Framework.Imaging.IccColorTransformOptions.ToneMapping), which says how the highlights are compressed. For the other photos,
  it is ignored.
- A grayscale image needs a grayscale profile: [IccProfile.SrgbGray](cref:Meziantou.Framework.Imaging.Metadata.IccProfile.SrgbGray).

The example only converts the colors and removes the profile:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_ConvertToSrgb;format=md-fenced-code-block;dedent=true -->
```csharp
public static void ConvertToSrgb(string inputPath, string outputPath)
{
    using var image = Image.Load(inputPath);

    // Decoders keep the embedded profile as a label; converting the colors is always explicit.
    // An image without profile is already sRGB (sGray for grayscale pixels): nothing is converted then.
    var grayscale = image.PixelFormat is PixelFormat.Gray8 or PixelFormat.Gray16;
    image.ConvertColorProfile(grayscale ? IccProfile.SrgbGray : IccProfile.Srgb);

    // Untagged pixels are read as sRGB: the label can be removed for formats that cannot store a profile
    image.Metadata.IccProfile = null;
    image.Save(outputPath);
}
```
<!-- endref -->

To keep the wide colors for the screens that can show them, skip the conversion and keep the profile: JPEG, PNG, WebP,
AVIF and HEIC files store it. See [Color management](../09-Color/03-ColorManagement.md).

### Remove the metadata

The EXIF data of a photo holds the GPS position, the date and the camera. Once the photo is upright and sRGB, nothing in
the metadata is needed to display it: [MetadataHandling.Strip](cref:Meziantou.Framework.Imaging.Formats.MetadataHandling.Strip) writes none. To keep some of it, such as the copyright,
see [Strip or edit metadata](08-StripOrEditMetadata.md).

### Choose the output

| Output | Settings |
| --- | --- |
| JPEG | `Quality` (85 is a common choice for photos); `AllowBitDepthReduction`, since 10-bit and 12-bit photos load with 16-bit samples |
| WebP | `Compression = WebPCompression.Lossy`, `Quality`; `AllowBitDepthReduction` |
| HEIC or AVIF | `Compression` set to lossy, `Quality`; `AllowBitDepthReduction` writes 16-bit images with 12 bits, `BitDepth` chooses 8, 10 or 12 |

Saving a HEIC photo as JPEG keeps its ICC profile and its EXIF block by default ([MetadataHandling.Strict](cref:Meziantou.Framework.Imaging.Formats.MetadataHandling.Strict)); converting
it to a format that cannot store them throws until you choose [DiscardUnsupported](cref:Meziantou.Framework.Imaging.Formats.MetadataHandling.DiscardUnsupported) or [Strip](cref:Meziantou.Framework.Imaging.Formats.MetadataHandling.Strip). See
[HEIC](../10-Formats/14-Heic.md), [AVIF](../10-Formats/13-Avif.md) and [JPEG](../10-Formats/04-Jpeg.md).

## Large photos

The photo of a recent phone has tens of millions of pixels, and an [Rgb48](cref:Meziantou.Framework.Imaging.Rgb48) image takes 6 bytes per pixel. The default
[resource limits](../05-LoadingAndSaving/07-ResourceLimits.md) allow 100 million pixels per frame and 512 MiB of live
memory for an image and the operation working on it; raise them in an [ImageConfiguration](cref:Meziantou.Framework.Imaging.ImageConfiguration) if your photos need more.
With [ImageConfiguration.MaxDegreeOfParallelism](cref:Meziantou.Framework.Imaging.ImageConfiguration.MaxDegreeOfParallelism) above 1, the tiles of a large HEIC photo are decoded by several
threads, and resizes use several threads too: see [Performance](../11-Performance.md).
