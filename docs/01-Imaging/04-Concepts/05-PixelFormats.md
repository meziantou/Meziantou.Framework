---
title: Pixel formats
description: The twelve pixel formats, which one a file loads as, how to convert, and how alpha and precision are kept.
---

Every image stores its pixels in one pixel format, such as `Rgba32` or `Gray16`. This page lists the formats, tells
which one a file loads as, and shows how to convert between them without losing information by accident.

## The formats

| Format | Bytes per pixel | Holds |
| --- | --- | --- |
| `Rgba32` | 4 | 8-bit red, green, blue and alpha: the usual choice for color images |
| `Bgra32` | 4 | The same, in blue, green, red, alpha order, for APIs that expect it |
| `Rgb24` | 3 | 8-bit color without alpha |
| `Rgba64` | 8 | 16-bit color and alpha |
| `Rgb48` | 6 | 16-bit color without alpha |
| `Gray8` | 1 | 8-bit gray levels |
| `Gray16` | 2 | 16-bit gray levels |
| `La16` | 2 | 8-bit gray and alpha |
| `La32` | 4 | 16-bit gray and alpha |
| `A8` | 1 | An alpha mask: no color, its pixels read as black with their alpha |
| `RgbaHalf` | 8 | Half-precision floating-point color and alpha, in linear light |
| `RgbaFloat` | 16 | Single-precision floating-point color and alpha, in linear light |

Alpha is always straight (not premultiplied), and 16-bit samples are in the native byte order of the machine.
`PixelFormats` describes each format (`HasAlpha`, `HasColor`, `IsGrayscale`, `IsFloatingPoint`, `GetBytesPerPixel`, …).

## `Image` and `Image<TPixel>`

`Image.Load` returns an `Image` whose `PixelFormat` is the smallest format that holds the file without loss. It is an
`Image<TPixel>` underneath, which you can test with a type pattern. Examples:

| Source | Untyped `Load` gives |
| --- | --- |
| 8-bit PNG with alpha, palettes, GIF | `Rgba32` |
| 16-bit PNG | `Gray16`, `Rgb48` or `Rgba64` |
| Gray PNG with alpha | `La16` or `La32` |
| JPEG | `Gray8` or `Rgb24` (`Gray16` or `Rgb48` for samples of more than 8 bits) |
| OpenEXR, floating-point TIFF pages | `RgbaHalf` or `RgbaFloat` |

`Image.Identify` tells the format before decoding (`ImageInfo.PixelFormat`): see
[Identifying images](../05-LoadingAndSaving/03-IdentifyingImages.md).

When your code works on pixels of a known type, load in that type with `Image.Load<TPixel>`, or convert with
`CloneAs<TPixel>`. A type that is not one of the twelve formats throws a `NotSupportedException` before anything is
read.

## Convert between formats

`CloneAs<TPixel>` returns a converted copy, and `Load<TPixel>` converts while loading:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_ConvertPrecision;format=md-fenced-code-block;dedent=true -->
```csharp
public static void ConvertToGray16(string inputPath, string outputPath)
{
    using var image = Image.Load<Rgba64>(inputPath);
    image.AutoOrient();
    using var gray = image.CloneAs<Gray16>(new PixelConversionOptions
    {
        BackgroundColor = new Rgba64(65535, 65535, 65535),
        DiscardIncompatibleColorProfile = true,
    });
    gray.Save(outputPath, new PngEncoder());
}
```
<!-- endref -->

- 8 to 16 bits is exact; 16 to 8 bits rounds to the nearest value. A 16-bit conversion never goes through 8 bits.
- Color to gray uses the Rec. 709 luma of the encoded values.
- Gray to RGBA and back, opaque `Rgb48` to `Rgba64` and back, and an alpha mask to any format with alpha and back are
  exact.
- A conversion never converts colors between color spaces: the ICC profile of the image is kept when it can still
  describe the pixels. `DiscardIncompatibleColorProfile` drops a profile that cannot, such as an RGB profile on gray
  pixels. To convert colors, see [Color management](../09-Color/03-ColorManagement.md).

## Nothing is lost silently

An operation that would lose information throws an `UnsupportedImageFeatureException`, unless you say how to convert:

| Loss | Control |
| --- | --- |
| Alpha, when converting or loading into a format without alpha | `PixelConversionOptions.BackgroundColor`: the pixels are composited over it. Opaque pixels never need one. |
| Alpha, when saving JPEG, BMP in a layout other than 32-bit, plain Netpbm or a portable bitmap | The `BackgroundColor` of the encoder |
| Partial alpha, when saving GIF (one transparent color) | `GifEncoder.AlphaMode`: a threshold (the default) or compositing over a background |
| 16-bit precision, when saving JPEG, WebP, QOI, BMP or TGA | `AllowBitDepthReduction` on the encoder |
| 16-bit precision, when saving AVIF or HEIC (at most 12 bits) | `BitDepth` or `AllowBitDepthReduction` on the encoder |
| 16-bit precision, when loading or converting | Request the narrower type explicitly (`Load<Rgba32>`, `CloneAs<Gray8>`) |
| Colors beyond the palette, when saving an indexed PNG or BMP | `AllowColorReduction` on the encoder. GIF always reduces each frame to 256 colors, deterministically: see [Color reduction](../06-Processing/08-ColorReduction.md) |
| Floating-point samples outside the range of the destination | `PixelConversionOptions.OutOfRangeSamples` or `ToneMapping` |
| Floating-point samples, when saving a format that stores integers | `ImageEncoder.FloatingPointConversion` |
| Metadata the output cannot store | `ImageEncoder.MetadataHandling`: see [Saving images](../05-LoadingAndSaving/04-SavingImages.md) |

