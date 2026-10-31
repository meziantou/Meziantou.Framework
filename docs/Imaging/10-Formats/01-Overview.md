---
title: Supported formats
description: The formats the library reads and writes, how it detects them, which extension selects which encoder, and how to choose one.
---

The library reads and writes every format below with its own managed codecs. This group has one page per format: what
is read, what is written, how to save with its encoder and its options, and the pitfalls.

## Support matrix

| Format | Reads | Writes | Animations |
| --- | --- | --- | --- |
| [PNG and APNG](02-Png.md) | Every color type and bit depth, palettes, interlacing, Apple CgBI files | Lossless: gray, RGB and RGBA at 8 or 16 bits, palettes on request | Read and written |
| [GIF](03-Gif.md) | GIF87a and GIF89a, interlacing, transparency, comments | Lossy by design: 256 colors per frame, one transparent color | Read and written |
| [JPEG](04-Jpeg.md) | Baseline, progressive, arithmetic coded, 12-bit, lossless; CMYK with a color conversion | Lossy, 8 bits, baseline or progressive, gray or YCbCr | No |
| [WebP](05-WebP.md) | Lossless, lossy, alpha | Lossless (default) or lossy, alpha | Read and written |
| [QOI](06-Qoi.md) | Every file | Lossless 8-bit RGB and RGBA | No |
| [BMP](07-Bmp.md) | Windows and OS/2 bitmaps, 1 to 32 bits, bit fields, run-length | 24 or 32 bits by default; indexed, run-length and 16 bits on request | No |
| [TGA](08-Tga.md) | True-color, grayscale and color-mapped, uncompressed or run-length | True-color and grayscale, uncompressed or run-length | No |
| [Netpbm](09-Netpbm.md) | PBM, PGM, PPM and PAM, 8 and 16 bits | Lossless; PBM on request | No |
| [TIFF and BigTIFF](10-Tiff.md) | Multi-page documents, most compressions and sample layouts, floating-point samples, CMYK with a color conversion | Multi-page documents, lossless compressions or JPEG, bilevel, palette, 32-bit floating-point | No (pages) |
| [ICO and CUR](11-IcoAndCur.md) | PNG and bitmap representations, cursor hotspots | PNG or 32-bit bitmap representations, hotspots | No (representations) |
| [ANI](12-Ani.md) | Animated cursors | Animated cursors | Read and written |
| [AVIF](13-Avif.md) | 8 to 12 bits, alpha, grids, HDR descriptions | Lossless (default) or lossy, 8 to 12 bits, alpha, grids | Read and written |
| [HEIC](14-Heic.md) | 8 to 12 bits, alpha, grids, photos of phones and cameras | Lossless (default) or lossy, 8 to 12 bits, alpha, grids | Read and written |
| [JPEG XL](15-JpegXl.md) | Lossless and lossy, up to 16 bits, recompressed JPEG files, progressive renderings | Lossless (default) or lossy, up to 16 bits; JPEG files recompressed without loss | Read and written |
| [JPEG 2000](16-Jpeg2000.md) | JP2 files and codestreams, up to 16 bits, HTJ2K | Lossless (default) or lossy, up to 16 bits | No |
| [OpenEXR](17-OpenExr.md) | Half, float and integer samples, most compressions | Lossless half or float samples | No |

What a format cannot store is never dropped silently: saving an animation as JPEG, transparency to a format without
it, 16-bit samples to an 8-bit format or metadata the format has no place for throws an
[UnsupportedImageFeatureException](cref:Meziantou.Framework.Imaging.UnsupportedImageFeatureException), unless a setting of the encoder allows the conversion. See
[Saving images](../05-LoadingAndSaving/04-SavingImages.md). What the codecs do not support is listed in
[Limitations](../12-Limitations.md).

## Detection by content

