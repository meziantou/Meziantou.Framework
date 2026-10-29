---
title: Drawing on animations
description: Draw the same thing on every frame of an animation, or a different drawing on each frame.
---

An animation is a list of full frames (see
[Images, frames and animations](../04-Concepts/02-ImagesFramesAndAnimations.md)), so drawing on it is drawing on its
frames: all of them at once with `Image.Draw`, or one at a time with `ImageFrame.Draw`. The durations of the frames
and the loop count are kept.

## Draw the same thing on every frame

`Image.Draw` paints every frame and the poster frame identically. The callback runs once, whatever the number of
frames:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_DrawOnAnimation;format=md-fenced-code-block;dedent=true -->
```csharp
public static void MarkAnimation(string inputPath, string outputPath)
{
    using var image = Image.Load(inputPath);
    var marker = new Pen(new Rgba32(255, 255, 0, 200), 4) { LineCap = LineCap.Round };

    // Image.Draw paints every frame and the poster identically; the callback runs once
    image.Draw(marker, static (canvas, pen) =>
    {
        // Coordinates are in pixels, with sub-pixel precision; shapes are clipped to the canvas
        var center = new PointD(canvas.Width / 2.0, canvas.Height / 2.0);
        var radius = Math.Min(canvas.Width, canvas.Height) / 4.0;
        canvas.StrokeCircle(pen, center, radius);
        canvas.StrokeLine(pen, new PointD(center.X - radius, center.Y), new PointD(center.X + radius, center.Y));
    });

    image.Save(outputPath);
}
```
<!-- endref -->

![An animation alternating between a red and a blue square, with the same yellow circle crossed by a horizontal line on both frames](images/MarkAnimation.png)

The example uses the overload that takes a state: the callback is `static`, so it captures nothing and the call
allocates no closure. Every method of the canvas that takes a callback has the same overload.

## Draw something different on each frame

`ImageFrame.Draw` draws on one frame. Loop over `Frames` to give each frame its own drawing:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/DrawingExamples.cs;region=Imaging_DrawOnEachFrame;format=md-fenced-code-block;dedent=true -->
```csharp
public static void DrawProgressBar(string inputPath, string outputPath)
{
    using var image = Image.Load(inputPath);
    var count = image.Frames.Count;
    for (var i = 0; i < count; i++)
    {
        // A bar that grows with the position of the frame in the animation
        var progress = (i + 1.0) / count;
        image.Frames[i].Draw(canvas =>
        {
            var track = new RectangleD(4, canvas.Height - 10, canvas.Width - 8, 6);
            canvas.FillRectangle(new Rgba32(0, 0, 0, 120), track);
            canvas.FillRectangle(Colors.White, new RectangleD(track.X, track.Y, track.Width * progress, track.Height));
        });
    }

    // The durations and the loop count of the animation are kept
    image.Save(outputPath);
}
```
<!-- endref -->

![An animation alternating between a red and a blue square, with a progress bar at the bottom: half white on the red frame, entirely white on the blue frame](images/DrawProgressBar.png)

- The poster frame of an APNG file (`PosterFrame`), the image that players that do not support animations show, is not
  in `Frames`: draw on it separately when the image has one.
- A frame can draw another frame of the same image with `DrawImage`, but not itself.

## Long animations

`Image.Load` decodes every frame in memory. To draw on a long animation with the memory of one frame, read it frame by
frame, draw on each frame and write it: see
[Streaming readers and writers](../05-LoadingAndSaving/05-StreamingReadersAndWriters.md).

## When drawing fails

Drawing changes the pixels in place. When it fails or is canceled, the shapes already drawn stay and the image stays
valid and disposable. A canceled shape leaves the same rows painted on every frame, and a shape rents its working memory
before it changes a pixel: a shape that exceeds the allocation limit paints nothing. See
[Thread safety and atomicity](../04-Concepts/06-ThreadSafetyAndAtomicity.md).
