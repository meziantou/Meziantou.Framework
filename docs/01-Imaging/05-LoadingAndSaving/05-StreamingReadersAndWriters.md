---
title: Streaming readers and writers
description: Read and write animations one frame at a time, with memory bounded by one frame whatever their length.
---

`Image.Load` decodes every frame of an animation into memory. For a long animation, or a service that must keep its
memory bounded, read and write the frames one at a time instead: a reader decodes the next frame when you ask for it,
and a writer encodes each frame as you give it. Memory stays bounded by about one frame of decoder or encoder state,
plus the images you keep.

## Transform an animation frame by frame

Open a reader on the input and a writer on the output, then copy the frames through your processing:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_StreamingTransform;format=md-fenced-code-block;dedent=true -->
```csharp
public static async Task StreamingResizeAsync(Stream input, Stream output, CancellationToken cancellationToken)
{
    await using var reader = await Image.OpenReaderAsync<Rgba32>(input, cancellationToken: cancellationToken);
    var info = reader.Info;
    var size = new Size(info.Width / 2, info.Height / 2);

    await using var writer = Image.CreateWriter<Rgba32>(output, new ImageWriterOptions(size)
    {
        Encoder = new GifEncoder(),
        Animation = info.Animation, // e.g. preserve the number of plays
        Metadata = info.Metadata,
    });

    while (await reader.ReadFrameAsync(cancellationToken) is { } frame)
    {
        using (frame)
        {
            frame.Resize(new ResizeOptions(size) { Mode = ResizeMode.Stretch }, cancellationToken);
            await writer.WriteFrameAsync(frame.Frames[0], cancellationToken);
        }
    }

    await writer.CompleteAsync(cancellationToken); // mandatory: disposing without Complete aborts the output
}
```
<!-- endref -->

`Image.OpenReader<TPixel>` and `Image.CreateWriter<TPixel>` take a path or a stream, and have asynchronous forms
(`OpenReaderAsync`, `ReadFrameAsync`, `WriteFrameAsync`, `CompleteAsync`). Readers and writers are disposable, also with
`await using`.

## Reuse one image for every frame

`ReadFrame` returns a new image for each frame, which you dispose. `ReadFrameInto` writes the next frame into an image
you own instead, so that nothing is allocated per frame:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_ReadFrameInto;format=md-fenced-code-block;dedent=true -->
```csharp
public static void StreamingWithReusedBuffer(string inputPath, string outputPath)
{
    using var reader = Image.OpenReader<Rgba32>(inputPath);
    var info = reader.Info;
    var frameCount = info.FrameCount ?? throw new InvalidOperationException("PNG output requires a known frame count.");

    using var writer = Image.CreateWriter<Rgba32>(outputPath, new ImageWriterOptions(info.Size)
    {
        Encoder = new PngEncoder { AnimationMode = PngAnimationMode.Animated },
        ExpectedFrameCount = frameCount,
        Animation = info.Animation,
    });

    if (reader.ReadPosterFrame() is { } poster)
    {
        using (poster)
        {
            writer.WritePosterFrame(poster.Frames[0]);
        }
    }

    using var buffer = new Image<Rgba32>(info.Width, info.Height);
    while (reader.ReadFrameInto(buffer))
    {
        buffer.Grayscale();
        writer.WriteFrame(buffer.Frames[0]);
    }

    writer.Complete(); // publishes outputPath atomically
}
```
<!-- endref -->

The destination must have the size of the canvas, one frame, no poster frame and no animation settings. Its duration,
hotspot and metadata are replaced with those of the frame read.

## Readers

| Member | Does |
| --- | --- |
| `Info` | What the header says: format, canvas size, frame count when the header declares it (`null` for GIF), animation settings and metadata |
| `ReadPosterFrame()` | Returns the APNG poster frame, or `null`. Allowed once, before the first frame |
| `ReadFrame()` | Returns the next frame as a still image you own, or `null` at the end of the animation |
| `ReadFrameInto(image)` | Overwrites your image with the next frame; returns `false` at the end, and leaves the image unchanged |
| `ReadFrameRefinementInto(image)` | Writes the next rendering of a progressive frame (JPEG XL, progressive JPEG, interlaced PNG), then the frame itself: see [Show an image while it downloads](06-ShowImagesWhileTheyDownload.md) |
| `FramesRead` | The number of frames read so far |

- `null` or `false` always means that the animation ended cleanly, or that `ImageReaderOptions.FrameLimit` was reached.
  A truncated or malformed input throws an `InvalidImageContentException`; it is never reported as an end.
- After an error in the data, a limit, an I/O error or a cancellation, the reader is faulted: later calls throw an
  `InvalidOperationException`. An invalid argument leaves it usable.
- The images a reader returns are independent: they stay valid after the reader is disposed. They are charged to the
  [allocation limit](07-ResourceLimits.md) of the reader until you dispose them.
- TIFF, ICO and CUR files are rejected: their entries are pages or sizes, not frames (use
  [`ImageCollection`](../04-Concepts/03-PagesAndRepresentations.md)). ANI files are rejected too, because the order of
  their frames is only known once the whole file is read (use `Image.Load`).

## Writers

A writer receives an optional poster frame (`WritePosterFrame`, animated PNG only, before the first frame), then each
frame (`WriteFrame`), then **`Complete`, which is mandatory**. Disposing a writer that was not completed aborts the
output: a file is not published, and a stream is left without the end of the image.

`ImageWriterOptions` gives the canvas size, and optionally the encoder (required for a stream; for a path, the
[extension](04-SavingImages.md#choose-the-format-from-the-extension) chooses it), the animation settings, the metadata,
the configuration and the number of frames. The writer copies the options when it is created, and checks the metadata
against the format before it writes anything.

Some formats need to know the number of frames in advance (`ExpectedFrameCount`), or need a destination that can seek:

| Output | `ExpectedFrameCount` | Destination |
| --- | --- | --- |
| PNG and APNG | Required | Any |
| AVIF, HEIC | Required | Seekable for an animation |
| GIF | Optional | Any |
| WebP | Optional | Seekable for an animation |
| JPEG XL | Optional | Any |
| ANI | Optional | Seekable |
| TIFF | Optional, one frame | Seekable |
| JPEG, JPEG 2000, QOI, BMP, TGA, Netpbm, ICO and CUR | Optional, one frame | Any |

`Complete` checks that at least one frame was written and that the count matches `ExpectedFrameCount`. An error found
before any work, such as a frame of the wrong size or a duration the format cannot store, leaves the writer usable; a
failure once the encoding started faults it.

## Streams and their owner

- A reader starts at the current position of the stream and consumes what it reads; a writer starts at the current
  position too. Streams that cannot seek are supported, except as the destinations listed above.
- Your stream is left open when the reader or the writer is disposed, unless you set `LeaveOpen = false` on
  `ImageReaderOptions` or `ImageWriterOptions`. With `LeaveOpen = false`, a reader owns the stream even when opening it
  fails.
- With a path, the reader or the writer opens and closes the file itself. A writer to a path publishes the file
  [atomically](04-SavingImages.md#files-are-published-atomically) when `Complete` succeeds.
- A reader or a writer supports one call at a time: overlapping calls throw an `InvalidOperationException`.
