---
title: Images, frames and animations
description: An image is a list of full frames with exact durations; build animations and animated cursors.
---

An `Image` holds one or more frames. Every frame is the complete picture a viewer shows at that point of the
animation, at the size of the image and in its pixel format. This page shows how to build an animation, set its
timing and play count, and how posters and animated cursors fit in.

## Build an animation

Create an image, append frames, give each one a duration, then save it:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_BuildAnimation;format=md-fenced-code-block;dedent=true -->
```csharp
public static void BuildAnimation(string apngPath, string gifPath)
{
    using var animation = new Image<Rgba32>(64, 64, new Rgba32(255, 0, 0));
    animation.Frames[0].Metadata.Duration = FrameDuration.FromMilliseconds(100);

    var second = animation.AppendFrame(); // creates default animation settings (infinite loop)
    second.Metadata.Duration = new FrameDuration(1, 30); // exactly 1/30 s
    second.ProcessPixelRows(static pixels =>
    {
        for (var y = 0; y < pixels.Height; y++)
        {
            pixels.GetRowSpan(y).Fill(new Rgba32(0, 0, 255));
        }
    });

    animation.Animation!.TotalPlays = 3; // played three times in total

    animation.Save(apngPath, new PngEncoder { AnimationMode = PngAnimationMode.Animated });
    animation.Save(gifPath, new GifEncoder { Quantization = new QuantizeOptions { Dithering = DitheringMode.FloydSteinberg } });
}
```
<!-- endref -->

- `image.Frames` lists the frames; `AppendFrame()` adds a frame whose pixels are all zero (transparent black) and
  whose duration is zero. `AppendFrame(frame)` and `InsertFrame(index, frame)` add a copy of an existing frame, which
  must have the size and the pixel format of the image: frames are never resized or converted implicitly.
- `RemoveFrame` and `MoveFrame` edit the order. An image always keeps at least one frame.
- `Frames[i].Metadata.Duration` is the duration of the frame, and `Image.Animation.TotalPlays` the number of plays.
- Every operation of the library, such as a resize, a rotation or a blur, applies to all the frames.

Because frames are full pictures, you never deal with the partial rectangles, blend operations or disposal methods
that GIF and APNG files store. Decoders resolve them, and encoders compute them again from the frames you give them, so
removing or reordering frames can never leave stale instructions behind.

## Still or animated

`IsAnimated` is true when the image has several frames, a poster frame, or animation settings (`Animation` is not
`null`):

- Adding a second frame or a poster creates default settings: an infinite loop.
- Removing frames down to one keeps the settings: the image is then a one-frame animation, and is saved as one. Set
  `Animation` to `null` to make it a still image; this is allowed only with one frame and no poster.
- A format that stores one picture (JPEG, a static PNG) rejects an animated image with an
  `UnsupportedImageFeatureException` instead of saving its first frame. Save the frame you want with
  `Frames[index].Save`, which writes it as a still image (`CloneFrame` copies it into a new still image instead):

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_ExportJpegFrame;format=md-fenced-code-block;dedent=true -->
```csharp
public static void ExportFrameAsJpeg(string animatedPath, int frameIndex, string jpegPath)
{
    using var animation = Image.Load(animatedPath);

    // The frame is saved as a still image, without copying its pixels
    animation.Frames[frameIndex].Save(jpegPath, new JpegEncoder
    {
        Quality = 85,
        BackgroundColor = new Rgba32(255, 255, 255),
        AllowBitDepthReduction = true,
        MetadataHandling = MetadataHandling.DiscardUnsupported,
    });
}
```
<!-- endref -->

## Durations

A duration is a `FrameDuration`: an exact fraction of a second, not a floating-point number. The delays of GIF
(hundredths of a second), APNG (any fraction) and ANI (sixtieths of a second) are kept exactly, zero durations
included, and no minimum delay is imposed the way some players do.

