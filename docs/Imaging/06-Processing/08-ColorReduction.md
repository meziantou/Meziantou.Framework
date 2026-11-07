---
title: Color reduction
description: Reduce the colors of an image to a palette, share one palette between the frames of an animation, and dither.
---

[Quantize](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.Quantize) reduces the colors of every frame to a palette, in place: at most [QuantizeOptions.MaxColors](cref:Meziantou.Framework.Imaging.QuantizeOptions.MaxColors) distinct colors
per frame, or only the colors of [QuantizeOptions.Palette](cref:Meziantou.Framework.Imaging.QuantizeOptions.Palette). Use it for a retro or poster look, to prepare an image for a
palette format, or to make a small file.

## Reduce to a number of colors

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_Quantize;format=md-fenced-code-block;dedent=true -->
```csharp
public static void QuantizePhoto(string inputPath, string outputPath)
{
    using var image = Image.Load(inputPath);

    // Each frame gets its own palette of at most 64 colors (its exact colors when they fit), and Atkinson error
    // diffusion hides the bands of smooth gradients. The pixel format is kept: the image can be saved in any format.
    image.Quantize(new QuantizeOptions { MaxColors = 64, Dithering = DitheringMode.Atkinson });
    image.Save(outputPath);
}
```
<!-- endref -->

- [MaxColors](cref:Meziantou.Framework.Imaging.QuantizeOptions.MaxColors) is between 2 and 256, the transparent entry included.
- A frame whose colors fit keeps them exactly. Otherwise each frame gets its own palette, computed from its pixels.
- The pixel format is kept: the image can then be saved in any format. The colors are reduced as 8-bit samples (a
  16-bit or floating-point sample gets the exact value of an 8-bit one), and every fully transparent pixel becomes
  transparent black.
- Translucent colors get palette entries of their own, and an opaque pixel always gets an opaque entry: opaque areas
  never become translucent.

## Share a palette between frames

[PaletteBuilder](cref:Meziantou.Framework.Imaging.PaletteBuilder) computes one [ImagePalette](cref:Meziantou.Framework.Imaging.ImagePalette) from several images or frames: the frames of an animation, or a set of
icons. [QuantizeOptions.Palette](cref:Meziantou.Framework.Imaging.QuantizeOptions.Palette) then maps each of them to the same colors, so that the colors do not flicker from one
frame to the next:

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

An [ImagePalette](cref:Meziantou.Framework.Imaging.ImagePalette) can also be listed color by color, from at most 256 [Rgba32](cref:Meziantou.Framework.Imaging.Rgba32) colors, for a fixed palette such as the
colors of a brand or of an old console (see [Binarization](09-Binarization.md) for black and white). A gray pixel
format uses only the gray entries of a fixed palette, and a format without alpha only its opaque entries.

## Dithering

Dithering hides the bands that fewer colors make in smooth gradients, by spreading the difference between each pixel
and its palette color. [QuantizeOptions.Dithering](cref:Meziantou.Framework.Imaging.QuantizeOptions.Dithering) chooses how:

| [DitheringMode](cref:Meziantou.Framework.Imaging.DitheringMode) | Kind |
| --- | --- |
| [None](cref:Meziantou.Framework.Imaging.DitheringMode.None) (default) | Every pixel gets its nearest palette color |
| [FloydSteinberg](cref:Meziantou.Framework.Imaging.DitheringMode.FloydSteinberg), [Atkinson](cref:Meziantou.Framework.Imaging.DitheringMode.Atkinson), [Burkes](cref:Meziantou.Framework.Imaging.DitheringMode.Burkes), [JarvisJudiceNinke](cref:Meziantou.Framework.Imaging.DitheringMode.JarvisJudiceNinke), [Sierra](cref:Meziantou.Framework.Imaging.DitheringMode.Sierra), [SierraTwoRow](cref:Meziantou.Framework.Imaging.DitheringMode.SierraTwoRow), [SierraLite](cref:Meziantou.Framework.Imaging.DitheringMode.SierraLite), [Stucki](cref:Meziantou.Framework.Imaging.DitheringMode.Stucki) | Error diffusion: the error of each opaque pixel goes to the next ones, from left to right and from the top row down |
| [Bayer2x2](cref:Meziantou.Framework.Imaging.DitheringMode.Bayer2x2), [Bayer4x4](cref:Meziantou.Framework.Imaging.DitheringMode.Bayer4x4), [Bayer8x8](cref:Meziantou.Framework.Imaging.DitheringMode.Bayer8x8) | Ordered dithering: a fixed pattern, spread over the mean distance between the palette colors |

[DitherStrength](cref:Meziantou.Framework.Imaging.QuantizeOptions.DitherStrength), from 0 to 1, scales the error or the pattern. A frame whose colors are all palette entries is never
dithered. [Atkinson](cref:Meziantou.Framework.Imaging.DitheringMode.Atkinson) is the best choice for photographs.

## Determinism

Every decision of the quantizer uses integer arithmetic, or IEEE operations that give the same result everywhere: the
same pixels and options give the same output on every platform.

## Encoders

The encoders of palette formats take the same options with their `Quantization` property, and use the same quantizer:
[GifEncoder](cref:Meziantou.Framework.Imaging.Formats.GifEncoder), [PngEncoder](cref:Meziantou.Framework.Imaging.Formats.PngEncoder) (for an indexed PNG) and [BmpEncoder](cref:Meziantou.Framework.Imaging.Formats.BmpEncoder). Each format applies its own transparency rules first:
GIF thresholds or flattens alpha, BMP composites it over its background color, and an indexed PNG keeps translucent
entries. See [GIF](../10-Formats/03-Gif.md), [PNG](../10-Formats/02-Png.md) and
[BMP](../10-Formats/07-Bmp.md).
