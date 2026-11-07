---
title: ANI
description: Read and write Windows animated cursors, with their durations, hotspots, title and author.
---

An animated cursor (`.ani`) is an animation whose frames are cursor images. [Image.Load](cref:Meziantou.Framework.Imaging.Image.Load) returns one frame per step of
the animation, in playback order, each with its duration and the hotspot of its cursor; [AniEncoder](cref:Meziantou.Framework.Imaging.Formats.AniEncoder) writes one step per
frame. **It needs a seekable destination.**

## What is read and written

| | Reading | Writing |
| --- | --- | --- |
| Frames | Icon and cursor images with PNG or bitmap payloads; a frame that several steps show becomes as many frames | One cursor image per frame, PNG or 32-bit bitmap; identical frames are stored once |
| Timing | Durations in sixtieths of a second (jiffies), exactly | Durations rounded to sixtieths of a second by default |
| Play count | Always infinite | Infinite only |
| Hotspots | Per frame | Per frame |
| Metadata | Title and author | Title and author |

Frames load as [Rgba32](cref:Meziantou.Framework.Imaging.Rgba32), or as [Rgba64](cref:Meziantou.Framework.Imaging.Rgba64) for the whole animation when a PNG frame has 16-bit samples.

## Write an animated cursor

Each frame stores a hotspot (`Frames[i].Metadata.Hotspot`), the top-left corner when it has none. An animated cursor
always loops, so an animation with a finite play count is rejected: set an [AnimationMetadata](cref:Meziantou.Framework.Imaging.Metadata.AnimationMetadata) with no play
count:

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

The hotspot follows its pixel through geometry operations, such as a resize.

## Options

[AniEncoder](cref:Meziantou.Framework.Imaging.Formats.AniEncoder) chooses how the frames are stored and how strictly their durations are kept:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/FormatExamples.cs;region=Imaging_SaveAni;format=md-fenced-code-block;dedent=true -->
```csharp
public static void SaveAnimatedCursor(string animationPath, string aniPath)
{
    using var animation = Image.Load(animationPath);

    // An animated cursor always loops: a finite play count is rejected, never dropped
    animation.Animation = new AnimationMetadata();
    animation.Metadata.TextEntries.Add(new ImageTextEntry("Title", "Busy"));
    animation.Metadata.TextEntries.Add(new ImageTextEntry("Author", "Contoso"));

    animation.Save(aniPath, new AniEncoder
    {
        // Auto writes 32-bit bitmaps up to 64 pixels per side, and PNG above
        PayloadFormat = IconPayloadFormat.Png,

        // A step lasts a whole number of sixtieths of a second: fail instead of rounding the other durations
        DurationRounding = FrameDurationRounding.RequireExact,
    });
}
```
<!-- endref -->

| Option | What it does | Default |
| --- | --- | --- |
| [PayloadFormat](cref:Meziantou.Framework.Imaging.Formats.AniEncoder.PayloadFormat) | [Auto](cref:Meziantou.Framework.Imaging.Formats.IconPayloadFormat.Auto) writes a 32-bit bitmap up to 64 pixels per side and a PNG above, or for 16-bit pixels; [Png](cref:Meziantou.Framework.Imaging.Formats.IconPayloadFormat.Png) and [Dib](cref:Meziantou.Framework.Imaging.Formats.IconPayloadFormat.Dib) force one | [Auto](cref:Meziantou.Framework.Imaging.Formats.IconPayloadFormat.Auto) |
| [DurationRounding](cref:Meziantou.Framework.Imaging.Formats.AniEncoder.DurationRounding) | [RoundToNearest](cref:Meziantou.Framework.Imaging.Formats.FrameDurationRounding.RoundToNearest) rounds durations to the nearest sixtieth of a second (ties up); [RequireExact](cref:Meziantou.Framework.Imaging.Formats.FrameDurationRounding.RequireExact) throws for the others | [RoundToNearest](cref:Meziantou.Framework.Imaging.Formats.FrameDurationRounding.RoundToNearest) |
| [MetadataHandling](cref:Meziantou.Framework.Imaging.Formats.MetadataHandling) | What to do with metadata ANI cannot store | [Strict](cref:Meziantou.Framework.Imaging.Formats.MetadataHandling.Strict) |

## Pitfalls

- The destination must be seekable: the counts and sizes at the start of the file are written once the last frame is
  known. Paths are always seekable.
- A frame is at most 256 pixels per side: resize larger animations first.
- Only one `Title` and one `Author` text entry (Latin-1 text) are stored. Other text entries, an ICC profile, EXIF, XMP,
  a resolution or an orientation follow [MetadataHandling](cref:Meziantou.Framework.Imaging.Formats.MetadataHandling), which rejects them by default.
- A still image is written as an animation of one step, and an ANI file always loads as an animation, even with one
  step.
- [Dib](cref:Meziantou.Framework.Imaging.Formats.IconPayloadFormat.Dib) rejects 16-bit pixels instead of reducing them.
- [Image.OpenReader](cref:Meziantou.Framework.Imaging.Image.OpenReader) does not read ANI files, and they are not an [ImageCollection](cref:Meziantou.Framework.Imaging.ImageCollection): load them with [Image.Load](cref:Meziantou.Framework.Imaging.Image.Load).
