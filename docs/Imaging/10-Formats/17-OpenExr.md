---
title: OpenEXR
description: Read and write OpenEXR files of half or float samples, the linear-light HDR format of rendering and visual effects.
---

OpenEXR stores scene-linear, high dynamic range images in 16-bit (half) or 32-bit floating-point samples, as rendering
and visual-effects tools produce them. Files load as [RgbaHalf](cref:Meziantou.Framework.Imaging.RgbaHalf) or [RgbaFloat](cref:Meziantou.Framework.Imaging.RgbaFloat) pixels, values above 1 included, and
[OpenExrEncoder](cref:Meziantou.Framework.Imaging.Formats.OpenExrEncoder) compresses the stored samples losslessly. Conversion between the library's straight colors and
OpenEXR's premultiplied colors can round them; this is not a general bit-exact source-pixel round-trip guarantee.

## What is read and written

| | Reading | Writing |
| --- | --- | --- |
| Files | Single-part scan-line and tiled files (the full-resolution level of mipmaps and ripmaps) | Single-part scan-line files |
| Samples | HALF, FLOAT and UINT | HALF or FLOAT |
| Channels | `R`, `G`, `B`, `A`, or `Y` (luminance) with or without `A`; or one layer of a file with other channels | `R`, `G`, `B` (and `A` for pixel formats with alpha), or `Y` for gray pixel formats |
| Windows | The display window; an overscan cropped to it, or the data window | Both windows are the image |
| Compression | NONE, RLE, ZIPS, ZIP, PIZ, PXR24, B44, B44A | NONE, RLE, ZIPS, ZIP (default) or PIZ, all lossless |
| Metadata | Chromaticities, the windows | Chromaticities |

A file of half samples without an alpha channel loads as [RgbaHalf](cref:Meziantou.Framework.Imaging.RgbaHalf), any other as [RgbaFloat](cref:Meziantou.Framework.Imaging.RgbaFloat), in linear light.
The extra precision is used when dividing colors by alpha. Infinities and samples that are not numbers are kept,
subject to the alpha conversion.

## Load and save an OpenEXR file

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_OpenExr;format=md-fenced-code-block;dedent=true -->
```csharp
public static Image<Rgba32> ConvertOpenExr(string exrPath, string pizPath)
{
    // Half-only files without alpha load as RgbaHalf; other OpenEXR files load as RgbaFloat, in linear light
    using var hdr = Image.Load(exrPath);

    // PIZ compresses the stored samples losslessly, values above 1 included; alpha conversion can round colors
    hdr.Save(pizPath, new OpenExrEncoder { Compression = OpenExrCompression.Piz });

    // Integer pixels cannot hold the highlights: a typed load compresses them with a tone mapping
    return Image.Load<Rgba32>(exrPath, new ImageDecodeOptions
    {
        Conversion = new PixelConversionOptions { ToneMapping = new HdrToneMappingOptions() },
    });
}
```
<!-- endref -->

Integer pixels cannot hold values above 1: loading an OpenEXR file as [Rgba32](cref:Meziantou.Framework.Imaging.Rgba32) or [Rgba64](cref:Meziantou.Framework.Imaging.Rgba64) rejects them, unless the
conversion clips them or compresses the highlights with a tone mapping, as in the example. The same applies to saving
floating-point images to other formats, with [ImageEncoder.FloatingPointConversion](cref:Meziantou.Framework.Imaging.Formats.ImageEncoder.FloatingPointConversion). See
[Pixel formats](../04-Concepts/05-PixelFormats.md).

## Read a layer of a renderer output

Renderers write depth (`Z`), other outputs in layers (`diffuse.R`, `diffuse.G`...) and often an overscan: a data window
larger than the display window. The default options reject such a file, since those channels and pixels would be lost.
[ImageConfiguration.OpenExrDecoding](cref:Meziantou.Framework.Imaging.ImageConfiguration.OpenExrDecoding) selects what is read:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_OpenExrLayers;format=md-fenced-code-block;dedent=true -->
```csharp
public static (Image Image, Point Origin) LoadRenderLayer(string exrPath, string layer)
{
    // Renderers store RGBA next to depth (Z) and other layers (diffuse.R, diffuse.G, ...), often with an overscan
    // around the display window: the default options reject such a file, since those channels and pixels would be lost
    var configuration = new ImageConfiguration
    {
        OpenExrDecoding = new OpenExrDecodingOptions
        {
            // "" is the default layer (R, G, B, A or Y); "diffuse" selects diffuse.R, diffuse.G, diffuse.B, diffuse.A
            Layer = layer,

            // DataWindow keeps every stored pixel instead
            WindowMode = OpenExrWindowMode.CropToDisplayWindow,
        },
    };

    // Identify, readers and collections take the same configuration, and report the same image
    var image = Image.Load(exrPath, new ImageDecodeOptions { Configuration = configuration });

    // The windows of the file, in its pixel coordinates: the cropped image starts at the display window
    var windows = image.Metadata.OpenExrWindows!;
    return (image, windows.DisplayWindowMin);
}
```
<!-- endref -->

