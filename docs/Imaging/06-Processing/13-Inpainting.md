---
title: Inpainting
description: Remove scratches, stains, captions and small objects by filling them from the pixels around them.
---

[Inpaint](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Inpaint) fills the pixels of a
[PixelMask](cref:Meziantou.Framework.Imaging.PixelMask) from the pixels around them: a scratch on a scanned photo, dust,
a date printed on a picture, a caption, a watermark or a small object. The pixels outside the mask do not change.

## Remove what a mask covers

Paint the mask in white on black, at the size of the photo, and read it with
[PixelMask.FromFrame](cref:Meziantou.Framework.Imaging.PixelMask.FromFrame), or set its pixels with the indexer and
[Fill](cref:Meziantou.Framework.Imaging.PixelMask.Fill):

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/ProcessingExamples.cs;region=Imaging_Inpaint;format=md-fenced-code-block;dedent=true -->
```csharp
public static void RemoveScratches(string photoPath, string maskPath, string outputPath)
{
    using var photo = Image.Load(photoPath);

    // The mask is painted in white on black, at the size of the photo
    PixelMask mask;
    using (var maskImage = Image.Load(maskPath))
    {
        mask = PixelMask.FromFrame(maskImage.Frames[0]);
    }

    // The pixels of the mask are filled from the pixels around them; a larger radius gives smoother fills
    photo.Inpaint(mask, new InpaintingOptions { Radius = 4 });
    photo.Save(outputPath);
}
```
<!-- endref -->

The mask must have the size of the image and leave at least one pixel out. Cover the whole defect, with a margin of a
pixel or two: a pixel of the defect left outside the mask is copied into the fill.

## How the pixels are filled

The method is the fast marching method of Telea (2004). The pixels of the mask are filled from its boundary inward, in
the order of their distance to it. Each one is the weighted mean of the extrapolations of the pixels already known
within [InpaintingOptions.Radius](cref:Meziantou.Framework.Imaging.InpaintingOptions.Radius) (5 pixels by default): their
value plus their gradient times the offset, so that the colors and the edges that reach the mask continue into it. The
nearest pixels, the ones in the direction the front moves and the ones at the same distance to the boundary count the
most.

- **Thin and small regions.** The method suits scratches, text and small objects. A large region gets a smooth fill:
  its texture is not reconstructed. Prefer several small masks to one large one.
- **Radius.** A radius of about half the width of the strokes of the mask is usually enough; a larger one gives
  smoother fills, and the time grows with its square.
- **Colors.** The colors are filled premultiplied by alpha, in double precision, and each channel stays within the range
  of the pixels it comes from. The result is the same on every platform.

## Animations and failures

Every frame of an animation and its poster frame are filled with the same mask. The operation is all or nothing: the
new frames replace the old ones only when they are all filled, so a resource limit or a cancellation leaves the image
unchanged. The working memory, about 17 bytes per pixel, a working copy of a frame and the new frames, counts toward
[MaxLiveAllocationBytes](cref:Meziantou.Framework.Imaging.ImageResourceLimits.MaxLiveAllocationBytes).

To remove an object and close the gap instead of filling it, see the removed region of
[seam carving](03-Resizing.md).