For example, a JPEG stores neither alpha nor 16-bit samples nor every kind of metadata, so exporting a frame of any
image to JPEG sets the three controls:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_ExportJpegFrame;format=md-fenced-code-block;dedent=true -->
```csharp
public static void ExportFrameAsJpeg(string animatedPath, int frameIndex, string jpegPath)
{
    using var animation = Image.Load(animatedPath);
    using var frame = animation.CloneFrame(frameIndex);
    frame.Save(jpegPath, new JpegEncoder
    {
        Quality = 85,
        BackgroundColor = new Rgba32(255, 255, 255),
        AllowBitDepthReduction = true,
        MetadataHandling = MetadataHandling.DiscardUnsupported,
    });
}
```
<!-- endref -->

## Compact formats

`La16`, `La32`, `Rgb48` and `A8` hold what many images need in less memory: a gray image with alpha in half the memory
of `Rgba32` or `Rgba64`, 16-bit color without alpha in three quarters of the memory of `Rgba64`, an alpha mask in one
byte per pixel. A conversion to `A8` keeps the alpha of the source and drops its color:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_CompactFormats;format=md-fenced-code-block;dedent=true -->
```csharp
public static Image<A8> ExtractAlphaMask(string inputPath, string maskPath)
{
    // Untyped loads pick the smallest lossless format: La16 for a gray image with alpha (2 bytes per pixel instead of
    // the 4 of Rgba32), Rgb48 for 16-bit color without alpha
    using var image = Image.Load(inputPath);

    // An alpha mask keeps the alpha of the image in 1 byte per pixel; its pixels read as black
    var mask = image.CloneAs<A8>();

    // PNG stores the mask as gray with alpha, which loads back as La16
    mask.Save(maskPath, new PngEncoder());
    return mask;
}
```
<!-- endref -->

Every processing operation and the drawing canvas accept these formats, except `Flatten` on an `A8` mask, which has no
color to hold the result. The PNG and TIFF encoders store them as they are (a mask as black gray with alpha); the other
encoders receive a copy widened without loss to `Rgba32` or `Rgba64`, then apply their own rules, such as the
background color a JPEG needs for pixels that are not opaque.

## Floating-point and high dynamic range

`RgbaHalf` and `RgbaFloat` hold linear light, where 1 is the reference white: a component above 1 is a highlight, a
negative one is outside the gamut of the primaries, and alpha is a coverage from 0 to 1. OpenEXR files and the
floating-point pages of TIFF files load in these formats; `Load<RgbaFloat>` converts any other file.

Integer formats cannot hold values outside [0, 1], so a conversion to them throws on such samples unless you compress
the highlights with a tone mapping or clip them. Encoders work the same way: only OpenEXR and TIFF store floating-point
samples, and the others need `FloatingPointConversion`:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_FloatingPoint;format=md-fenced-code-block;dedent=true -->
```csharp
public static Image<Rgba64> ToneMapHighlights(Image<RgbaFloat> hdr, string pngPath)
{
    // Integer pixels cannot store the highlights: compress them with a tone mapping, or clip them
    var toneMapped = hdr.CloneAs<Rgba64>(new PixelConversionOptions
    {
        ToneMapping = new HdrToneMappingOptions { Operator = HdrToneMappingOperator.Reinhard },
    });

    // Encoders store integers too: the encoder says how to reduce the samples
    hdr.Save(pngPath, new PngEncoder
    {
        FloatingPointConversion = new PixelConversionOptions { OutOfRangeSamples = OutOfRangeSampleHandling.Clip },
        MetadataHandling = MetadataHandling.DiscardUnsupported,
    });
    return toneMapped;
}
```
<!-- endref -->

| Conversion | Result |
| --- | --- |
| Integer to `RgbaFloat` | Exact, and back to the same integer for every sample |
| Integer to `RgbaHalf` | Exact back for 8-bit samples, not for 16-bit ones (a half has 11 significant bits) |
| `RgbaHalf` to `RgbaFloat` | Exact |
| `RgbaFloat` to `RgbaHalf` | Nearest; a finite sample beyond ±65,504 is rejected unless `OutOfRangeSamples` is `Clip` |
| Floating point to integer | A sample outside [0, 1] is rejected unless `ToneMapping` is set or `OutOfRangeSamples` is `Clip`; a sample that is not a number is always rejected |

Every check runs on every frame before anything is allocated. A conversion keeps the transfer function label of its
source: an sRGB `Rgba64` image becomes sRGB-encoded floats, not linear light. See
[Color management](../09-Color/03-ColorManagement.md) and [OpenEXR](../10-Formats/17-OpenExr.md).

## See also

- [Color values](../09-Color/04-ColorValues.md): named colors, hexadecimal notations and the other color types.
