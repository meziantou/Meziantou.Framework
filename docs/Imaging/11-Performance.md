---
title: Performance
description: Decode only what you need, bound the memory of large inputs, and choose when to use several threads.
---

The library is designed to be fast with its defaults. This page lists the choices that make the biggest difference in
an application: what to decode, how much memory to hold, how many threads to use, and which encoder settings to pick.

## Decode only what you need

- **Read the header instead of the pixels.** To get the size, the format, the frame count or the metadata of a file,
  use [Image.Identify](cref:Meziantou.Framework.Imaging.Image.Identify): it does not decode the pixels. See [Identifying images](05-LoadingAndSaving/03-IdentifyingImages.md).
- **Stop after the first frames.** [ImageDecodeOptions.FrameLimit](cref:Meziantou.Framework.Imaging.ImageDecodeOptions.FrameLimit) stops decoding after that many frames; the rest of
  the file is not read. A preview of an animation only needs its first frame:

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/PerformanceExamples.cs;region=Imaging_FirstFramePreview;format=md-fenced-code-block;dedent=true -->
```csharp
public static void SavePreview(string animationPath, string previewPath)
{
    // Decoding stops after the first displayed frame: the rest of the file is not read
    using var image = Image.Load(animationPath, new ImageDecodeOptions { FrameLimit = 1 });

    image.Resize(new ResizeOptions(128, 128) { AllowUpscaling = false });

    // The image keeps its animation settings: save its frame as a still image
    image.Frames[0].Save(previewPath, new PngEncoder());
}
```
<!-- endref -->

