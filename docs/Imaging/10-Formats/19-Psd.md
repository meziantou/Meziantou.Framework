---
title: PSD
description: Read the composite image of Adobe Photoshop documents (PSD and PSB files), and save images as flat documents that Photoshop opens.
---

Adobe Photoshop documents (`.psd`) and large documents (PSB, `.psb`) are read and written. The library reads their
composite image: the flattened image Photoshop stores next to the layers when "Maximize Compatibility" is on. The
layers, their masks, adjustment layers, effects and blend modes are never rendered. It writes flat documents whose
composite is the image, with [PsdEncoder](cref:Meziantou.Framework.Imaging.Formats.PsdEncoder). Files are detected by
their signature `8BPS`, and load like the other formats with [Image.Load](cref:Meziantou.Framework.Imaging.Image.Load) or a
reader.

## What is read and written

| | Reading | Writing |
| --- | --- | --- |
| Files | PSD files (version 1, at most 30,000 pixels per side) and PSB files (version 2, at most 300,000 pixels per side); one image | PSD files, or PSB files above 30,000 pixels per side or on request |
| Color modes | Bitmap, grayscale, duotone (read as its gray samples), indexed, RGB, CMYK and Lab | Grayscale, RGB and CMYK |
| Samples | 1 bit (bitmap), 8 and 16 bits, and 32-bit floating-point grayscale and RGB | 8 and 16 bits, and 32-bit floating-point RGB |
| Image data | Raw and RLE (PackBits) | RLE (PackBits, default) or raw |
| Transparency | The merged alpha channel of the composite, and the transparent index of an indexed image | The merged alpha channel, with one layer that holds the image |
| Metadata | Resolution, ICC profile, EXIF (the orientation is reported) and XMP | Resolution, ICC profile, EXIF (with the orientation) and XMP |

An image loads in the smallest pixel format that holds its samples: [Gray8](cref:Meziantou.Framework.Imaging.Gray8) for a
bitmap (a sample of 1 is black), [Gray16](cref:Meziantou.Framework.Imaging.Gray16), [La16](cref:Meziantou.Framework.Imaging.La16), [La32](cref:Meziantou.Framework.Imaging.La32), [Rgb24](cref:Meziantou.Framework.Imaging.Rgb24) for an indexed
image ([Rgba32](cref:Meziantou.Framework.Imaging.Rgba32) when an index is transparent), [Rgb48](cref:Meziantou.Framework.Imaging.Rgb48), [Rgba32](cref:Meziantou.Framework.Imaging.Rgba32) or [Rgba64](cref:Meziantou.Framework.Imaging.Rgba64). A 32-bit document loads as
[RgbaFloat](cref:Meziantou.Framework.Imaging.RgbaFloat), or [GrayFloat](cref:Meziantou.Framework.Imaging.GrayFloat) ([LaFloat](cref:Meziantou.Framework.Imaging.LaFloat) with transparency) for a grayscale one, labeled linear light unless an ICC profile describes it. A NaN sample is read quiet, with its sign
and payload, so that every pixel format gives the same bits.

## CMYK and Lab documents

