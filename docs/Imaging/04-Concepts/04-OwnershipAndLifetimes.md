---
title: Ownership and lifetimes
description: What to dispose, how long frames stay valid, how to read and write pixels, and how memory is accounted.
---

Images hold large buffers, so the library is explicit about who releases them. This page tells you what to dispose,
how long a frame stays valid, and how to access the pixels.

## What you own

| Object | Lifetime |
| --- | --- |
| [Image](cref:Meziantou.Framework.Imaging.Image) and [Image\<TPixel\>](cref:Meziantou.Framework.Imaging.Image{TPixel}), from a constructor, [Load](cref:Meziantou.Framework.Imaging.Image.Load), [Clone](cref:Meziantou.Framework.Imaging.Image.Clone), [CloneAs](cref:Meziantou.Framework.Imaging.Image.CloneAs), [CloneFrame](cref:Meziantou.Framework.Imaging.Image.CloneFrame), [ClonePosterFrame](cref:Meziantou.Framework.Imaging.Image.ClonePosterFrame), [ImportPixelData](cref:Meziantou.Framework.Imaging.Image.ImportPixelData), [ImportPixelBytes](cref:Meziantou.Framework.Imaging.Image.ImportPixelBytes) or a reader | Yours: dispose it. `Dispose` can be called more than once. |
| [ImageFrame](cref:Meziantou.Framework.Imaging.ImageFrame), [ImageFrame\<TPixel\>](cref:Meziantou.Framework.Imaging.ImageFrame{TPixel}) and the frame collections | Views of their image, not disposable. |
| [ImageCollection](cref:Meziantou.Framework.Imaging.ImageCollection) | Yours: dispose it. Its entries are views; the images they decode are yours. |
| Clones, extracted frames, images returned by a reader | Independent: they stay valid after the source image or the reader is disposed. |
| Files, streams and spans given to [Load](cref:Meziantou.Framework.Imaging.Image.Load) and [Identify](cref:Meziantou.Framework.Imaging.Image.Identify) | Never kept: the library copies what it needs while loading, and never closes your stream. |
| [MetadataBlob](cref:Meziantou.Framework.Imaging.Metadata.MetadataBlob), profiles, options, encoders, brushes, pens, paths | Immutable: share them freely. |

A frame becomes invalid when it is removed or replaced, or when its image is disposed: using it then throws an
`ObjectDisposedException`. A geometry change does not invalidate it: after a resize, a frame reference still designates
the same frame of the image.

## Read and write pixels

Pixels are accessed through a callback that receives the rows of a frame. Each row is a `Span<TPixel>` of exactly the
visible pixels of that row:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_TypedRows;format=md-fenced-code-block;dedent=true -->
```csharp
public static void InvertColors(string path)
{
    using var image = Image.Load<Rgba32>(path);
    foreach (var frame in image.Frames)
    {
        frame.ProcessPixelRows(static pixels =>
        {
            for (var y = 0; y < pixels.Height; y++)
            {
                foreach (ref var pixel in pixels.GetRowSpan(y))
                {
                    pixel = new Rgba32((byte)(255 - pixel.R), (byte)(255 - pixel.G), (byte)(255 - pixel.B), pixel.A);
                }
            }
        });
    }

    image.Save(path);
}
```
<!-- endref -->

The ways to access pixels:

| API | Gives |
| --- | --- |
| `frame.ProcessPixelRows` | Typed rows (`Span<TPixel>`), on an [ImageFrame\<TPixel\>](cref:Meziantou.Framework.Imaging.ImageFrame{TPixel}) |
| `frame.ProcessPixelBytes` | Raw rows (`Span<byte>`, samples in the native byte order), on any frame |
| `frame[x, y]` | One pixel, to read or write a few; prefer the rows for loops |
| [CopyPixelDataTo](cref:Meziantou.Framework.Imaging.ImageFrame{TPixel}.CopyPixelDataTo), [CopyPixelBytesTo](cref:Meziantou.Framework.Imaging.ImageFrame.CopyPixelBytesTo) | A copy of the pixels in your own buffer, with an optional stride |
| [Draw](cref:Meziantou.Framework.Imaging.ImageFrame.Draw) | A canvas for shapes and text: see [Drawing](../07-Drawing/01-Overview.md) |

Rows are contiguous in memory, but the whole image is not: no API gives a span of the whole image. Process it row by
row.

The callback receives a `ref struct`, so it cannot be asynchronous and cannot keep the span after it returns. To pass
data in or out without a closure, use the overload that takes a state; with a `static` lambda, nothing is allocated per
row:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_TypedRowsState;format=md-fenced-code-block;dedent=true -->
```csharp
public static long ComputeLuminanceSum(Image<Gray16> image)
{
    var total = new StrongBox<long>();
    image.Frames[0].ProcessPixelRows(total, static (pixels, state) =>
    {
        for (var y = 0; y < pixels.Height; y++)
        {
            foreach (var pixel in pixels.GetRowSpan(y))
            {
                state.Value += pixel.Value;
            }
        }
    });

    return total.Value;
}
```
<!-- endref -->

## Leases

While a callback runs, it holds an exclusive lease on the image. During the lease, disposing the image, adding or
removing frames, or accessing the pixels in a conflicting way throws an `InvalidOperationException`. This detection is
a safety net, not a lock: it does not make an image safe to use from several threads (see
[Thread safety and atomicity](06-ThreadSafetyAndAtomicity.md)). The lease is released when the callback returns, even
when it throws.

## Import and export raw pixels

[Image.ImportPixelBytes](cref:Meziantou.Framework.Imaging.Image.ImportPixelBytes) and [Image.ImportPixelData](cref:Meziantou.Framework.Imaging.Image.ImportPixelData) create an image from pixels you already have, for example from a
camera or another library. [CopyPixelBytesTo](cref:Meziantou.Framework.Imaging.ImageFrame.CopyPixelBytesTo) and [CopyPixelDataTo](cref:Meziantou.Framework.Imaging.ImageFrame{TPixel}.CopyPixelDataTo) copy them back:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_RawPixels;format=md-fenced-code-block;dedent=true -->
```csharp
public static byte[] RoundTripRawPixels(ReadOnlySpan<byte> rgba, int width, int height)
{
    using var image = Image.ImportPixelBytes<Rgba32>(rgba, width, height);
    image.Crop(new Rectangle(0, 0, width / 2, height));
    var result = new byte[image.Width * image.Height * 4];
    image.Frames[0].CopyPixelBytesTo(result);
    return result;
}
```
<!-- endref -->

The pixels are always copied: the image never wraps your memory, so you can reuse your buffer right away. When your
rows have padding, pass their stride (`strideInBytes`, `strideInPixels`).

## Memory accounting

Every buffer the library allocates is charged to its owner (an image, or a reader and the images it returns), and the
total is bounded by [ImageResourceLimits.MaxLiveAllocationBytes](cref:Meziantou.Framework.Imaging.ImageResourceLimits.MaxLiveAllocationBytes). An operation that would exceed it throws an
[ImageResourceLimitException](cref:Meziantou.Framework.Imaging.ImageResourceLimitException) before allocating. The images a reader returns stay charged to the reader until you
dispose them, even after the reader is disposed.

This bound is a safety limit per owner, not a cap on the memory of the process: ten images can each use up to the limit.
See [Resource limits](../05-LoadingAndSaving/07-ResourceLimits.md).
