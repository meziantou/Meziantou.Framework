---
title: Processing
description: Crop, resize, rotate, blur, adjust the colors, reduce to a palette or to black and white, and compare images.
---

The processing operations are extension methods of `Image` and `ImageFrame` (`ImageProcessingExtensions`, in the
[Meziantou.Framework.Imaging](cref:Meziantou.Framework.Imaging) namespace). They change the image in place: clone it first with `Clone()` to keep the
original.

## The operations

| Operation | Methods | Page |
| --- | --- | --- |
| Crop, trim the background, add a border, enlarge the canvas | `Crop`, `AutoCrop`, `AnalyzeAutoCrop`, `Pad`, `ExtendCanvas` | [Crop and auto-crop](02-CropAndAutoCrop.md) |
| Resize, fit, fill or letterbox | `Resize` | [Resizing](03-Resizing.md) |
| Rotate, mirror, skew, correct a perspective, apply the EXIF orientation | `Rotate`, `Flip`, `Skew`, `Transform`, `AutoOrient` | [Rotate, flip and transform](04-RotateFlipAndTransform.md) |
| Blur, sharpen, apply a convolution matrix, detect edges | `GaussianBlur`, `BoxBlur`, `MedianBlur`, `Sharpen`, `Convolve`, `DetectEdges` | [Blur, sharpen and convolution](05-BlurSharpenAndConvolution.md) |
| Change the colors, apply a photo effect | `Grayscale`, `ApplyColorMatrix`, `ReplaceColor`, `Vignette`, `Glow`, `Pixelate`, `OilPaint`, `Flatten` | [Color adjustments and effects](06-ColorAdjustmentsAndEffects.md) |
| Count the levels, equalize, stretch the contrast | `ComputeHistogram`, `Equalize`, `AutoLevels` | [Histograms and levels](07-HistogramsAndLevels.md) |
| Reduce to a palette, dither | `Quantize`, `PaletteBuilder` | [Color reduction](08-ColorReduction.md) |
| Make black and white | `Threshold` | [Binarization](09-Binarization.md) |
| Measure how similar two images look | `ImageSimilarity` | [Comparing images](10-ComparingImages.md) |

Color profile conversions (`ConvertColorProfile`) are described in [Color management](../09-Color/03-ColorManagement.md).

## Every frame, and the poster

An operation on an `Image` applies to every frame of an animation and to its poster frame, so that an animation stays
consistent: a resized GIF has every frame resized, with the same durations. The operations that read pixels to decide
something, such as `AutoCrop` and `ComputeHistogram`, look at every frame too. See
[Images, frames and animations](../04-Concepts/02-ImagesFramesAndAnimations.md).

The pixel operations also exist for one frame: `Flip`, `Grayscale`, `ApplyColorMatrix`, `ReplaceColor`, the effects,
`Convolve`, the blurs, `Sharpen`, `DetectEdges`, `Equalize`, `AutoLevels`, `ComputeHistogram`, `Quantize` and
`Threshold`. The other frames are left as they are:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/ProcessingExamples.cs;region=Imaging_ProcessOneFrame;format=md-fenced-code-block;dedent=true -->
```csharp
public static void BlurFirstFrame(string inputPath, string outputPath)
{
    using var image = Image.Load(inputPath);

    // A frame is a view of its image: the blur changes this frame only, the other frames and the timing are kept
    image.Frames[0].GaussianBlur(2);
    image.Save(outputPath);
}
```
<!-- endref -->

The operations that change the size of the canvas (crops, resizes, rotations and transformations) exist only for the
whole image, since every frame of an image has the size of its canvas.

## Geometry is all or nothing

`Crop`, `AutoCrop`, `Resize`, `Pad`, `ExtendCanvas`, `Rotate`, `Skew`, `Transform` and `AutoOrient` build the new frames
before they replace the old ones. When they fail (a resource limit, a cursor hotspot that would be cut, a cancellation),
the image is left exactly as it was: its size, its pixels, its frames and its metadata.

The pixel operations write in place: a failure or a cancellation can leave some frames or rows changed, but the image
stays valid. `Convolve`, the blurs, `Sharpen`, `Equalize` and `AutoLevels` rent their working memory first, so a
resource limit leaves the image unchanged. See
[Thread safety and atomicity](../04-Concepts/06-ThreadSafetyAndAtomicity.md).

## What the operations keep

- **Pixel format and precision.** An operation keeps the pixel format of the image: a 16-bit image is processed with
  16-bit precision, and alpha is kept. `RgbaFloat` and `RgbaHalf` images accept every operation, except
  `ComputeHistogram`, `Equalize`, `AutoLevels` and `Threshold`, which need discrete levels. See
  [Pixel formats](../04-Concepts/05-PixelFormats.md).
- **Metadata.** The metadata stays. Geometry changes update the EXIF dimensions, and every operation that changes pixels
  removes the EXIF thumbnail, which no longer matches them. Only `AutoOrient` changes the orientation.
- **Cursor hotspots.** The hotspot of a cursor frame follows the pixel it points at. See
  [Rotate, flip and transform](04-RotateFlipAndTransform.md).
- **Same input, same pixels.** The results do not depend on the platform, the processor or the number of threads.

## Cancellation and parallelism

Every operation takes an optional `CancellationToken`.

Operations run on the calling thread by default. With `ImageConfiguration.MaxDegreeOfParallelism` above 1, large
resizes, transformations, convolutions, blurs, medians, unsharp masks and edge detections split their frames into bands
processed concurrently, with the same result. See [Performance](../11-Performance.md).

## Pages of this section

- [Crop and auto-crop](02-CropAndAutoCrop.md): keep a rectangle, trim a uniform background, add a border or a larger
  canvas.
- [Resizing](03-Resizing.md): fit in a box, fill it, letterbox, and choose the resampling filter.
- [Rotate, flip and transform](04-RotateFlipAndTransform.md): quarter turns, any angle, skews, affine and perspective
  transformations, the EXIF orientation.
- [Blur, sharpen and convolution](05-BlurSharpenAndConvolution.md): Gaussian, box and median blurs, unsharp masks, your
  own matrices and edge detection.
- [Color adjustments and effects](06-ColorAdjustmentsAndEffects.md): grayscale, brightness, contrast, saturation,
  presets, color replacement and spatial effects.
- [Histograms and levels](07-HistogramsAndLevels.md): read a histogram, equalize globally or locally, stretch the levels.
- [Color reduction](08-ColorReduction.md): reduce to a palette, share a palette between frames, dither.
- [Binarization](09-Binarization.md): make black and white with a global or an adaptive threshold.
- [Comparing images](10-ComparingImages.md): similarity scores and perceptual hashes.