[Image.Load](cref:Meziantou.Framework.Imaging.Image.Load), [Image.Identify](cref:Meziantou.Framework.Imaging.Image.Identify) and the readers detect the format from the first bytes of the data, never from the file
name: a PNG file named `photo.jpg` loads as a PNG. [Image.DetectFormat](cref:Meziantou.Framework.Imaging.Image.DetectFormat) returns the format of a prefix of the data, and
needs [Image.FormatDetectionPrefixLength](cref:Meziantou.Framework.Imaging.Image.FormatDetectionPrefixLength) (18) bytes to recognize every format: TGA has no signature, so it is
recognized from its whole header, and checked last. Data that no format recognizes throws an
[UnknownImageFormatException](cref:Meziantou.Framework.Imaging.UnknownImageFormatException). See [Identifying images](../05-LoadingAndSaving/03-IdentifyingImages.md).

## Extensions of `Save(path)`

`Save(path)` without an encoder selects the encoder from the extension, with its default settings.
`Save(stream, encoder)` always needs an encoder.

| Extension | Encoder | Output |
| --- | --- | --- |
| `.png` | [PngEncoder](cref:Meziantou.Framework.Imaging.Formats.PngEncoder) | PNG, or APNG when the image is animated |
| `.apng` | `PngEncoder { AnimationMode = PngAnimationMode.Animated }` | Always APNG |
| `.gif` | [GifEncoder](cref:Meziantou.Framework.Imaging.Formats.GifEncoder) | GIF |
| `.jpg`, `.jpeg` | [JpegEncoder](cref:Meziantou.Framework.Imaging.Formats.JpegEncoder) | Baseline JPEG, quality 90 |
| `.webp` | [WebPEncoder](cref:Meziantou.Framework.Imaging.Formats.WebPEncoder) | Lossless WebP |
| `.qoi` | [QoiEncoder](cref:Meziantou.Framework.Imaging.Formats.QoiEncoder) | QOI |
| `.bmp`, `.dib` | [BmpEncoder](cref:Meziantou.Framework.Imaging.Formats.BmpEncoder) | 24-bit, or 32-bit with alpha |
| `.tga`, `.icb`, `.vda`, `.vst` | [TgaEncoder](cref:Meziantou.Framework.Imaging.Formats.TgaEncoder) | Uncompressed TGA |
| `.pnm`, `.pam`, `.ppm`, `.pgm` | [PnmEncoder](cref:Meziantou.Framework.Imaging.Formats.PnmEncoder) | The Netpbm variant of the pixel format (PGM, PPM or PAM) |
| `.pbm` | `PnmEncoder { PixelLayout = PnmPixelLayout.Bitmap }` | A portable bitmap, from black and white pixels only |
| `.tif`, `.tiff` | [TiffEncoder](cref:Meziantou.Framework.Imaging.Formats.TiffEncoder) | Deflate-compressed TIFF |
| `.ico` | [IcoEncoder](cref:Meziantou.Framework.Imaging.Formats.IcoEncoder) | Icon |
| `.cur` | `IcoEncoder { Kind = IconKind.Cursor }` | Cursor |
| `.ani` | [AniEncoder](cref:Meziantou.Framework.Imaging.Formats.AniEncoder) | Animated cursor |
| `.avif`, `.avifs` | [AvifEncoder](cref:Meziantou.Framework.Imaging.Formats.AvifEncoder) | Lossless AVIF |
| `.heic`, `.heif`, `.heics`, `.heifs` | [HeicEncoder](cref:Meziantou.Framework.Imaging.Formats.HeicEncoder) | Lossless HEIC |
| `.jxl` | [JpegXlEncoder](cref:Meziantou.Framework.Imaging.Formats.JpegXlEncoder) | Lossless JPEG XL |
| `.jp2` | [Jpeg2000Encoder](cref:Meziantou.Framework.Imaging.Formats.Jpeg2000Encoder) | Lossless JP2 file |
| `.j2k`, `.j2c`, `.jpc` | `Jpeg2000Encoder { Container = Jpeg2000Container.Codestream }` | Lossless JPEG 2000 codestream |
| `.exr` | [OpenExrEncoder](cref:Meziantou.Framework.Imaging.Formats.OpenExrEncoder) | ZIP-compressed OpenEXR |

