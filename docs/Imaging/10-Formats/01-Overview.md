---
title: Supported formats
description: The formats the library reads and writes, how it detects them, which extension selects which encoder, and how to choose one.
---

The library reads every format below with its own managed codecs, and writes all of them. This
group has one
page per format: what is read, what is written, how to save with its encoder and its options, and the pitfalls.

## Support matrix

| Format | Reads | Writes | Animations |
| --- | --- | --- | --- |
| [PNG and APNG](02-Png.md) | Every color type and bit depth, palettes, interlacing, Apple CgBI files | Lossless: gray, RGB and RGBA at 8 or 16 bits, palettes on request | Read and written |
| [GIF](03-Gif.md) | GIF87a and GIF89a, interlacing, transparency, comments | Lossy by design: 256 colors per frame, one transparent color | Read and written |
| [JPEG](04-Jpeg.md) | Baseline, progressive, arithmetic coded, 12-bit, lossless; CMYK as CMYK pixels or with a color conversion | Lossy (8 or 12 bits, baseline or progressive, gray or YCbCr) or lossless (2 to 16 bits, gray or RGB); Huffman or arithmetic coded; 8-bit baseline CMYK | No |
| [WebP](05-WebP.md) | Lossless, lossy, alpha | Lossless (default) or lossy, alpha | Read and written |
| [QOI](06-Qoi.md) | Every file | Lossless 8-bit RGB and RGBA | No |
| [BMP](07-Bmp.md) | Windows and OS/2 bitmaps, 1 to 32 bits, bit fields, run-length | 24 or 32 bits by default; indexed, run-length and 16 bits on request | No |
| [TGA](08-Tga.md) | True-color, grayscale and color-mapped, uncompressed or run-length | True-color and grayscale, uncompressed or run-length | No |
| [Netpbm](09-Netpbm.md) | PBM, PGM, PPM and PAM, 8 and 16 bits | Lossless; PBM on request | No |
| [TIFF and BigTIFF](10-Tiff.md) | Multi-page documents, most compressions and sample layouts, floating-point samples, CMYK as CMYK pixels or with a color conversion | Multi-page documents, lossless compressions or JPEG, bilevel, palette, 32-bit floating-point, CMYK | No (pages) |
| [ICO and CUR](11-IcoAndCur.md) | PNG and bitmap representations, cursor hotspots | PNG or 32-bit bitmap representations, hotspots | No (representations) |
| [ANI](12-Ani.md) | Animated cursors | Animated cursors | Read and written |
| [AVIF](13-Avif.md) | 8 to 12 bits, alpha, grids, HDR descriptions | Lossless (default) or lossy, 8 to 12 bits, alpha, grids | Read and written |
| [HEIC](14-Heic.md) | 8 to 12 bits, alpha, grids, photos of phones and cameras | Lossless (default) or lossy, 8 to 12 bits, alpha, grids | Read and written |
| [JPEG XL](15-JpegXl.md) | Lossless and lossy, up to 16 bits, recompressed JPEG files, progressive renderings | Lossless (default) or lossy, up to 16 bits; JPEG files recompressed without loss | Read and written |
| [JPEG 2000](16-Jpeg2000.md) | JP2 files and codestreams, up to 16 bits, HTJ2K | Lossless (default) or lossy, up to 16 bits | No |
| [OpenEXR](17-OpenExr.md) | Half, float and integer samples, most compressions | Half or float samples, scan lines or tiles with mipmaps, lossless by default or lossy (PXR24, B44, DWA) | No |
| [Radiance HDR](18-Radiance.md) | RGBE pictures, flat and run-length encoded, every orientation | RGBE pictures, run-length encoded (default) or flat | No |
| [PSD](19-Psd.md) | The composite image of PSD and PSB files (not the layers), 1 to 32 bits, CMYK and Lab with a color conversion | Flat gray, RGB or CMYK documents of 8, 16 or 32 bits, raw or PackBits, PSB; one layer for transparency | No |
| [DNG](20-Dng.md) | Raw photos rendered to linear light, their raw mosaic, previews | Linear raw data (the rendered image), 8 or 16-bit integers or 32-bit floats; uncompressed, lossless JPEG or Deflate | No |
| [DDS](21-Dds.md) | Textures: BC1 to BC7 and the common uncompressed formats, mipmaps, cube maps, arrays, volume textures | BC1 to BC7 and uncompressed formats, generated mipmaps, cube maps and arrays | No (surfaces) |
| [DPX](22-Dpx.md) | 1 to 16-bit and floating-point components, RGB, luma, Cb, Y, Cr, both byte orders | RGB, RGBA or luma, 8, 10, 12, 16 bits or 32-bit floats | No |
| [Cineon](23-Cineon.md) | 10-bit (and 1 to 16-bit) RGB and gray film scans | RGB or gray, 8, 10 or 16 bits, packing 5, the header of the source | No |
| [KTX 2.0](24-Ktx2.md) | GPU textures: levels, layers, cube faces and slices; uncompressed, BCn, ETC2, EAC, ASTC, ETC1S and UASTC; Zstandard and ZLIB | Uncompressed, BCn, ETC2, EAC and ASTC LDR, generated mip levels, cube maps and arrays; ZLIB | Read (`KTXanimData`), and every subresource as an entry |

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
| `.jph` | `Jpeg2000Encoder { BlockCoder = Jpeg2000BlockCoder.HighThroughput }` | Lossless JPH (HTJ2K) file |
| `.jhc` | `Jpeg2000Encoder { BlockCoder = Jpeg2000BlockCoder.HighThroughput, Container = Jpeg2000Container.Codestream }` | Lossless HTJ2K codestream |
| `.exr` | [OpenExrEncoder](cref:Meziantou.Framework.Imaging.Formats.OpenExrEncoder) | ZIP-compressed OpenEXR |
| `.hdr`, `.pic` | [RadianceEncoder](cref:Meziantou.Framework.Imaging.Formats.RadianceEncoder) | Run-length encoded Radiance HDR |
| `.dpx` | [DpxEncoder](cref:Meziantou.Framework.Imaging.Formats.DpxEncoder) | Big-endian DPX at the depth of the pixels |
| `.psd` | [PsdEncoder](cref:Meziantou.Framework.Imaging.Formats.PsdEncoder) | PackBits PSD file (PSB above 30,000 pixels per side) |
| `.psb` | `PsdEncoder { LargeDocument = true }` | PackBits PSB file |
| `.dng` | [DngEncoder](cref:Meziantou.Framework.Imaging.Formats.DngEncoder) | Linear raw DNG, lossless JPEG (Deflate for floating-point pixels) |
| `.cin` | [CineonEncoder](cref:Meziantou.Framework.Imaging.Formats.CineonEncoder) | 10-bit Cineon (16-bit for 16-bit pixels) |
| `.dds` | [DdsEncoder](cref:Meziantou.Framework.Imaging.Formats.DdsEncoder) | Uncompressed DDS at the depth of the pixels |
| `.ktx2` | [Ktx2Encoder](cref:Meziantou.Framework.Imaging.Formats.Ktx2Encoder) | Uncompressed KTX 2.0 at the depth of the pixels |
Any other extension throws an `ArgumentException`, including `.jpx` and `.jpf` (JPEG 2000): those files are read, not
written. Note that WebP, AVIF, HEIC, JPEG XL and JPEG 2000 are lossless by default, which makes large
files from photos: pass an encoder with lossy compression for them.

