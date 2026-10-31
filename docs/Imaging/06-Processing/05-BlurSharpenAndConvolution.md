---
title: Blur, sharpen and convolution
description: Blur, remove specks, sharpen with an unsharp mask, add a motion blur, apply your own convolution matrix and detect edges.
---

These operations compute each pixel from its neighbors. They keep the size, the pixel format, the frames and the
metadata of the image, apply to every frame and to the poster frame, and also exist for one frame.

## Blur and sharpen

| Method | What it does |
| --- | --- |
| `GaussianBlur(sigma)` | A blur with a bell curve of standard deviation `sigma` (the radius CSS gives to a blur is twice the deviation) |
| `BoxBlur(radius)` | Every sample becomes the mean of the square of `2 radius + 1` pixels around it; the cost per pixel does not depend on the radius while the square fits in the image |
| `MedianBlur(radius)` | Every sample becomes the median of the square of `2 radius + 1` pixels around it: isolated specks disappear and edges stay sharp |
| `PercentileFilter(radius, percentile)` | Every sample becomes a percentile of the same square: 50 is the median, 0 the darkest sample (an erosion) and 100 the brightest (a dilation); 10 or 90 darken or brighten while ignoring a few outliers |
| `Sharpen(sigma, amount, threshold)` | An unsharp mask: adds `amount` times the difference from a Gaussian blur of deviation `sigma`, except where the difference is less than `threshold` times the range of a sample |

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_BlurAndSharpen;format=md-fenced-code-block;dedent=true -->
```csharp
public static void CleanUpScan(string inputPath, string cleanedPath, string backgroundPath)
{
    using var image = Image.Load(inputPath);

    // The median of every 3x3 square removes isolated specks and keeps edges. The unsharp mask then adds half the
    // difference from a Gaussian blur of deviation 1.5, except where it is under 2% of the range: flat paper stays flat
    image.MedianBlur(1);
    image.Sharpen(1.5, amount: 0.5, threshold: 0.02);
    image.Save(cleanedPath);

    // A box blur costs the same whatever its radius; a separable matrix is applied to the rows, then to the columns
    image.BoxBlur(25, ConvolutionEdgeMode.Mirror);
    image.Convolve(new ConvolutionOptions(ConvolutionKernel.CreateSeparable([0.25, 0.5, 0.25], [0.25, 0.5, 0.25])));
    image.Save(backgroundPath);
}
```
<!-- endref -->

- The threshold of [Sharpen](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Sharpen) keeps flat areas and their noise as they are, while the details get more contrast. Its
  result is clamped to the range of the samples: the ringing beyond black and white is clipped, except for the colors
  of floating-point samples.
- [GaussianBlur](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.GaussianBlur), [BoxBlur](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.BoxBlur) and [Sharpen](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Sharpen) take an edge mode and a working space, as a convolution does (see below).
  [MedianBlur](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.MedianBlur) takes an edge mode and filters every sample, alpha included, as stored: it computes nothing, it only
  picks one of the samples. So does [PercentileFilter](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.PercentileFilter), with the same cost.

## Add a motion blur

[ConvolutionKernel.CreateMotionBlur](cref:Meziantou.Framework.Imaging.ConvolutionKernel.CreateMotionBlur) gives the matrix of a straight trail of `length` pixels at an angle (clockwise from the
horizontal, as [Rotate](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Rotate) turns), for [Convolve](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Convolve): the blur of a camera or a subject that moved while the shutter was
open. Every pixel weighs the length of the trail inside it, so the brightness is kept, and the trail is centered on
each pixel, so the image does not move. The cost grows with the number of pixels the trail crosses.

## Apply a convolution matrix