Any other extension throws an `ArgumentException`, including `.jpx`, `.jpf`, `.jph` and `.jhc`: those JPEG 2000 files
are read, not written. Note that WebP, AVIF, HEIC, JPEG XL and JPEG 2000 are lossless by default, which makes large
files from photos: pass an encoder with lossy compression for them.

## Choosing a format

- **Photos**: JPEG is read everywhere. WebP, AVIF and JPEG XL have lossy modes too, usually smaller at the same visual
  quality; the AVIF and HEIC encoders search longer, and their `Effort` setting trades encoding time for size. HEIC is
  the format of the photos of phones: read it, and convert it to share it (see
  [Handling phone photos](../03-Guides/05-HandlePhonePhotos.md)).
- **Graphics, screenshots, logos and transparency**: PNG, or lossless WebP. An image with few colors gets smaller as
  an indexed PNG; [PngOptimization.MinimumSize](cref:Meziantou.Framework.Imaging.Formats.PngOptimization.MinimumSize) searches the smallest lossless PNG.
- **Animations**: GIF is read everywhere but has 256 colors per frame and no partial transparency. APNG and lossless
  WebP keep every color and alpha value; lossy WebP and AVIF image sequences are smaller. See
  [Processing animated images](../03-Guides/04-ProcessAnimatedImages.md).
- **High dynamic range**: OpenEXR stores floating-point samples, and so does TIFF; AVIF, HEIC, JPEG XL and 16-bit PNG
  store HDR samples as integers with their color description. See [Pixel formats](../04-Concepts/05-PixelFormats.md).
- **Archival and lossless masters**: PNG, TIFF (also for multi-page documents), and JPEG XL or JPEG 2000 in lossless
  mode, which keep 16-bit samples. A JPEG file can be recompressed to JPEG XL without loss and rebuilt byte for byte
  (see [JPEG XL](15-JpegXl.md)).
- **Scanned documents**: a bilevel TIFF with CCITT Group 4 compression.
- **Icons and cursors**: ICO, CUR and ANI.
- **Tooling, game assets and test fixtures**: QOI, TGA, BMP and Netpbm are simple formats, lossless by default.

To convert files from one format to another, see [Converting between formats](../03-Guides/03-ConvertBetweenFormats.md).

## Format pages

- [PNG and APNG](02-Png.md): lossless still images and animations, palettes, the smallest file.
- [GIF](03-Gif.md): animations for every viewer, and how colors and transparency are reduced.
- [JPEG](04-Jpeg.md): photos; quality, progressive files, smaller baseline files, and what JPEG cannot store.
- [WebP](05-WebP.md): lossless and lossy images and animations for the web.
- [QOI](06-Qoi.md): a simple lossless format for 8-bit RGB and RGBA images.
- [BMP](07-Bmp.md): Windows bitmaps, indexed and run-length layouts.
- [TGA](08-Tga.md): asset pipelines; a format without a signature.
- [Netpbm](09-Netpbm.md): PBM, PGM, PPM and PAM, for tools and fixtures.
- [TIFF and BigTIFF](10-Tiff.md): multi-page documents, scans and floating-point pages.
- [ICO and CUR](11-IcoAndCur.md): icons and cursors with several sizes.
- [ANI](12-Ani.md): animated cursors.
- [AVIF](13-Avif.md): small photos and image sequences, HDR.
- [HEIC](14-Heic.md): photos of phones and cameras.
- [JPEG XL](15-JpegXl.md): lossless and lossy images, progressive files, lossless JPEG recompression.
- [JPEG 2000](16-Jpeg2000.md): lossless and lossy images for archives and imaging pipelines.
- [OpenEXR](17-OpenExr.md): linear-light, floating-point images for rendering and visual effects.
