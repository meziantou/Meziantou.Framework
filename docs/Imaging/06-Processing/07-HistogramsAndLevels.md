---
title: Histograms and levels
description: Count the pixels of every level, equalize a histogram globally or locally (CLAHE), and stretch the levels of a dull photo.
---

A histogram counts the pixels of every level of a channel. `Equalize` and `AutoLevels` remap the levels from such
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

`ComputeHistogram(channel)` counts the pixels of every level of a `HistogramChannel`: `Luminance` (the Rec. 709 luma
that `Grayscale` stores), `Red`, `Green`, `Blue` or `Alpha`. On an image, it counts every frame and the poster; on a
frame, that frame only. It never changes the image.

The `ImageHistogram` has one bin per level: `BinCount` is 256 for 8-bit samples and 65,536 for 16-bit samples.
`Counts`, or the indexer, gives the number of pixels of each level, and `TotalCount` their sum. `GetOtsuThreshold()`
returns the level that best separates the dark pixels from the bright ones (see [Binarization](09-Binarization.md)).

## Stretch the levels

`AutoLevels` stretches the levels between two percentiles of the histogram linearly to the whole range: a dull or
foggy photo gets its full contrast back.

| `AutoLevelsOptions` | Default | Meaning |
| --- | --- | --- |
| `LowPercentile`, `HighPercentile` | 0 and 100 | The levels that become black and white. The defaults are the darkest and the brightest levels present; 0.5 and 99.5 ignore a few extreme pixels |
| `ColorMode` | `Luminance` | See [Colors](#colors) below |
| `ShareHistogramAcrossFrames` | `false` | Gives every frame of an animation the mapping of their combined histograms |

## Equalize the histogram

`Equalize` spreads the levels so that they are used evenly, which reveals the details of dark or bright areas.

- **Global equalization** (`HistogramEqualizationMode.Global`, the default) maps every level through the cumulative
  histogram of the frame.
- **Adaptive equalization** (`HistogramEqualizationMode.Adaptive`, also called CLAHE) equalizes each tile of a grid
  with its own histogram, clipped to limit the contrast, and blends the tiles without seams. It raises the local
  contrast, for example of a photo with both shadows and highlights.

| `HistogramEqualizationOptions` | Default | Meaning |
| --- | --- | --- |
| `Mode` | `Global` | `Global` or `Adaptive` |
| `TileCountX`, `TileCountY` | 8 | The grid of the adaptive mode |
| `ClipLimit` | 2 | The adaptive mode clips each histogram at this many times the average bin height, and spreads the excess over every bin: a lower value limits the contrast more |
| `ColorMode` | `Luminance` | See [Colors](#colors) below |
| `ShareHistogramAcrossFrames` | `false` | Gives every frame of an animation the mapping of their combined histograms |

## Colors

`HistogramColorMode.Luminance`, the default, remaps the luminance and adds its change to the red, green and blue
samples: the hues are kept, except where a sample is clamped. `HistogramColorMode.PerChannel` remaps each channel from
its own histogram, which also neutralizes a color cast, and can change the hues.

## Animations

Each frame is remapped from its own histogram by default, so the frames of a video-like animation can flicker when
their content changes. `ShareHistogramAcrossFrames` gives every frame the same mapping, computed from the histograms of
all of them.

## Pixel formats

The computation is made on the stored samples, at their precision, in exact integer arithmetic (except the blend of the
adaptive mode): the results are the same on every platform. `Equalize` and `AutoLevels` do not count fully transparent
pixels, but they remap them. Floating-point formats (`RgbaFloat`, `RgbaHalf`) have no discrete levels:
`ComputeHistogram`, `Equalize` and `AutoLevels` throw an `UnsupportedImageFeatureException` on them, so convert them to
`Rgba64` first with `CloneAs<Rgba64>()`. The mapping tables are charged to the
allocation limit of the image, and are allocated before any pixel changes.