| To | Use |
| --- | --- |
| Create a duration | `FrameDuration.FromMilliseconds(100)`, `new FrameDuration(1, 30)` (exactly 1/30 s), `FrameDuration.FromTimeSpan` |
| Read it exactly | `Numerator` and `Denominator` |
| Convert it | `ToTimeSpan()` (rounded to the nearest 100 ns tick) |
| Display it | `TotalMilliseconds`, `TotalSeconds` (floating-point approximations) |

When the output format cannot store a duration exactly, the `DurationRounding` setting of the encoder decides:
`RequireExact` throws, `RoundToNearest` uses the nearest duration the format can store. The PNG encoder requires exact
durations by default; the GIF encoder rounds to the nearest hundredth of a second and the ANI encoder to the nearest
sixtieth. A duration beyond the range of the format is always rejected, never clamped.

## Play count

`Animation.TotalPlays` is the total number of times the animation plays, the first one included, or `null` for an
infinite loop. The library converts it to and from the conventions of each format: a GIF file stores a number of
repetitions, so a GIF that repeats twice has `TotalPlays = 3`.

The animation settings are part of the structure of the image: options that remove metadata never remove them.

## Poster frames

An APNG file can store a default image that is not part of the animation: programs that do not support APNG show it,
and APNG players skip it. The library exposes it as `PosterFrame`:

- It is not counted in `Frames.Count`, and is kept by loads and by the operations on the whole image, such as a
  resize.
- `SetPosterFrame` sets it to a copy of a frame, `RemovePosterFrame` removes it, and `ClonePosterFrame` extracts it as a
  still image; `PosterFrame.Save` saves it as one.
- Only animated PNG output can store a poster. Other formats reject an image that has one: remove it first.

## Animated cursors

An ANI file is an animation whose frames are cursor images. Each frame has a hotspot, the pixel that designates the
position of the pointer, in `Frames[i].Metadata.Hotspot`:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_AnimatedCursor;format=md-fenced-code-block;dedent=true -->
```csharp
public static Point WriteAndReadAnimatedCursor(string animationPath, string cursorPath)
{
    using var animation = Image.Load(animationPath);
    animation.Resize(new ResizeOptions(32, 32) { Mode = ResizeMode.Stretch });
    foreach (var frame in animation.Frames)
    {
        frame.Metadata.Hotspot = new Point(4, 2); // the pixel that designates the pointer position
    }

    // An animated cursor always loops: a finite play count is rejected instead of being dropped
    animation.Animation = new AnimationMetadata();
    animation.Save(cursorPath); // ".ani" selects AniEncoder; durations are rounded to sixtieths of a second

    // A hotspot follows its pixel through geometry operations: (4, 2) at 32x32 is (8, 4) at 64x64
    using var cursor = Image.Load(cursorPath);
    cursor.Resize(new ResizeOptions(64, 64) { Mode = ResizeMode.Stretch, AllowUpscaling = true });
    return cursor.Frames[0].Metadata.Hotspot!.Value;
}
```
<!-- endref -->

- A hotspot is always inside its frame: setting it elsewhere throws an `ArgumentOutOfRangeException`. Frames that are
  not cursor images have no hotspot (`null`).
- Geometry operations move the hotspot with its pixel: after doubling the size of the cursor, the hotspot `(4, 2)`
  becomes `(8, 4)`.
- An animated cursor always loops: it loads with `TotalPlays = null`, and the ANI encoder rejects any other value. Its
  durations are stored in sixtieths of a second.
- A format that cannot store a hotspot treats it as metadata it cannot store: see
  [Metadata and orientation](../09-Color/02-MetadataAndOrientation.md).

## See also

- [Process animated images](../03-Guides/04-ProcessAnimatedImages.md): resize, convert and edit animations.
- [Streaming readers and writers](../05-LoadingAndSaving/05-StreamingReadersAndWriters.md): process a long animation one
  frame at a time.
- [Drawing on animations](../07-Drawing/07-DrawingOnAnimations.md).
- [Pages and representations](03-PagesAndRepresentations.md): the pages of a document and the sizes of an icon are not
  frames.