A CMYK document of 8 or 16 bits keeps its inks when it is loaded as [Cmyk32](cref:Meziantou.Framework.Imaging.Cmyk32) or [Cmyk64](cref:Meziantou.Framework.Imaging.Cmyk64), or is converted to RGB with
[ImageDecodeOptions.CmykConversion](cref:Meziantou.Framework.Imaging.ImageDecodeOptions.CmykConversion), from its CMYK profile, like a CMYK JPEG or TIFF file. CMYK pixels have no alpha: a
document with transparency is only loaded with the conversion. An untyped load without it throws an
[UnsupportedImageFeatureException](cref:Meziantou.Framework.Imaging.UnsupportedImageFeatureException). See [Color management](../09-Color/03-ColorManagement.md#keep-cmyk-images).

An 8-bit Lab document is only loaded with
[ImageDecodeOptions.CieLabConversion](cref:Meziantou.Framework.Imaging.ImageDecodeOptions.CieLabConversion), which converts its colors, relative to D50, to the RGB profile of the
options: see [Color management](../09-Color/03-ColorManagement.md).

[Image.Identify](cref:Meziantou.Framework.Imaging.Image.Identify) reports [ImageColorModel.Cmyk](cref:Meziantou.Framework.Imaging.ImageColorModel.Cmyk) or [ImageColorModel.CieLab](cref:Meziantou.Framework.Imaging.ImageColorModel.CieLab) without the conversion, so you can choose the options
before loading.

## Transparency

A composite has an alpha channel only when Photoshop flags it, with a negative layer count: the first channel after the
color channels is then its transparency. The other extra channels (saved selections, spot colors) are not part of the
image and are skipped.

Photoshop stores the colors of a transparent composite blended over white. The decoder removes the white, so the
colors are the ones of the document, as ImageMagick reads them: a pixel that is partly transparent gets its own color
back, rounded, and a fully transparent pixel keeps the samples stored in the file (usually white), whose
color is undefined.

## Save a PSD file

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_PsdSave;format=md-fenced-code-block;dedent=true -->
```csharp
public static void SavePsd(Image image, string psdPath, string psbPath)
{
    // A flat document whose composite is the image; an image with transparency also gets one layer that holds it.
    // PackBits rows by default; the ICC profile, EXIF, XMP and the resolution are written as image resources
    image.Save(psdPath, new PsdEncoder { Compression = PsdCompression.RunLength });

    // The large document format: selected by the extension or by LargeDocument, and always above 30,000 pixels per side
    image.Save(psbPath);
}
```
<!-- endref -->

The image is the composite of the document. Gray pixel formats give a grayscale document,
[Cmyk32](cref:Meziantou.Framework.Imaging.Cmyk32) and [Cmyk64](cref:Meziantou.Framework.Imaging.Cmyk64) pixels a CMYK
document, and the others an RGB document, at the depth of the pixel format: 8 or 16 bits, or 32 bits for
[RgbaFloat](cref:Meziantou.Framework.Imaging.RgbaFloat) and [RgbaHalf](cref:Meziantou.Framework.Imaging.RgbaHalf).
Photoshop reads 32-bit samples as linear light: such an image must be labeled linear (the default of floating-point
images) or have an ICC profile.

An opaque image is written without layer, as the flat documents of Photoshop. An image with a pixel that is not fully
opaque gets a transparency channel and one layer, `Layer 0`, that holds the image: the format flags the transparency of
the composite only through its layers, and Photoshop shows it through them. As Photoshop does, the colors of the
composite are stored blended over white, which readers undo; the colors of partly transparent pixels of 8 and 16 bits
come back rounded, and the layer holds them as they are.

| Option | What it does | Default |
| --- | --- | --- |
| [Compression](cref:Meziantou.Framework.Imaging.Formats.PsdEncoder.Compression) | [PsdCompression.RunLength](cref:Meziantou.Framework.Imaging.Formats.PsdCompression.RunLength) (PackBits rows) or [PsdCompression.None](cref:Meziantou.Framework.Imaging.Formats.PsdCompression.None) | `RunLength` |
| [LargeDocument](cref:Meziantou.Framework.Imaging.Formats.PsdEncoder.LargeDocument) | Writes a PSB file even when the image fits a PSD file; `.psb` sets it | `false` |

Text entries and the other metadata the format has no place for follow
[MetadataHandling](cref:Meziantou.Framework.Imaging.Formats.ImageEncoder.MetadataHandling): rejected by default. A PSD file
stores one image: an animation is rejected.

## Pitfalls

- A document saved with "Maximize Compatibility" off has no real composite: it is identified, and a load throws an
  [UnsupportedImageFeatureException](cref:Meziantou.Framework.Imaging.UnsupportedImageFeatureException) (`No composite image`), since the library does not render layers. Save it again
  from Photoshop with the option on.
- The image is the composite as Photoshop saved it. Hidden layers, layer effects and the content of the layers are not
  available.
- ZIP-compressed image data, multichannel documents, 32-bit CMYK, 16 and 32-bit Lab and 32-bit duotone documents throw
  an [UnsupportedImageFeatureException](cref:Meziantou.Framework.Imaging.UnsupportedImageFeatureException); all of them but ZIP image data already from
  [Image.Identify](cref:Meziantou.Framework.Imaging.Image.Identify).
- A duotone document is read as its gray samples: the inks and curves of its duotone specification are not applied.
- The ICC profile is kept only when it matches the samples (gray, RGB or CMYK); the profile of a Lab document is not
  used. The thumbnail and IPTC data are not read.
- The channels are stored one after the other, so a load holds about one more copy of the image while it decodes: see
  [Resource limits](../05-LoadingAndSaving/07-ResourceLimits.md).
- There is no scaled or region decoding, and no lenient decoding: a truncated or malformed file throws an
  [InvalidImageContentException](cref:Meziantou.Framework.Imaging.InvalidImageContentException).
- The PackBits rows of a PSD file record their length in 16 bits: a row of 32-bit samples wider than about 16,000
  pixels does not fit, and the save throws an
  [UnsupportedImageFeatureException](cref:Meziantou.Framework.Imaging.UnsupportedImageFeatureException). Write it raw,
  or as a PSB file.
- The encoder compresses every row twice: once to know the lengths the file gives before the rows, once to write them.
- ImageMagick reads the files the encoder writes, but does not remove the white from the colors of a gray composite
  with transparency.
