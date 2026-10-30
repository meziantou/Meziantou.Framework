---
title: AVIF
description: Read and write AVIF images and image sequences, lossless or lossy, at 8, 10 or 12 bits.
---

AVIF stores AV1-coded images in the HEIF container: still images, image sequences (animations), alpha, 10 and 12-bit
samples and HDR color descriptions. The library has its own AV1 decoder and encoder. [AvifEncoder](cref:Meziantou.Framework.Imaging.Formats.AvifEncoder) is **lossless by
default**, which makes large files from photos: choose lossy compression for them.

## What is read and written

| | Reading | Writing |
| --- | --- | --- |
| Images | Still images and image sequences; grids; overlays and derived images | Still images and image sequences; grids |
| Samples | 8, 10 and 12 bits; 4:4:4, 4:2:2, 4:2:0 and monochrome | The same |
| Alpha | Straight or premultiplied | Lossless, straight or premultiplied |
| Geometry | Clean aperture applied; orientation reported | Clean aperture and orientation |
| Metadata | ICC profile, EXIF, XMP, CICP color description, content light level, mastering display, pixel aspect ratio | The same |

8-bit files load as [Rgb24](cref:Meziantou.Framework.Imaging.Rgb24), [Rgba32](cref:Meziantou.Framework.Imaging.Rgba32) with alpha, or [Gray8](cref:Meziantou.Framework.Imaging.Gray8); 10 and 12-bit files load as [Rgb48](cref:Meziantou.Framework.Imaging.Rgb48), [Rgba64](cref:Meziantou.Framework.Imaging.Rgba64) or
[Gray16](cref:Meziantou.Framework.Imaging.Gray16), their samples scaled to 16 bits exactly. Premultiplied colors are converted to straight alpha.

## Load an AVIF file

The orientation of the file (`irot` and `imir`) is reported in `Metadata.Orientation`, never applied: call [AutoOrient](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.AutoOrient)
to rotate the pixels. The color description is a label, like an ICC profile:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_LoadAvif;format=md-fenced-code-block;dedent=true -->
```csharp
public static string ConvertAvif(string avifPath, string pngPath)
{
    using var image = Image.Load(avifPath);

    // The color description of the file (primaries, transfer characteristics, matrix and range code points) is a
    // label, as an ICC profile is: the matrix and the range were applied to produce RGB, nothing else was converted.
    var description = $"{image.Width}x{image.Height} {image.PixelFormat}, frames: {image.Frames.Count}, color: {image.Metadata.Cicp}";

    // PNG keeps 16-bit samples, alpha and animations, without loss, and the primaries and the transfer
    // characteristics of the description (cICP chunk), so the samples of an HDR image keep their meaning.
    image.Save(pngPath, new PngEncoder { MetadataHandling = MetadataHandling.Strict });
    return description;
}
```
<!-- endref -->

## Save an AVIF file

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_SaveAvif;format=md-fenced-code-block;dedent=true -->
```csharp
public static void SaveAvif(string photoPath, string avifPath, string losslessPath, string animatedPath, string animatedAvifPath)
{
    using (var photo = Image.Load(photoPath))
    {
        // Lossy: Y'CbCr 4:2:0 by default. The alpha plane, when the image has one, is always stored losslessly.
        photo.Save(avifPath, new AvifEncoder
        {
            Compression = AvifCompression.Lossy,
            Quality = 80, // 0-100: size versus fidelity
            Effort = 6,   // 0-9: encoding time versus size
            AllowBitDepthReduction = true, // 16-bit sources are written with 12 bits
        });

        // The default is lossless: every sample of an 8-bit image is decoded back exactly. AVIF stores at most 12 bits:
        // a 16-bit image needs a bit depth, which is the consent to round its samples.
        photo.Save(losslessPath, new AvifEncoder { BitDepth = 12 });
    }

    // An animation is written as an image sequence, with its exact durations and its play count. It needs a seekable
    // destination (the index of the images is written last): a path, a FileStream or a MemoryStream.
    using var animation = Image.Load(animatedPath);
    animation.Save(animatedAvifPath, new AvifEncoder());
}
```
<!-- endref -->

