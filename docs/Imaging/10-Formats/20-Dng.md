---
title: DNG
description: Read DNG (Adobe Digital Negative) raw photos, render them to linear light, and get their raw mosaic before the demosaicing.
---

DNG (Adobe Digital Negative, `.dng`) files store what the sensor of a camera measured: one sample per pixel behind a
color filter array (or every color of linear raw data), the black and white levels of the sensor, and a camera profile
that says how the camera sees colors. Phones and cameras write DNG, and raw converters convert the raw files of other
cameras to it. Loading a DNG file renders it: the samples become linear-light
[RgbaFloat](cref:Meziantou.Framework.Imaging.RgbaFloat) pixels with the primaries of BT.709, the size of the image the
camera framed. Monochrome linear raw data (one sample per pixel) render to [GrayFloat](cref:Meziantou.Framework.Imaging.GrayFloat) pixels. DNG files are read, not written.

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

## Errors

A file that needs a feature the decoder does not implement (a staggered sensor, non-square pixels, a required
correction it does not know) throws an
[UnsupportedImageFeatureException](cref:Meziantou.Framework.Imaging.UnsupportedImageFeatureException), from `Identify`
too. With lenient decoding ([DecodingStrictness.Lenient](cref:Meziantou.Framework.Imaging.DecodingStrictness.Lenient), see
[Error handling](../05-LoadingAndSaving/08-ErrorHandling.md)), the corrections it does not know are skipped.
