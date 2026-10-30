---
title: Resizing
description: Resize an image or an animation, keep the aspect ratio, fill a box, letterbox, and choose the resampling filter.
---

[Resize](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Resize) resizes every frame of an image and its poster frame, in one operation: an animation keeps its frames, their
durations and its loop count.

## Fit inside a box

By default, [Resize](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Resize) fits the image inside the size of its [ResizeOptions](cref:Meziantou.Framework.Imaging.ResizeOptions) and keeps the aspect ratio:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_ResizeAnimation;format=md-fenced-code-block;dedent=true -->
```csharp
public static void ResizeAnimation(string inputPath, string outputPath)
{
    using var image = Image.Load(inputPath);
    image.Resize(new ResizeOptions(320, 240) { Mode = ResizeMode.Contain, Filter = ResamplingFilter.Lanczos3 });

    // Same format as the extension; APNG/GIF timing and play count come from the decoded image
    image.Save(outputPath);
}
```
<!-- endref -->

A 400x200 image resized into 320x240 becomes 320x160. The image is enlarged when it is smaller than the box: set
`AllowUpscaling = false` to keep it at its size instead.

## Fill a box exactly

[ResizeMode.Cover](cref:Meziantou.Framework.Imaging.ResizeMode.Cover) scales the image to cover the whole box and cuts what overflows. [Anchor](cref:Meziantou.Framework.Imaging.ResizeOptions.Anchor) says which part is kept:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/ProcessingExamples.cs;region=Imaging_ResizeToFill;format=md-fenced-code-block;dedent=true -->
```csharp
public static void ResizeToFill(string inputPath, string outputPath)
{
    using var image = Image.Load(inputPath);

    // Exactly 200x200: the image is scaled to cover the box and the overflow is cut. The top anchor keeps the top of
    // a portrait, and cuts its bottom
    image.Resize(new ResizeOptions(200, 200) { Mode = ResizeMode.Cover, Anchor = ResizeAnchor.Top });
    image.Save(outputPath);
}
```
<!-- endref -->

## Letterbox

[ResizeMode.Pad](cref:Meziantou.Framework.Imaging.ResizeMode.Pad) fits the image inside the box, then pads it to exactly the size of the box with [BackgroundColor](cref:Meziantou.Framework.Imaging.ResizeOptions.BackgroundColor), the
image placed at [Anchor](cref:Meziantou.Framework.Imaging.ResizeOptions.Anchor):

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

The padding is transparent black by default. A pixel format without alpha ([Rgb24](cref:Meziantou.Framework.Imaging.Rgb24), [Gray8](cref:Meziantou.Framework.Imaging.Gray8), [Gray16](cref:Meziantou.Framework.Imaging.Gray16), the formats of
most JPEG images) cannot store it: set an opaque [BackgroundColor](cref:Meziantou.Framework.Imaging.ResizeOptions.BackgroundColor), or [Resize](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Resize) throws an
[UnsupportedImageFeatureException](cref:Meziantou.Framework.Imaging.UnsupportedImageFeatureException). A centered image gets the extra pixel of an odd padding on its right or bottom
side. The padding never blends into the edges of the image.

## Modes

| [Mode](cref:Meziantou.Framework.Imaging.ResizeOptions.Mode) | Result | Size of the result |
| --- | --- | --- |
| [Contain](cref:Meziantou.Framework.Imaging.ResizeMode.Contain) (default) | Fits inside the box, keeps the aspect ratio | At most the box; one side matches it |
| [Cover](cref:Meziantou.Framework.Imaging.ResizeMode.Cover) | Covers the box, keeps the aspect ratio, cuts the overflow around [Anchor](cref:Meziantou.Framework.Imaging.ResizeOptions.Anchor) | Exactly the box |
| [Stretch](cref:Meziantou.Framework.Imaging.ResizeMode.Stretch) | Fills the box, changes the aspect ratio | Exactly the box |
| [Pad](cref:Meziantou.Framework.Imaging.ResizeMode.Pad) | Fits inside the box like [Contain](cref:Meziantou.Framework.Imaging.ResizeMode.Contain), then pads it at [Anchor](cref:Meziantou.Framework.Imaging.ResizeOptions.Anchor) | Exactly the box |
| [BoxPad](cref:Meziantou.Framework.Imaging.ResizeMode.BoxPad) | Like [Pad](cref:Meziantou.Framework.Imaging.ResizeMode.Pad), but an image that already fits is only placed on the canvas, never enlarged | Exactly the box |

[Anchor](cref:Meziantou.Framework.Imaging.ResizeOptions.Anchor) is one of the nine [ResizeAnchor](cref:Meziantou.Framework.Imaging.ResizeAnchor) positions ([Center](cref:Meziantou.Framework.Imaging.ResizeAnchor.Center) by default, [TopLeft](cref:Meziantou.Framework.Imaging.ResizeAnchor.TopLeft), [Top](cref:Meziantou.Framework.Imaging.ResizeAnchor.Top), [TopRight](cref:Meziantou.Framework.Imaging.ResizeAnchor.TopRight), [Left](cref:Meziantou.Framework.Imaging.ResizeAnchor.Left),
[Right](cref:Meziantou.Framework.Imaging.ResizeAnchor.Right), [BottomLeft](cref:Meziantou.Framework.Imaging.ResizeAnchor.BottomLeft), [Bottom](cref:Meziantou.Framework.Imaging.ResizeAnchor.Bottom), [BottomRight](cref:Meziantou.Framework.Imaging.ResizeAnchor.BottomRight)).

