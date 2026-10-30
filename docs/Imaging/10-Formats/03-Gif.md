---
title: GIF
description: Read and write GIF images and animations, and control how colors and transparency are reduced.
---

GIF is the animation format that every viewer plays. It stores at most 256 colors per frame and one fully transparent
color, so writing a GIF is lossy by design: choosing [GifEncoder](cref:Meziantou.Framework.Imaging.Formats.GifEncoder) is the consent to reduce the colors, the alpha values
and 16-bit samples, and the encoder needs no other setting to do it.

## What is read and written

| | Reading | Writing |
| --- | --- | --- |
| Files | GIF87a and GIF89a, global and local palettes, interlacing | GIF89a, one local palette per frame, optional interlacing |
| Animations | Composited into full frames with exact durations (hundredths of a second) and the play count | Full frames, durations to the hundredth of a second, play count |
| Transparency | The transparent color; cleared areas are transparent black | One transparent color: alpha is thresholded or flattened, never partial |
| Metadata | Comments | Comments only |

Every GIF loads as [Rgba32](cref:Meziantou.Framework.Imaging.Rgba32). A frame whose colors fit its palette is written exactly; the others are reduced with the
deterministic quantizer of [Quantize](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Quantize).

## Save a GIF

[AlphaMode](cref:Meziantou.Framework.Imaging.Formats.GifEncoder.AlphaMode) decides what happens to the pixels that are not fully opaque, and [Quantization](cref:Meziantou.Framework.Imaging.Formats.GifEncoder.Quantization) how many colors each frame
keeps and how they are dithered:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/FormatExamples.cs;region=Imaging_SaveGif;format=md-fenced-code-block;dedent=true -->
```csharp
public static void SaveGif(string inputPath, string gifPath, string flattenedGifPath)
{
    using var image = Image.Load(inputPath);

    // GIF has one fully transparent palette entry and no partial alpha: pixels whose alpha is below the threshold
    // become transparent, the others opaque. 64 colors per frame (the transparent entry included), dithered.
    image.Save(gifPath, new GifEncoder
    {
        AlphaThreshold = 64,
        Quantization = new QuantizeOptions { MaxColors = 64, Dithering = DitheringMode.FloydSteinberg },
        MetadataHandling = MetadataHandling.DiscardUnsupported, // GIF only stores comments: drop the ICC profile, EXIF...
    });

    // Soft edges look better composited over the color of the page that shows the image
    image.Save(flattenedGifPath, new GifEncoder
    {
        AlphaMode = GifAlphaMode.Flatten,
        BackgroundColor = new Rgba32(255, 255, 255),
        Interlaced = true,
        MetadataHandling = MetadataHandling.DiscardUnsupported,
    });
}
```
<!-- endref -->

- With [GifAlphaMode.Threshold](cref:Meziantou.Framework.Imaging.Formats.GifAlphaMode.Threshold) (the default), a pixel whose alpha is below [AlphaThreshold](cref:Meziantou.Framework.Imaging.Formats.GifEncoder.AlphaThreshold) becomes transparent and the
  others become opaque. Antialiased edges then look jagged on a background of another color.
- With [GifAlphaMode.Flatten](cref:Meziantou.Framework.Imaging.Formats.GifAlphaMode.Flatten), every pixel is composited over [BackgroundColor](cref:Meziantou.Framework.Imaging.Formats.GifEncoder.BackgroundColor), and the GIF is opaque. Without
  [BackgroundColor](cref:Meziantou.Framework.Imaging.Formats.GifEncoder.BackgroundColor), saving throws an `ArgumentException`.
- GIF only stores `Comment` text entries. An ICC profile, EXIF, XMP or a resolution follow [MetadataHandling](cref:Meziantou.Framework.Imaging.Formats.MetadataHandling), which
  rejects them by default: the example discards them.

## Options

