---
title: JPEG
description: Read every common JPEG file, write baseline, progressive, 12-bit, arithmetic-coded or lossless JPEG files, and handle what JPEG cannot store.
---

JPEG is the lossy format of photos. The library reads nearly every JPEG file, and writes baseline or progressive files
with [JpegEncoder](cref:Meziantou.Framework.Imaging.Formats.JpegEncoder), with 8-bit or 12-bit samples and Huffman or arithmetic coding, and lossless files. JPEG stores one opaque
picture: an animation, transparency or 16-bit samples in a lossy file need an explicit setting before they can be saved.

## What is read and written

| | Reading | Writing |
| --- | --- | --- |
| Processes | Baseline, extended sequential and progressive, Huffman or arithmetic coded; lossless, Huffman or arithmetic coded; hierarchical; JPEG-LS (ITU-T T.87) | Baseline (default), extended sequential, progressive and lossless, Huffman or arithmetic coded |
| Samples | 8 and 12 bits; 2 to 16 bits for lossless and JPEG-LS files | 8 and 12 bits; 2 to 16 bits for lossless files |
| Colors | Gray, YCbCr and RGB; CMYK and YCCK as CMYK pixels or with a color conversion | Gray (for gray pixel formats), YCbCr, RGB for lossless files, or CMYK (for the CMYK pixel formats, 8-bit baseline Huffman only) |
| Chroma subsampling | Any integral sampling | 4:4:4, 4:2:2 or 4:2:0 |
| Metadata | EXIF, XMP, ICC profile, resolution, comments | The same |
| Gain maps | Listed and rendered by [AuxiliaryImageCollection](cref:Meziantou.Framework.Imaging.AuxiliaryImageCollection) (Ultra HDR) | Ultra HDR, with [GainMap](cref:Meziantou.Framework.Imaging.Formats.JpegEncoder.GainMap) |
| Reduced size | 1/2, 1/4 or 1/8 of the size, except lossless, JPEG-LS and hierarchical files | - |
| Area | Any area, alone or at a reduced size | - |