`AllowUpscaling = false` keeps [Contain](cref:Meziantou.Framework.Imaging.ResizeMode.Contain), [Pad](cref:Meziantou.Framework.Imaging.ResizeMode.Pad) and [BoxPad](cref:Meziantou.Framework.Imaging.ResizeMode.BoxPad) within the original size (a "max" resize), and makes
[Cover](cref:Meziantou.Framework.Imaging.ResizeMode.Cover) and [Stretch](cref:Meziantou.Framework.Imaging.ResizeMode.Stretch) throw an `ArgumentException` instead of enlarging. `AllowDownscaling = false` keeps [Contain](cref:Meziantou.Framework.Imaging.ResizeMode.Contain) at
or above the original size (a "min" resize), and makes the other modes throw an `ArgumentException` instead of
reducing.

The target size is computed with exact arithmetic, rounded to the nearest pixel, and is at least 1 pixel. A resize to
the current size leaves the image unchanged.

## Resampling filters

[Filter](cref:Meziantou.Framework.Imaging.ResizeOptions.Filter) chooses how the new pixels are computed from the old ones:

| Filter | Kernel | Radius | Interpolating |
| --- | --- | --- | --- |
| [NearestNeighbor](cref:Meziantou.Framework.Imaging.ResamplingFilter.NearestNeighbor) | The pixel that contains the center: no new color is created | - | yes |
| [Box](cref:Meziantou.Framework.Imaging.ResamplingFilter.Box) | The average of the pixels in the footprint of the output pixel (exact for integer reductions) | 1/2 | yes |
| [Bilinear](cref:Meziantou.Framework.Imaging.ResamplingFilter.Bilinear) | Triangle | 1 | yes |
| [Hermite](cref:Meziantou.Framework.Imaging.ResamplingFilter.Hermite) | Cubic, B = 0, C = 0 | 1 | yes |
| [Bicubic](cref:Meziantou.Framework.Imaging.ResamplingFilter.Bicubic) (default) | Catmull-Rom, cubic B = 0, C = 1/2 | 2 | yes |
| [MitchellNetravali](cref:Meziantou.Framework.Imaging.ResamplingFilter.MitchellNetravali) | Cubic, B = C = 1/3 | 2 | no |
| [Robidoux](cref:Meziantou.Framework.Imaging.ResamplingFilter.Robidoux), [RobidouxSharp](cref:Meziantou.Framework.Imaging.ResamplingFilter.RobidouxSharp) | Keys cubics | 2 | no |
| [Spline](cref:Meziantou.Framework.Imaging.ResamplingFilter.Spline) | Cubic B-spline, B = 1, C = 0 | 2 | no |
| [Lanczos2](cref:Meziantou.Framework.Imaging.ResamplingFilter.Lanczos2), [Lanczos3](cref:Meziantou.Framework.Imaging.ResamplingFilter.Lanczos3), [Lanczos5](cref:Meziantou.Framework.Imaging.ResamplingFilter.Lanczos5), [Lanczos8](cref:Meziantou.Framework.Imaging.ResamplingFilter.Lanczos8) | Sinc windowed by sinc, with 2, 3, 5 or 8 lobes | 2, 3, 5, 8 | yes |

The filters are sampled at the pixel centers, with the edges of the image repeated. When an image is reduced, the
radius of the filter widens with the reduction. A non-interpolating filter also smooths an axis that keeps its size,
for example the width of an image whose height only changes.

## Transparency and color space

Colors are filtered premultiplied by alpha: the hidden color of a transparent pixel never bleeds into its neighbors,
and a pixel whose alpha becomes zero is transparent black. A frame whose pixels are all opaque stays opaque. 16-bit
images are filtered with 16-bit precision, and the colors of floating-point samples are not clamped.

[WorkingSpace](cref:Meziantou.Framework.Imaging.ResizeOptions.WorkingSpace) chooses the values that are averaged:

- [ResizeWorkingSpace.Encoded](cref:Meziantou.Framework.Imaging.ResizeWorkingSpace.Encoded) (default) filters the stored sample values, like most image tools.
- [ResizeWorkingSpace.LinearSrgb](cref:Meziantou.Framework.Imaging.ResizeWorkingSpace.LinearSrgb) converts the samples to linear light first and back afterward, which gives more
  physically correct averages, for example for thin bright lines. An untagged image and an ICC profile recognized as
  sRGB or sGray use the sRGB transfer function; any other ICC profile is rejected rather than misinterpreted: convert
  such an image first with [ConvertColorProfile](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.ConvertColorProfile) (see [Color management](../09-Color/03-ColorManagement.md)).

## Cursors and metadata

A cursor hotspot moves to `floor(x * newWidth / width)` horizontally, and likewise vertically, as Windows scales a
cursor. A [Cover](cref:Meziantou.Framework.Imaging.ResizeMode.Cover) resize that cuts the pixel of the hotspot throws an [UnsupportedImageFeatureException](cref:Meziantou.Framework.Imaging.UnsupportedImageFeatureException) and leaves
the image unchanged. The EXIF dimensions are updated and the EXIF thumbnail is removed.

For thumbnails of many images and for large images, see also [Create thumbnails](../03-Guides/02-CreateThumbnails.md)
and [Performance](../11-Performance.md).
