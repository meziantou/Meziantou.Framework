---
title: WebP
description: Read and write lossless and lossy WebP images and animations, and choose between the two compressions.
---

WebP stores 8-bit RGB images with alpha, lossless or lossy, still or animated. [WebPEncoder](cref:Meziantou.Framework.Imaging.Formats.WebPEncoder) writes both compressions;
**its default is lossless**, which keeps every sample but makes large files from photos.

## What is read and written

| | Reading | Writing |
| --- | --- | --- |
| Compression | Lossless and lossy (VP8), mixed in one animation | Lossless (default) or lossy, for every frame |
| Pixels | 8-bit RGB, with alpha | 8-bit RGB, with alpha; gray images are stored as RGB |
| Animations | Composited into full frames with exact millisecond durations and the play count | Full frames, durations in milliseconds, play count |
| Metadata | ICC profile, EXIF, XMP | The same (RGB ICC profiles only) |

Still images without alpha load as [Rgb24](cref:Meziantou.Framework.Imaging.Rgb24); the others, and every animation, as [Rgba32](cref:Meziantou.Framework.Imaging.Rgba32).

## Save a WebP file

Choose lossy compression for photos, and lossless compression for graphics, screenshots and animations that must keep
every color:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_SaveWebp;format=md-fenced-code-block;dedent=true -->
```csharp
public static void SaveWebP(string photoPath, string webpPath, string animatedPath, string animatedWebPPath)
{
    using (var photo = Image.Load(photoPath))
    {
        photo.Save(webpPath, new WebPEncoder
        {
            Compression = WebPCompression.Lossy,
            Quality = 80, // 0-100: size versus fidelity
            Effort = 6,   // 0-9: encoding time versus size
            AllowBitDepthReduction = true, // 16-bit sources are written with 8 bits
        });
    }

    // Every sample is kept, including the colors of fully transparent pixels. An animation needs a seekable
    // destination (the file size is patched at the end): a path, a FileStream or a MemoryStream.
    using var animation = Image.Load(animatedPath);
    animation.Save(animatedWebPPath, new WebPEncoder { Compression = WebPCompression.Lossless });
}
```
<!-- endref -->

Lossy output uses 4:2:0 chroma subsampling, and keeps alpha losslessly. Lossless output keeps every 8-bit sample,
including the colors of fully transparent pixels.

At effort 2 and above, lossy encoding tries 4x4 luma prediction. If the modes exceed the VP8 first-partition limit
(524,287 bytes), it retries with 16x16 prediction only at the same quantizer. That can change the decoded quality and
file size; [Quality](cref:Meziantou.Framework.Imaging.Formats.WebPEncoder.Quality) is not a target distortion guarantee. If the partition still does not fit, encoding throws
[UnsupportedImageFeatureException](cref:Meziantou.Framework.Imaging.UnsupportedImageFeatureException): use lossless output or reduce the image dimensions.

The managed encoder makes its own prediction and entropy-coding decisions: lossless output uses one prefix-code group
(no spatial entropy image), and lossy output uses one quantizer for the frame and one token partition. It is not libwebp.
There is no general
guarantee of comparable file size, encoding speed or perceptual quality, and equal quality/effort numbers do not
establish a matched comparison. Compare representative images at matched decoded quality for lossy output.

Lossless encoding works on one frame at a time. Its working memory counts against [MaxLiveAllocationBytes](cref:Meziantou.Framework.Imaging.ImageResourceLimits.MaxLiveAllocationBytes): a copy of
the frame (4 bytes per pixel), the hash chains of the LZ77 parse (4 bytes per pixel, released before the output is
written), the recorded parse (at most 2 bytes per pixel) and the compressed stream, whose buffer doubles as it grows.
That is about 10 bytes per pixel, more for images that hardly compress. Effort 9 adds a second copy of the frame and
keeps two streams to pick the smaller one. A 16,384 x 16,384 image needs more than the default 512 MiB: raise the limit
to encode images of that size.

