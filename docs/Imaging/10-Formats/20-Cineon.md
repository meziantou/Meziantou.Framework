---
title: Cineon
description: Read Kodak Cineon film scans, the predecessor of DPX.
---

Kodak Cineon files (`.cin`) hold film scans, almost always three channels of 10-bit printing densities. DPX succeeded
the format, and the library reads Cineon files but does not write them: save them as [DPX](19-Dpx.md).

## What is read

| | Reading |
| --- | --- |
| Channels | One (gray) or three (RGB, placed by their red, green and blue designators), of the same size and depth |
| Samples | 1 to 16 bits, in cells of 8, 16 or 32 bits, left or right justified, one pixel per cell or shared; both byte orders |
| Header | The channel designators and ranges, the chromaticities, the origination and the motion-picture sections, reported |

The pixels are the code values the file stores: a 10-bit code value `v` is the 16-bit sample `round(v × 65535 / 1023)`
of an [Rgb48](cref:Meziantou.Framework.Imaging.Rgb48) image. Printing densities are logarithmic and are never converted to linear light.

## Load a Cineon file and save it as DPX

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_Cineon;format=md-fenced-code-block;dedent=true -->
```csharp
public static int ConvertCineonToDpx(string cinPath, string dpxPath)
{
    // Cineon files are read only: their 10-bit printing densities load as Rgb48, as stored
    using var scan = Image.Load(cinPath);
    var bits = scan.Metadata.CineonHeader!.BitsPerSample;
    scan.Save(dpxPath, new DpxEncoder { BitDepth = bits == 10 ? 10 : null, AllowBitDepthReduction = true });
    return bits;
}
```
<!-- endref -->

The header is reported in [ImageMetadata.CineonHeader](cref:Meziantou.Framework.Imaging.Metadata.ImageMetadata.CineonHeader): the version, the channel designators, the bits per
sample, the minimum and maximum code values and quantities of each channel, the chromaticities, the device, the KEYKODE
edge code, the frame position and rate. A field the file leaves undefined is `null`.

## Pitfalls

- Saving to `.cin` throws an `ArgumentException`: use `.dpx`.
- Images of two or four to eight channels, line or channel interleaving, signed samples, samples of more than 16 bits
  and bit-field packing of 10 or 12-bit samples are rejected with an
  [UnsupportedImageFeatureException](cref:Meziantou.Framework.Imaging.UnsupportedImageFeatureException).
- 8 and 16-bit samples are a stream of bytes and 16-bit words, as ImageMagick and OpenImageIO read them, and each line
  ends on a cell boundary, as ImageMagick writes it. OpenImageIO reads lines that do not end on a 32-bit word without
  that padding.
- The orientation is reported in [ImageMetadata.Orientation](cref:Meziantou.Framework.Imaging.Metadata.ImageMetadata.Orientation) and never applied. A negative image (image
  sense 1) is not inverted: [CineonHeader.IsNegative](cref:Meziantou.Framework.Imaging.Metadata.CineonHeader.IsNegative) says so.
