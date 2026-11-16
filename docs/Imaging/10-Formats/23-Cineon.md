---
title: Cineon
description: Read and write Kodak Cineon film scans, the predecessor of DPX.
---

Kodak Cineon files (`.cin`) hold film scans, almost always three channels of 10-bit printing densities. DPX succeeded
the format. The library reads Cineon files, and writes them with
[CineonEncoder](cref:Meziantou.Framework.Imaging.Formats.CineonEncoder).

## What is read and written

| | Reading | Writing |
| --- | --- | --- |
| Channels | One (gray) or three (RGB, placed by their red, green and blue designators), of the same size and depth | One (gray) or three (RGB) |
| Samples | 1 to 16 bits, in cells of 8, 16 or 32 bits, left or right justified, one pixel per cell or shared; both byte orders | 8, 10 or 16 bits, packing 5 (32-bit cells, left justified), big-endian |
| Header | The channel designators and ranges, the chromaticities, the origination and the motion-picture sections, reported | Copied from the header of the image |

The pixels are the code values the file stores: a 10-bit code value `v` is the 16-bit sample `round(v × 65535 / 1023)`
of an [Rgb48](cref:Meziantou.Framework.Imaging.Rgb48) image. Printing densities are logarithmic and are never converted to linear light.

## Load a Cineon file and save it as DPX

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_Cineon;format=md-fenced-code-block;dedent=true -->
```csharp
public static int ConvertCineonToDpx(string cinPath, string dpxPath)
{
    // The 10-bit printing densities of a Cineon file load as Rgb48, as stored
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

## Save a Cineon file

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_CineonSave;format=md-fenced-code-block;dedent=true -->
```csharp
public static void SaveCineon(string cinPath, string outputPath)
{
    // The header of a loaded Cineon file (the film, the scanner, the designators and ranges of the channels) is copied
    using var scan = Image.Load(cinPath);

    // 10-bit code values by default for 8-bit pixels, packed one RGB pixel per 32-bit word (packing 5): 16-bit pixels are
    // written with 16 bits unless the reduction is allowed
    scan.Save(outputPath, new CineonEncoder { BitDepth = 10, AllowBitDepthReduction = true });
}
```
<!-- endref -->

A 10-bit RGB pixel is one 32-bit word, red in its 10 most significant bits; 10-bit gray samples share the words, three
per word. 8 and 16-bit samples are a stream of bytes or 16-bit words, every line ending on a 32-bit word, as ImageMagick
writes them. The code values are written as they are: the encoder converts nothing.

| Option | What it does | Default |
| --- | --- | --- |
| [BitDepth](cref:Meziantou.Framework.Imaging.Formats.CineonEncoder.BitDepth) | 8, 10 or 16 bits per sample: a sample `v` becomes the code value `round(v × (2^n - 1) / max)` | 10 bits for 8-bit pixels, which keeps their values; 16 bits for 16-bit pixels |
| [AllowBitDepthReduction](cref:Meziantou.Framework.Imaging.Formats.CineonEncoder.AllowBitDepthReduction) | Allows writing 16-bit pixels with fewer bits, which rounds them | `false` |

The fields of [ImageMetadata.CineonHeader](cref:Meziantou.Framework.Imaging.Metadata.ImageMetadata.CineonHeader) are copied:
the film, the scanner, the dates and names, the image sense, and the designators and ranges of the channels when the
image has as many channels and the header as many bits (otherwise the printing densities of the universal metric, with
the full range of the code values). The orientation is written as its code. Cineon has no alpha: an image with a pixel
that is not fully opaque is rejected, flatten it first.

## Pitfalls

- OpenImageIO and ImageMagick read the files the encoder writes. ImageMagick converts Cineon files from logarithmic
  densities unless the colorspace is set after reading (`-set colorspace sRGB`, or `Gray` for one channel), and reads
  the three samples of a 10-bit gray word in the reverse order. FFmpeg has no Kodak Cineon decoder.
- Images of two or four to eight channels, line or channel interleaving, signed samples, samples of more than 16 bits
  and bit-field packing of 10 or 12-bit samples are rejected with an
  [UnsupportedImageFeatureException](cref:Meziantou.Framework.Imaging.UnsupportedImageFeatureException).
- 8 and 16-bit samples are a stream of bytes and 16-bit words, as ImageMagick and OpenImageIO read them, and each line
  ends on a cell boundary, as ImageMagick writes it. OpenImageIO reads lines that do not end on a 32-bit word without
  that padding.
- The orientation is reported in [ImageMetadata.Orientation](cref:Meziantou.Framework.Imaging.Metadata.ImageMetadata.Orientation) and never applied. A negative image (image
  sense 1) is not inverted: [CineonHeader.IsNegative](cref:Meziantou.Framework.Imaging.Metadata.CineonHeader.IsNegative) says so.
