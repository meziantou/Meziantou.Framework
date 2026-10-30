---
title: AVIF
description: Read and write AVIF images and image sequences, lossless or lossy, at 8, 10 or 12 bits.
---

AVIF stores AV1-coded images in the HEIF container: still images, image sequences (animations), alpha, 10 and 12-bit
samples and HDR color descriptions. The library has its own AV1 decoder and encoder. `AvifEncoder` is **lossless by
default**, which makes large files from photos: choose lossy compression for them.

## What is read and written

| | Reading | Writing |
| --- | --- | --- |
| Images | Still images and image sequences; grids; overlays and derived images | Still images and image sequences; grids |
| Samples | 8, 10 and 12 bits; 4:4:4, 4:2:2, 4:2:0 and monochrome | The same |
| Alpha | Straight or premultiplied | Lossless, straight or premultiplied |
| Geometry | Clean aperture applied; orientation reported | Clean aperture and orientation |
| Metadata | ICC profile, EXIF, XMP, CICP color description, content light level, mastering display, pixel aspect ratio | The same |

8-bit files load as `Rgb24`, `Rgba32` with alpha, or `Gray8`; 10 and 12-bit files load as `Rgb48`, `Rgba64` or
`Gray16`, their samples scaled to 16 bits exactly. Premultiplied colors are converted to straight alpha.

## Load an AVIF file

The orientation of the file (`irot` and `imir`) is reported in `Metadata.Orientation`, never applied: call `AutoOrient`
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
| `Compression` | `Lossless` or `Lossy` | `Lossless` |
| `Quality` | 0 (smallest) to 100 (best) for lossy output | 75 |
| `Effort` | 0 (fastest, largest) to 9 (slowest, smallest): trades encoding time for size, never changes lossless pixels | 5 |
| `ChromaSubsampling` | `Auto` (4:2:0), `Ratio420`, `Ratio422` or `Ratio444`, for lossy output | `Auto` |
| `BitDepth` | 8, 10 or 12; `null` keeps the depth of the pixel format. A value for a 16-bit pixel format allows rounding to it | `null` |
| `AllowBitDepthReduction` | Lets 16-bit pixel formats be rounded to 12 bits when `BitDepth` is not set | `false` |
| `ClearTransparentColors` | Replaces the color of fully transparent pixels with black, which makes the file smaller | `false` |
| `PremultiplyAlpha` | Stores colors multiplied by their alpha; the colors of nearly transparent pixels are rounded | `false` |
| `GridTileSize` | Cuts a still image larger than a tile into tiles coded separately (64 to 65,536 pixels per side) | `null` |
| `CleanAperture` | The rectangle readers show; every pixel is still stored | `null` |
| `KeyFrameInterval` | The number of images from a key frame to the next in a sequence; 0 makes the first image the only key frame | 0 |
| `DurationRounding` | `RequireExact` stores durations exactly or throws; `RoundToNearest` rounds them to microseconds when they need it | `RequireExact` |
| `MetadataHandling` | What to do with metadata AVIF cannot store (text, resolution) | `Strict` |

## Encoding time

Encoding a large image takes time: a lower `Effort` encodes faster, with larger files. A large still image with
`GridTileSize` (512 x 512 is a common tile size) is coded tile by tile, with bounded memory; with
`ImageConfiguration.MaxDegreeOfParallelism` above 1, the tiles are coded concurrently and the file is identical. Lossy
output can show the edges of the tiles.

## Pitfalls

- `Save("photo.avif")` is lossless: pass an `AvifEncoder` with `Compression = AvifCompression.Lossy` for photos.
- AVIF stores at most 12 bits: `Rgba64` and `Gray16` images throw unless `BitDepth` or `AllowBitDepthReduction` is set.
- An image sequence needs a seekable destination and a known frame count (`ImageWriterOptions.ExpectedFrameCount` for
  a writer), because the index of the images is written last. A still image goes to any stream.
- An image sequence of one image cannot store animation settings: it is read back as a still image.
- A side is at most 65,536 pixels (65,535 for an image sequence), and a still image at most 4 GiB.
- Thumbnails, gain maps and depth maps are not read; the decoder presents the primary image, or the sequence of a file
  with the `avis` brand.
- What the decoder rejects (such as images built from images of several color descriptions) is listed in
  [Limitations](../12-Limitations.md).
