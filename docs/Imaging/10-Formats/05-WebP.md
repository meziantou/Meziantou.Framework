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

Lossy output uses 4:2:0 chroma subsampling, and keeps alpha losslessly unless `AlphaQuality` is below 100. Lossless
output keeps every 8-bit sample, including the colors of fully transparent pixels, unless `NearLossless` is below 100.

At effort 2 and above, lossy encoding tries 4x4 luma prediction. The modes are stored in the VP8 first partition, at
most 524,287 bytes. When they do not fit, which only happens for very large detailed images (photographs of about 40
megapixels and more at high quality), the frame is encoded once more with a budget for the mode bits: the macroblocks
where 4x4 prediction gains least switch to cheaper modes, spread over the whole image. The quantizer stays the same, so
the decoded quality decreases gradually with the image size instead of dropping at a threshold, and every size up to
16,383 x 16,383 fits.

The managed encoder makes its own prediction and entropy-coding decisions. From effort 3, lossless output compares
transform choices (color table, prediction of the color indexes, predictor tile sizes, no predictor) by encoding each one
quickly without storing it, and codes regions with different statistics with their own prefix codes (a spatial entropy
image); from effort 5 it also chooses its backward references by their estimated cost. Lossy output uses one quantizer
for the frame (no adaptive quantization) and one token
partition. It is not libwebp, and its [Quality](cref:Meziantou.Framework.Imaging.Formats.WebPEncoder.Quality) scale is not `cwebp -q`: the
same decoded quality usually needs a higher [Quality](cref:Meziantou.Framework.Imaging.Formats.WebPEncoder.Quality) here, and the gap varies
with the content. To match the output of another encoder, compare representative images at matched decoded quality,
not at equal quality numbers.

Hidden colors cost almost nothing in lossy output: the fully transparent areas are filled with one value that the
prediction reproduces. The alpha filter (none, horizontal, vertical or gradient) is the one whose lossless stream is
smallest.

Lossless encoding works on one frame at a time. Its working memory counts against [MaxLiveAllocationBytes](cref:Meziantou.Framework.Imaging.ImageResourceLimits.MaxLiveAllocationBytes): a copy of
the frame (4 bytes per pixel), the hash chains of the LZ77 parse (4 bytes per pixel, released before the output is
written), the recorded parse (at most 2 bytes per pixel), from effort 5 a second parse (4 bytes per pixel, released with
the hash chains), and the compressed stream, whose buffer doubles as it grows. Comparing the transform choices (effort 3
and above) works on a copy of the frame, or of a sample of about a quarter of it for frames larger than 262,144 pixels.
That is about 14 bytes per pixel, more for images that hardly compress. Effort 9 adds a second copy of the frame and
keeps two streams to pick the smaller one. A 16,384 x 16,384 image needs more than the default 512 MiB: raise the limit
to encode images of that size.

## Options

| Option | What it does | Default |
| --- | --- | --- |
| [Compression](cref:Meziantou.Framework.Imaging.Formats.WebPEncoder.Compression) | [Lossless](cref:Meziantou.Framework.Imaging.Formats.WebPCompression.Lossless) or [Lossy](cref:Meziantou.Framework.Imaging.Formats.WebPCompression.Lossy) | [Lossless](cref:Meziantou.Framework.Imaging.Formats.WebPCompression.Lossless) |
| [Quality](cref:Meziantou.Framework.Imaging.Formats.WebPEncoder.Quality) | 0 (smallest) to 100 (best) for lossy output; 100 is still lossy. Ignored by lossless output | 75 |
| [Effort](cref:Meziantou.Framework.Imaging.Formats.WebPEncoder.Effort) | 0 (fastest, largest) to 9 (slowest, smallest): trades encoding time for size, never changes lossless pixels | 5 |
| `AlphaQuality` | Lossy output: 0 (2 alpha levels) to 100 (exact alpha); fewer levels are kept only when they compress smaller, and fully transparent and opaque pixels stay exact | 100 |
| `NearLossless` | Lossless output: 0 (each color component within 16 of the source) to 100 (exact); rounds the colors of edges and texture, keeps smooth areas, alpha and the image border exact | 100 |
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

## Store only what changes

With [OptimizeFrames](cref:Meziantou.Framework.Imaging.Formats.WebPEncoder.OptimizeFrames), each frame after the first one stores the rectangle of the pixels that differ from
the canvas it is drawn on, grown to even coordinates (a WebP frame starts at an even offset), with the disposal of the
previous frame that gives the smallest rectangle. The rectangle is blended over the canvas, its unchanged pixels fully
transparent, when every pixel that changes is opaque; otherwise it replaces the canvas, which reproduces the transparent
pixels and their colors too. A frame equal to the previous one is a 1x1 frame. Lossless output shows the same frames as
full frames; lossy output compresses only the rectangle, and a pixel that does not change keeps the decoded color of the
frame that drew it. Every frame uses the same [Compression](cref:Meziantou.Framework.Imaging.Formats.WebPEncoder.Compression).

## Pitfalls

- `Save("image.webp")` is lossless: pass a [WebPEncoder](cref:Meziantou.Framework.Imaging.Formats.WebPEncoder) with `Compression = WebPCompression.Lossy` for photos.
- 16-bit images (from a 16-bit PNG, for instance) throw an [UnsupportedImageFeatureException](cref:Meziantou.Framework.Imaging.UnsupportedImageFeatureException) unless
  [AllowBitDepthReduction](cref:Meziantou.Framework.Imaging.Formats.WebPEncoder.AllowBitDepthReduction) is set.
- A side is at most 16,383 pixels for lossy output and 16,384 for lossless output; a frame lasts at most 16,777,215 ms;
  a play count is at most 65,535; a file is at most 4 GiB.
- WebP has no text or resolution field: they follow [MetadataHandling](cref:Meziantou.Framework.Imaging.Formats.MetadataHandling), which rejects them by default.
- Animations are written as full frames by default. Set [OptimizeFrames](cref:Meziantou.Framework.Imaging.Formats.WebPEncoder.OptimizeFrames) to store only the rectangle that
  changes in each frame (see below).
- The background color of an animation is never painted: areas a frame clears are transparent black.