## Choosing a format

- **Photos**: JPEG is read everywhere. WebP, AVIF and JPEG XL have lossy modes too, usually smaller at the same visual
  quality; the AVIF and HEIC encoders search longer, and their `Effort` setting trades encoding time for size. HEIC is
  the format of the photos of phones: read it, and convert it to share it (see
  [Handling phone photos](../03-Guides/05-HandlePhonePhotos.md)).
- **Graphics, screenshots, logos and transparency**: PNG, or lossless WebP. An image with few colors gets smaller as
  an indexed PNG; [PngOptimization.Layout](cref:Meziantou.Framework.Imaging.Formats.PngOptimization.Layout) picks the narrowest exact layout from one pass over
  the pixels, and [PngOptimization.MinimumSize](cref:Meziantou.Framework.Imaging.Formats.PngOptimization.MinimumSize) searches the smallest lossless PNG.
- **Animations**: GIF is read everywhere but has 256 colors per frame and no partial transparency. APNG and lossless
  WebP keep every color and alpha value; lossy WebP and AVIF image sequences are smaller. See
  [Processing animated images](../03-Guides/04-ProcessAnimatedImages.md).
- **High dynamic range**: OpenEXR stores floating-point samples, and so does TIFF; Radiance HDR stores RGBE pixels (8
  bits of mantissa shared by the three colors), the format of most environment maps; AVIF, HEIC, JPEG XL and 16-bit PNG
  store HDR samples as integers with their color description. See [Pixel formats](../04-Concepts/05-PixelFormats.md).
- **Archival and lossless masters**: PNG, TIFF (also for multi-page documents), and JPEG XL or JPEG 2000 in lossless
  mode, which keep 16-bit samples. A JPEG file can be recompressed to JPEG XL without loss and rebuilt byte for byte
  (see [JPEG XL](15-JpegXl.md)).
- **Scanned documents**: a bilevel TIFF with CCITT Group 4 compression.
- **Icons and cursors**: ICO, CUR and ANI.
- **Photoshop documents**: PSD and PSB files are read (their composite image) and written as flat documents, which
  Photoshop opens; the layers of a document are not read.
- **Raw photos**: DNG files are rendered to linear light; an image saved as DNG is linear raw data that raw converters
  open, not the mosaic of a sensor.
- **Tooling, game assets and test fixtures**: QOI, TGA, BMP and Netpbm are simple formats, lossless by default. The
  block-compressed textures of game engines are DDS files: the library reads and writes them, BC1 to BC7 included.
- **Tooling, game assets and test fixtures**: QOI, TGA, BMP and Netpbm are simple formats, lossless by default. GPU
  textures (KTX 2.0) are read and written, block-compressed with BCn, ETC2, EAC or ASTC.

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
- [Radiance HDR](18-Radiance.md): RGBE pictures of lighting simulations and HDRI environment maps.
- [PSD](19-Psd.md): the composite image of Adobe Photoshop documents, and flat documents for Photoshop.
- [DNG](20-Dng.md): raw photos of cameras and phones, and linear raw files.
- [DDS](21-Dds.md): the textures of Direct3D and game engines, and their mipmaps, cube maps and arrays.
- [DPX](22-Dpx.md): film scans and video frames of post-production, with their time codes.
- [Cineon](23-Cineon.md): the Kodak film scans that preceded DPX.
- [KTX 2.0](24-Ktx2.md): GPU textures, their levels, layers and faces, block-compressed or supercompressed.