| Option | What it does | Default |
| --- | --- | --- |
| [Layer](cref:Meziantou.Framework.Imaging.OpenExrDecodingOptions.Layer) | The layer to decode: `""` for the default layer, `"diffuse"` for `diffuse.R`, `diffuse.G`, `diffuse.B` and `diffuse.A`; the other channels are ignored | `null`: every channel must be `R`, `G`, `B`, `A` or `Y` |
| [WindowMode](cref:Meziantou.Framework.Imaging.OpenExrDecodingOptions.WindowMode) | [DisplayWindow](cref:Meziantou.Framework.Imaging.OpenExrWindowMode.DisplayWindow), [CropToDisplayWindow](cref:Meziantou.Framework.Imaging.OpenExrWindowMode.CropToDisplayWindow) (the pixels outside the display window are discarded) or [DataWindow](cref:Meziantou.Framework.Imaging.OpenExrWindowMode.DataWindow) (every stored pixel is the image) | [DisplayWindow](cref:Meziantou.Framework.Imaging.OpenExrWindowMode.DisplayWindow): an overscan is rejected |

The options apply to identification, eager loads, readers and collections alike.
[ImageMetadata.OpenExrWindows](cref:Meziantou.Framework.Imaging.Metadata.ImageMetadata.OpenExrWindows) gives the two windows of the file; it is informational, and the encoder
writes both windows from (0, 0). The decoder never falls back to another layer: a layer without color channel, a layer
with RGB and luminance channels, a `Y` channel next to an `X` channel (a vector such as `N.X`, `N.Y`, `N.Z`, or CIE XYZ
colors) and the chroma channels `RY` and `BY` are rejected with an [UnsupportedImageFeatureException](cref:Meziantou.Framework.Imaging.UnsupportedImageFeatureException).

## Options

| Option | What it does | Default |
| --- | --- | --- |
| [Compression](cref:Meziantou.Framework.Imaging.Formats.OpenExrEncoder.Compression) | [None](cref:Meziantou.Framework.Imaging.Formats.OpenExrCompression.None), [Rle](cref:Meziantou.Framework.Imaging.Formats.OpenExrCompression.Rle), [Zips](cref:Meziantou.Framework.Imaging.Formats.OpenExrCompression.Zips), [Zip](cref:Meziantou.Framework.Imaging.Formats.OpenExrCompression.Zip) or [Piz](cref:Meziantou.Framework.Imaging.Formats.OpenExrCompression.Piz); PIZ is usually the smallest for photos and noisy images | [Zip](cref:Meziantou.Framework.Imaging.Formats.OpenExrCompression.Zip) |
| [PixelType](cref:Meziantou.Framework.Imaging.Formats.OpenExrEncoder.PixelType) | [Half](cref:Meziantou.Framework.Imaging.Formats.OpenExrPixelType.Half) or [Float](cref:Meziantou.Framework.Imaging.Formats.OpenExrPixelType.Float); `null` chooses half for [RgbaHalf](cref:Meziantou.Framework.Imaging.RgbaHalf) and 8-bit pixels, float otherwise | `null` |
| [AllowBitDepthReduction](cref:Meziantou.Framework.Imaging.Formats.OpenExrEncoder.AllowBitDepthReduction) | Lets [RgbaFloat](cref:Meziantou.Framework.Imaging.RgbaFloat) and 16-bit pixels be rounded to half samples when [PixelType](cref:Meziantou.Framework.Imaging.Formats.OpenExrEncoder.PixelType) is [Half](cref:Meziantou.Framework.Imaging.Formats.OpenExrPixelType.Half) | `false` |
| [MetadataHandling](cref:Meziantou.Framework.Imaging.Formats.MetadataHandling) | What to do with metadata OpenEXR cannot store | [Strict](cref:Meziantou.Framework.Imaging.Formats.MetadataHandling.Strict) |

By default, [RgbaHalf](cref:Meziantou.Framework.Imaging.RgbaHalf) and 8-bit pixels are written as half samples, and [RgbaFloat](cref:Meziantou.Framework.Imaging.RgbaFloat) and 16-bit pixels as float samples.
Integer pixels are written as their value divided by the largest sample (`v / max`).

Nonzero alpha multiplies the colors on encoding and divides them on decoding. Rounding, underflow or overflow can
change the colors even with the default sample type. A zero alpha keeps its colors without multiplication or division.
Lossless compression preserves the resulting stored samples, independently of integer normalization, half-sample
rounding and alpha conversion.

## Pitfalls

- OpenEXR samples are linear light. An image labeled sRGB, which is the label of integer pixel formats, or with an ICC
  profile follows [MetadataHandling](cref:Meziantou.Framework.Imaging.Formats.MetadataHandling), which rejects it by default: saving an 8-bit PNG as OpenEXR throws. A CICP
  description with linear transfer characteristics and BT.709, BT.2020, DCI-P3 or Display P3 primaries is written as
  chromaticities.
- Compressed output needs a seekable destination (the offset table precedes the chunks); [None](cref:Meziantou.Framework.Imaging.Formats.OpenExrCompression.None) does not.
- Half samples are limited to ±65,504: a larger finite value is rejected when it is written as half.
- EXIF, XMP, an orientation, a resolution and text entries cannot be stored, and the attributes of a file other than its
  windows and chromaticities are not read.
- Multi-part files, deep data, the DWAA, DWAB, HTJ2K and ZSTD compressions, the chroma channels `RY` and `BY`, and,
  unless a layer is selected, channels other than `R`, `G`, `B`, `A` and `Y` (layers, depth) are rejected with an
  [UnsupportedImageFeatureException](cref:Meziantou.Framework.Imaging.UnsupportedImageFeatureException).
- The decoder reads the full-resolution level of mipmaps and ripmaps; the lower levels and the preview image are not
  read.
- A file whose writer stopped before patching its offset table (zeros in the table) is rejected by default; the
  [lenient decoding](../05-LoadingAndSaving/08-ErrorHandling.md#accept-the-files-browsers-accept) rebuilds the table
  from the chunks, as OpenEXR does, when they are all there.
