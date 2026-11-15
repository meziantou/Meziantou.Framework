---
title: DPX
description: Read and write DPX (SMPTE ST 268) film scans and video frames, and read their headers.
---

DPX files (`.dpx`, SMPTE ST 268) hold the frames of film scans and of video in post-production and visual effects: 10-bit
logarithmic printing densities, linear or video RGB, luma, or Cb, Y, Cr samples, with a time code and the edge code of
the film. Files load at the precision of their components, and [DpxEncoder](cref:Meziantou.Framework.Imaging.Formats.DpxEncoder) writes them.

## What is read and written

| | Reading | Writing |
| --- | --- | --- |
| Components | R, G, B, A or Y alone; RGB, RGBA and ABGR; Cb, Y, Cr 4:4:4 and 4:2:2, with or without alpha (converted to RGB) | RGB (opaque images), RGBA, or Y for gray pixel formats |
| Bit depths | 1, 8, 10, 12 and 16-bit integers, 32 and 64-bit floating point | 8, 10, 12 and 16-bit integers, 32-bit floating point |
| Packing | Filled with method A or B, and packed, for 10 and 12 bits; both byte orders | 10 and 12 bits filled with method A; big-endian (default) or little-endian |
| Image elements | The first one; the others are counted | One |
| Headers | File, image, orientation, film and television headers, reported | The orientation, the pixel aspect ratio, the transfer and the colorimetry |

The pixels are the code values the file stores: a 10-bit code value `v` is the 16-bit sample
`round(v × 65535 / 1023)` of an [Rgb48](cref:Meziantou.Framework.Imaging.Rgb48) image, 8 and 16-bit components keep their value, and floating-point
components load as [RgbaFloat](cref:Meziantou.Framework.Imaging.RgbaFloat), or [GrayFloat](cref:Meziantou.Framework.Imaging.GrayFloat) for a luma (or single-color) element and [LaFloat](cref:Meziantou.Framework.Imaging.LaFloat) for
an alpha element. [GrayFloat](cref:Meziantou.Framework.Imaging.GrayFloat) and [GrayHalf](cref:Meziantou.Framework.Imaging.GrayHalf) images are written as floating-point luma elements. Nothing converts
them to linear light.

## Load and save a DPX file

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_Dpx;format=md-fenced-code-block;dedent=true -->
```csharp
public static (string? TimeCode, DpxTransferCharacteristic Transfer) ConvertDpx(string dpxPath, string outputPath)
{
    // 10-bit components load as Rgb48: each code value v is the 16-bit sample round(v × 65535 / 1023)
    using var scan = Image.Load(dpxPath);

    // The headers are reported: the time code, the film edge code, the frame rate, and how the code values encode
    // light (printing densities are logarithmic: they are never converted implicitly)
    var header = scan.Metadata.DpxHeader!;

    // 10-bit components filled to 32-bit words with method A, big-endian; the orientation and the linear or BT.709
    // labels are written too
    scan.Save(outputPath, new DpxEncoder { BitDepth = 10, AllowBitDepthReduction = true });
    return (header.TimeCode, header.Transfer);
}
```
<!-- endref -->

The headers are reported in [ImageMetadata.DpxHeader](cref:Meziantou.Framework.Imaging.Metadata.ImageMetadata.DpxHeader): the version, the descriptor, the bit depth and the
packing, the transfer characteristic and the colorimetric specification, the reference code values, the creator and the
project, the film edge code, the frame position and rate, the slate, and the SMPTE time code as `hh:mm:ss:ff`. A field
the file leaves undefined is `null`.

The transfer characteristic is a label. A linear element is labeled
[ColorTransferFunction.Linear](cref:Meziantou.Framework.Imaging.Metadata.ColorTransferFunction.Linear), and the BT.709 and BT.601 television transfers and colorimetries with their
CICP description. Printing densities (the logarithmic encoding of film scans) and the other transfers have no label:
[DpxHeader.Transfer](cref:Meziantou.Framework.Imaging.Metadata.DpxHeader.Transfer) says what the code values are, and converting them to linear light is up to
your color pipeline.

## Options

| Option | What it does | Default |
| --- | --- | --- |
| [BitDepth](cref:Meziantou.Framework.Imaging.Formats.DpxEncoder.BitDepth) | 8, 10, 12, 16 (integers) or 32 (floating point) | `null`: 8 bits for 8-bit pixel formats, 16 bits for 16-bit ones, 32 for floating-point ones |
| [AllowBitDepthReduction](cref:Meziantou.Framework.Imaging.Formats.DpxEncoder.AllowBitDepthReduction) | Allows 16-bit pixels to be written with 8, 10 or 12 bits | `false` |
| [ByteOrder](cref:Meziantou.Framework.Imaging.Formats.DpxEncoder.ByteOrder) | [BigEndian](cref:Meziantou.Framework.Imaging.Formats.DpxByteOrder.BigEndian) (`SDPX`) or [LittleEndian](cref:Meziantou.Framework.Imaging.Formats.DpxByteOrder.LittleEndian) (`XPDS`) | [BigEndian](cref:Meziantou.Framework.Imaging.Formats.DpxByteOrder.BigEndian) |
| [MetadataHandling](cref:Meziantou.Framework.Imaging.Formats.MetadataHandling) | What to do with metadata DPX cannot store | [Strict](cref:Meziantou.Framework.Imaging.Formats.MetadataHandling.Strict) |

The encoder writes the transfer and the colorimetry from the metadata: linear light as the linear transfer (2), the CICP
descriptions of BT.709 and of the two BT.601 systems as their codes, and the other images as user defined (0) with the
primaries of BT.709.

## Pitfalls

- Run-length encoded DPX files are rejected with an [UnsupportedImageFeatureException](cref:Meziantou.Framework.Imaging.UnsupportedImageFeatureException): no other reader reads them.
  So are encrypted files, signed components, and the depth, composite and user-defined descriptors.
- Every line starts on a 32-bit word, as the standard says and FFmpeg writes. OpenImageIO, and ImageMagick for 12 and
  16-bit components, write the lines of an odd width without that padding: such files decode to shifted lines.
- 1-bit components start at the least significant bit of each word (SMPTE ST 268, Table 3B). ImageMagick reads and
  writes them from the most significant bit.
- 64-bit components load in single precision when it holds them exactly; a file whose values need double precision is
  rejected rather than rounded.
- The orientation is reported in [ImageMetadata.Orientation](cref:Meziantou.Framework.Imaging.Metadata.ImageMetadata.Orientation) and never applied. Call
  [AutoOrient](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.AutoOrient) to turn the pixels upright.
- Only the first image element is decoded: an alpha element stored after the color element is ignored, and
  [DpxHeader.ImageElementCount](cref:Meziantou.Framework.Imaging.Metadata.DpxHeader.ImageElementCount) says how many the file has.
- The encoder ignores [DpxHeader](cref:Meziantou.Framework.Imaging.Metadata.DpxHeader): the time code, the edge code and the other fields of a source DPX file are
  not written again.
