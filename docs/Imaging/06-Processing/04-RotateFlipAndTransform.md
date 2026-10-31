---
title: Rotate, flip and transform
description: Turn an image by quarter turns or by any angle, mirror it, skew it, correct a perspective and apply the EXIF orientation.
---

Quarter turns and mirrors move pixels without changing them. Rotations by any angle, skews and transformations compute
new pixels with a resampling filter, like a resize. All of them apply to every frame and to the poster frame.

## Quarter turns and mirrors

`Rotate(RotateMode)` turns the image clockwise by 90, 180 or 270 degrees, and `Flip(FlipMode)` mirrors it
horizontally or vertically:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/ProcessingExamples.cs;region=Imaging_RotateAndFlip;format=md-fenced-code-block;dedent=true -->
```csharp
public static void RotateAndFlip(string inputPath, string outputPath)
{
    using var image = Image.Load(inputPath);

    // Make the stored pixels upright first: a camera stores how to turn the photo in its EXIF orientation
    image.AutoOrient();

    // A clockwise quarter turn, then a mirror: every pixel is moved, none is resampled
    image.Rotate(RotateMode.Rotate90);
    image.Flip(FlipMode.Horizontal);
    image.Save(outputPath);
}
```
<!-- endref -->

These are exact pixel permutations: every sample, 16-bit low bits, floating-point values and alpha included, is
copied unchanged. A quarter turn swaps the width and the height, and the axes of the pixel aspect ratio. [Flip](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Flip) also
exists for one frame.

## Apply the EXIF orientation

A camera or a phone often stores the pixels as the sensor read them, with an EXIF orientation that tells viewers how
to turn them. [AutoOrient](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.AutoOrient) applies that orientation to the pixels (all eight EXIF orientations: mirrors, rotations and
the diagonal reflections), then sets it to [ExifOrientation.TopLeft](cref:Meziantou.Framework.Imaging.Metadata.ExifOrientation.TopLeft). It does nothing when the image is already
upright.

The other operations work on the stored pixels and keep the orientation as it is. Call [AutoOrient](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.AutoOrient) first, before a
crop, a resize or a rotation, so that they apply to what the viewer sees: a rotation of a photo that still has its
orientation would be turned again by the viewers. See also [Handle phone photos](../03-Guides/05-HandlePhonePhotos.md)
and [Metadata and orientation](../09-Color/02-MetadataAndOrientation.md).

## Rotate by any angle

`Rotate(degrees, options)` turns the image around its center. A positive angle turns clockwise. By default, the canvas
grows to hold the whole turned image, and the corners it gains get [TransformOptions.BackgroundColor](cref:Meziantou.Framework.Imaging.TransformOptions.BackgroundColor):

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_Straighten;format=md-fenced-code-block;dedent=true -->
```csharp
public static void Straighten(string inputPath, string outputPath, double degrees)
{
    using var image = Image.Load(inputPath);

    // The canvas grows to hold the whole turned image. A JPEG image has no alpha: the corners need an opaque color
    image.Rotate(degrees, new TransformOptions { Filter = ResamplingFilter.Lanczos3, BackgroundColor = new Rgba64(65535, 65535, 65535, 65535) });
    image.Save(outputPath);
}
```
<!-- endref -->

A rotation by a multiple of 90 degrees is the exact permutation of `Rotate(RotateMode)`, and a rotation by a multiple
of 360 degrees leaves the image unchanged.

## Skews and affine transformations

`Skew(degreesX, degreesY)` leans the vertical lines of the image by one angle and its horizontal lines by another one,
around its center. [Transform](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Transform) applies an [AffineTransform](cref:Meziantou.Framework.Imaging.AffineTransform): any combination of translations, scalings, rotations and
skews:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/ProcessingExamples.cs;region=Imaging_SkewAndTransform;format=md-fenced-code-block;dedent=true -->
```csharp
public static void SkewAndTransform(string inputPath, string skewedPath, string transformedPath)
{
    using var image = Image.Load(inputPath);

    // The vertical lines lean by 20 degrees. The canvas grows to hold the whole image, and the area the image does
    // not cover is transparent: a pixel format without alpha needs an opaque TransformOptions.BackgroundColor
    using (var skewed = image.Clone())
    {
        skewed.Skew(20, 0);
        skewed.Save(skewedPath);
    }

    // Half the size, then a 45-degree turn: the transformations are combined first, so the pixels are resampled once
    var transform = AffineTransform.CreateScale(0.5, 0.5).Then(AffineTransform.CreateRotation(45));
    image.Transform(transform, new TransformOptions { Filter = ResamplingFilter.Lanczos3 });
    image.Save(transformedPath);
}
```
<!-- endref -->

