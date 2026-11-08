---
title: Custom operations
description: Write your own pixel filters and neighborhood filters, applied to every frame, in parallel, on any pixel format.
---

A custom operation is a filter of your own, applied like a built-in one: to every frame of the image and to its poster
frame, in bands of rows processed by several threads when the configuration allows it.

## Edit pixels in place

An [IPixelRowOperation\<TPixel\>](cref:Meziantou.Framework.Imaging.IPixelRowOperation{TPixel}) edits one row at a time.
[ApplyRowOperation](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.ApplyRowOperation) runs it on every row:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/ExtensibilityExamples.cs;region=Imaging_RowOperation;format=md-fenced-code-block;dedent=true -->
```csharp
public static void Solarize(Image image)
{
    // Rgba64 rows: the rows of any integer pixel format are converted to them without loss, and back after the call
    image.ApplyRowOperation<Rgba64, SolarizeFilter>(new SolarizeFilter(threshold: 32768));
}

/// <summary>Inverts the samples above a threshold. A struct: the processing loop is compiled for it.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct SolarizeFilter(ushort threshold) : IPixelRowOperation<Rgba64>
{
    // Called by several threads at once when MaxDegreeOfParallelism > 1: it only reads its fields
    public void Invoke(int y, Span<Rgba64> row)
    {
        foreach (ref var pixel in row)
        {
            pixel = new Rgba64(Solarize(pixel.R), Solarize(pixel.G), Solarize(pixel.B), pixel.A);
        }
    }

    private ushort Solarize(ushort sample) => sample > threshold ? (ushort)(ushort.MaxValue - sample) : sample;
}
```
<!-- endref -->

## Read the neighbors of a pixel

A blur or an edge detector reads the pixels around the one it computes. An
[IPixelNeighborhoodOperation\<TPixel\>](cref:Meziantou.Framework.Imaging.IPixelNeighborhoodOperation{TPixel}) receives
a [PixelRowSource\<TPixel\>](cref:Meziantou.Framework.Imaging.PixelRowSource{TPixel}) that holds the pixels of the frame
before the operation, and writes one row of the result.
[ApplyNeighborhoodOperation](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.ApplyNeighborhoodOperation) runs
it on every row:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/ExtensibilityExamples.cs;region=Imaging_NeighborhoodOperation;format=md-fenced-code-block;dedent=true -->
```csharp
public static void Emboss(Image image)
{
    // RgbaFloat rows hold the samples of every pixel format; results outside [0, 1] are clipped when they are stored
    image.ApplyNeighborhoodOperation<RgbaFloat, EmbossFilter>(default);
}

public readonly struct EmbossFilter : IPixelNeighborhoodOperation<RgbaFloat>
{
    public void Invoke(int y, PixelRowSource<RgbaFloat> source, Span<RgbaFloat> destination)
    {
        // The source holds the pixels before the operation, even those of the rows already written
        var above = source.GetRowClamped(y - 1);
        var below = source.GetRowClamped(y + 1);
        for (var x = 0; x < destination.Length; x++)
        {
            var left = above[Math.Max(x - 1, 0)];
            var right = below[Math.Min(x + 1, destination.Length - 1)];
            destination[x] = new RgbaFloat(
                0.5f + right.R - left.R,
                0.5f + right.G - left.G,
                0.5f + right.B - left.B,
                source.GetRow(y)[x].A);
        }
    }
}
```
<!-- endref -->

[GetRowClamped](cref:Meziantou.Framework.Imaging.PixelRowSource{TPixel}.GetRowClamped) repeats the edge rows for the
rows outside the frame. The source is a copy of the frame, made one frame at a time, which counts toward
[MaxLiveAllocationBytes](cref:Meziantou.Framework.Imaging.ImageResourceLimits.MaxLiveAllocationBytes).

## Choose the pixel type of the rows

The first type argument is the pixel type of the rows your operation receives:

| Rows | Images | Cost |
| --- | --- | --- |
| The pixel type of the image | That pixel type only | None: the rows of the image, in place |
| [Rgba64](cref:Meziantou.Framework.Imaging.Rgba64) | Every integer pixel format | A conversion of each row, and back |
| [RgbaFloat](cref:Meziantou.Framework.Imaging.RgbaFloat) | Every pixel format | A conversion of each row, and back |

The rows are converted without loss; other combinations throw a `NotSupportedException` before anything changes. When
a row is stored back, the samples that the pixel format of the image cannot store are clipped (a floating-point value
above 1 in an 8-bit image), a sample that is not a number throws an
[UnsupportedImageFeatureException](cref:Meziantou.Framework.Imaging.UnsupportedImageFeatureException), and so does a
transparent pixel in a format without alpha.

## Performance

- **Write the operation as a struct.** The processing loop is then compiled for it, without an interface call per row.
- **Use several threads.** The rows of a frame of at least 65,536 pixels are split into bands processed by up to
  [MaxDegreeOfParallelism](cref:Meziantou.Framework.Imaging.ImageConfiguration.MaxDegreeOfParallelism) workers, with
  the same result as one worker. The workers call the same operation concurrently: it must not change its own state.
- **Work in the pixel type of the image** when you can: the conversions add work to every row.

Like the built-in pixel operations, a failure or a cancellation can leave some rows changed, but the image stays valid.
For the pixels of one frame, without parallelism, use
[ProcessPixelRows](cref:Meziantou.Framework.Imaging.ImageFrame{TPixel}.ProcessPixelRows): see
[Ownership and lifetimes](../04-Concepts/04-OwnershipAndLifetimes.md).