[Convolve](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Convolve) replaces every sample by the weighted sum of the same sample of the neighboring pixels: sharpening,
blurring, embossing and edge detection are all matrices. A [ConvolutionKernel](cref:Meziantou.Framework.Imaging.ConvolutionKernel) has odd dimensions, its middle weight
is the one of the pixel itself, and its weights are given row by row:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_Convolve;format=md-fenced-code-block;dedent=true -->
```csharp
public static void SharpenAndDetectEdges(string inputPath, string sharpenedPath, string edgesPath)
{
    using var image = Image.Load(inputPath);

    // The matrix is applied as written, row by row; weights that add up to 1 keep the overall brightness
    var sharpen = new ConvolutionKernel(3, 3, [0, -1, 0, -1, 5, -1, 0, -1, 0]);
    using (var sharpened = image.Clone())
    {
        sharpened.Convolve(new ConvolutionOptions(sharpen));
        sharpened.Save(sharpenedPath);
    }

    // Weights that add up to 0 would also filter alpha down to 0 (transparent black): keep the alpha instead, and
    // add half of the range so that negative sums show as darker than mid-gray instead of black
    var options = new ConvolutionOptions(ConvolutionKernel.Laplacian3x3) { PreserveAlpha = true, EdgeMode = ConvolutionEdgeMode.Mirror, Bias = 0.5 };
    image.Convolve(options);
    image.Save(edgesPath);
}
```
<!-- endref -->

The matrix is applied as written: the top-left weight multiplies the top-left neighbor (it is not flipped), and the
weights are used as given. Divide them by their sum to keep the overall brightness. [ConvolutionKernel](cref:Meziantou.Framework.Imaging.ConvolutionKernel) has the
published matrices [SobelX](cref:Meziantou.Framework.Imaging.ConvolutionKernel.SobelX), [SobelY](cref:Meziantou.Framework.Imaging.ConvolutionKernel.SobelY), [ScharrX](cref:Meziantou.Framework.Imaging.ConvolutionKernel.ScharrX), [ScharrY](cref:Meziantou.Framework.Imaging.ConvolutionKernel.ScharrY), [PrewittX](cref:Meziantou.Framework.Imaging.ConvolutionKernel.PrewittX), [PrewittY](cref:Meziantou.Framework.Imaging.ConvolutionKernel.PrewittY) (horizontal and vertical
gradients), [Laplacian3x3](cref:Meziantou.Framework.Imaging.ConvolutionKernel.Laplacian3x3), [Laplacian5x5](cref:Meziantou.Framework.Imaging.ConvolutionKernel.Laplacian5x5), [LaplacianOfGaussian](cref:Meziantou.Framework.Imaging.ConvolutionKernel.LaplacianOfGaussian), [Emboss](cref:Meziantou.Framework.Imaging.ConvolutionKernel.Emboss) and [Sharpen3x3](cref:Meziantou.Framework.Imaging.ConvolutionKernel.Sharpen3x3).

[ConvolutionOptions](cref:Meziantou.Framework.Imaging.ConvolutionOptions) chooses:

| Option | Default | Meaning |
| --- | --- | --- |
| [EdgeMode](cref:Meziantou.Framework.Imaging.ConvolutionOptions.EdgeMode) | [Clamp](cref:Meziantou.Framework.Imaging.ConvolutionEdgeMode.Clamp) | The pixels read outside the image: [Clamp](cref:Meziantou.Framework.Imaging.ConvolutionEdgeMode.Clamp) (the nearest edge pixel), [Mirror](cref:Meziantou.Framework.Imaging.ConvolutionEdgeMode.Mirror) (the image reflected about its border), [Wrap](cref:Meziantou.Framework.Imaging.ConvolutionEdgeMode.Wrap) (the image tiled) or [Zero](cref:Meziantou.Framework.Imaging.ConvolutionEdgeMode.Zero) (transparent black) |
| [PreserveAlpha](cref:Meziantou.Framework.Imaging.ConvolutionOptions.PreserveAlpha) | `false` | By default, colors are filtered premultiplied by alpha and alpha is filtered too, so that hidden colors never bleed. A matrix whose weights add up to zero then turns every pixel transparent: set `true` to filter the colors as stored and keep alpha |
| [WorkingSpace](cref:Meziantou.Framework.Imaging.ConvolutionOptions.WorkingSpace) | [Encoded](cref:Meziantou.Framework.Imaging.ConvolutionWorkingSpace.Encoded) | [Encoded](cref:Meziantou.Framework.Imaging.ConvolutionWorkingSpace.Encoded) filters the stored values; [LinearSrgb](cref:Meziantou.Framework.Imaging.ConvolutionWorkingSpace.LinearSrgb) filters linear light, as for [resizing](03-Resizing.md) |
| [Bias](cref:Meziantou.Framework.Imaging.ConvolutionOptions.Bias) | 0 | Added to every color sum, as a fraction of the range: 0.5 shows the negative sums of a gradient or an emboss as darker than mid-gray instead of black |