- **Decode a large photo at the size you need.** [ImageDecodeOptions.TargetSize](cref:Meziantou.Framework.Imaging.ImageDecodeOptions.TargetSize) decodes a JPEG file at 1/2, 1/4 or 1/8
  of its size, and a JPEG 2000 file at a lower resolution level, when the result is still at least that large: the decoder computes fewer pixels and allocates the memory
  of the smaller image. See [Decode a smaller image](05-LoadingAndSaving/02-LoadingImages.md#decode-a-smaller-image).
- **Load in the pixel format you work with.** [Image.Load](cref:Meziantou.Framework.Imaging.Image.Load) returns the smallest pixel format that keeps the source
  exactly ([Rgb24](cref:Meziantou.Framework.Imaging.Rgb24) or [Gray8](cref:Meziantou.Framework.Imaging.Gray8) for most JPEG files, [Rgba32](cref:Meziantou.Framework.Imaging.Rgba32) for GIF, 16-bit formats for 16-bit PNG files...).
  [Image.Load\<TPixel\>](cref:Meziantou.Framework.Imaging.Image.Load) converts while loading: ask for the format your code processes, so that you do not convert the
  image a second time. See [Pixel formats](04-Concepts/05-PixelFormats.md).

## Bound the memory

- **Stream long animations.** [Image.Load](cref:Meziantou.Framework.Imaging.Image.Load) holds every frame in memory. The readers and writers of
  [Image.OpenReader](cref:Meziantou.Framework.Imaging.Image.OpenReader) and [Image.CreateWriter](cref:Meziantou.Framework.Imaging.Image.CreateWriter) hold one frame at a time, plus the frames you keep. See
  [Streaming readers and writers](05-LoadingAndSaving/05-StreamingReadersAndWriters.md).
- **Dispose images as soon as possible.** An image holds its pixels until it is disposed. Use `using` declarations, and
  dispose intermediate images (clones, extracted frames) when you are done with them.
- **Set resource limits.** The limits of [ImageResourceLimits](cref:Meziantou.Framework.Imaging.ImageResourceLimits) bound the size of the canvas, the number of frames and
  the memory an image may allocate. They protect a service from files that would exhaust its memory. See
  [Resource limits](05-LoadingAndSaving/07-ResourceLimits.md).

## Use several threads where it pays

Operations run on the calling thread by default ([ImageConfiguration.MaxDegreeOfParallelism](cref:Meziantou.Framework.Imaging.ImageConfiguration.MaxDegreeOfParallelism) is 1), and every image is
independent: a service that processes several images at the same time already uses several threads, one per image.
Keep the default in that case. The limit applies per operation, so `n` concurrent operations with a limit of `m` may use
up to `n × m` threads.

A tool that processes one large image at a time can let an operation use several threads:

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/PerformanceExamples.cs;region=Imaging_ParallelResize;format=md-fenced-code-block;dedent=true -->
```csharp
public static void ResizeWithAllCores(string inputPath, string outputPath)
{
    // The configuration is captured by the loaded image: its resizes, transformations, convolutions and blurs split
    // large frames into bands processed concurrently. The result is the same as with one worker.
    var configuration = new ImageConfiguration { MaxDegreeOfParallelism = Environment.ProcessorCount };

    using var image = Image.Load(inputPath, new ImageDecodeOptions { Configuration = configuration });
    image.Resize(new ResizeOptions(image.Width / 2, image.Height / 2) { Filter = ResamplingFilter.Lanczos3 });
    image.Save(outputPath);
}
```
<!-- endref -->

- The configuration is set when the image is loaded or created; every operation on the image uses it.
- Resizing, transformations, convolutions, blurs, sharpening and edge detection split large frames into bands.
- The HEIC and AVIF decoders decode the tiles of large grid images (the photos of phones) concurrently, and the HEIC and
  AVIF encoders encode the tiles concurrently when `GridTileSize` is set.
- The codecs split the independent parts of images of at least 65,536 pixels between threads:

  | Format | Decoded and encoded concurrently |
  |---|---|
  | OpenEXR | Chunks |
  | JPEG 2000 | Code-blocks and wavelet transforms |
  | JPEG XL | Groups of 256x256 pixels |
  | TIFF | LZW and Deflate strips and tiles (and CCITT ones when decoding) |
  | HEIC | Grid tiles; tiles and wavefront rows of a picture (decoding only) |
  | AVIF | Grid tiles; tiles of a frame that has several |
  | JPEG | Transforms and color conversion of MCU rows |

  The gain depends on the share of the work that stays on one thread. OpenEXR, JPEG 2000 and TIFF scale well. JPEG
  gains the least: its entropy decoding, a third to half of the time, stays on one thread. The files of the AVIF and
  HEIC encoders have a single tile and no wavefront, unless they are grids, AVIF frames wider than 4,096 pixels or AVIF
  files encoded with `AvifEncoder.AutoTiling`, so they decode with less parallelism than files with several tiles.
- The other decoders and encoders, drawing and the other operations run on one thread.
- The result does not depend on the number of threads: the pixels and the encoded bytes are the same.
- Each worker needs its own working memory, which counts against the memory limit of the image.

## Choose encoder settings

- **Lossless output is large.** AVIF, HEIC, JPEG XL, WebP and JPEG 2000 are lossless by default when saved by extension.
  For photographs, select the lossy compression and a quality; see the pages of the [formats](10-Formats/01-Overview.md).
- **Effort trades time for size.** `Effort` (AVIF, HEIC, JPEG XL, WebP) and [PngEncoder.Optimization](cref:Meziantou.Framework.Imaging.Formats.PngEncoder.Optimization) search more
  options to make the file smaller, and take longer to encode. Lower the effort when encoding time matters more than
  size; raise it for assets that are encoded once and downloaded many times.
- **Large AVIF and HEIC images can be cut in tiles.** `GridTileSize` encodes a large image as a grid of tiles, which
  bounds the memory of the encoder and lets several workers encode it.
- **`AvifEncoder.AutoTiling` makes large AVIF images faster to encode and decode with several workers.** It splits each
  frame into up to 8 AV1 tiles, for a file about 0.3% larger.

## Read files efficiently

- **Pass a path or a seekable stream for TIFF, ICO, CUR and ANI.** These formats point to their data: on a seekable
  source, only the bytes they need are read. A non-seekable stream is buffered first.
- **Use the asynchronous methods for network streams.** [Image.LoadAsync](cref:Meziantou.Framework.Imaging.Image.LoadAsync), [Image.IdentifyAsync](cref:Meziantou.Framework.Imaging.Image.IdentifyAsync) and the asynchronous
  readers and writers read and write with asynchronous I/O and accept a `CancellationToken`.
