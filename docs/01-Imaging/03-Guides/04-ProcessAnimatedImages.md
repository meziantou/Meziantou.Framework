---
title: Process animated images
description: Resize and edit GIF, APNG and WebP animations, change their speed or loop count, extract a frame, choose a GIF palette.
---

An animated GIF, APNG or WebP file loads as one `Image` whose `Frames` are the full pictures that a viewer shows, each
with its exact duration. You never deal with the partial rectangles and the disposal methods that the files store: edit
the frames as complete pictures, and the encoder computes what to store. See
[Images, frames and animations](../04-Concepts/02-ImagesFramesAndAnimations.md) for the model.

## Resize an animation

Operations on an image apply to every frame:

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

- Crop, resize, rotate, flip, blur, color adjustments and drawing change every frame, and the poster frame of an APNG
  file. A geometry change (crop, resize, rotation) is all or nothing: when it fails, no frame has changed.
- `Save` writes the frames, their durations and the loop count. The output format must store animations: GIF, PNG
  (APNG), WebP, AVIF, HEIC or JPEG XL. A JPEG output throws an `UnsupportedImageFeatureException` instead of keeping the
  first frame silently.
- To change one frame only, use the frame overloads of the pixel operations, such as `Frames[i].Draw`. See
  [Drawing on animations](../07-Drawing/07-DrawingOnAnimations.md).

## Change the speed and the loop count

`Frames[i].Metadata.Duration` is a `FrameDuration`: an exact fraction of a second, never rounded on load.
`Animation.TotalPlays` is the number of times the animation plays, the first play included, or `null` for an infinite
loop:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/GuideExamples.cs;region=Imaging_ChangeAnimationSpeed;format=md-fenced-code-block;dedent=true -->
```csharp
public static void PlayTwiceAsFast(string inputPath, string outputPath)
{
    using var animation = Image.Load(inputPath);
    foreach (var frame in animation.Frames)
    {
        // Durations are exact fractions of a second: half of 1/30 s is exactly 1/60 s
        var duration = frame.Metadata.Duration;
        frame.Metadata.Duration = new FrameDuration(duration.Numerator, duration.Denominator * 2);
    }

    // Played three times in total, the first play included; TotalPlays = null loops forever
    animation.Animation = new AnimationMetadata { TotalPlays = 3 };

    // GIF stores hundredths of a second: its encoder rounds each duration to the nearest one by default
    animation.Save(outputPath);
}
```
<!-- endref -->

Each format stores durations with its own precision, and the encoder says what to do with a duration it cannot store
exactly (`DurationRounding`):

| Format | Durations | Default rounding |
| --- | --- | --- |
| GIF | Hundredths of a second, up to 655.35 s | To the nearest hundredth (`RoundToNearest`) |
| APNG | A fraction with a 16-bit numerator and denominator | Exact, or `UnsupportedImageFeatureException` (`RequireExact`) |
| WebP | Milliseconds | To the nearest millisecond (`RoundToNearest`) |

A GIF file stores the number of repetitions after the first play: a file that loops 2 times plays 3 times, and
`TotalPlays` is 3. The library converts both ways. Zero durations are kept as they are: the minimum delays that some
players apply are not imitated.

## Build an animation

`AppendFrame` adds a frame. A still image that gets a second frame becomes an animation, with default settings (an
infinite loop):

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

`InsertFrame`, `RemoveFrame` and `MoveFrame` edit the list of frames. A frame added from another image must have the
same size and pixel format: frames are never resized or converted implicitly.

## Extract a frame

`CloneFrame(index)` copies one frame into a new still image, with its metadata but without the animation settings:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_ExportJpegFrame;format=md-fenced-code-block;dedent=true -->
```csharp
public static void ExportFrameAsJpeg(string animatedPath, int frameIndex, string jpegPath)
{
    using var animation = Image.Load(animatedPath);
    using var frame = animation.CloneFrame(frameIndex);
    frame.Save(jpegPath, new JpegEncoder
    {
        Quality = 85,
        BackgroundColor = new Rgba32(255, 255, 255),
        AllowBitDepthReduction = true,
        MetadataHandling = MetadataHandling.DiscardUnsupported,
    });
}
```
<!-- endref -->

When you need the first frame only, decode only that frame: `ImageDecodeOptions.FrameLimit = 1` stops reading the file
after it.

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/PerformanceExamples.cs;region=Imaging_FirstFramePreview;format=md-fenced-code-block;dedent=true -->
```csharp
public static void SavePreview(string animationPath, string previewPath)
{
    // Decoding stops after the first displayed frame: the rest of the file is not read
    using var image = Image.Load(animationPath, new ImageDecodeOptions { FrameLimit = 1 });

    // The image keeps its animation settings: export the frame as a still image
    using var preview = image.CloneFrame(0);
    preview.Resize(new ResizeOptions(128, 128) { AllowUpscaling = false });
    preview.Save(previewPath, new PngEncoder());
}
```
<!-- endref -->

## Choose the palette of a GIF

A GIF frame holds at most 256 colors. The encoder keeps the colors of a frame that fits, and otherwise computes a palette
for each frame. A palette per frame can make the colors of a smooth area change from one frame to the next; a palette
shared by every frame avoids it:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_SharedPalette;format=md-fenced-code-block;dedent=true -->
```csharp
public static void SaveGifWithSharedPalette(string inputPath, string outputPath)
{
    using var animation = Image.Load(inputPath);

    // The palette of the pixels of every frame and of the poster: 255 colors leave room for the transparent entry
    ImagePalette palette;
    using (var builder = new PaletteBuilder())
    {
        builder.Add(animation);
        palette = builder.Build(maxColors: 255);
    }

    // Every frame maps to the same colors, so they do not flicker from one frame to the next
    animation.Save(outputPath, new GifEncoder
    {
        Quantization = new QuantizeOptions { Palette = palette, Dithering = DitheringMode.FloydSteinberg },
    });
}
```
<!-- endref -->

The settings of `GifEncoder`:

| Setting | Effect |
| --- | --- |
| `Quantization.MaxColors` | The largest palette of a frame, from 2 to 256 (default), the transparent entry included. Fewer colors, smaller file |
| `Quantization.Palette` | One fixed palette for every frame, such as the one of `PaletteBuilder` above |
| `Quantization.Dithering` | Mixes palette colors to simulate the missing ones: error diffusion (`FloydSteinberg`...) or ordered (`Bayer4x4`...). `None` by default |
| `AlphaMode` | `Threshold` (default): pixels whose alpha is below `AlphaThreshold` (128) become transparent, the others opaque. `Flatten`: blended on the opaque `BackgroundColor` |
| `DurationRounding` | `RoundToNearest` hundredth (default) or `RequireExact` |

GIF stores no partial transparency and no 16-bit samples: choosing GIF is the opt-in to these losses. To keep every
color and soft edges, save the animation as APNG or WebP instead. See [GIF](../10-Formats/03-Gif.md) and
[Color reduction](../06-Processing/08-ColorReduction.md).

## Long animations

`Image.Load` decodes every frame into memory. To process an animation of thousands of frames, or one larger than the
default limit of 1,000 frames, read and write it one frame at a time: see
[Streaming readers and writers](../05-LoadingAndSaving/05-StreamingReadersAndWriters.md) and
[Resource limits](../05-LoadingAndSaving/07-ResourceLimits.md).
