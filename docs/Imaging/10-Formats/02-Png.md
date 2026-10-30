---
title: PNG and APNG
description: Read and write lossless PNG images and APNG animations, write palettes, and find the smallest file.
---

PNG stores lossless still images, and APNG (animated PNG) lossless animations with full alpha. `PngEncoder` writes both:
nothing is ever reduced, unless you ask for a palette with color reduction.

## What is read and written

| | Reading | Writing |
| --- | --- | --- |
| Pixels | Every color type and bit depth (1 to 16 bits), palettes with transparency, Adam7 interlacing | Gray, gray and alpha, RGB and RGBA at 8 or 16 bits; palettes of 1, 2, 4 or 8 bits on request; Adam7 |
| Animations | APNG animations and their poster frame, composited into full frames with exact delays | APNG with exact delays, play count and poster frame |
| Metadata | ICC profile, color description (`cICP`), EXIF, XMP, text, resolution, HDR light levels | The same |
| Other | Apple CgBI files (the iOS-optimized PNG files of `xcrun pngcrush -iphone`) | Standard PNG only |

By default, the layout follows the pixel format of the image: `Gray8` and `Gray16` become grayscale, `Rgb24` and `Rgb48`
RGB, `Rgba32`, `Bgra32` and `Rgba64` RGBA, and 16-bit pixel formats keep their 16 bits. The encoder never looks at the
pixel values to reduce them: an opaque `Rgba32` image is written as RGBA.

## Options

| Option | What it does | Default |
| --- | --- | --- |
| `AnimationMode` | `Auto` writes an APNG when the image is animated (several frames, a poster or animation settings); `Animated` always writes an APNG; `Static` rejects animated images | `Auto` |
| `PixelLayout` | `Indexed` writes a palette of the colors of the image, each with its own alpha | `Auto` |
| `AllowColorReduction` | Lets `Indexed` reduce the colors of an image that does not fit in 256 colors | `false` |
| `Quantization` | How `Indexed` reduces colors: number of colors, dithering, fixed palette (see [Color reduction](../06-Processing/08-ColorReduction.md)) | 256 colors, no dithering |
| `Optimization` | `MinimumSize` searches the lossless options and writes the smallest file it finds | `None` |
| `CompressionLevel` | The zlib level; ignored by `MinimumSize` | `Optimal` |
| `Filter` | The row filter strategy, or one fixed filter for every row | `Adaptive` |
| `Interlaced` | Adam7 interlacing: a viewer can show the image while it downloads (see [Show an image while it downloads](../05-LoadingAndSaving/06-ShowImagesWhileTheyDownload.md)) | `false` |
| `DurationRounding` | `RoundToNearest` rounds the delays an APNG cannot store exactly (16-bit fractions); `RequireExact` throws | `RequireExact` |
| `MetadataHandling` | What to do with metadata PNG cannot store | `Strict` |

The `.png` extension selects `PngEncoder` with its defaults, and `.apng` always writes an APNG.

## Write a palette

An indexed PNG is usually smaller for images with few colors (icons, charts, pixel art). `PixelLayout = Indexed` stores
the colors exactly, or fails before writing anything when there are more than 256 of them, or 16-bit samples that are
not exactly 8-bit values:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_SavePngIndexed;format=md-fenced-code-block;dedent=true -->
```csharp
public static bool SaveIndexedPng(string inputPath, string outputPath)
{
    using var image = Image.Load(inputPath);
    try
    {
        // One palette (PLTE, plus tRNS when a color is not opaque) for the colors of every frame, and 1-, 2-, 4- or
        // 8-bit indexes. The pixels are stored exactly, alpha included: nothing is ever reduced.
        image.Save(outputPath, new PngEncoder { PixelLayout = PngPixelLayout.Indexed });
        return true;
    }
    catch (UnsupportedImageFeatureException exception) when (exception.Feature is "Color reduction" or "Bit depth reduction")
    {
        // More than 256 colors, or 16-bit samples that 8 bits cannot represent: the save failed before it wrote
        // anything. The default layout follows the pixel format and stores every image.
        image.Save(outputPath, new PngEncoder());
        return false;
    }
}
```
<!-- endref -->