[AffineTransform](cref:Meziantou.Framework.Imaging.AffineTransform) has the factories [CreateTranslation](cref:Meziantou.Framework.Imaging.AffineTransform.CreateTranslation), [CreateScale](cref:Meziantou.Framework.Imaging.AffineTransform.CreateScale), [CreateRotation](cref:Meziantou.Framework.Imaging.AffineTransform.CreateRotation) and [CreateSkew](cref:Meziantou.Framework.Imaging.AffineTransform.CreateSkew), and [Then](cref:Meziantou.Framework.Imaging.AffineTransform.Then)
combines two transformations: the first one is applied, then the next one. Combine the transformations before calling
[Transform](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Transform), so that the pixels are resampled once. The coordinates are those of the stored pixels: the image covers
`[0, width] x [0, height]`.

## Correct a perspective

[CorrectPerspective](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.CorrectPerspective) straightens a document, a whiteboard or a painting photographed at an angle: give the four
corners of the quadrilateral in the photo, in order around it, and they become the corners of the image. Without a size,
the result is as wide as the longer of the top and bottom edges and as tall as the longer of the left and right edges:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_CorrectPerspective;format=md-fenced-code-block;dedent=true -->
```csharp
public static Size StraightenDocument(string inputPath, string outputPath, PointD topLeft, PointD topRight, PointD bottomRight, PointD bottomLeft)
{
    using var photo = Image.Load(inputPath);

    // The corners of the page in the photo, in order around it, become the corners of the image: the result is as
    // wide as the longer of the top and bottom edges, and as tall as the longer of the left and right edges. A JPEG
    // photo has no alpha: a corner outside the photo shows an opaque background
    photo.CorrectPerspective(topLeft, topRight, bottomRight, bottomLeft, options: new TransformOptions { BackgroundColor = new Rgba64(65535, 65535, 65535, 65535) });
    photo.Save(outputPath);
    return photo.Size;
}
```
<!-- endref -->

The corners are in pixel coordinates, where the image covers `[0, width] x [0, height]`: the top-left corner of the
top-left pixel is `(0, 0)` and its center `(0.5, 0.5)`. The filter, the background color (for corners outside the photo)
and the working space come from [TransformOptions](cref:Meziantou.Framework.Imaging.TransformOptions); its canvas settings are not used.

[Transform](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Transform) also applies a [ProjectiveTransform](cref:Meziantou.Framework.Imaging.ProjectiveTransform), the image seen in perspective.
[ProjectiveTransform.CreateFromQuadrilateral](cref:Meziantou.Framework.Imaging.ProjectiveTransform.CreateFromQuadrilateral) maps a rectangle to four points, such as the corners of a page in a
photo; its inverse maps the page back to a rectangle:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_FlattenPage;format=md-fenced-code-block;dedent=true -->
```csharp
public static void FlattenPage(string inputPath, string outputPath, PointD topLeft, PointD topRight, PointD bottomRight, PointD bottomLeft, Size pageSize)
{
    using var image = Image.Load(inputPath);

    // The page seen in perspective, inverted: from the photo to the page
    var page = new RectangleD(0, 0, pageSize.Width, pageSize.Height);
    var pageToPhoto = ProjectiveTransform.CreateFromQuadrilateral(page, topLeft, topRight, bottomRight, bottomLeft);
    if (!pageToPhoto.TryInvert(out var photoToPage))
        throw new ArgumentException("The corners do not delimit a page.", nameof(topLeft));

    // The canvas is the page: what the photo shows around it is cut
    image.Transform(photoToPage, new TransformOptions { CanvasMode = TransformCanvasMode.Explicit, CanvasSize = pageSize, BackgroundColor = new Rgba64(65535, 65535, 65535, 65535) });
    image.Save(outputPath);
}
```
<!-- endref -->

