---
title: BMP
description: Read Windows and OS/2 bitmaps, and write 24-bit, 32-bit, indexed, run-length or 16-bit BMP files.
---

BMP is the uncompressed bitmap format of Windows. `BmpEncoder` writes the most widely readable layouts by default, and
the indexed, run-length and 16-bit layouts on request. Nothing is lost silently: a layout that drops alpha, colors or
precision needs an explicit setting.

## What is read and written

| | Reading | Writing |
| --- | --- | --- |
| Headers | Windows headers up to V5, OS/2 1.x and 2.x headers | Windows headers |
| Layouts | 1, 4 and 8-bit indexed, 16, 24 and 32 bits, bit fields | 24-bit, 32-bit with alpha, 1, 4 and 8-bit indexed, 16-bit 5-6-5 and 5-5-5 |
| Compression | RLE4, RLE8 and the OS/2 RLE24 | RLE4 and RLE8 |
| Rows | Bottom-up and top-down | Bottom-up (default) or top-down |
| Metadata | Resolution | Resolution |

A 32-bit file is transparent only when it declares an alpha mask: the fourth byte of a plain 32-bit file is padding, so
such a file loads as opaque `Rgb24`.

## Save a BMP file

By default (`BmpPixelLayout.Auto`), pixel formats with alpha are written as 32-bit with an explicit alpha mask, and the
others as 24-bit. Another layout is chosen explicitly, with the settings for what it drops:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_SaveAssetFormats;format=md-fenced-code-block;dedent=true -->
```csharp
public static void SaveAssetFormats(string inputPath, string bmpPath, string tgaPath, string pnmPath)
{
    using var image = Image.Load(inputPath);
    var frame = image.Frames[0]; // BMP, TGA and Netpbm store one still image

    // BMP: 32-bit with an explicit alpha mask for pixel formats with alpha, 24-bit otherwise. Forcing 24-bit discards
    // alpha, so it needs a background color.
    frame.Save(bmpPath, new BmpEncoder { PixelLayout = BmpPixelLayout.Bgr24, BackgroundColor = new Rgba64(65535, 65535, 65535), AllowBitDepthReduction = true });

    // TGA: run-length packets are lossless and usually smaller on graphic content
    frame.Save(tgaPath, new TgaEncoder { Compression = TgaCompression.RunLength, AllowBitDepthReduction = true });

    // Netpbm: the variant follows the pixel format (PGM, PPM, or PAM when the pixels have alpha), and MAXVAL follows
    // the precision, so 16-bit images keep every bit. A portable bitmap (PBM) is only written on request, with
    // PnmPixelLayout.Bitmap, and only from black and white pixels.
    frame.Save(pnmPath, new PnmEncoder());
}
```
<!-- endref -->

## Write an indexed bitmap

The indexed layouts store the colors of the image exactly when the palette can hold them (2, 16 or 256 colors), and reject
the image otherwise, unless `AllowColorReduction` is set. `Indexed4` and `Indexed8` can be run-length encoded:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_SaveBmpIndexed;format=md-fenced-code-block;dedent=true -->
```csharp
public static void SaveIndexedBmp(string inputPath, string outputPath)
{
    using var image = Image.Load(inputPath);
    var frame = image.Frames[0];

    // 8-bit indexes, run-length encoded (BI_RLE8). The palette holds the colors of the frame exactly when there are at
    // most 256 of them; AllowColorReduction accepts a deterministic palette for the other frames, here with dithering.
    // A BMP palette has no alpha, so non-opaque pixels need a background color.
    frame.Save(outputPath, new BmpEncoder
    {
        PixelLayout = BmpPixelLayout.Indexed8,
        Compression = BmpCompression.RunLength,
        AllowColorReduction = true,
        Quantization = new QuantizeOptions { Dithering = DitheringMode.FloydSteinberg },
        BackgroundColor = new Rgba64(65535, 65535, 65535),
        AllowBitDepthReduction = true,
    });
}
```
<!-- endref -->

A BMP palette has no alpha, so an image with pixels that are not opaque needs `BackgroundColor`. Run-length output is
lossless, but can be larger than uncompressed output.

## Options

| Option | What it does | Default |
| --- | --- | --- |
| `PixelLayout` | `Auto`, `Bgr24`, `Bgra32`, `Indexed1`, `Indexed4`, `Indexed8`, `Bgr565` or `Bgr555` | `Auto` |
| `Compression` | `RunLength` encodes `Indexed4` and `Indexed8` bottom-up layouts | `None` |
| `RowOrder` | `BottomUp` or `TopDown` | `BottomUp` |
| `BackgroundColor` | The opaque color pixels that are not opaque are composited over, by every layout but `Bgra32`; `null` rejects them | `null` |
| `AllowBitDepthReduction` | Lets 16-bit pixel formats be rounded to 8 bits, and any pixel format to the 5 or 6 bits of the 16-bit layouts | `false` |
| `AllowColorReduction` | Lets an indexed layout reduce the colors that do not fit its palette | `false` |
| `Quantization` | How the colors are reduced: number of colors, dithering, fixed palette (see [Color reduction](../06-Processing/08-ColorReduction.md)) | 256 colors (limited by the layout), no dithering |
| `MetadataHandling` | What to do with metadata BMP cannot store | `Strict` |

`MaxColors` and `Dithering` are obsolete aliases of `Quantization.MaxColors` and `Quantization.Dithering`.

## Pitfalls

- BMP stores one frame: save one frame of an animation with `image.Frames[i].Save`.
- `Compression = RunLength` with another layout than `Indexed4` or `Indexed8`, or with top-down rows, throws an
  `ArgumentException`.
- BMP stores no ICC profile, EXIF, XMP, orientation or text: they follow `MetadataHandling`, which rejects them by
  default. Embedded ICC profiles of V4 and V5 headers are not read.
- The decoder rejects embedded JPEG or PNG payloads, OS/2 Huffman compression, 2-bit files and channels wider than 8 bits
  with an `UnsupportedImageFeatureException`. Run-length data is read strictly: a run past its row or a missing
  end-of-bitmap marker is an `InvalidImageContentException`.
- The indexed layouts keep one byte per pixel of working memory while they encode.
