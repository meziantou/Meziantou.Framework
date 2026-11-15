---
title: DNG
description: Read DNG (Adobe Digital Negative) raw photos, render them to linear light, get their raw mosaic before the demosaicing, and save images as linear raw DNG files.
---

DNG (Adobe Digital Negative, `.dng`) files store what the sensor of a camera measured: one sample per pixel behind a
color filter array (or every color of linear raw data), the black and white levels of the sensor, and a camera profile
that says how the camera sees colors. Phones and cameras write DNG, and raw converters convert the raw files of other
cameras to it. Loading a DNG file renders it: the samples become linear-light
[RgbaFloat](cref:Meziantou.Framework.Imaging.RgbaFloat) pixels with the primaries of BT.709, the size of the image the
camera framed. Monochrome linear raw data (one sample per pixel) render to [GrayFloat](cref:Meziantou.Framework.Imaging.GrayFloat) pixels.
[DngEncoder](cref:Meziantou.Framework.Imaging.Formats.DngEncoder) saves an image as linear raw data, a DNG file that raw
converters open.

## Render a raw photo and read its mosaic

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_Dng;format=md-fenced-code-block;dedent=true -->
```csharp
public static (Image<RgbaFloat> Rendering, Image<Rgba32> Display, string? Camera, float FirstLinearValue) ReadDng(string dngPath)
{
    // DNG files render to RgbaFloat: scene-referred linear light (BT.709 primaries), the size of the default crop
    var rendering = Image.Load<RgbaFloat>(dngPath);
    var camera = rendering.Metadata.DngProperties?.UniqueCameraModel;

    // The raw image before the demosaicing: the stored samples, the levels, the pattern and the linear values
    using var raw = DngRawImage.Load(dngPath);
    var row = new float[raw.ActiveArea.Width * raw.SamplesPerPixel];
    raw.CopyLinearRow(0, row);

    // The highlights are above 1: a display version tone maps them into 8-bit sRGB
    var display = Image.Load<Rgba32>(dngPath, new ImageDecodeOptions
    {
        Conversion = new PixelConversionOptions { ToneMapping = new HdrToneMappingOptions() },
    });
    return (rendering, display, camera, row[0]);
}
```
<!-- endref -->

[DngRawImage](cref:Meziantou.Framework.Imaging.DngRawImage) gives the raw image before the demosaicing: the samples as
the file stores them, the color of each position of the pattern, the levels and areas of the sensor, and the samples
mapped to linear values between 0 (no light) and 1 (the white level). These values are the same in every reader; the
rendering is not, because each reader interpolates the missing colors its own way.

## What is read

| | Reading |
| --- | --- |
| Raw data | Color filter arrays of any rectangular pattern (Bayer, four colors, larger patterns) and linear raw data |
| Samples | 8 to 16-bit integers, 16, 24 and 32-bit floating-point numbers (high dynamic range DNG) |
| Compression | Uncompressed, Deflate, lossless JPEG, lossy JPEG and JPEG XL, in strips or tiles |
| Processing | Linearization table, black and white levels, active area, default crop, white balance, camera profiles of one or two illuminants, baseline exposure, the common corrections of the opcode lists (lens distortion and vignetting, bad pixels, gain maps, tables and polynomials) |
| Metadata | The version and the camera ([DngProperties](cref:Meziantou.Framework.Imaging.Metadata.DngProperties)), EXIF, XMP, the orientation (reported, not applied) |
| Previews | The previews, depth maps and masks, through [AuxiliaryImageCollection](cref:Meziantou.Framework.Imaging.AuxiliaryImageCollection) |

The rendering is scene-referred: highlights above the white of the scene are values above 1, and colors outside the
gamut of BT.709 have negative components. To display it, convert it with a tone mapping, as the example does, or keep
the floating-point pixels for further processing. The look of the camera profile (its tone curve and its color tables)
is not applied, and the missing colors of a color filter array are interpolated bilinearly: the rendering is close to,
but not the same as, the rendering of a raw converter.

