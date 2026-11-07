---
title: Processing
description: Crop, resize, rotate, blur, adjust the colors, reduce to a palette or to black and white, and compare images.
---

The processing operations are extension methods of [Image](cref:Meziantou.Framework.Imaging.Image) and [ImageFrame](cref:Meziantou.Framework.Imaging.ImageFrame) ([ImageProcessingExtensions](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions), in the
[Meziantou.Framework.Imaging](cref:Meziantou.Framework.Imaging) namespace). They change the image in place: clone it first with [Clone()](cref:Meziantou.Framework.Imaging.Image.Clone) to keep the
original.

## The operations

| Operation | Methods | Page |
| --- | --- | --- |
| Crop, trim the background, suggest a crop for an aspect ratio, add a border, enlarge the canvas | [Crop](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Crop), [AutoCrop](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.AutoCrop), [AnalyzeAutoCrop](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.AnalyzeAutoCrop), [SuggestCrop](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.SuggestCrop), [Pad](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Pad), [ExtendCanvas](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.ExtendCanvas) | [Crop and auto-crop](02-CropAndAutoCrop.md) |
| Resize, fit, fill or letterbox | [Resize](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Resize) | [Resizing](03-Resizing.md) |
| Rotate, mirror, skew, correct a perspective, apply the EXIF orientation | [Rotate](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Rotate), [Flip](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Flip), [Skew](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Skew), [Transform](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Transform), [CorrectPerspective](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.CorrectPerspective), [AutoOrient](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.AutoOrient) | [Rotate, flip and transform](04-RotateFlipAndTransform.md) |
| Blur, smooth noise and keep edges, sharpen, apply a convolution matrix, detect edges | [GaussianBlur](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.GaussianBlur), [BoxBlur](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.BoxBlur), [MedianBlur](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.MedianBlur), [PercentileFilter](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.PercentileFilter), [BilateralFilter](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.BilateralFilter), [GuidedFilter](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.GuidedFilter), [RadialBlur](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.RadialBlur), [Sharpen](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Sharpen), [Convolve](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Convolve), [DetectEdges](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.DetectEdges) | [Blur, sharpen and convolution](05-BlurSharpenAndConvolution.md) |
| Change the colors, apply a photo effect | [Grayscale](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Grayscale), [ApplyColorMatrix](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.ApplyColorMatrix), [ReplaceColor](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.ReplaceColor), [Vignette](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Vignette), [Glow](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Glow), [Pixelate](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Pixelate), [OilPaint](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.OilPaint), [Flatten](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Flatten) | [Color adjustments and effects](06-ColorAdjustmentsAndEffects.md) |
| Count the levels, equalize, stretch the contrast, apply levels, curves and color lookup tables | [ComputeHistogram](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.ComputeHistogram), [Equalize](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Equalize), [AutoLevels](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.AutoLevels), [ApplyToneCurve](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.ApplyToneCurve), [ApplyToneCurves](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.ApplyToneCurves), [ApplyLut](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.ApplyLut) | [Histograms, levels and curves](07-HistogramsAndLevels.md) |
| Reduce to a palette, dither | [Quantize](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Quantize), [PaletteBuilder](cref:Meziantou.Framework.Imaging.PaletteBuilder) | [Color reduction](08-ColorReduction.md) |
| Make black and white | [Threshold](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Threshold) | [Binarization](09-Binarization.md) |
| Measure how similar two images look, draw their differences | [ImageSimilarity](cref:Meziantou.Framework.Imaging.ImageSimilarity) | [Comparing images](10-ComparingImages.md) |
| Erode, dilate, open, close, extract small bright or dark details | [Erode](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Erode), [Dilate](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Dilate), [MorphologicalOpen](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.MorphologicalOpen), [MorphologicalClose](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.MorphologicalClose), [TopHat](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.TopHat), [BlackHat](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.BlackHat) | [Morphology](11-Morphology.md) |

Color profile conversions ([ConvertColorProfile](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.ConvertColorProfile)) are described in [Color management](../09-Color/03-ColorManagement.md).

## Every frame, and the poster

An operation on an [Image](cref:Meziantou.Framework.Imaging.Image) applies to every frame of an animation and to its poster frame, so that an animation stays
consistent: a resized GIF has every frame resized, with the same durations. The operations that read pixels to decide
something, such as [AutoCrop](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.AutoCrop) and [ComputeHistogram](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.ComputeHistogram), look at every frame too. See
[Images, frames and animations](../04-Concepts/02-ImagesFramesAndAnimations.md).