An [AffineTransform](cref:Meziantou.Framework.Imaging.AffineTransform) converts implicitly to a [ProjectiveTransform](cref:Meziantou.Framework.Imaging.ProjectiveTransform).

## Transform options

| [TransformOptions](cref:Meziantou.Framework.Imaging.TransformOptions) | Default | Meaning |
| --- | --- | --- |
| [CanvasMode](cref:Meziantou.Framework.Imaging.TransformOptions.CanvasMode) | [Expand](cref:Meziantou.Framework.Imaging.TransformCanvasMode.Expand) | [Expand](cref:Meziantou.Framework.Imaging.TransformCanvasMode.Expand) makes the smallest canvas that holds the whole transformed image, with the image centered in it. [Keep](cref:Meziantou.Framework.Imaging.TransformCanvasMode.Keep) keeps the size of the image, and [Explicit](cref:Meziantou.Framework.Imaging.TransformCanvasMode.Explicit) uses [CanvasSize](cref:Meziantou.Framework.Imaging.TransformOptions.CanvasSize): with these two, [Transform](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Transform) applies the transformation as given and cuts what lands outside the canvas, and [Rotate](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Rotate) and [Skew](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Skew) center the image on the canvas |
| [CanvasSize](cref:Meziantou.Framework.Imaging.TransformOptions.CanvasSize) | - | The size of the canvas with [Explicit](cref:Meziantou.Framework.Imaging.TransformCanvasMode.Explicit) |
| [Filter](cref:Meziantou.Framework.Imaging.TransformOptions.Filter) | [Bicubic](cref:Meziantou.Framework.Imaging.ResamplingFilter.Bicubic) | The resampling filter, as for [resizing](03-Resizing.md). [NearestNeighbor](cref:Meziantou.Framework.Imaging.ResamplingFilter.NearestNeighbor) copies pixels without creating new colors |
| [BackgroundColor](cref:Meziantou.Framework.Imaging.TransformOptions.BackgroundColor) | Transparent black | The color beyond the edges of the image, into which the edges blend, and of the canvas the image does not cover |
| [WorkingSpace](cref:Meziantou.Framework.Imaging.TransformOptions.WorkingSpace) | [Encoded](cref:Meziantou.Framework.Imaging.ResizeWorkingSpace.Encoded) | [Encoded](cref:Meziantou.Framework.Imaging.ResizeWorkingSpace.Encoded) or [LinearSrgb](cref:Meziantou.Framework.Imaging.ResizeWorkingSpace.LinearSrgb), as for resizing |

A pixel format without alpha ([Rgb24](cref:Meziantou.Framework.Imaging.Rgb24), [Gray8](cref:Meziantou.Framework.Imaging.Gray8), [Gray16](cref:Meziantou.Framework.Imaging.Gray16)) needs an opaque [BackgroundColor](cref:Meziantou.Framework.Imaging.TransformOptions.BackgroundColor) when the result shows it:
otherwise the operation throws an [UnsupportedImageFeatureException](cref:Meziantou.Framework.Imaging.UnsupportedImageFeatureException). A transformation that flattens the image to a
line, or a perspective whose horizon crosses the image with an [Expand](cref:Meziantou.Framework.Imaging.TransformCanvasMode.Expand) canvas, throws an `ArgumentException`. The
canvas is checked against the [resource limits](../05-LoadingAndSaving/07-ResourceLimits.md) before anything is
allocated, and the image is unchanged when an operation fails.

## Cursor hotspots

The hotspot of a cursor frame (`Frames[i].Metadata.Hotspot`) follows the pixel it points at: quarter turns, mirrors
and [AutoOrient](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.AutoOrient) move it exactly, and rotations by any angle, skews and transformations move it to the pixel that
contains the image of the center of its pixel. When that pixel leaves the canvas, the operation throws an
[UnsupportedImageFeatureException](cref:Meziantou.Framework.Imaging.UnsupportedImageFeatureException) and leaves the image unchanged: set the hotspot to `null` or to a pixel that is kept
first. See [ICO and CUR](../10-Formats/11-IcoAndCur.md).
