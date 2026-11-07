---
title: Crop and auto-crop
description: Keep a rectangle of an image, trim its uniform background, find the crop of an aspect ratio that keeps its subject, add a border or place it on a larger canvas.
---

These operations change the canvas without resampling: the pixels they keep are copied unchanged, 16-bit samples, alpha
and floating-point values included. They apply to every frame and to the poster frame, and leave the image unchanged
when they fail.

## Crop to a rectangle

[Crop](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Crop) keeps a [Rectangle](cref:Meziantou.Framework.Imaging.Rectangle) of the canvas. [ExtendCanvas](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.ExtendCanvas) does the opposite: it makes the canvas larger and places the
image on it at one of nine [ResizeAnchor](cref:Meziantou.Framework.Imaging.ResizeAnchor) positions:

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

- The rectangle of [Crop](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Crop) must not be empty and must be entirely inside the canvas: it is never clamped, and a
  rectangle that reaches outside throws an `ArgumentOutOfRangeException`. Compute it from `image.Width` and
  `image.Height`.
- The coordinates are those of the stored pixels. A photo whose EXIF orientation says it is shown turned is cropped as
  stored: call [AutoOrient](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.AutoOrient) first to crop what the viewer sees. See
  [Rotate, flip and transform](04-RotateFlipAndTransform.md).
- Each dimension given to [ExtendCanvas](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.ExtendCanvas) must be at least that of the image (use [Crop](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Crop) to make it smaller). A centered
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

The new area of [Pad](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Pad) and [ExtendCanvas](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.ExtendCanvas) is transparent black by default. A pixel format without alpha ([Rgb24](cref:Meziantou.Framework.Imaging.Rgb24),
[Gray8](cref:Meziantou.Framework.Imaging.Gray8), [Gray16](cref:Meziantou.Framework.Imaging.Gray16), the formats of most JPEG images) cannot store it: pass an opaque color, or the operation throws an
[UnsupportedImageFeatureException](cref:Meziantou.Framework.Imaging.UnsupportedImageFeatureException). The [Resize](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Resize) call of this example is described in [Resizing](03-Resizing.md).

## Trim the background

[AutoCrop](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.AutoCrop) removes the uniform background around the content of an image, such as the margin of a product photo or of
a scan, and returns whether the image changed. [AnalyzeAutoCrop](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.AnalyzeAutoCrop) runs the detection alone and never changes the image:
its [AutoCropAnalysis](cref:Meziantou.Framework.Imaging.AutoCropAnalysis) tells whether it found content ([Success](cref:Meziantou.Framework.Imaging.AutoCropAnalysis.Success)), where ([Bounds](cref:Meziantou.Framework.Imaging.AutoCropAnalysis.Bounds)) and on which background color
([BackgroundColor](cref:Meziantou.Framework.Imaging.AutoCropAnalysis.BackgroundColor)). Pass the analysis to [AutoCrop](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.AutoCrop) to apply it, to this image or to another image of the same size:

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
   poster. The border is accepted when it has fewer than [ColorThreshold](cref:Meziantou.Framework.Imaging.AutoCropOptions.ColorThreshold) distinct colors. Otherwise, the detection is
   retried once with half the threshold, without the outer 5% of the image on each side.
2. **The content** is the bounding box of the pixels that are not background, over every frame and the poster, so that
   an animation is cropped the same way on every frame. It must be at least 3x3 pixels.

