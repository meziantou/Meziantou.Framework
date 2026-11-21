---
title: Select pixels by color
description: Select a region of similar color with a magic wand, combine selections, and apply them to an image.
---

[PixelMask.FromSimilarColors](cref:Meziantou.Framework.Imaging.PixelMask.FromSimilarColors) is the magic wand of image
editors: it selects the pixels of a frame whose color is close to the color of a seed pixel, such as a uniform
background, the sky or a colored shape. The selection is a [PixelMask](cref:Meziantou.Framework.Imaging.PixelMask),
which you can combine with other selections, then apply to an image with the layers of the drawing API, or pass to
[Inpaint](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Inpaint).

## Remove a uniform background

Select the background from a corner, turn the selection into an alpha image, and erase what it covers:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/ProcessingExamples.cs;region=Imaging_RemoveBackground;format=md-fenced-code-block;dedent=true -->
```csharp
public static void RemoveBackground(string photoPath, string outputPath)
{
    using var photo = Image.Load(photoPath);
    var frame = photo.Frames[0];

    // The magic wand: the pixels connected to a corner whose samples are within 8% of the color of that corner
    var options = new ColorSelectionOptions { Tolerance = 0.08 };
    var background = PixelMask.FromSimilarColors(frame, new Point(0, 0), options);
    background.UnionWith(PixelMask.FromSimilarColors(frame, new Point(photo.Width - 1, photo.Height - 1), options));

    // The selection as an alpha image, blurred to soften its edges
    using var mask = background.ToImage();
    mask.GaussianBlur(1);

    // A copy with alpha, where the mask erases what it covers
    using var cutout = photo.CloneAs<Rgba32>();
    cutout.Draw(canvas => canvas.Layer(1, layer => layer.DrawImage(mask.Frames[0], new PointD(0, 0)), CompositeMode.DestinationOut));
    cutout.Save(outputPath);
}
```
<!-- endref -->

- The image must have alpha to become transparent: the example erases a copy in
  [Rgba32](cref:Meziantou.Framework.Imaging.Rgba32).
- The subject can split the background into several parts: select each one from its own seed and combine them, as the
  example does with two corners.
- The frame is not changed by the selection. A selection covers one frame: select each frame of an animation on its
  own.

## Choose what is selected

[ColorSelectionOptions](cref:Meziantou.Framework.Imaging.ColorSelectionOptions) sets which pixels are close enough to
the seed, and how the selection spreads:

| Option | Default | Effect |
| --- | --- | --- |
| [Tolerance](cref:Meziantou.Framework.Imaging.ColorSelectionOptions.Tolerance) | 0 | How far each sample of a pixel can be from the one of the seed, as a fraction of the range of a sample, from 0 to 1 |
| [Connectivity](cref:Meziantou.Framework.Imaging.ColorSelectionOptions.Connectivity) | [Four](cref:Meziantou.Framework.Imaging.PixelConnectivity.Four) | The neighbors a selection grows to: the pixels that share a side, or with [Eight](cref:Meziantou.Framework.Imaging.PixelConnectivity.Eight), a side or a corner |
| [Contiguous](cref:Meziantou.Framework.Imaging.ColorSelectionOptions.Contiguous) | `true` | Selects the pixels connected to the seed through pixels of a close color; with `false`, every pixel of a close color in the frame |

- The comparison is the one of [ReplaceColor](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.ReplaceColor):
  each pixel is read as its 16-bit color, and its four samples, alpha included, must each be within the tolerance of
  the samples of the seed. A tolerance of 0 selects the exact color only, and `1 / 255` the next 8-bit value on each
  side. Two transparent pixels of different colors do not match.
- The seed is always selected. Use [Eight](cref:Meziantou.Framework.Imaging.PixelConnectivity.Eight) to follow lines one
  pixel wide drawn at an angle, which only touch by their corners.
- No color profile is applied, and the result is the same on every platform. A floating-point frame is compared through
  its 16-bit colors, so its samples must be in [0, 1]. CMYK frames are rejected.

## Combine selections

A [PixelMask](cref:Meziantou.Framework.Imaging.PixelMask) holds one bit per pixel. Masks of the same size combine in
place, like the methods of a set:

| Method | Keeps |
| --- | --- |
| [UnionWith](cref:Meziantou.Framework.Imaging.PixelMask.UnionWith) | The pixels of either mask |
| [IntersectWith](cref:Meziantou.Framework.Imaging.PixelMask.IntersectWith) | The pixels of both masks |
| [ExceptWith](cref:Meziantou.Framework.Imaging.PixelMask.ExceptWith) | The pixels of this mask that are not in the other one |
| [SymmetricExceptWith](cref:Meziantou.Framework.Imaging.PixelMask.SymmetricExceptWith) | The pixels of one mask but not of both |
| [Invert](cref:Meziantou.Framework.Imaging.PixelMask.Invert) | The pixels that were not in the mask |

[Clone](cref:Meziantou.Framework.Imaging.PixelMask.Clone) keeps a copy before a change. The indexer and
[Fill](cref:Meziantou.Framework.Imaging.PixelMask.Fill) add or remove pixels and rectangles by hand, and
[Count](cref:Meziantou.Framework.Imaging.PixelMask.Count) gives the number of selected pixels.

## Apply a selection

[ToImage](cref:Meziantou.Framework.Imaging.PixelMask.ToImage) turns a mask into an [A8](cref:Meziantou.Framework.Imaging.A8)
image of its size: the selected pixels are opaque, the others transparent. Draw it on the image with the
[layers, masks and compositing operators](../07-Drawing/05-LayersMasksAndFilters.md) of the drawing API:

- in a [Layer](cref:Meziantou.Framework.Imaging.Drawing.DrawingCanvas.Layer) with
  [DestinationOut](cref:Meziantou.Framework.Imaging.Drawing.CompositeMode.DestinationOut), to erase the selection, or with
  [DestinationIn](cref:Meziantou.Framework.Imaging.Drawing.CompositeMode.DestinationIn), to keep only the selection;
- as the mask of [Mask](cref:Meziantou.Framework.Imaging.Drawing.DrawingCanvas.Mask), to draw inside the selection only:
  a color, a gradient, another photo.

Blur the alpha image with [GaussianBlur](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.GaussianBlur) before
drawing it to soften the edges of the selection (feathering): a larger standard deviation gives a wider fade.
[PixelMask.FromFrame](cref:Meziantou.Framework.Imaging.PixelMask.FromFrame) reads an alpha image back as a
mask, and the masks also give [Inpaint](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Inpaint) the pixels to
fill (see [Inpainting](13-Inpainting.md)) and [SeamCarve](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.SeamCarve)
the regions to keep and to remove (see [Resizing](03-Resizing.md)).

## Memory

A mask holds one bit per pixel in memory of its own, not charged to an image. A contiguous selection also keeps a
second mask and the runs of pixels it has yet to visit while it runs. The alpha image of
[ToImage](cref:Meziantou.Framework.Imaging.PixelMask.ToImage) is an image like any other: it takes a byte per pixel,
counts toward the [resource limits](../05-LoadingAndSaving/07-ResourceLimits.md) of its configuration, and must be
disposed.