| Option | What it does | Default |
| --- | --- | --- |
| [AlphaMode](cref:Meziantou.Framework.Imaging.Formats.GifEncoder.AlphaMode) | [Threshold](cref:Meziantou.Framework.Imaging.Formats.GifAlphaMode.Threshold) makes each pixel transparent or opaque; [Flatten](cref:Meziantou.Framework.Imaging.Formats.GifAlphaMode.Flatten) composites the image over [BackgroundColor](cref:Meziantou.Framework.Imaging.Formats.GifEncoder.BackgroundColor) | [Threshold](cref:Meziantou.Framework.Imaging.Formats.GifAlphaMode.Threshold) |
| [AlphaThreshold](cref:Meziantou.Framework.Imaging.Formats.GifEncoder.AlphaThreshold) | The alpha (8-bit scale) below which a pixel becomes transparent | 128 |
| [BackgroundColor](cref:Meziantou.Framework.Imaging.Formats.GifEncoder.BackgroundColor) | The opaque color [Flatten](cref:Meziantou.Framework.Imaging.Formats.GifAlphaMode.Flatten) composites over | `null` |
| [Quantization](cref:Meziantou.Framework.Imaging.Formats.GifEncoder.Quantization) | The number of colors per frame (2 to 256, the transparent entry included), the dithering, or a fixed palette for every frame | 256 colors, no dithering |
| [Interlaced](cref:Meziantou.Framework.Imaging.Formats.GifEncoder.Interlaced) | Writes interlaced frames | `false` |
| [DurationRounding](cref:Meziantou.Framework.Imaging.Formats.GifEncoder.DurationRounding) | [RoundToNearest](cref:Meziantou.Framework.Imaging.Formats.FrameDurationRounding.RoundToNearest) rounds durations to the nearest hundredth of a second (ties up); [RequireExact](cref:Meziantou.Framework.Imaging.Formats.FrameDurationRounding.RequireExact) throws | [RoundToNearest](cref:Meziantou.Framework.Imaging.Formats.FrameDurationRounding.RoundToNearest) |
| [MetadataHandling](cref:Meziantou.Framework.Imaging.Formats.MetadataHandling) | What to do with metadata GIF cannot store | [Strict](cref:Meziantou.Framework.Imaging.Formats.MetadataHandling.Strict) |

[Dithering](cref:Meziantou.Framework.Imaging.Formats.GifEncoder.Dithering) and [MaxColors](cref:Meziantou.Framework.Imaging.Formats.GifEncoder.MaxColors) are obsolete aliases of `Quantization.Dithering` and `Quantization.MaxColors`.

## Share one palette between frames

Each frame gets its own palette by default, so the colors of a gradient can change slightly from one frame to the next
and flicker. A palette built from every frame and given to every frame avoids it:

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

The palette leaves room for the transparent entry: a palette of 256 colors with [Threshold](cref:Meziantou.Framework.Imaging.Formats.GifAlphaMode.Threshold) and a pixel format with
alpha throws an `ArgumentException`. The palette and the dithering modes are described in
[Color reduction](../06-Processing/08-ColorReduction.md).

## Pitfalls

- Durations are hundredths of a second: 1/30 s is rounded to 0.03 s by default. A duration above 655.35 s is rejected,
  never clamped, and so is a play count above 65,536.
- A frame is at most 65,535 pixels per side.
- Every frame is written as a full frame, without delta rectangles, so an animation that changes a small area stays
  large. An APNG saved with [PngOptimization.MinimumSize](cref:Meziantou.Framework.Imaging.Formats.PngOptimization.MinimumSize) stores only the rectangles that changed (see
  [PNG and APNG](02-Png.md)).
- The plain-text extension (text rendered by the viewer) is rejected with an [UnsupportedImageFeatureException](cref:Meziantou.Framework.Imaging.UnsupportedImageFeatureException). Other
  unknown extensions, such as XMP or ICC application extensions, are skipped.
- The background color of the file is never painted: areas a frame clears are transparent black.
- A writer created with [Image.CreateWriter](cref:Meziantou.Framework.Imaging.Image.CreateWriter) streams frames as they come and does not need to know the frame count.