| Option | Default | Meaning |
| --- | --- | --- |
| [ColorThreshold](cref:Meziantou.Framework.Imaging.AutoCropOptions.ColorThreshold) | 35 | The largest color difference, on the 8-bit scale and weighted by luma, at which a pixel is still background. Also the number of distinct border colors below which the border is accepted |
| [BucketThreshold](cref:Meziantou.Framework.Imaging.AutoCropOptions.BucketThreshold) | not set | For noisy and JPEG backgrounds: the border is also accepted when this fraction of its pixels have about the same brightness as the background. 0.945 is a good start |
| [PaddingX](cref:Meziantou.Framework.Imaging.AutoCropOptions.PaddingX), [PaddingY](cref:Meziantou.Framework.Imaging.AutoCropOptions.PaddingY) | 0 | A margin to keep around the content, in pixels, made of the original pixels |
| [PaddingMode](cref:Meziantou.Framework.Imaging.AutoCropOptions.PaddingMode) | [Expand](cref:Meziantou.Framework.Imaging.AutoCropPaddingMode.Expand) | Where the margin reaches outside the canvas: [Expand](cref:Meziantou.Framework.Imaging.AutoCropPaddingMode.Expand) enlarges the canvas and fills the new area with the background color, [Contain](cref:Meziantou.Framework.Imaging.AutoCropPaddingMode.Contain) clamps the margin to the canvas |
| [AnalyzeWeights](cref:Meziantou.Framework.Imaging.AutoCropOptions.AnalyzeWeights) | `false` | Measures on which side the content is heavier ([WeightX](cref:Meziantou.Framework.Imaging.AutoCropAnalysis.WeightX) and [WeightY](cref:Meziantou.Framework.Imaging.AutoCropAnalysis.WeightY), from -1 to 1) and moves the margin that way |

Fully transparent pixels are all equal, whatever color they hide, so a transparent margin is trimmed like an opaque
one. When the image has no uniform border or no content, [AnalyzeAutoCrop](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.AnalyzeAutoCrop) returns an analysis whose [Success](cref:Meziantou.Framework.Imaging.AutoCropAnalysis.Success) is
`false`, and [AutoCrop](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.AutoCrop) returns `false` without changing the image.

On an [Image\<TPixel\>](cref:Meziantou.Framework.Imaging.Image{TPixel}), [AnalyzeAutoCrop](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.AnalyzeAutoCrop) returns an [AutoCropAnalysis\<TPixel\>](cref:Meziantou.Framework.Imaging.AutoCropAnalysis{TPixel}), whose [BackgroundColor](cref:Meziantou.Framework.Imaging.AutoCropAnalysis.BackgroundColor) is a pixel of
the image as stored; on an [Image](cref:Meziantou.Framework.Imaging.Image), it is the same color as an [Rgba64](cref:Meziantou.Framework.Imaging.Rgba64).

## Crop to an aspect ratio around the subject

[SuggestCrop](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.SuggestCrop) finds where to cut an image to an aspect ratio, such as a square thumbnail or a 16:9
banner, without cutting its subject. It changes nothing: it returns the rectangle, to pass to
[Crop](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Crop).

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_SuggestCrop;format=md-fenced-code-block;dedent=true -->
```csharp
public static Rectangle CropToSquare(string inputPath, string outputPath)
{
    using var photo = Image.Load(inputPath);

    // The largest square that keeps the most detail; CropEnergy.Saliency keeps the colors that stand out instead
    var crop = photo.SuggestCrop(1, 1);
    photo.Crop(crop);
    photo.Save(outputPath);
    return crop;
}
```
<!-- endref -->

The rectangle is the largest of the ratio that fits in the image (a 4000x3000 photo gets 3000x3000 for 1:1 and 4000x2250
for 16:9), or [Scale](cref:Meziantou.Framework.Imaging.CropSuggestionOptions.Scale) times that to zoom on the subject. Among all its positions, it keeps
the one with the most content in every frame and the poster:

| [Energy](cref:Meziantou.Framework.Imaging.CropSuggestionOptions.Energy) | What counts as content |
| --- | --- |
| [CropEnergy.Edges](cref:Meziantou.Framework.Imaging.CropEnergy.Edges) (default) | The details: the differences of brightness and of alpha between neighboring pixels. Flat backgrounds, such as a sky or a studio wall, are cut first |
| [CropEnergy.Saliency](cref:Meziantou.Framework.Imaging.CropEnergy.Saliency) | The colors that stand out from the mean color of the image, even on a smooth subject |

The result is deterministic. When several positions keep the same content, the one nearest to the center wins, so an
image without any detail gets a centered crop. Transparent pixels have no content, whatever color they hide.

## Cursors

A cursor hotspot moves with the pixel it points at. A crop or an auto-crop that would remove that pixel throws an
[UnsupportedImageFeatureException](cref:Meziantou.Framework.Imaging.UnsupportedImageFeatureException) and leaves the image unchanged: set the hotspot (`Frames[i].Metadata.Hotspot`) to
`null` or to a pixel that is kept first.