With `AllowColorReduction`, an image that does not fit is reduced with the quantizer of `Quantize`, and dithered as
`Quantization` says. An image that fits is still stored exactly:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_SavePngColorReduction;format=md-fenced-code-block;dedent=true -->
```csharp
public static void SavePalettePng(string inputPath, string outputPath)
{
    using var image = Image.Load(inputPath);

    // At most 256 colors, each with its own alpha (tRNS), in one palette for every frame. An image whose colors fit
    // is stored exactly; the others are reduced and dithered.
    image.Save(outputPath, new PngEncoder
    {
        PixelLayout = PngPixelLayout.Indexed,
        AllowColorReduction = true,
        Quantization = new QuantizeOptions { Dithering = DitheringMode.Atkinson },
    });
}
```
<!-- endref -->

- Fully transparent pixels keep their color, so they count as colors too.
- A file has one palette. `Save` computes it from every frame and the poster of an animation; a sequential writer takes
  it from the first image it is given, and maps the later frames to it.
- The palette order is deterministic. An indexed file loads as `Rgba32`.

## Write the smallest file

`Optimization = PngOptimization.MinimumSize` tries the layouts the pixels need (grayscale, a transparent color key, a
palette, 8 instead of 16 bits), every filter strategy and several zlib levels, and for an animation the rectangle that
changed in each frame. The pixels, alpha values, frames, durations and metadata stay exact; encoding takes several times
longer:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_SavePngMinimumSize;format=md-fenced-code-block;dedent=true -->
```csharp
public static void SaveSmallestPng(string inputPath, string outputPath)
{
    using var image = Image.Load(inputPath);

    // The layout the pixels need (grayscale, a color key, a palette, 8 instead of 16 bits...), the best filters and
    // zlib level, and for an animation the frame regions that changed. The pixels, alpha values, frames, durations and
    // metadata are stored exactly; only the encoding changes, and it takes several times longer.
    image.Save(outputPath, new PngEncoder { Optimization = PngOptimization.MinimumSize });
}
```
<!-- endref -->

The file may load with another pixel format than the one it was written from (`Rgb24` for an opaque `Rgba32` image,
`Rgba32` for a palette or a color key), with the same pixel values. A sequential writer chooses the layout from the first
image and rejects a later frame that the layout cannot store.

## Animations

Every frame of an image is written as a full frame with new control data, so the delta rectangles and disposal methods
of the source file never survive an edit. Delays are stored exactly by default: a duration that is not a fraction with
16-bit numerator and denominator throws, unless `DurationRounding` is `RoundToNearest`. A poster frame
(`PosterFrame`) is written as the default image, which viewers that do not play APNG show.

An APNG streams to any destination, including non-seekable streams, but a writer created with `Image.CreateWriter`
needs `ImageWriterOptions.ExpectedFrameCount`, because the frame count precedes the frames. See
[Streaming readers and writers](../05-LoadingAndSaving/05-StreamingReadersAndWriters.md).

## Malformed files

A malformed file is rejected with an `InvalidImageContentException`. With
[lenient decoding](../05-LoadingAndSaving/08-ErrorHandling.md), two deviations that browsers tolerate are accepted:

- A grayscale or RGB transparency key (`tRNS`) that is too short is ignored: the pixels are opaque.
- An APNG with fewer frames than it declares (one at least) is the frames it has. Identifying its header still reports
  the declared count; a full scan reports the frames present.

## Pitfalls

- PNG has no lossy mode: for photos, use a lossy format ([JPEG](04-Jpeg.md), [WebP](05-WebP.md), [AVIF](13-Avif.md) or
  [JPEG XL](15-JpegXl.md)).
- `AnimationMode = Static` throws for an animated image instead of dropping its frames: save one frame with
  `image.Frames[i].Save` to write a still image.
- Unknown critical chunks are rejected with an `UnsupportedImageFeatureException`; unknown ancillary chunks are skipped
  and not written back.
- Apple CgBI files store premultiplied colors: they are decoded, but the colors of nearly transparent pixels are lossy.
  They are never written.