## Options

| Option | What it does | Default |
| --- | --- | --- |
| [Compression](cref:Meziantou.Framework.Imaging.Formats.WebPEncoder.Compression) | [Lossless](cref:Meziantou.Framework.Imaging.Formats.WebPCompression.Lossless) or [Lossy](cref:Meziantou.Framework.Imaging.Formats.WebPCompression.Lossy) | [Lossless](cref:Meziantou.Framework.Imaging.Formats.WebPCompression.Lossless) |
| [Quality](cref:Meziantou.Framework.Imaging.Formats.WebPEncoder.Quality) | 0 (smallest) to 100 (best) for lossy output; 100 is still lossy. Ignored by lossless output | 75 |
| [Effort](cref:Meziantou.Framework.Imaging.Formats.WebPEncoder.Effort) | 0 (fastest, largest) to 9 (slowest, smallest): trades encoding time for size, never changes lossless pixels | 5 |
| [ClearTransparentColors](cref:Meziantou.Framework.Imaging.Formats.WebPEncoder.ClearTransparentColors) | Replaces the color of fully transparent pixels with transparent black, which usually makes the file smaller | `false` |
| [AllowBitDepthReduction](cref:Meziantou.Framework.Imaging.Formats.WebPEncoder.AllowBitDepthReduction) | Lets 16-bit pixel formats be rounded to 8 bits | `false` |
| [DurationRounding](cref:Meziantou.Framework.Imaging.Formats.WebPEncoder.DurationRounding) | [RoundToNearest](cref:Meziantou.Framework.Imaging.Formats.FrameDurationRounding.RoundToNearest) rounds durations to the nearest millisecond; [RequireExact](cref:Meziantou.Framework.Imaging.Formats.FrameDurationRounding.RequireExact) throws | [RoundToNearest](cref:Meziantou.Framework.Imaging.Formats.FrameDurationRounding.RoundToNearest) |
| [MetadataHandling](cref:Meziantou.Framework.Imaging.Formats.MetadataHandling) | What to do with metadata WebP cannot store (text, resolution) | [Strict](cref:Meziantou.Framework.Imaging.Formats.MetadataHandling.Strict) |

## Animations need a seekable destination

An animation declares the size of the file before its frames, so it is written to a **seekable** destination: a path, a
`FileStream` or a `MemoryStream`. A non-seekable stream (a network or HTTP response stream) is rejected with an
`ArgumentException` before anything is written; save to a `MemoryStream` first, then copy it. A still image streams to
any destination.

A writer created with [Image.CreateWriter](cref:Meziantou.Framework.Imaging.Image.CreateWriter) writes an animation unless [ImageWriterOptions.ExpectedFrameCount](cref:Meziantou.Framework.Imaging.ImageWriterOptions.ExpectedFrameCount) is 1: set
it to write a still image to a non-seekable stream.

## Pitfalls

- `Save("image.webp")` is lossless: pass a [WebPEncoder](cref:Meziantou.Framework.Imaging.Formats.WebPEncoder) with `Compression = WebPCompression.Lossy` for photos.
- 16-bit images (from a 16-bit PNG, for instance) throw an [UnsupportedImageFeatureException](cref:Meziantou.Framework.Imaging.UnsupportedImageFeatureException) unless
  [AllowBitDepthReduction](cref:Meziantou.Framework.Imaging.Formats.WebPEncoder.AllowBitDepthReduction) is set.
- A side is at most 16,383 pixels for lossy output and 16,384 for lossless output; a frame lasts at most 16,777,215 ms;
  a play count is at most 65,535; a file is at most 4 GiB.
- WebP has no text or resolution field: they follow [MetadataHandling](cref:Meziantou.Framework.Imaging.Formats.MetadataHandling), which rejects them by default.
- Animations are written as full frames, without delta rectangles or blending.
- The background color of an animation is never painted: areas a frame clears are transparent black.
