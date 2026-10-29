---
title: OpenEXR
description: Read and write OpenEXR files of half or float samples, the linear-light HDR format of rendering and visual effects.
---

OpenEXR stores scene-linear, high dynamic range images in 16-bit (half) or 32-bit floating-point samples, as rendering
and visual-effects tools produce them. Files load as `RgbaHalf` or `RgbaFloat` pixels, values above 1 included, and
`OpenExrEncoder` writes them without loss.

## What is read and written

| | Reading | Writing |
| --- | --- | --- |
| Files | Single-part scan-line and tiled files (the full-resolution level of mipmaps and ripmaps) | Single-part scan-line files |
| Samples | HALF, FLOAT and UINT | HALF or FLOAT |
| Channels | `R`, `G`, `B`, `A`, or `Y` (luminance) with or without `A` | `R`, `G`, `B` (and `A` for pixel formats with alpha), or `Y` for gray pixel formats |
| Compression | NONE, RLE, ZIPS, ZIP, PIZ, PXR24, B44, B44A | NONE, RLE, ZIPS, ZIP (default) or PIZ, all lossless |
| Metadata | Chromaticities | Chromaticities |

A file of half samples loads as `RgbaHalf`, any other as `RgbaFloat`, in linear light. Infinities and samples that are
not numbers are kept.

## Load and save an OpenEXR file

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_OpenExr;format=md-fenced-code-block;dedent=true -->
```csharp
public static Image<Rgba32> ConvertOpenExr(string exrPath, string pizPath)
{
    // An OpenEXR file loads as RgbaHalf (half samples) or RgbaFloat (float or 32-bit integer samples), in linear light
    using var hdr = Image.Load(exrPath);

    // Lossless: the samples are written as they are, values above 1 included. PIZ is often the smallest
    hdr.Save(pizPath, new OpenExrEncoder { Compression = OpenExrCompression.Piz });

    // Integer pixels cannot hold the highlights: a typed load compresses them with a tone mapping
    return Image.Load<Rgba32>(exrPath, new ImageDecodeOptions
    {
        Conversion = new PixelConversionOptions { ToneMapping = new HdrToneMappingOptions() },
    });
}
```
<!-- endref -->

Integer pixels cannot hold values above 1: loading an OpenEXR file as `Rgba32` or `Rgba64` rejects them, unless the
conversion clips them or compresses the highlights with a tone mapping, as in the example. The same applies to saving
floating-point images to other formats, with `ImageEncoder.FloatingPointConversion`. See
[Pixel formats](../04-Concepts/05-PixelFormats.md).

## Options

| Option | What it does | Default |
| --- | --- | --- |
| `Compression` | `None`, `Rle`, `Zips`, `Zip` or `Piz`; PIZ is usually the smallest for photos and noisy images | `Zip` |
| `PixelType` | `Half` or `Float`; `null` chooses the narrowest type that stores the pixels exactly | `null` |
| `AllowBitDepthReduction` | Lets `RgbaFloat` and 16-bit pixels be rounded to half samples when `PixelType` is `Half` | `false` |
| `MetadataHandling` | What to do with metadata OpenEXR cannot store | `Strict` |

By default, `RgbaHalf` and 8-bit pixels are written as half samples, and `RgbaFloat` and 16-bit pixels as float samples.
Integer pixels are written as their value divided by the largest sample (`v / max`).

## Pitfalls

- OpenEXR samples are linear light. An image labeled sRGB, which is the label of integer pixel formats, or with an ICC
  profile follows `MetadataHandling`, which rejects it by default: saving an 8-bit PNG as OpenEXR throws. A CICP
  description with linear transfer characteristics and BT.709, BT.2020, DCI-P3 or Display P3 primaries is written as
  chromaticities.
- Compressed output needs a seekable destination (the offset table precedes the chunks); `None` does not.
- Half samples are limited to ±65,504: a larger finite value is rejected when it is written as half.
- EXIF, XMP, an orientation, a resolution and text entries cannot be stored, and the attributes of a file other than its
  windows and chromaticities are not read.
- Multi-part files, deep data, the DWAA, DWAB, HTJ2K and ZSTD compressions, and channels other than `R`, `G`, `B`, `A`
  and `Y` (layers, depth) are rejected with an `UnsupportedImageFeatureException`.
- The decoder reads the full-resolution level of mipmaps and ripmaps; the lower levels and the preview image are not
  read.
