---
title: JPEG
description: Read every common JPEG file, write baseline or progressive JPEG files, and handle what JPEG cannot store.
---

JPEG is the lossy format of photos. The library reads nearly every JPEG file, and writes 8-bit baseline or progressive
files with [JpegEncoder](cref:Meziantou.Framework.Imaging.Formats.JpegEncoder). JPEG stores one opaque picture with 8-bit samples: an animation, transparency or 16-bit samples
need an explicit setting before they can be saved.

## What is read and written

| | Reading | Writing |
| --- | --- | --- |
| Processes | Baseline, extended sequential and progressive, Huffman or arithmetic coded; lossless (Huffman coded) | Baseline (default) or progressive, Huffman coded |
| Samples | 8 and 12 bits; 2 to 16 bits for lossless files | 8 bits |
| Colors | Gray, YCbCr and RGB; CMYK and YCCK with a color conversion | Gray (for gray pixel formats) or YCbCr |
| Chroma subsampling | Any integral sampling | 4:4:4, 4:2:2 or 4:2:0 |
| Metadata | EXIF, XMP, ICC profile, resolution, comments | The same |
| Reduced size | 1/2, 1/4 or 1/8 of the size, except lossless files | - |

8-bit files load as [Gray8](cref:Meziantou.Framework.Imaging.Gray8) or [Rgb24](cref:Meziantou.Framework.Imaging.Rgb24); 12-bit and lossless files of more than 8 bits load as [Gray16](cref:Meziantou.Framework.Imaging.Gray16) or [Rgb48](cref:Meziantou.Framework.Imaging.Rgb48).

[ImageDecodeOptions.TargetSize](cref:Meziantou.Framework.Imaging.ImageDecodeOptions.TargetSize) and [ImageDecodeOptions.ScaleDenominator](cref:Meziantou.Framework.Imaging.ImageDecodeOptions.ScaleDenominator) decode a file at a reduced size, from the
coefficients of its blocks: the pixels are the means of the pixels of the full image they replace, and a subsampled
chrominance is reduced less, so that it needs less upsampling. See
[Decode a smaller image](../05-LoadingAndSaving/02-LoadingImages.md#decode-a-smaller-image).

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

## Options

| Option | What it does | Default |
| --- | --- | --- |
| [Quality](cref:Meziantou.Framework.Imaging.Formats.JpegEncoder.Quality) | 1 (smallest) to 100 (best); 100 is still lossy | 90 |
| [Process](cref:Meziantou.Framework.Imaging.Formats.JpegEncoder.Process) | [Baseline](cref:Meziantou.Framework.Imaging.Formats.JpegEncodingProcess.Baseline) or [Progressive](cref:Meziantou.Framework.Imaging.Formats.JpegEncodingProcess.Progressive) | [Baseline](cref:Meziantou.Framework.Imaging.Formats.JpegEncodingProcess.Baseline) |
| [ChromaSubsampling](cref:Meziantou.Framework.Imaging.Formats.JpegEncoder.ChromaSubsampling) | [Auto](cref:Meziantou.Framework.Imaging.Formats.JpegChromaSubsampling.Auto) (4:2:0 for color images), [Ratio444](cref:Meziantou.Framework.Imaging.Formats.JpegChromaSubsampling.Ratio444), [Ratio422](cref:Meziantou.Framework.Imaging.Formats.JpegChromaSubsampling.Ratio422) or [Ratio420](cref:Meziantou.Framework.Imaging.Formats.JpegChromaSubsampling.Ratio420); ignored for gray pixel formats | [Auto](cref:Meziantou.Framework.Imaging.Formats.JpegChromaSubsampling.Auto) |
| [BackgroundColor](cref:Meziantou.Framework.Imaging.Formats.JpegEncoder.BackgroundColor) | The opaque color that pixels that are not opaque are composited over; `null` rejects them | `null` |
| [AllowBitDepthReduction](cref:Meziantou.Framework.Imaging.Formats.JpegEncoder.AllowBitDepthReduction) | Lets 16-bit pixel formats be rounded to 8 bits | `false` |
| [MetadataHandling](cref:Meziantou.Framework.Imaging.Formats.MetadataHandling) | What to do with metadata JPEG cannot store | [Strict](cref:Meziantou.Framework.Imaging.Formats.MetadataHandling.Strict) |

The component layout follows the pixel format, never the pixel values: a gray pixel format writes one grayscale
component, every other pixel format YCbCr, even when its pixels are gray.

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
- [AllowBitDepthReduction](cref:Meziantou.Framework.Imaging.Formats.JpegEncoder.AllowBitDepthReduction) is needed for [Rgba64](cref:Meziantou.Framework.Imaging.Rgba64) and [Gray16](cref:Meziantou.Framework.Imaging.Gray16) images, from a 16-bit PNG for instance.
- [MetadataHandling.DiscardUnsupported](cref:Meziantou.Framework.Imaging.Formats.MetadataHandling.DiscardUnsupported) drops the metadata JPEG cannot store, such as PNG text entries other than
  comments.

## Pitfalls

- CMYK and YCCK files (from print workflows) are recognized but load only with [ImageDecodeOptions.CmykConversion](cref:Meziantou.Framework.Imaging.ImageDecodeOptions.CmykConversion),
  which converts them to RGB with ICC profiles; otherwise loading throws an [UnsupportedImageFeatureException](cref:Meziantou.Framework.Imaging.UnsupportedImageFeatureException). See
  [Color management](../09-Color/03-ColorManagement.md).
- Hierarchical files, lossless files with arithmetic coding and JPEG-LS files are rejected with an
  [UnsupportedImageFeatureException](cref:Meziantou.Framework.Imaging.UnsupportedImageFeatureException), also by [Image.Identify](cref:Meziantou.Framework.Imaging.Image.Identify).
- The EXIF orientation is reported in `Metadata.Orientation`, never applied: call [AutoOrient](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.AutoOrient) to rotate the pixels. See
  [Metadata and orientation](../09-Color/02-MetadataAndOrientation.md).
- An EXIF block is limited to 65,527 bytes and an XMP packet to 65,504 bytes; extended XMP is not supported.
- The encoder writes no restart markers and no arithmetic-coded, lossless, 12-bit, RGB or CMYK files.
- To store a JPEG file in a smaller file without any loss, recompress it to JPEG XL with [JpegXlTranscoder](cref:Meziantou.Framework.Imaging.JpegXlTranscoder): the original
  file can be rebuilt byte for byte. See [JPEG XL](15-JpegXl.md).
- A progressive file can be shown while it is read, with [ReadFrameRefinementInto](cref:Meziantou.Framework.Imaging.ImageReader{TPixel}.ReadFrameRefinementInto). See
  [Show an image while it downloads](../05-LoadingAndSaving/06-ShowImagesWhileTheyDownload.md).
- A file without its end-of-image marker, or whose restart marker has a garbled number, is rejected by default. With
  [lenient decoding](../05-LoadingAndSaving/08-ErrorHandling.md#accept-the-files-browsers-accept), it loads with the
  pixels the reference decoders give: the end-of-image marker may be missing once every scan of the picture is
  complete, and a restart marker numbered 3 to 5 after the expected one is read as the expected one in a sequential or
  lossless file. A file cut inside the picture is still rejected, since its missing pixels would have to be guessed.