The pixel operations also exist for one frame: [Flip](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Flip), [Grayscale](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Grayscale), [ApplyColorMatrix](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.ApplyColorMatrix), [ReplaceColor](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.ReplaceColor), the effects,
[Convolve](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Convolve), the blurs, [Sharpen](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Sharpen), [DetectEdges](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.DetectEdges), [Equalize](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Equalize), [AutoLevels](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.AutoLevels), [ComputeHistogram](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.ComputeHistogram), [Quantize](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Quantize) and
[Threshold](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Threshold). The other frames are left as they are:

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

[Crop](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Crop), [AutoCrop](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.AutoCrop), [Resize](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Resize), [Pad](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Pad), [ExtendCanvas](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.ExtendCanvas), [Rotate](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Rotate), [Skew](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Skew), [Transform](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Transform) and [AutoOrient](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.AutoOrient) build the new frames
before they replace the old ones. When they fail (a resource limit, a cursor hotspot that would be cut, a cancellation),
the image is left exactly as it was: its size, its pixels, its frames and its metadata.

The pixel operations write in place: a failure or a cancellation can leave some frames or rows changed, but the image
stays valid. [Convolve](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Convolve), the blurs, [Sharpen](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Sharpen), [Equalize](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Equalize) and [AutoLevels](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.AutoLevels) rent their working memory first, so a
resource limit leaves the image unchanged. See
[Thread safety and atomicity](../04-Concepts/06-ThreadSafetyAndAtomicity.md).

## What the operations keep

- **Pixel format and precision.** An operation keeps the pixel format of the image: a 16-bit image is processed with
  16-bit precision, and alpha is kept. [RgbaFloat](cref:Meziantou.Framework.Imaging.RgbaFloat) and [RgbaHalf](cref:Meziantou.Framework.Imaging.RgbaHalf) images accept every operation, except
  [ComputeHistogram](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.ComputeHistogram), [Equalize](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Equalize), [AutoLevels](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.AutoLevels) and [Threshold](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Threshold), which need discrete levels. See
  [Pixel formats](../04-Concepts/05-PixelFormats.md).
- **Metadata.** The metadata stays. Geometry changes update the EXIF dimensions, and every operation that changes pixels
  removes the EXIF thumbnail, which no longer matches them. Only [AutoOrient](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.AutoOrient) changes the orientation.
- **Cursor hotspots.** The hotspot of a cursor frame follows the pixel it points at. See
  [Rotate, flip and transform](04-RotateFlipAndTransform.md).
- **Same input, same pixels.** The results do not depend on the platform, the processor or the number of threads.

## Cancellation and parallelism

Every operation takes an optional `CancellationToken`.

Operations run on the calling thread by default. With [ImageConfiguration.MaxDegreeOfParallelism](cref:Meziantou.Framework.Imaging.ImageConfiguration.MaxDegreeOfParallelism) above 1, large
resizes, transformations, convolutions, blurs, medians, unsharp masks and edge detections split their frames into bands
processed concurrently, with the same result. See [Performance](../11-Performance.md).

## Pages of this section

- [Crop and auto-crop](02-CropAndAutoCrop.md): keep a rectangle, trim a uniform background, find the crop of an aspect
  ratio that keeps the subject, add a border or a larger canvas.
- [Resizing](03-Resizing.md): fit in a box, fill it, letterbox, and choose the resampling filter.
- [Rotate, flip and transform](04-RotateFlipAndTransform.md): quarter turns, any angle, skews, affine and perspective
  transformations, straightening a photographed document, the EXIF orientation.
- [Blur, sharpen and convolution](05-BlurSharpenAndConvolution.md): Gaussian, box, median, motion, zoom and spin blurs,
  percentile filters, edge-preserving smoothing, unsharp masks, your own matrices and edge detection.
- [Color adjustments and effects](06-ColorAdjustmentsAndEffects.md): grayscale, brightness, contrast, saturation,
  presets, color replacement and spatial effects.
- [Histograms, levels and curves](07-HistogramsAndLevels.md): read a histogram, equalize globally or locally, stretch the
  levels, apply gamma, levels, curves and color lookup tables.
- [Color reduction](08-ColorReduction.md): reduce to a palette, share a palette between frames, dither.
- [Binarization](09-Binarization.md): make black and white with a global or an adaptive threshold.
- [Comparing images](10-ComparingImages.md): similarity scores at 8 or 16 bits or on high dynamic range samples,
  difference maps and perceptual hashes.
- [Morphology](11-Morphology.md): erosion, dilation, opening, closing and top-hat transforms.