- Lossless output codes the RGB samples as they are (4:4:4), and gray images as monochrome: every sample is decoded back
  exactly. Lossy output codes YCbCr samples, 4:2:0 by default.
- Alpha is always stored losslessly, as a separate image. An image whose pixels are all opaque has none.
- An animation is written as an image sequence with its exact durations and its play count.

## Options

| Option | What it does | Default |
| --- | --- | --- |
| [Compression](cref:Meziantou.Framework.Imaging.Formats.AvifEncoder.Compression) | [Lossless](cref:Meziantou.Framework.Imaging.Formats.AvifCompression.Lossless) or [Lossy](cref:Meziantou.Framework.Imaging.Formats.AvifCompression.Lossy) | [Lossless](cref:Meziantou.Framework.Imaging.Formats.AvifCompression.Lossless) |
| [Quality](cref:Meziantou.Framework.Imaging.Formats.AvifEncoder.Quality) | 0 (smallest) to 100 (best) for lossy output | 75 |
| [Effort](cref:Meziantou.Framework.Imaging.Formats.AvifEncoder.Effort) | 0 (fastest, largest) to 9 (slowest, smallest): trades encoding time for size, never changes lossless pixels | 5 |
| [ChromaSubsampling](cref:Meziantou.Framework.Imaging.Formats.AvifEncoder.ChromaSubsampling) | [Auto](cref:Meziantou.Framework.Imaging.Formats.AvifChromaSubsampling.Auto) (4:2:0), [Ratio420](cref:Meziantou.Framework.Imaging.Formats.AvifChromaSubsampling.Ratio420), [Ratio422](cref:Meziantou.Framework.Imaging.Formats.AvifChromaSubsampling.Ratio422) or [Ratio444](cref:Meziantou.Framework.Imaging.Formats.AvifChromaSubsampling.Ratio444), for lossy output | [Auto](cref:Meziantou.Framework.Imaging.Formats.AvifChromaSubsampling.Auto) |
| [BitDepth](cref:Meziantou.Framework.Imaging.Formats.AvifEncoder.BitDepth) | 8, 10 or 12; `null` keeps the depth of the pixel format. A value for a 16-bit pixel format allows rounding to it | `null` |
| [AllowBitDepthReduction](cref:Meziantou.Framework.Imaging.Formats.AvifEncoder.AllowBitDepthReduction) | Lets 16-bit pixel formats be rounded to 12 bits when [BitDepth](cref:Meziantou.Framework.Imaging.Formats.AvifEncoder.BitDepth) is not set | `false` |
| [ClearTransparentColors](cref:Meziantou.Framework.Imaging.Formats.AvifEncoder.ClearTransparentColors) | Replaces the color of fully transparent pixels with black, which makes the file smaller | `false` |
| [PremultiplyAlpha](cref:Meziantou.Framework.Imaging.Formats.AvifEncoder.PremultiplyAlpha) | Stores colors multiplied by their alpha; the colors of nearly transparent pixels are rounded | `false` |
| [GridTileSize](cref:Meziantou.Framework.Imaging.Formats.AvifEncoder.GridTileSize) | Cuts a still image larger than a tile into tiles coded separately (64 to 65,536 pixels per side) | `null` |
| [AutoTiling](cref:Meziantou.Framework.Imaging.Formats.AvifEncoder.AutoTiling) | Splits each frame into AV1 tiles from its size (4x2 for 3840x2160), which workers encode and decode at once; files are slightly larger | `false` |
| [CleanAperture](cref:Meziantou.Framework.Imaging.Formats.AvifEncoder.CleanAperture) | The rectangle readers show; every pixel is still stored | `null` |
| [KeyFrameInterval](cref:Meziantou.Framework.Imaging.Formats.AvifEncoder.KeyFrameInterval) | The number of images from a key frame to the next in a sequence; 0 makes the first image the only key frame | 0 |
| [DurationRounding](cref:Meziantou.Framework.Imaging.Formats.AvifEncoder.DurationRounding) | [RequireExact](cref:Meziantou.Framework.Imaging.Formats.FrameDurationRounding.RequireExact) stores durations exactly or throws; [RoundToNearest](cref:Meziantou.Framework.Imaging.Formats.FrameDurationRounding.RoundToNearest) rounds them to microseconds when they need it | [RequireExact](cref:Meziantou.Framework.Imaging.Formats.FrameDurationRounding.RequireExact) |
| [MetadataHandling](cref:Meziantou.Framework.Imaging.Formats.MetadataHandling) | What to do with metadata AVIF cannot store (text, resolution) | [Strict](cref:Meziantou.Framework.Imaging.Formats.MetadataHandling.Strict) |

