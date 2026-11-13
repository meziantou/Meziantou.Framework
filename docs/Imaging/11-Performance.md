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
  of its size, a JPEG 2000 file at a lower resolution level, a lossy JPEG XL file at 1/8, an interlaced PNG file at 1/2, 1/4 or 1/8, and a TIFF file from a reduced-resolution image it stores (a pyramid), when the result is still at least
  that large: the decoder computes fewer pixels and allocates the memory of the smaller image. See [Decode a smaller image](05-LoadingAndSaving/02-LoadingImages.md#decode-a-smaller-image).
- **Decode only the area you show.** [ImageDecodeOptions.Region](cref:Meziantou.Framework.Imaging.ImageDecodeOptions.Region) decodes a rectangle of a JPEG or JPEG 2000 file,
  a tiled or stripped TIFF page, or an AVIF or HEIC grid, without decoding the blocks, strips or tiles around it, and an
  AVIF or HEIC image that is not a grid down to the rows it needs. Store large
  scans as tiled TIFF files ([TiffEncoder.TileWidth](cref:Meziantou.Framework.Imaging.Formats.TiffEncoder.TileWidth)) to make viewports cheap. See
  [Decode an area](05-LoadingAndSaving/02-LoadingImages.md#decode-an-area).
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
- **Bound the buffer pool.** The library keeps up to 32 MiB of returned buffers for the next operations, outside every
  image's budget. In a memory-constrained container, lower [ImageBufferPool.MaxRetainedBytes](cref:Meziantou.Framework.Imaging.ImageBufferPool.MaxRetainedBytes),
  or set it to 0 to disable retention; [ImageBufferPool.Trim](cref:Meziantou.Framework.Imaging.ImageBufferPool.Trim)
  releases what the pool holds. The settings never change the output of an operation:

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/ExtensibilityExamples.cs;region=Imaging_BufferPool;format=md-fenced-code-block;dedent=true -->
```csharp
public static void ConfigureForSmallContainer()
{
    // At startup: keep at most 4 MiB of returned buffers for reuse (0 disables retention)
    ImageBufferPool.MaxRetainedBytes = 4 * 1024 * 1024;
}
```
<!-- endref -->

- **Process memory you already have.** [Image.WrapMemory](cref:Meziantou.Framework.Imaging.Image.WrapMemory) works on a
  buffer of another component without copying it. See [Ownership and lifetimes](04-Concepts/04-OwnershipAndLifetimes.md).

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
  | HEIC | Grid tiles; when decoding, the tiles, wavefront rows, in-loop filters and color conversion of a picture |
  | AVIF | Grid tiles; tiles of a frame that has several; when decoding, the filters and color conversion of a frame |
  | JPEG | Transforms and color conversion of MCU rows |

  The gain depends on the share of the work that stays on one thread. OpenEXR, JPEG 2000 and TIFF scale well. JPEG
  gains the least: its entropy decoding, a third to half of the time, stays on one thread. The files of the AVIF encoder
  have a single tile, unless they are grids, frames wider than 4,096 pixels or files encoded with
  `AvifEncoder.AutoTiling`, so they decode with less parallelism than files with several tiles. The HEIC encoder writes
  the rows of a large still image as wavefront rows, which the decoder decodes concurrently.
- Drawing composites the shapes, images and layers that may cover at least 131,072 pixels with several threads, by
  batches of rows: see [Draw large canvases on several threads](#draw-large-canvases-on-several-threads).
- The other decoders and encoders and the other operations run on one thread.
- The result does not depend on the number of threads: the pixels and the encoded bytes are the same.
- Each worker needs its own working memory, which counts against the memory limit of the image.

## Draw large canvases on several threads

Posters, maps and screenshots cover millions of pixels. With
[ImageConfiguration.MaxDegreeOfParallelism](cref:Meziantou.Framework.Imaging.ImageConfiguration.MaxDegreeOfParallelism)
above 1, the draw calls on the image share the work of their large shapes between threads:

<!-- ref:../../samples/Meziantou.Framework.Imaging.Samples/PerformanceExamples.cs;region=Imaging_ParallelDrawing;format=md-fenced-code-block;dedent=true -->
```csharp
public static void DrawPoster(string outputPath)
{
    // The configuration of the image lets its draw calls composite large shapes on several threads.
    // The pixels are the same as with one thread.
    var configuration = new ImageConfiguration { MaxDegreeOfParallelism = Environment.ProcessorCount };
    using var poster = new Image<Rgba32>(2400, 1600, new Rgba32(255, 255, 255), configuration);
    poster.Draw(canvas =>
    {
        canvas.Fill(new RadialGradientBrush(new PointD(1200, 800), 1400, [new GradientStop(0, new Rgba32(255, 200, 0)), new GradientStop(1, new Rgba32(0, 60, 160))]));
        canvas.FillEllipse(new SolidBrush(new Rgba32(255, 255, 255, 128)), new PointD(1200, 800), 900, 600);
    });
    poster.Save(outputPath);
}
```
<!-- endref -->

- The calling thread computes the coverage of each row of a shape, and hands batches of rows to the other threads,
  which paint and composite them. Fills, strokes, images, image brushes, patterns, gradients and the end of a layer
  are drawn this way when their rows may cover at least 131,072 pixels; smaller shapes stay on the calling thread.
- The pixels are the same as with one thread, and so is the error when a draw call fails.
- Paints that cost more per pixel gain the most: gradients, images that are turned, scaled or sampled with
  [ImageSampling.Bicubic](cref:Meziantou.Framework.Imaging.Drawing.ImageSampling.Bicubic), translucent colors and
  layers. An opaque solid color on its own gains little: computing the coverage costs about as much as writing the
  pixels.
- Texts, clip masks, shadows, blurs and filters, and gradients mixed in a
  [paint profile](07-Drawing/06-RenderingQuality.md#draw-colors-of-a-color-profile), are drawn on the calling thread.
- Each thread needs about 128 KB and 40 bytes per pixel of the width of the canvas, counted by
  [ImageResourceLimits.MaxLiveAllocationBytes](cref:Meziantou.Framework.Imaging.ImageResourceLimits.MaxLiveAllocationBytes).
  Set the value to 1, the default, to draw on the calling thread only.

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
