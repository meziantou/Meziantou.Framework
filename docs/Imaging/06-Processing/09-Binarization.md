---
title: Binarization
description: Make an image black and white with a global threshold (Otsu, fixed) or an adaptive one (mean, Sauvola) that follows uneven lighting.
---

[Threshold](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Threshold) makes every frame black and white, in place: a pixel whose luminance is greater than its threshold becomes
[ThresholdOptions.UpperColor](cref:Meziantou.Framework.Imaging.ThresholdOptions.UpperColor) (white by default), and any other pixel [LowerColor](cref:Meziantou.Framework.Imaging.ThresholdOptions.LowerColor) (black by default): a scanned
document, a mask, or the input of a bilevel TIFF page or a portable bitmap.

## Binarize a scanned page

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_Threshold;format=md-fenced-code-block;dedent=true -->
```csharp
public static void SaveScanAsBilevel(string inputPath, string tiffPath, string pbmPath)
{
    using var page = Image.Load(inputPath);

    // Sauvola's threshold follows uneven lighting: each pixel is compared with the mean and the contrast of the
    // 31 x 31 pixels around it, so the text becomes black and the paper white, even in a shadow. The pixels are now
    // exactly black or white, in the same pixel format.
    page.Threshold(new ThresholdOptions { Method = ThresholdMethod.Sauvola, WindowSize = 31 });

    // A bilevel page with Group 4 compression, the usual format of scanned documents
    page.Save(tiffPath, new TiffEncoder { PixelLayout = TiffPixelLayout.Bilevel, Compression = TiffCompression.CcittGroup4 });

    // A portable bitmap: one bit per pixel
    page.Save(pbmPath, new PnmEncoder { PixelLayout = PnmPixelLayout.Bitmap });
}
```
<!-- endref -->

The pixel format is kept: the pixels are only black or white, which is what the bilevel layouts of the TIFF and PNM
encoders require. These encoders never threshold an image themselves: they accept only pixels that are exactly black
or white. See [TIFF](../10-Formats/10-Tiff.md) and [Netpbm](../10-Formats/09-Netpbm.md).

## Choose the threshold

| [ThresholdMethod](cref:Meziantou.Framework.Imaging.ThresholdMethod) | Threshold | Use it for |
| --- | --- | --- |
| [Otsu](cref:Meziantou.Framework.Imaging.ThresholdMethod.Otsu) (default) | The level that best separates the two classes of the luminance histogram of the frame | Images with an even lighting and two clear groups of tones |
| [Fixed](cref:Meziantou.Framework.Imaging.ThresholdMethod.Fixed) | [Level](cref:Meziantou.Framework.Imaging.ThresholdOptions.Level), a fraction of the range (0.5 by default: 128 and above become white on 8-bit samples) | A threshold you know |
| [Mean](cref:Meziantou.Framework.Imaging.ThresholdMethod.Mean) | The mean of the [WindowSize](cref:Meziantou.Framework.Imaging.ThresholdOptions.WindowSize) square around each pixel, minus [Offset](cref:Meziantou.Framework.Imaging.ThresholdOptions.Offset) | Uneven lighting |
| [Sauvola](cref:Meziantou.Framework.Imaging.ThresholdMethod.Sauvola) | A threshold computed from the mean and the contrast of the window around each pixel ([SauvolaK](cref:Meziantou.Framework.Imaging.ThresholdOptions.SauvolaK), [SauvolaR](cref:Meziantou.Framework.Imaging.ThresholdOptions.SauvolaR)) | Documents: a blank background stays blank, even in a shadow |

- [ImageHistogram.GetOtsuThreshold()](cref:Meziantou.Framework.Imaging.ImageHistogram.GetOtsuThreshold) returns the Otsu level of any histogram, for example to inspect it before
  thresholding with [Fixed](cref:Meziantou.Framework.Imaging.ThresholdMethod.Fixed) (see [Histograms, levels and curves](07-HistogramsAndLevels.md)).
- [WindowSize](cref:Meziantou.Framework.Imaging.ThresholdOptions.WindowSize) (15 by default) is an odd number of pixels. Choose a window larger than the strokes to keep: about twice
  the height of a line of text, for a document.
- [EdgeMode](cref:Meziantou.Framework.Imaging.ThresholdOptions.EdgeMode) ([Mirror](cref:Meziantou.Framework.Imaging.ConvolutionEdgeMode.Mirror) by default) says which pixels the windows read outside the frame.
- [ShareHistogramAcrossFrames](cref:Meziantou.Framework.Imaging.ThresholdOptions.ShareHistogramAcrossFrames) gives every frame of an animation the Otsu level of their combined histograms, so that
  it does not flicker.

## Pixel formats

The luminance is the Rec. 709 luma that [Grayscale](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Grayscale) stores, at the storage precision. Alpha is ignored, and every pixel
gets one of the two colors, alpha included: the pixel format must store them exactly, or [Threshold](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Threshold) throws an
`ArgumentException`. Floating-point formats have no discrete levels and throw an [UnsupportedImageFeatureException](cref:Meziantou.Framework.Imaging.UnsupportedImageFeatureException):
convert them to [Rgba64](cref:Meziantou.Framework.Imaging.Rgba64) first. The results are the same on every platform.

## Dither to black and white

A threshold makes every gray area all black or all white. To render the gray levels as patterns of black and white
pixels instead, quantize to a palette of the two colors with dithering (see [Color reduction](08-ColorReduction.md)):

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/ProcessingExamples.cs;region=Imaging_DitherToBlackAndWhite;format=md-fenced-code-block;dedent=true -->
```csharp
public static void DitherToBlackAndWhite(string inputPath, string outputPath)
{
    using var image = Image.Load(inputPath);

    // Only the colors of the palette are used. Error diffusion turns the gray levels into patterns of black and white
    // pixels, where a threshold would make them all black or all white
    var blackAndWhite = new ImagePalette([Colors.Black, Colors.White]);
    image.Quantize(new QuantizeOptions { Palette = blackAndWhite, Dithering = DitheringMode.FloydSteinberg });
    image.Save(outputPath);
}
```
<!-- endref -->
