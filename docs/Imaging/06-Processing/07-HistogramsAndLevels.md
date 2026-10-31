---
title: Histograms, levels and curves
description: Count the pixels of every level, equalize a histogram (CLAHE), stretch the levels, and apply gamma, levels, curves and color lookup tables.
---

A histogram counts the pixels of every level of a channel. [Equalize](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Equalize) and [AutoLevels](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.AutoLevels) remap the levels from such
histograms to raise the contrast of an image, in place.

## Enhance the contrast

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_Histogram;format=md-fenced-code-block;dedent=true -->
```csharp
public static ImageHistogram EnhanceContrast(string inputPath, string outputPath)
{
    using var image = Image.Load(inputPath);

    // Stretch the luminance between its 0.5th and 99.5th percentiles: a few very dark or very bright pixels do not
    // limit the stretch. The hues are kept.
    image.AutoLevels(new AutoLevelsOptions { LowPercentile = 0.5, HighPercentile = 99.5 });

    // Raise the local contrast: each of 8 x 8 tiles is equalized with a contrast limit, and the tiles are blended
    // without seams. The frames of an animation share their histograms, so that they do not flicker.
    image.Equalize(new HistogramEqualizationOptions
    {
        Mode = HistogramEqualizationMode.Adaptive,
        ClipLimit = 2,
        ShareHistogramAcrossFrames = true,
    });

    image.Save(outputPath);

    // The number of pixels at each luminance level, over every frame
    return image.ComputeHistogram(HistogramChannel.Luminance);
}
```
<!-- endref -->

## Read a histogram

`ComputeHistogram(channel)` counts the pixels of every level of a [HistogramChannel](cref:Meziantou.Framework.Imaging.HistogramChannel): [Luminance](cref:Meziantou.Framework.Imaging.HistogramColorMode.Luminance) (the Rec. 709 luma
that [Grayscale](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Grayscale) stores), [Red](cref:Meziantou.Framework.Imaging.HistogramChannel.Red), [Green](cref:Meziantou.Framework.Imaging.HistogramChannel.Green), [Blue](cref:Meziantou.Framework.Imaging.HistogramChannel.Blue) or [Alpha](cref:Meziantou.Framework.Imaging.HistogramChannel.Alpha). On an image, it counts every frame and the poster; on a
frame, that frame only. It never changes the image.

The [ImageHistogram](cref:Meziantou.Framework.Imaging.ImageHistogram) has one bin per level: [BinCount](cref:Meziantou.Framework.Imaging.ImageHistogram.BinCount) is 256 for 8-bit samples and 65,536 for 16-bit samples.
[Counts](cref:Meziantou.Framework.Imaging.ImageHistogram.Counts), or the indexer, gives the number of pixels of each level, and [TotalCount](cref:Meziantou.Framework.Imaging.ImageHistogram.TotalCount) their sum. [GetOtsuThreshold()](cref:Meziantou.Framework.Imaging.ImageHistogram.GetOtsuThreshold)
returns the level that best separates the dark pixels from the bright ones (see [Binarization](09-Binarization.md)).

## Stretch the levels

[AutoLevels](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.AutoLevels) stretches the levels between two percentiles of the histogram linearly to the whole range: a dull or
foggy photo gets its full contrast back.

