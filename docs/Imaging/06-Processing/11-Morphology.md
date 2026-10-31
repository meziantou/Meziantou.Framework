---
title: Morphology
description: Erode, dilate, open and close an image, and extract its small bright or dark details with top-hat transforms.
---

The morphological filters replace every sample by the darkest or the brightest sample of a neighborhood, the
[StructuringElement](cref:Meziantou.Framework.Imaging.StructuringElement). Combined, they remove or extract the details that are smaller than the neighborhood: specks, scratches,
text on a page lit unevenly. They keep the size, the pixel format, the frames and the metadata of the image, apply to
every frame and to the poster frame, and also exist for one frame.

## Extract dark text from an uneven background

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_Morphology;format=md-fenced-code-block;dedent=true -->
```csharp
public static void ExtractText(string inputPath, string outputPath)
{
    using var page = Image.Load(inputPath);
    page.Grayscale();

    // The closing by a disk wider than the strokes is the paper without its text; the black top-hat transform is
    // their difference, so the text becomes bright on black whatever the lighting of the page
    page.BlackHat(StructuringElement.CreateEllipse(5, 5));

    // The 95th percentile of each 3x3 square, nearly its brightest sample, closes the small gaps of the strokes, and
    // Otsu's threshold makes them white
    page.PercentileFilter(1, 95);
    page.Threshold();
    page.Save(outputPath);
}
```
<!-- endref -->

## The operations

| Method | What it does |
| --- | --- |
| [Erode](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Erode) | Every sample becomes the darkest one of the neighborhood: dark areas grow, bright details smaller than the neighborhood disappear |
| [Dilate](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Dilate) | Every sample becomes the brightest one: bright areas grow, dark details disappear |
| [MorphologicalOpen](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.MorphologicalOpen) | An erosion then a dilation: removes the bright details smaller than the neighborhood and keeps the larger shapes as they are |
| [MorphologicalClose](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.MorphologicalClose) | A dilation then an erosion: fills the dark details (holes, gaps, thin lines) smaller than the neighborhood |
| [TopHat](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.TopHat) | The image minus its opening: only the bright details smaller than the neighborhood, on black |
| [BlackHat](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.BlackHat) | The closing minus the image: the dark details smaller than the neighborhood, bright on black |

## The neighborhood

[StructuringElement.CreateRectangle](cref:Meziantou.Framework.Imaging.StructuringElement.CreateRectangle) gives a rectangle of `2 radiusX + 1` by `2 radiusY + 1` pixels; its cost does not
depend on its size, so a large rectangle is as fast as a small one. [StructuringElement.CreateEllipse](cref:Meziantou.Framework.Imaging.StructuringElement.CreateEllipse) gives an
ellipse, a disk when both radii are equal, which keeps round shapes round; its cost grows with its height. Choose a
neighborhood slightly larger than the details to remove or to extract: the strokes of the text, the specks of dust.

## Samples, alpha and edges

Every result is one of the samples: nothing is computed or rounded, so 16-bit images keep their precision and
floating-point images their values. Each sample is filtered on its own, alpha included: an erosion also shrinks the
opaque area of an image with transparency. [TopHat](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.TopHat) and [BlackHat](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.BlackHat) are the exception: they change the colors and keep
alpha, so they reject an [A8](cref:Meziantou.Framework.Imaging.A8) mask, which has no color.

The edge mode chooses what the filters read beyond the image, as for a [convolution](05-BlurSharpenAndConvolution.md):
the default, [Clamp](cref:Meziantou.Framework.Imaging.ConvolutionEdgeMode.Clamp), behaves as if there was nothing beyond the edges. The working memory is charged to the
allocation limit of the image and rented before any pixel changes, so an exceeded
[limit](../05-LoadingAndSaving/07-ResourceLimits.md) leaves the image unchanged. The EXIF thumbnail, now stale, is
removed.

For the median and the other percentiles of a square, see
[PercentileFilter](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.PercentileFilter) in [Blur, sharpen and convolution](05-BlurSharpenAndConvolution.md).
