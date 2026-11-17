---
title: Radiance HDR
description: Read and write Radiance HDR (RGBE) files, the high dynamic range format of lighting simulations and HDRI environment maps.
---

Radiance HDR files (`.hdr`, formerly `.pic`) store high dynamic range pictures with four bytes per pixel: an 8-bit
mantissa for each color and an exponent the three colors share. Lighting simulations write them, and most HDRI
environment maps are distributed in this format. Files load as [RgbaFloat](cref:Meziantou.Framework.Imaging.RgbaFloat) pixels in linear light, and
[RadianceEncoder](cref:Meziantou.Framework.Imaging.Formats.RadianceEncoder) writes them.

## What is read and written

| | Reading | Writing |
| --- | --- | --- |
| Pixels | RGB samples (RGBE); XYZE files are identified but not loaded | RGB samples (RGBE) |
| Scanlines | Flat, "old" and "new" run-length encoded, mixed in one file | New run-length encoding (default) or flat |
| Orientation | The eight orientations of the resolution string; the image is always upright | The standard orientation (rows from the top) |
| Header | Exposure, color correction, primaries, pixel aspect ratio, gamma, view and software, reported | The primaries |

A pixel of mantissas `m` and exponent `e` is the color `(m + 0.5) × 2^(e - 136)`: the middle of the interval the writer
truncated the color into, as Radiance reads it. A float holds these values exactly. FFmpeg and Apple ImageIO read the
bottom of the interval, `m × 2^(e - 136)`, so their values are half a unit lower.

## Load and save a Radiance HDR file

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_Radiance;format=md-fenced-code-block;dedent=true -->
```csharp
public static (Image<Rgba32> Preview, double Exposure) ConvertRadiance(string hdrPath, string flatPath)
{
    // Radiance HDR files load as RgbaFloat, in linear light: the pixels are the values the file stores
    using var hdr = Image.Load<RgbaFloat>(hdrPath);

    // The header is reported, never applied: divide by the exposure to get the radiances before it was adjusted
    var exposure = hdr.Metadata.RadianceHeader?.Exposure ?? 1;

    // Run-length encoded scanlines by default; flat ones on request. Each color is stored with 8 bits of mantissa
    hdr.Save(flatPath, new RadianceEncoder { Compression = RadianceCompression.None });

    // Integer pixels cannot hold the highlights: a typed load compresses them with a tone mapping
    var preview = Image.Load<Rgba32>(hdrPath, new ImageDecodeOptions
    {
        Conversion = new PixelConversionOptions { ToneMapping = new HdrToneMappingOptions() },
    });
    return (preview, exposure);
}
```
<!-- endref -->

The header is reported in [ImageMetadata.RadianceHeader](cref:Meziantou.Framework.Imaging.Metadata.ImageMetadata.RadianceHeader), and never applied: the pixels are the
values the file stores. [Exposure](cref:Meziantou.Framework.Imaging.Metadata.RadianceHeader.Exposure) is the product of the `EXPOSURE` lines, the multiplier
applied to the pixels since the picture was rendered; divide by it to get the original radiances. A value that cannot
be read, such as the `PIXASPECT=inf` FFmpeg writes, is skipped.

Integer pixels cannot hold values above 1: loading a Radiance HDR file as [Rgba32](cref:Meziantou.Framework.Imaging.Rgba32) rejects them, unless the
conversion clips them or compresses the highlights with a tone mapping, as in the example. See
[Pixel formats](../04-Concepts/05-PixelFormats.md).

## Options

| Option | What it does | Default |
| --- | --- | --- |
| [Compression](cref:Meziantou.Framework.Imaging.Formats.RadianceEncoder.Compression) | [RunLength](cref:Meziantou.Framework.Imaging.Formats.RadianceCompression.RunLength) or [None](cref:Meziantou.Framework.Imaging.Formats.RadianceCompression.None) (flat scanlines, four bytes per pixel) | [RunLength](cref:Meziantou.Framework.Imaging.Formats.RadianceCompression.RunLength) |
| [BackgroundColor](cref:Meziantou.Framework.Imaging.Formats.RadianceEncoder.BackgroundColor) | The opaque color pixels that are not opaque are composited over, in linear light | `null`: such pixels are rejected |
| [MetadataHandling](cref:Meziantou.Framework.Imaging.Formats.MetadataHandling) | What to do with metadata Radiance HDR cannot store | [Strict](cref:Meziantou.Framework.Imaging.Formats.MetadataHandling.Strict) |

Each color is truncated to 8 bits of mantissa, relative to the largest color of its pixel: RGBE has about 1% of
precision, over a range from 1E-38 to 1E38. Integer pixels are written as their value divided by the largest sample
(`v / max`), and gray pixel formats as three equal colors. A scanline shorter than 8 pixels or longer than 32,767
pixels is always flat.

## Pitfalls

- Radiance HDR samples are linear light. An image labeled sRGB, which is the label of integer pixel formats, or with an
  ICC profile follows [MetadataHandling](cref:Meziantou.Framework.Imaging.Formats.MetadataHandling), which rejects it by default. A CICP description with linear transfer
  characteristics and BT.709, BT.2020, DCI-P3 or Display P3 primaries is written as `PRIMARIES`.
- Negative colors, infinities and samples that are not numbers cannot be stored, and are rejected before anything is
  written: clamp the colors first. A resampling filter such as Lanczos can make small negative colors.
- XYZE files (CIE XYZ samples) are rejected by a load with an [UnsupportedImageFeatureException](cref:Meziantou.Framework.Imaging.UnsupportedImageFeatureException): converting them to RGB is
  a color conversion, which the library never makes implicitly. Convert them with `ra_xyze -r` first.
- The standard primaries of Radiance are read as those of BT.709 (sRGB), as other readers do: they differ by 0.01 in
  the x of green and by their white point. Other primaries label the image with a CICP description or an ICC profile.
- The encoder writes no exposure: [RadianceHeader](cref:Meziantou.Framework.Imaging.Metadata.RadianceHeader) is ignored when saving, so the pixels written are
  the values of the image.
- A truncated file is rejected: there is no lenient decoding for Radiance HDR.