The sums are computed in double precision at the storage precision, then rounded and clamped to the range of the
samples: negative sums become 0. A separable matrix, whose weights are the products of a row and a column of weights
(a box, a binomial, Sobel), is applied as two passes, rows then columns: its cost grows with the sum of its
dimensions instead of their product.
`ConvolutionKernel.CreateSeparable(rowWeights, columnWeights)` makes one, and [IsSeparable](cref:Meziantou.Framework.Imaging.ConvolutionKernel.IsSeparable) tells whether a matrix is
one.

## Detect edges

`DetectEdges(detector)` replaces every frame by its edges: bright where the colors change, black where they are flat:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_DetectEdges;format=md-fenced-code-block;dedent=true -->
```csharp
public static void DetectEdges(string inputPath, string edgesPath)
{
    using var image = Image.Load(inputPath);

    // White edges on black, computed on the brightness (grayscale: false keeps the edges of every color); alpha is kept
    image.DetectEdges(EdgeDetector.Sobel);
    image.Save(edgesPath);
}
```
<!-- endref -->

| [EdgeDetector](cref:Meziantou.Framework.Imaging.EdgeDetector) | Strength of an edge |
| --- | --- |
| [Sobel](cref:Meziantou.Framework.Imaging.EdgeDetector.Sobel), [Scharr](cref:Meziantou.Framework.Imaging.EdgeDetector.Scharr), [Prewitt](cref:Meziantou.Framework.Imaging.EdgeDetector.Prewitt) | The magnitude of the horizontal and vertical gradients |
| [Kirsch](cref:Meziantou.Framework.Imaging.EdgeDetector.Kirsch), [Robinson](cref:Meziantou.Framework.Imaging.EdgeDetector.Robinson) | The largest response of the eight directions of a compass |
| [Laplacian3x3](cref:Meziantou.Framework.Imaging.EdgeDetector.Laplacian3x3), [Laplacian5x5](cref:Meziantou.Framework.Imaging.EdgeDetector.Laplacian5x5), [LaplacianOfGaussian](cref:Meziantou.Framework.Imaging.EdgeDetector.LaplacianOfGaussian) | The absolute value of a Laplacian |

By default, [DetectEdges](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.DetectEdges) converts every frame to grayscale first, which gives white edges on black; with
`grayscale: false`, every color has its own edges. Alpha is kept. The edge mode [Clamp](cref:Meziantou.Framework.Imaging.ConvolutionEdgeMode.Clamp) (default) finds no edge along
the border of the image, and [Zero](cref:Meziantou.Framework.Imaging.ConvolutionEdgeMode.Zero) reads black beyond it.

## Precision and cost

16-bit images are filtered with 16-bit precision, and the colors of floating-point images are not clamped. The working
memory is charged to the allocation limit of the image. [Convolve](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Convolve), the blurs and [Sharpen](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Sharpen) rent it before any pixel
changes, so an exceeded [limit](../05-LoadingAndSaving/07-ResourceLimits.md) leaves the image unchanged. The EXIF
thumbnail, now stale, is removed.

The cost of [GaussianBlur](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.GaussianBlur) grows with its deviation, the cost of [BoxBlur](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.BoxBlur) does not depend on its radius while its square fits in the image (beyond, each row and column costs a time proportional to the radius), and the cost
of a matrix grows with its number of non-zero weights, unless it is separable. Large frames can be split between
several threads: see [Performance](../11-Performance.md).