The raw formats of camera vendors (CR2, CR3, NEF, ARW, RAF, ORF and others) are not read: they are not documented.
Convert them to DNG first, for example with the free Adobe DNG Converter.

## Save a DNG file

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_DngSave;format=md-fenced-code-block;dedent=true -->
```csharp
public static void SaveDng(Image image, string dngPath)
{
    // Linear raw data whose camera color space is linear BT.709: the samples are stored as they are, with a table that
    // maps sRGB samples to linear values. Lossless JPEG tiles (Deflate for floating-point samples)
    image.Save(dngPath, new DngEncoder { Compression = DngCompression.LosslessJpeg, TileSize = 256 });
}
```
<!-- endref -->

The encoder writes the image as linear raw data (`LinearRaw`): the image is already rendered, so there is no color filter
array to interpolate. Its camera profile says that the camera sees linear RGB with the primaries of BT.709 and the D65
white point, without tone curve, so DNG readers show the colors of the image, and
[Image.Load](cref:Meziantou.Framework.Imaging.Image.Load) renders it back to linear light.

- Integer pixels are stored as they are, 8 or 16 bits per sample. The samples of an sRGB image are mapped to linear
  values by a linearization table of the sRGB transfer function, with the 16-bit precision of DNG; an image labeled
  linear light stores its linear values. [DngRawImage.CopyStoredRow](cref:Meziantou.Framework.Imaging.DngRawImage.CopyStoredRow)
  gives the samples back exactly.
- [RgbaFloat](cref:Meziantou.Framework.Imaging.RgbaFloat) and [RgbaHalf](cref:Meziantou.Framework.Imaging.RgbaHalf) pixels
  are stored as 32-bit floats. They are linear light, so the image must be labeled linear (the default of
  floating-point images), and their values must be finite and not negative. Highlights above 1 keep their values.
- Gray pixel formats give a monochrome image of one sample per pixel.
- The orientation and the XMP packet are written. An ICC profile, EXIF, a resolution and text entries follow
  [MetadataHandling](cref:Meziantou.Framework.Imaging.Formats.ImageEncoder.MetadataHandling): rejected by default.
- The image file directory follows the image data, so the destination stream must be seekable.

| Option | What it does | Default |
| --- | --- | --- |
| [Compression](cref:Meziantou.Framework.Imaging.Formats.DngEncoder.Compression) | [DngCompression.LosslessJpeg](cref:Meziantou.Framework.Imaging.Formats.DngCompression.LosslessJpeg) for integer samples, [DngCompression.Deflate](cref:Meziantou.Framework.Imaging.Formats.DngCompression.Deflate) for floating-point ones, or [DngCompression.None](cref:Meziantou.Framework.Imaging.Formats.DngCompression.None) | Lossless JPEG, Deflate for floats |
| [TileSize](cref:Meziantou.Framework.Imaging.Formats.DngEncoder.TileSize) | Tiles of this size (a multiple of 16), never larger than the image | 256 with lossless JPEG, strips otherwise |

DNG raw images have no transparency: an image with a pixel that is not fully opaque is rejected, flatten it first. A
[DngRawImage](cref:Meziantou.Framework.Imaging.DngRawImage) cannot be written back: it has no camera profile, so the
colors of its mosaic would be lost.

## Errors

A file that needs a feature the decoder does not implement (a staggered sensor, non-square pixels, a required
correction it does not know) throws an
[UnsupportedImageFeatureException](cref:Meziantou.Framework.Imaging.UnsupportedImageFeatureException), from `Identify`
too. With lenient decoding ([DecodingStrictness.Lenient](cref:Meziantou.Framework.Imaging.DecodingStrictness.Lenient), see
[Error handling](../05-LoadingAndSaving/08-ErrorHandling.md)), the corrections it does not know are skipped.
