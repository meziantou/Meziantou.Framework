---
title: Crop and auto-crop
description: Keep a rectangle of an image, trim its uniform background, add a border or place it on a larger canvas.
---

These operations change the canvas without resampling: the pixels they keep are copied unchanged, 16-bit samples, alpha
and floating-point values included. They apply to every frame and to the poster frame, and leave the image unchanged
when they fail.

## Crop to a rectangle

`Crop` keeps a `Rectangle` of the canvas. `ExtendCanvas` does the opposite: it makes the canvas larger and places the
image on it at one of nine `ResizeAnchor` positions:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/ProcessingExamples.cs;region=Imaging_CropAndExtendCanvas;format=md-fenced-code-block;dedent=true -->
```csharp
public static void CropAndExtendCanvas(string inputPath, string outputPath)
{
    using var image = Image.Load(inputPath);

    // The largest centered square. The rectangle must be entirely inside the canvas: it is never clamped
    var side = Math.Min(image.Width, image.Height);
    image.Crop(new Rectangle((image.Width - side) / 2, (image.Height - side) / 2, side, side));

    // A canvas twice as wide, the square on its left. The new area is opaque white, which every pixel format stores
    image.ExtendCanvas(new Size(side * 2, side), ResizeAnchor.Left, Colors.White);
    image.Save(outputPath);
}
```
<!-- endref -->

- The rectangle of `Crop` must not be empty and must be entirely inside the canvas: it is never clamped, and a
  rectangle that reaches outside throws an `ArgumentOutOfRangeException`. Compute it from `image.Width` and
  `image.Height`.
- The coordinates are those of the stored pixels. A photo whose EXIF orientation says it is shown turned is cropped as
  stored: call `AutoOrient` first to crop what the viewer sees. See
  [Rotate, flip and transform](04-RotateFlipAndTransform.md).
- Each dimension given to `ExtendCanvas` must be at least that of the image (use `Crop` to make it smaller). A centered
  image gets the extra pixel of an odd padding on its right or bottom side.

## Add a border

`Pad(left, top, right, bottom, backgroundColor)` adds the given numbers of rows and columns on each side:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_Letterbox;format=md-fenced-code-block;dedent=true -->
```csharp
public static void Letterbox(string inputPath, string outputPath)
{
    using var image = Image.Load(inputPath);

    // Fits inside 320x240, then pads to exactly 320x240 with black bars, the image centered (ResizeOptions.Anchor).
    // The padding is transparent by default, which a pixel format without alpha (an RGB JPEG) cannot store
    image.Resize(new ResizeOptions(320, 240) { Mode = ResizeMode.Pad, BackgroundColor = new Rgba64(0, 0, 0) });

    // A 4-pixel white frame on every side: 328x248
    image.Pad(4, 4, 4, 4, new Rgba64(65535, 65535, 65535));
    image.Save(outputPath);
}
```
<!-- endref -->

The new area of `Pad` and `ExtendCanvas` is transparent black by default. A pixel format without alpha (`Rgb24`,
`Gray8`, `Gray16`, the formats of most JPEG images) cannot store it: pass an opaque color, or the operation throws an
`UnsupportedImageFeatureException`. The `Resize` call of this example is described in [Resizing](03-Resizing.md).

## Trim the background

`AutoCrop` removes the uniform background around the content of an image, such as the margin of a product photo or of
a scan, and returns whether the image changed. `AnalyzeAutoCrop` runs the detection alone and never changes the image:
its `AutoCropAnalysis` tells whether it found content (`Success`), where (`Bounds`) and on which background color
(`BackgroundColor`). Pass the analysis to `AutoCrop` to apply it, to this image or to another image of the same size:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_AutoCrop;format=md-fenced-code-block;dedent=true -->
```csharp
public static bool TrimBackground(string inputPath, string outputPath)
{
    using var image = Image.Load(inputPath);

    // The detection works on the stored pixels: apply the EXIF orientation first
    image.AutoOrient();

    // Read-only: the background color and the bounding box of everything else, in every frame
    var options = new AutoCropOptions { PaddingX = 8, PaddingY = 8, BucketThreshold = 0.945 };
    var analysis = image.AnalyzeAutoCrop(options);
    if (!analysis.Success)
        return false; // no uniform border, or no content of at least 3x3 pixels

    // Keeps 8 pixels around the content. Where the canvas is too small for the margin, it is enlarged and filled with
    // the background color (AutoCropPaddingMode.Contain clamps the margin instead)
    var changed = image.AutoCrop(analysis, options);
    image.Save(outputPath);
    return changed;
}
```
<!-- endref -->

The detection works in two steps:

1. **The background** is the most frequent color of the one-pixel outer border of the image, over every frame and the
   poster. The border is accepted when it has fewer than `ColorThreshold` distinct colors. Otherwise, the detection is
   retried once with half the threshold, without the outer 5% of the image on each side.
2. **The content** is the bounding box of the pixels that are not background, over every frame and the poster, so that
   an animation is cropped the same way on every frame. It must be at least 3x3 pixels.

| Option | Default | Meaning |
| --- | --- | --- |
| `ColorThreshold` | 35 | The largest color difference, on the 8-bit scale and weighted by luma, at which a pixel is still background. Also the number of distinct border colors below which the border is accepted |
| `BucketThreshold` | not set | For noisy and JPEG backgrounds: the border is also accepted when this fraction of its pixels have about the same brightness as the background. 0.945 is a good start |
| `PaddingX`, `PaddingY` | 0 | A margin to keep around the content, in pixels, made of the original pixels |
| `PaddingMode` | `Expand` | Where the margin reaches outside the canvas: `Expand` enlarges the canvas and fills the new area with the background color, `Contain` clamps the margin to the canvas |
| `AnalyzeWeights` | `false` | Measures on which side the content is heavier (`WeightX` and `WeightY`, from -1 to 1) and moves the margin that way |

Fully transparent pixels are all equal, whatever color they hide, so a transparent margin is trimmed like an opaque
one. When the image has no uniform border or no content, `AnalyzeAutoCrop` returns an analysis whose `Success` is
`false`, and `AutoCrop` returns `false` without changing the image.

On an `Image<TPixel>`, `AnalyzeAutoCrop` returns an `AutoCropAnalysis<TPixel>`, whose `BackgroundColor` is a pixel of
the image as stored; on an `Image`, it is the same color as an `Rgba64`.

## Cursors

A cursor hotspot moves with the pixel it points at. A crop or an auto-crop that would remove that pixel throws an
`UnsupportedImageFeatureException` and leaves the image unchanged: set the hotspot (`Frames[i].Metadata.Hotspot`) to
`null` or to a pixel that is kept first.
