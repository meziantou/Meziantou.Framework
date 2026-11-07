---
title: Thread safety and atomicity
description: What can be shared between threads, and what state an image is in after an operation fails or is canceled.
---

This page tells you what you can share between threads, and what you can rely on when an operation throws or is
canceled.

## Process images in parallel

Distinct images can be processed concurrently, and the immutable objects can be shared by every thread:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/LoadingExamples.cs;region=Imaging_ProcessInParallel;format=md-fenced-code-block;dedent=true -->
```csharp
public static void CreateThumbnails(IReadOnlyList<string> inputPaths, string outputDirectory, CancellationToken cancellationToken)
{
    // Options and encoders are immutable: one instance serves every thread
    var resize = new ResizeOptions(128, 128);
    var encoder = new PngEncoder();

    Parallel.ForEach(inputPaths, new ParallelOptions { CancellationToken = cancellationToken }, inputPath =>
    {
        // Each thread works on its own image
        using var image = Image.Load(inputPath);
        image.Resize(resize, cancellationToken);
        image.Save(Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(inputPath) + ".png"), encoder);
    });
}
```
<!-- endref -->

| Thread-safe | Not thread-safe |
| --- | --- |
| The static methods: [Load](cref:Meziantou.Framework.Imaging.Image.Load), [Identify](cref:Meziantou.Framework.Imaging.Image.Identify), [DetectFormat](cref:Meziantou.Framework.Imaging.Image.DetectFormat), … | An [Image](cref:Meziantou.Framework.Imaging.Image) and its frames, while any thread changes them or holds a [lease](04-OwnershipAndLifetimes.md#leases) |
| The immutable types: configurations, limits, options, encoders, [MetadataBlob](cref:Meziantou.Framework.Imaging.Metadata.MetadataBlob), profiles, brushes, pens, paths | A reader or a writer: one call at a time |

An image supports one operation at a time: do not use it from another thread while one thread changes it or holds a
lease on it, and every pixel access holds one. Readers and writers detect overlapping calls and throw an
`InvalidOperationException`. For images, the detection of conflicting leases is a best-effort safety net, not a lock:
when several threads share an image, synchronize them yourself.

## Parallelism inside an operation

By default, an operation runs on the calling thread only ([ImageConfiguration.MaxDegreeOfParallelism](cref:Meziantou.Framework.Imaging.ImageConfiguration.MaxDegreeOfParallelism) is 1). With a
higher value, large resizes, transformations, convolutions, blurs, medians and unsharp masks split their rows into
bands processed concurrently, and the decoders and encoders of JPEG, TIFF, OpenEXR, JPEG 2000, JPEG XL, AVIF and HEIC
split the independent parts of large images (rows, strips, chunks, code-blocks, groups, tiles) between threads. The
result is identical to the sequential one: the same pixels, the same encoded bytes and the same error. Each thread
needs memory of its own, counted by
[ImageResourceLimits.MaxLiveAllocationBytes](cref:Meziantou.Framework.Imaging.ImageResourceLimits.MaxLiveAllocationBytes),
so a limit that fits one thread may not fit several.

Keep the default in a service that already processes several images at once: the bound applies to each operation, so
`n` concurrent operations can use up to `n` times that many threads. See [Performance](../11-Performance.md).

## Geometry changes are all or nothing

[Crop](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Crop), [AutoCrop](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.AutoCrop), [Resize](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Resize), [Pad](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Pad), [ExtendCanvas](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.ExtendCanvas), [Rotate](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Rotate), [Skew](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Skew), [Transform](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Transform) and [AutoOrient](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.AutoOrient) apply to every
frame and to the poster in one transaction. If they fail before the end, because of a limit, an allocation failure or a
cancellation, the image is unchanged: its size, pixels, frames and their order, cursor hotspots and metadata:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/LoadingExamples.cs;region=Imaging_AtomicResize;format=md-fenced-code-block;dedent=true -->
```csharp
public static bool TryUpscale(Image image, int width, int height)
{
    try
    {
        image.Resize(new ResizeOptions(width, height) { Mode = ResizeMode.Stretch, AllowUpscaling = true });
        return true;
    }
    catch (ImageResourceLimitException)
    {
        // Geometry is all or nothing: every frame keeps its size, its pixels and its metadata
        return false;
    }
}
```
<!-- endref -->

The new pixels are allocated while the old ones are still alive, so a geometry change of a large image temporarily
needs memory for both, within the allocation limit of the image.

## Pixel edits can be partial

The operations that change pixels in place write as they go: [Flip](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Flip), [Grayscale](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Grayscale), [Convolve](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Convolve) and the blurs and
sharpening, [Equalize](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Equalize), [AutoLevels](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.AutoLevels), [Draw](cref:Meziantou.Framework.Imaging.Image.Draw), your own row callbacks, and [ReadFrameInto](cref:Meziantou.Framework.Imaging.ImageReader{TPixel}.ReadFrameInto) of a reader. If they fail or
are canceled, some rows or frames may already be updated, but the image stays valid and you can dispose it, keep
editing it or save it.

- [Convolve](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Convolve), [GaussianBlur](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.GaussianBlur), [BoxBlur](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.BoxBlur), [MedianBlur](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.MedianBlur), [Sharpen](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Sharpen), [Equalize](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Equalize) and [AutoLevels](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.AutoLevels) allocate their working
  memory first: exceeding the allocation limit leaves the image unchanged.
- In a [Draw](cref:Meziantou.Framework.Imaging.Image.Draw) callback, the shapes already drawn stay drawn.

When you need all or nothing for a pixel edit, work on a [Clone()](cref:Meziantou.Framework.Imaging.Image.Clone) and keep the original until the edit succeeds.

## Cancellation

The processing operations, the asynchronous loads and saves, and the readers and writers accept a
`CancellationToken`. A canceled operation throws an `OperationCanceledException` and follows the rules above: a geometry
change leaves the image unchanged, a pixel edit may be partial. A canceled save to a path publishes no file (see
[Saving images](../05-LoadingAndSaving/04-SavingImages.md)), and a canceled load returns no image.

A failed or canceled operation always releases the resources it acquired.