8-bit files load as [Gray8](cref:Meziantou.Framework.Imaging.Gray8) or [Rgb24](cref:Meziantou.Framework.Imaging.Rgb24); 12-bit files, and lossless and JPEG-LS files of more than 8 bits, load as [Gray16](cref:Meziantou.Framework.Imaging.Gray16) or [Rgb48](cref:Meziantou.Framework.Imaging.Rgb48).
CMYK and YCCK files load as [Cmyk32](cref:Meziantou.Framework.Imaging.Cmyk32) or [Cmyk64](cref:Meziantou.Framework.Imaging.Cmyk64) when you ask for these pixel formats (see
[Color management](../09-Color/03-ColorManagement.md#keep-cmyk-images)), and a CMYK image is saved as an Adobe CMYK file.

[ImageDecodeOptions.TargetSize](cref:Meziantou.Framework.Imaging.ImageDecodeOptions.TargetSize) and [ImageDecodeOptions.ScaleDenominator](cref:Meziantou.Framework.Imaging.ImageDecodeOptions.ScaleDenominator) decode a file at a reduced size, from the
coefficients of its blocks: the pixels are the means of the pixels of the full image they replace, and a subsampled
chrominance is reduced less, so that it needs less upsampling. See
[Decode a smaller image](../05-LoadingAndSaving/02-LoadingImages.md#decode-a-smaller-image).

[ImageDecodeOptions.Region](cref:Meziantou.Framework.Imaging.ImageDecodeOptions.Region) decodes an area of a file, with the pixels of a full decode there: only the blocks the area
reads are transformed, the image holds the area only, and a sequential file stops being decoded after the last row of
blocks the area needs, so a viewport at the top of a large photo is cheap. Every block before that row is still read, as
the entropy-coded data have no index. See [Decode an area](../05-LoadingAndSaving/02-LoadingImages.md#decode-an-area).

## Save a JPEG

[Quality](cref:Meziantou.Framework.Imaging.Formats.JpegEncoder.Quality) sets the compression. A progressive file holds the same pixels as a baseline one, in several scans of increasing
precision, so a viewer can show the whole image before the end of a download:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_SaveProgressiveJpeg;format=md-fenced-code-block;dedent=true -->
```csharp
public static void SaveProgressiveJpeg(string sourcePath, string jpegPath)
{
    using var image = Image.Load<Rgb24>(sourcePath);
    image.Save(jpegPath, new JpegEncoder
    {
        Quality = 85,
        Process = JpegEncodingProcess.Progressive,
        MetadataHandling = MetadataHandling.DiscardUnsupported,
    });
}
```
<!-- endref -->

The Huffman tables of a progressive file are computed from its own data, which usually makes it smaller than the baseline
file. It keeps the coefficients of the whole image in memory until the last scan, where the baseline process only keeps
a few rows.

## Make a baseline file smaller

A baseline file uses the typical Huffman tables of the JPEG specification by default.
[OptimizeHuffmanTables](cref:Meziantou.Framework.Imaging.Formats.JpegEncoder.OptimizeHuffmanTables) computes them from the image instead: the file holds the same coefficients, so it
decodes to the same pixels, in fewer bytes. [RestartInterval](cref:Meziantou.Framework.Imaging.Formats.JpegEncoder.RestartInterval) writes a restart marker every given number of
MCUs, from which a decoder can resume after damaged data; here, one before each row of MCUs:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_SaveOptimizedJpeg;format=md-fenced-code-block;dedent=true -->
```csharp
public static void SaveOptimizedJpeg(string sourcePath, string jpegPath)
{
    using var image = Image.Load<Rgb24>(sourcePath);
    image.Save(jpegPath, new JpegEncoder
    {
        Quality = 85,
        ChromaSubsampling = JpegChromaSubsampling.Ratio420,
        OptimizeHuffmanTables = true,

        // An MCU of 4:2:0 output covers 16x16 pixels
        RestartInterval = (image.Width + 15) / 16,
    });
}
```
<!-- endref -->

- Optimized tables cost a second pass over the image, and the coefficients of the whole image stay in memory until the
  scan is written, as for a progressive file. The default tables keep encoding streamed by rows.
- An MCU is the block of pixels coded together: 8x8 pixels for a grayscale image or 4:4:4 output, 16x8 for 4:2:2 and
  16x16 for 4:2:0. The MCUs are counted from the start of each scan; a progressive scan of one component counts its
  8x8 blocks.
- Restart markers make the file slightly larger and do not change its pixels.
- A file written with either option can still be recompressed to JPEG XL with
  [JpegXlTranscoder](cref:Meziantou.Framework.Imaging.JpegXlTranscoder) and rebuilt byte for byte.

## Save a lossless, 12-bit or arithmetic-coded file

[JpegEncodingProcess.Lossless](cref:Meziantou.Framework.Imaging.Formats.JpegEncodingProcess.Lossless) writes the samples exactly: the file decodes to the pixels of the image, with 8 bits for
an 8-bit pixel format and 16 bits for a 16-bit one. Each sample is predicted from its decoded neighbors
([LosslessPredictor](cref:Meziantou.Framework.Imaging.Formats.JpegEncoder.LosslessPredictor), 1 to 7) and the difference is coded:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_SaveLosslessJpeg;format=md-fenced-code-block;dedent=true -->
```csharp
public static void SaveLosslessJpeg(string sourcePath, string jpegPath)
{
    using var image = Image.Load(sourcePath);
    image.Save(jpegPath, new JpegEncoder
    {
        Process = JpegEncodingProcess.Lossless,
        LosslessPredictor = 7,
    });
}
```
<!-- endref -->

- Color pixel formats are written as RGB components, without chroma subsampling; the JFIF segment, and so the JFIF
  resolution, is not written for them, since it would mean YCbCr.
- [BitDepth](cref:Meziantou.Framework.Imaging.Formats.JpegEncoder.BitDepth) stores 2 to 16 bits per sample: the samples are rounded to it, and a value below the depth of a
  16-bit pixel format is the consent to reduce it.
- A restart interval of a lossless file is a whole number of rows: [RestartInterval](cref:Meziantou.Framework.Imaging.Formats.JpegEncoder.RestartInterval) must be a multiple of the width.
- The file is several times larger than a lossy one. To store a photo losslessly in a smaller file, prefer PNG, WebP
  lossless or JPEG XL.

[BitDepth](cref:Meziantou.Framework.Imaging.Formats.JpegEncoder.BitDepth) set to 12 writes a lossy file of 12-bit samples, which keeps more of the precision of a 16-bit image,
and [EntropyCoding](cref:Meziantou.Framework.Imaging.Formats.JpegEncoder.EntropyCoding) set to [Arithmetic](cref:Meziantou.Framework.Imaging.Formats.JpegEntropyCoding.Arithmetic) codes the same coefficients in fewer bytes, so
the file decodes to the same pixels as the Huffman-coded one:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_SaveTwelveBitJpeg;format=md-fenced-code-block;dedent=true -->
```csharp
public static void SaveTwelveBitJpeg(string sourcePath, string jpegPath)
{
    using var image = Image.Load<Rgb48>(sourcePath);
    image.Save(jpegPath, new JpegEncoder
    {
        Quality = 90,
        BitDepth = 12,
        EntropyCoding = JpegEntropyCoding.Arithmetic,
    });
}
```
<!-- endref -->

- A 12-bit file uses the quantization tables of an 8-bit one, as libjpeg does, so its coefficients are quantized
  more finely relative to the range of the samples, and it is larger.
- The Huffman tables of a 12-bit or lossless file are always computed from the image, and arithmetic coding needs no
  table: as with [OptimizeHuffmanTables](cref:Meziantou.Framework.Imaging.Formats.JpegEncoder.OptimizeHuffmanTables), the coefficients of the whole image stay in memory until the
  scans are written.
- Web browsers and many other decoders do not read 12-bit, arithmetic-coded or lossless files: keep the default
  settings for files that are shared.

## Options

| Option | What it does | Default |
| --- | --- | --- |
| [Quality](cref:Meziantou.Framework.Imaging.Formats.JpegEncoder.Quality) | 1 (smallest) to 100 (best); 100 is still lossy | 90 |
| [Process](cref:Meziantou.Framework.Imaging.Formats.JpegEncoder.Process) | [Baseline](cref:Meziantou.Framework.Imaging.Formats.JpegEncodingProcess.Baseline), [Progressive](cref:Meziantou.Framework.Imaging.Formats.JpegEncodingProcess.Progressive) or [Lossless](cref:Meziantou.Framework.Imaging.Formats.JpegEncodingProcess.Lossless) | [Baseline](cref:Meziantou.Framework.Imaging.Formats.JpegEncodingProcess.Baseline) |
| [EntropyCoding](cref:Meziantou.Framework.Imaging.Formats.JpegEncoder.EntropyCoding) | [Huffman](cref:Meziantou.Framework.Imaging.Formats.JpegEntropyCoding.Huffman) or [Arithmetic](cref:Meziantou.Framework.Imaging.Formats.JpegEntropyCoding.Arithmetic) (smaller, same pixels, read by fewer decoders) | [Huffman](cref:Meziantou.Framework.Imaging.Formats.JpegEntropyCoding.Huffman) |
| [BitDepth](cref:Meziantou.Framework.Imaging.Formats.JpegEncoder.BitDepth) | The bits per sample: 8 or 12 for a lossy file, 2 to 16 for a lossless one | `null`: 8 bits, or the depth of the pixel format for a lossless file |
| [LosslessPredictor](cref:Meziantou.Framework.Imaging.Formats.JpegEncoder.LosslessPredictor) | The predictor of a lossless file, 1 to 7 | 1 |
| [ChromaSubsampling](cref:Meziantou.Framework.Imaging.Formats.JpegEncoder.ChromaSubsampling) | [Auto](cref:Meziantou.Framework.Imaging.Formats.JpegChromaSubsampling.Auto) (4:2:0 for color images), [Ratio444](cref:Meziantou.Framework.Imaging.Formats.JpegChromaSubsampling.Ratio444), [Ratio422](cref:Meziantou.Framework.Imaging.Formats.JpegChromaSubsampling.Ratio422) or [Ratio420](cref:Meziantou.Framework.Imaging.Formats.JpegChromaSubsampling.Ratio420); ignored for gray pixel formats | [Auto](cref:Meziantou.Framework.Imaging.Formats.JpegChromaSubsampling.Auto) |
| [BackgroundColor](cref:Meziantou.Framework.Imaging.Formats.JpegEncoder.BackgroundColor) | The opaque color that pixels that are not opaque are composited over; `null` rejects them | `null` |
| [OptimizeHuffmanTables](cref:Meziantou.Framework.Imaging.Formats.JpegEncoder.OptimizeHuffmanTables) | Computes the Huffman tables of a baseline file from the image: smaller, same pixels, slower; no effect on progressive files, whose tables are always computed | `false` |
| [RestartInterval](cref:Meziantou.Framework.Imaging.Formats.JpegEncoder.RestartInterval) | The number of MCUs between two restart markers, 0 to 65,535; 0 writes none | 0 |
| [AllowBitDepthReduction](cref:Meziantou.Framework.Imaging.Formats.JpegEncoder.AllowBitDepthReduction) | Lets 16-bit pixel formats be rounded to 8 bits when [BitDepth](cref:Meziantou.Framework.Imaging.Formats.JpegEncoder.BitDepth) is not set | `false` |
| [GainMap](cref:Meziantou.Framework.Imaging.Formats.JpegEncoder.GainMap) | Writes a gain map next to a still image, as an Ultra HDR file: the gain map image follows the image, with its parameters (see [Color management](../09-Color/03-ColorManagement.md)); 8-bit Huffman-coded output only | `null` |
| [MetadataHandling](cref:Meziantou.Framework.Imaging.Formats.MetadataHandling) | What to do with metadata JPEG cannot store | [Strict](cref:Meziantou.Framework.Imaging.Formats.MetadataHandling.Strict) |

The component layout follows the pixel format, never the pixel values: a gray pixel format writes one grayscale
component, every other pixel format YCbCr (RGB in a lossless file), even when its pixels are gray.

## Save one frame of an animation

JPEG stores one frame. Saving an animation, or an image with a poster frame, throws an [UnsupportedImageFeatureException](cref:Meziantou.Framework.Imaging.UnsupportedImageFeatureException)
before anything is written: save the frame you want with `Frames[index].Save`, and give the settings for what JPEG cannot store:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_ExportJpegFrame;format=md-fenced-code-block;dedent=true -->
```csharp
public static void ExportFrameAsJpeg(string animatedPath, int frameIndex, string jpegPath)
{
    using var animation = Image.Load(animatedPath);

    // The frame is saved as a still image, without copying its pixels
    animation.Frames[frameIndex].Save(jpegPath, new JpegEncoder
    {
        Quality = 85,
        BackgroundColor = new Rgba32(255, 255, 255),
        AllowBitDepthReduction = true,
        MetadataHandling = MetadataHandling.DiscardUnsupported,
    });
}
```
<!-- endref -->

- [BackgroundColor](cref:Meziantou.Framework.Imaging.Formats.JpegEncoder.BackgroundColor) is only needed when a pixel is not opaque: a GIF or PNG with transparency needs it, an opaque image
  never does.
- [AllowBitDepthReduction](cref:Meziantou.Framework.Imaging.Formats.JpegEncoder.AllowBitDepthReduction) is needed for [Rgba64](cref:Meziantou.Framework.Imaging.Rgba64) and [Gray16](cref:Meziantou.Framework.Imaging.Gray16) images, from a 16-bit PNG for instance, unless a
  lossless file or [BitDepth](cref:Meziantou.Framework.Imaging.Formats.JpegEncoder.BitDepth) keeps their bits.
- [MetadataHandling.DiscardUnsupported](cref:Meziantou.Framework.Imaging.Formats.MetadataHandling.DiscardUnsupported) drops the metadata JPEG cannot store, such as PNG text entries other than
  comments.

## Pitfalls

- CMYK and YCCK files (from print workflows) load as [Cmyk32](cref:Meziantou.Framework.Imaging.Cmyk32) or [Cmyk64](cref:Meziantou.Framework.Imaging.Cmyk64) pixels with
  `Image.Load<Cmyk32>`, or as RGB with [ImageDecodeOptions.CmykConversion](cref:Meziantou.Framework.Imaging.ImageDecodeOptions.CmykConversion), which converts them with ICC profiles; an untyped
  load without the option throws an [UnsupportedImageFeatureException](cref:Meziantou.Framework.Imaging.UnsupportedImageFeatureException). See
  [Color management](../09-Color/03-ColorManagement.md).
- CMYK output has four components without subsampling ([ChromaSubsampling](cref:Meziantou.Framework.Imaging.Formats.JpegEncoder.ChromaSubsampling) plays no role), the luminance
  quantization table for all of them, and is 8-bit, Huffman-coded baseline only: [JpegEncodingProcess.Progressive](cref:Meziantou.Framework.Imaging.Formats.JpegEncodingProcess.Progressive), [JpegEncodingProcess.Lossless](cref:Meziantou.Framework.Imaging.Formats.JpegEncodingProcess.Lossless), arithmetic coding and 12-bit samples throw. Its coefficients
  are kept in memory until the file is written (8 bytes per pixel). [Cmyk64](cref:Meziantou.Framework.Imaging.Cmyk64) images need
  [AllowBitDepthReduction](cref:Meziantou.Framework.Imaging.Formats.JpegEncoder.AllowBitDepthReduction).
- Hierarchical files are decoded frame by frame, each differential frame added to the previous one expanded, in
  integers as ITU-T T.81 describes it. The ISO reference decoder keeps fractional bits between frames, so the pixels
  can differ from its own by a few units; for the same reason, a lossless frame after a DCT-based frame restores the
  original samples only with the inverse DCT of the encoder (T.81 does not define the inverse DCT exactly). A frame
  that does not code every component is rejected with an
  [UnsupportedImageFeatureException](cref:Meziantou.Framework.Imaging.UnsupportedImageFeatureException).
- JPEG-LS files are read in every interleave mode, lossless or near-lossless, with preset coding parameters,
  restart intervals and the HP color transforms that CharLS writes. Mapping tables (palettes), the point transform
  and the extensions of ITU-T T.870 are rejected with an [UnsupportedImageFeatureException](cref:Meziantou.Framework.Imaging.UnsupportedImageFeatureException). The three components of a
  JPEG-LS file are RGB samples unless an Adobe segment says otherwise.
- The EXIF orientation is reported in `Metadata.Orientation`, never applied: call [AutoOrient](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.AutoOrient) to rotate the pixels. See
  [Metadata and orientation](../09-Color/02-MetadataAndOrientation.md).
- An EXIF block is limited to 65,527 bytes and an XMP packet to 65,504 bytes; extended XMP is not supported.
- The encoder writes no hierarchical, JPEG-LS or YCCK files, and RGB components only in lossless files.
- To store a JPEG file in a smaller file without any loss, recompress it to JPEG XL with [JpegXlTranscoder](cref:Meziantou.Framework.Imaging.JpegXlTranscoder): the original
  file can be rebuilt byte for byte. See [JPEG XL](15-JpegXl.md).
- A progressive file can be shown while it is read, with [ReadFrameRefinementInto](cref:Meziantou.Framework.Imaging.ImageReader{TPixel}.ReadFrameRefinementInto). See
  [Show an image while it downloads](../05-LoadingAndSaving/06-ShowImagesWhileTheyDownload.md).
- A file without its end-of-image marker, or whose restart marker has a garbled number, is rejected by default. With
  [lenient decoding](../05-LoadingAndSaving/08-ErrorHandling.md#accept-the-files-browsers-accept), it loads with the
  pixels the reference decoders give: the end-of-image marker may be missing once every scan of the picture is
  complete, and a restart marker numbered 3 to 5 after the expected one is read as the expected one in a sequential or
  lossless file. A file cut inside the picture is still rejected, since its missing pixels would have to be guessed.
