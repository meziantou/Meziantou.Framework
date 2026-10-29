---
title: JPEG
description: Read every common JPEG file, write baseline or progressive JPEG files, and handle what JPEG cannot store.
---

JPEG is the lossy format of photos. The library reads nearly every JPEG file, and writes 8-bit baseline or progressive
files with `JpegEncoder`. JPEG stores one opaque picture with 8-bit samples: an animation, transparency or 16-bit samples
need an explicit setting before they can be saved.

## What is read and written

| | Reading | Writing |
| --- | --- | --- |
| Processes | Baseline, extended sequential and progressive, Huffman or arithmetic coded; lossless (Huffman coded) | Baseline (default) or progressive, Huffman coded |
| Samples | 8 and 12 bits; 2 to 16 bits for lossless files | 8 bits |
| Colors | Gray, YCbCr and RGB; CMYK and YCCK with a color conversion | Gray (for gray pixel formats) or YCbCr |
| Chroma subsampling | Any integral sampling | 4:4:4, 4:2:2 or 4:2:0 |
| Metadata | EXIF, XMP, ICC profile, resolution, comments | The same |

8-bit files load as `Gray8` or `Rgb24`; 12-bit and lossless files of more than 8 bits load as `Gray16` or `Rgb48`.

## Save a JPEG

`Quality` sets the compression. A progressive file holds the same pixels as a baseline one, in several scans of increasing
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
| `Quality` | 1 (smallest) to 100 (best); 100 is still lossy | 90 |
| `Process` | `Baseline` or `Progressive` | `Baseline` |
| `ChromaSubsampling` | `Auto` (4:2:0 for color images), `Ratio444`, `Ratio422` or `Ratio420`; ignored for gray pixel formats | `Auto` |
| `BackgroundColor` | The opaque color that pixels that are not opaque are composited over; `null` rejects them | `null` |
| `AllowBitDepthReduction` | Lets 16-bit pixel formats be rounded to 8 bits | `false` |
| `MetadataHandling` | What to do with metadata JPEG cannot store | `Strict` |

The component layout follows the pixel format, never the pixel values: a gray pixel format writes one grayscale
component, every other pixel format YCbCr, even when its pixels are gray.

## Save one frame of an animation

JPEG stores one frame. Saving an animation, or an image with a poster frame, throws an `UnsupportedImageFeatureException`
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

- `BackgroundColor` is only needed when a pixel is not opaque: a GIF or PNG with transparency needs it, an opaque image
  never does.
- `AllowBitDepthReduction` is needed for `Rgba64` and `Gray16` images, from a 16-bit PNG for instance.
- `MetadataHandling.DiscardUnsupported` drops the metadata JPEG cannot store, such as PNG text entries other than
  comments.

## Pitfalls

- CMYK and YCCK files (from print workflows) are recognized but load only with `ImageDecodeOptions.CmykConversion`,
  which converts them to RGB with ICC profiles; otherwise loading throws an `UnsupportedImageFeatureException`. See
  [Color management](../09-Color/03-ColorManagement.md).
- Hierarchical files, lossless files with arithmetic coding and JPEG-LS files are rejected with an
  `UnsupportedImageFeatureException`, also by `Image.Identify`.
- The EXIF orientation is reported in `Metadata.Orientation`, never applied: call `AutoOrient` to rotate the pixels. See
  [Metadata and orientation](../09-Color/02-MetadataAndOrientation.md).
- An EXIF block is limited to 65,527 bytes and an XMP packet to 65,504 bytes; extended XMP is not supported.
- The encoder writes no restart markers and no arithmetic-coded, lossless, 12-bit, RGB or CMYK files.
- To store a JPEG file in a smaller file without any loss, recompress it to JPEG XL with `JpegXlTranscoder`: the original
  file can be rebuilt byte for byte. See [JPEG XL](15-JpegXl.md).
- A progressive file can be shown while it is read, with `ReadFrameRefinementInto`. See
  [Show an image while it downloads](../05-LoadingAndSaving/06-ShowImagesWhileTheyDownload.md).