## Encoding time

Encoding a large image takes time: a lower [Effort](cref:Meziantou.Framework.Imaging.Formats.AvifEncoder.Effort) encodes faster, with larger files. A large still image with
[GridTileSize](cref:Meziantou.Framework.Imaging.Formats.AvifEncoder.GridTileSize) (512 x 512 is a common tile size) is coded tile by tile, with bounded memory; with
[ImageConfiguration.MaxDegreeOfParallelism](cref:Meziantou.Framework.Imaging.ImageConfiguration.MaxDegreeOfParallelism) above 1, the tiles are coded concurrently and the file is identical. Lossy
output can show the edges of the tiles.

[AutoTiling](cref:Meziantou.Framework.Imaging.Formats.AvifEncoder.AutoTiling) splits each frame into up to 8 AV1 tiles of at least 512 x 512 pixels (4x2 tiles for 3840 x 2160), which
the workers of [ImageConfiguration.MaxDegreeOfParallelism](cref:Meziantou.Framework.Imaging.ImageConfiguration.MaxDegreeOfParallelism) encode at once, and which the decoder decodes at once. The
layout depends on the size of the image only, so the file is the same whatever the number of workers. The tiles cost
size even with one worker: on a 3840 x 2160 photo, the file is 0.3% larger at the default effort (0.6% at effort 0,
0.04% losslessly); with 8 workers it is encoded about 6.5 times faster (2 times at effort 0) and decoded about 1.9 times
faster.

## Pitfalls

- `Save("photo.avif")` is lossless: pass an [AvifEncoder](cref:Meziantou.Framework.Imaging.Formats.AvifEncoder) with `Compression = AvifCompression.Lossy` for photos.
- AVIF stores at most 12 bits: [Rgba64](cref:Meziantou.Framework.Imaging.Rgba64) and [Gray16](cref:Meziantou.Framework.Imaging.Gray16) images throw unless [BitDepth](cref:Meziantou.Framework.Imaging.Formats.AvifEncoder.BitDepth) or [AllowBitDepthReduction](cref:Meziantou.Framework.Imaging.Formats.AvifEncoder.AllowBitDepthReduction) is set.
- An image sequence needs a seekable destination and a known frame count ([ImageWriterOptions.ExpectedFrameCount](cref:Meziantou.Framework.Imaging.ImageWriterOptions.ExpectedFrameCount) for
  a writer), because the index of the images is written last. A still image goes to any stream.
- An image sequence of one image cannot store animation settings: it is read back as a still image.
- A side is at most 65,536 pixels (65,535 for an image sequence), and a still image at most 4 GiB.
- Thumbnails, gain maps and depth maps are not read; the decoder presents the primary image, or the sequence of a file
  with the `avis` brand.
- What the decoder rejects (such as images built from images of several color descriptions) is listed in
  [Limitations](../12-Limitations.md).