| [AutoLevelsOptions](cref:Meziantou.Framework.Imaging.AutoLevelsOptions) | Default | Meaning |
| --- | --- | --- |
| [LowPercentile](cref:Meziantou.Framework.Imaging.AutoLevelsOptions.LowPercentile), [HighPercentile](cref:Meziantou.Framework.Imaging.AutoLevelsOptions.HighPercentile) | 0 and 100 | The levels that become black and white. The defaults are the darkest and the brightest levels present; 0.5 and 99.5 ignore a few extreme pixels |
| [ColorMode](cref:Meziantou.Framework.Imaging.AutoLevelsOptions.ColorMode) | [Luminance](cref:Meziantou.Framework.Imaging.HistogramColorMode.Luminance) | See [Colors](#colors) below |
| [ShareHistogramAcrossFrames](cref:Meziantou.Framework.Imaging.AutoLevelsOptions.ShareHistogramAcrossFrames) | `false` | Gives every frame of an animation the mapping of their combined histograms |

## Equalize the histogram

[Equalize](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Equalize) spreads the levels so that they are used evenly, which reveals the details of dark or bright areas.

- **Global equalization** ([HistogramEqualizationMode.Global](cref:Meziantou.Framework.Imaging.HistogramEqualizationMode.Global), the default) maps every level through the cumulative
  histogram of the frame.
- **Adaptive equalization** ([HistogramEqualizationMode.Adaptive](cref:Meziantou.Framework.Imaging.HistogramEqualizationMode.Adaptive), also called CLAHE) equalizes each tile of a grid
  with its own histogram, clipped to limit the contrast, and blends the tiles without seams. It raises the local
  contrast, for example of a photo with both shadows and highlights.

| [HistogramEqualizationOptions](cref:Meziantou.Framework.Imaging.HistogramEqualizationOptions) | Default | Meaning |
| --- | --- | --- |
| [Mode](cref:Meziantou.Framework.Imaging.HistogramEqualizationOptions.Mode) | [Global](cref:Meziantou.Framework.Imaging.HistogramEqualizationMode.Global) | [Global](cref:Meziantou.Framework.Imaging.HistogramEqualizationMode.Global) or [Adaptive](cref:Meziantou.Framework.Imaging.HistogramEqualizationMode.Adaptive) |
| [TileCountX](cref:Meziantou.Framework.Imaging.HistogramEqualizationOptions.TileCountX), [TileCountY](cref:Meziantou.Framework.Imaging.HistogramEqualizationOptions.TileCountY) | 8 | The grid of the adaptive mode |
| [ClipLimit](cref:Meziantou.Framework.Imaging.HistogramEqualizationOptions.ClipLimit) | 2 | The adaptive mode clips each histogram at this many times the average bin height, and spreads the excess over every bin: a lower value limits the contrast more |
| [ColorMode](cref:Meziantou.Framework.Imaging.HistogramEqualizationOptions.ColorMode) | [Luminance](cref:Meziantou.Framework.Imaging.HistogramColorMode.Luminance) | See [Colors](#colors) below |
| [ShareHistogramAcrossFrames](cref:Meziantou.Framework.Imaging.HistogramEqualizationOptions.ShareHistogramAcrossFrames) | `false` | Gives every frame of an animation the mapping of their combined histograms |

## Colors

[HistogramColorMode.Luminance](cref:Meziantou.Framework.Imaging.HistogramColorMode.Luminance), the default, remaps the luminance and adds its change to the red, green and blue
samples: the hues are kept, except where a sample is clamped. [HistogramColorMode.PerChannel](cref:Meziantou.Framework.Imaging.HistogramColorMode.PerChannel) remaps each channel from
its own histogram, which also neutralizes a color cast, and can change the hues.

## Animations

Each frame is remapped from its own histogram by default, so the frames of a video-like animation can flicker when
their content changes. [ShareHistogramAcrossFrames](cref:Meziantou.Framework.Imaging.HistogramEqualizationOptions.ShareHistogramAcrossFrames) gives every frame the same mapping, computed from the histograms of
all of them.

## Pixel formats

The computation is made on the stored samples, at their precision, in exact integer arithmetic (except the blend of the
adaptive mode): the results are the same on every platform. [Equalize](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Equalize) and [AutoLevels](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.AutoLevels) do not count fully transparent
pixels, but they remap them. Floating-point formats ([RgbaFloat](cref:Meziantou.Framework.Imaging.RgbaFloat), [RgbaHalf](cref:Meziantou.Framework.Imaging.RgbaHalf)) have no discrete levels:
[ComputeHistogram](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.ComputeHistogram), [Equalize](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Equalize) and [AutoLevels](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.AutoLevels) throw an [UnsupportedImageFeatureException](cref:Meziantou.Framework.Imaging.UnsupportedImageFeatureException) on them, so convert them to
[Rgba64](cref:Meziantou.Framework.Imaging.Rgba64) first with [CloneAs\<Rgba64\>()](cref:Meziantou.Framework.Imaging.Image.CloneAs). The mapping tables are charged to the
allocation limit of the image, and are allocated before any pixel changes.

## Adjust the levels and the curves

[ApplyToneCurve](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.ApplyToneCurve) gives a new value to the red, green and blue samples with a [ToneCurve](cref:Meziantou.Framework.Imaging.ToneCurve), the adjustments of photo
editors, and [ApplyToneCurves](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.ApplyToneCurves) with one curve per channel, which corrects or creates a color cast:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_ToneCurves;format=md-fenced-code-block;dedent=true -->
```csharp
public static void GradePhoto(string inputPath, string cubePath, string outputPath)
{
    using var image = Image.Load(inputPath);

    // Levels clip the darkest and the brightest 4% of the levels and brighten the midtones; a gentle S curve through
    // control points then adds contrast, in the same pass
    var levels = ToneCurve.CreateLevels(inputBlack: 0.04, inputWhite: 0.96, gamma: 1.2);
    var contrast = ToneCurve.CreateFromPoints(new PointD(0, 0), new PointD(0.25, 0.2), new PointD(0.75, 0.82), new PointD(1, 1));
    image.ApplyToneCurve(levels.Then(contrast));

    // A curve per channel warms the colors: more red, less blue
    image.ApplyToneCurves(red: ToneCurve.CreateGamma(1.1), green: ToneCurve.Identity, blue: ToneCurve.CreateGamma(0.9));

    // A look exported by a grading tool as a .cube file, interpolated between the points of its lattice
    var look = CubeLut.Load(cubePath);
    image.ApplyLut(look, LutInterpolation.Tetrahedral);
    image.Save(outputPath);
}
```
<!-- endref -->

| [ToneCurve](cref:Meziantou.Framework.Imaging.ToneCurve) | What it does |
| --- | --- |
| [CreateGamma](cref:Meziantou.Framework.Imaging.ToneCurve.CreateGamma) | A gamma above 1 brightens the midtones and keeps black and white, as ImageMagick's `-gamma` |
| [CreateLevels](cref:Meziantou.Framework.Imaging.ToneCurve.CreateLevels) | The levels of Photoshop and GIMP: the input range becomes the output range, with a gamma for the midtones; what is outside the input range is clipped |
| [CreateFromPoints](cref:Meziantou.Framework.Imaging.ToneCurve.CreateFromPoints) | A smooth curve through control points (a monotone cubic, which never overshoots its points nor inverts the tones between two of them), flat before the first point and after the last one |
| [CreateFromTable](cref:Meziantou.Framework.Imaging.ToneCurve.CreateFromTable) | A one-dimensional lookup table: values at evenly spaced inputs from 0 to 1, interpolated linearly |
| [Then](cref:Meziantou.Framework.Imaging.ToneCurve.Then) | Two curves one after the other, applied in one pass |

The values are fractions of the largest sample value, so a curve works the same on 8-bit, 16-bit and floating-point
images. The stored samples are mapped, as photo editors do, without a color profile; alpha is kept, and a gray image
stores the luma of its mapped color. Floating-point samples are never clamped: a gamma keeps the highlights above 1,
while the levels and the curves through points clip them.

## Apply a color lookup table

[ApplyLut](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.ApplyLut) gives every pixel the color of a [CubeLut](cref:Meziantou.Framework.Imaging.CubeLut), the `.cube` files that grading tools export for a film
look or a camera conversion: [CubeLut.Load](cref:Meziantou.Framework.Imaging.CubeLut.Load) reads a file or a stream, and [CubeLut.Parse](cref:Meziantou.Framework.Imaging.CubeLut.Parse) a text. A
three-dimensional table gives each color a new one, interpolated between the points of its lattice with
[LutInterpolation.Tetrahedral](cref:Meziantou.Framework.Imaging.LutInterpolation.Tetrahedral) (the default, which keeps grays on the gray axis) or [LutInterpolation.Trilinear](cref:Meziantou.Framework.Imaging.LutInterpolation.Trilinear); a one-dimensional table
maps each channel on its own. The colors are clamped to the domain of the table on input. A malformed file throws a
`FormatException` that gives the line.

