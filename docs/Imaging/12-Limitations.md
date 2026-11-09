---
title: Limitations
description: What the library does not read, write or do, so you can plan around it.
---

The library never decodes or encodes a file approximately: a feature it does not support throws an
[UnsupportedImageFeatureException](cref:Meziantou.Framework.Imaging.UnsupportedImageFeatureException), and a conversion that would lose information needs an explicit setting. This page
lists the main gaps. Each [format page](10-Formats/01-Overview.md) gives the details of its format.

## General

- **No custom pixel types.** Images use the built-in pixel formats; the buffer pool has settings
  ([ImageBufferPool](cref:Meziantou.Framework.Imaging.ImageBufferPool)) but cannot be replaced by an allocator of yours.
- **No floating-point grayscale.** [RgbaFloat](cref:Meziantou.Framework.Imaging.RgbaFloat) and [RgbaHalf](cref:Meziantou.Framework.Imaging.RgbaHalf) are the floating-point formats. OpenEXR and TIFF read and
  write them, JPEG XL reads them; the other encoders need an explicit reduction to 16 bits.
- **Scaled and region decoding are limited to some formats.** JPEG and JPEG 2000 decode at a reduced size, and JPEG 2000,
  TIFF, AVIF and HEIC decode an area; the other formats are decoded whole: resize or crop the image afterward. See
  [Loading images](05-LoadingAndSaving/02-LoadingImages.md#decode-a-smaller-image).
- **Color profiles are never applied implicitly.** Colors change only when you convert them
  ([ConvertColorProfile](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.ConvertColorProfile)). See [Color management](09-Color/03-ColorManagement.md).
- **Color management covers ICC versions 2 and 4** grayscale, RGB and CMYK profiles, and device-link and abstract
  profiles in sequences of profiles; no iccMAX or named-color profiles. Gamut mapping is per-channel clipping or a
  chroma reduction, opt-in.

## Formats

| Format | Main limitations |
| --- | --- |
| JPEG | JPEG-LS mapping tables (palettes) are not read. Writing stores one frame, and CMYK only as 8-bit Huffman-coded baseline. |
| GIF | At most 256 colors per frame and one fully transparent color; plain-text extensions are rejected. |
| PNG / APNG | Sub-byte grayscale requires [PngOptimization.MinimumSize](cref:Meziantou.Framework.Imaging.Formats.PngOptimization.MinimumSize) or [PngOptimization.Layout](cref:Meziantou.Framework.Imaging.Formats.PngOptimization.Layout) (still images), and APNG delta rectangles [OptimizeFrames](cref:Meziantou.Framework.Imaging.Formats.PngEncoder.OptimizeFrames) or MinimumSize; palettes are selected by [MinimumSize](cref:Meziantou.Framework.Imaging.Formats.PngOptimization.MinimumSize), [Layout](cref:Meziantou.Framework.Imaging.Formats.PngOptimization.Layout) or [PngPixelLayout.Indexed](cref:Meziantou.Framework.Imaging.Formats.PngPixelLayout.Indexed). Sequential writers fix their layout and palette from the first image. |
| WebP | 8-bit RGB(A) only; lossy output is 4:2:0; animations need a seekable destination. |
| QOI | 8-bit RGB(A) only, one still image, no metadata. |
| BMP | No embedded JPEG/PNG payloads, no linked ICC profile; one still image is written. |
| TGA | No premultiplied alpha; true-color and grayscale output only, no metadata. |
| Netpbm | Only the first image of a concatenation is read; no metadata. |
| TIFF | Subsampled YCbCr data in separate planes, ITU L\*a\*b\*, lightness-only L\*a\*b\* and some extra-sample layouts are not read; L\*a\*b\* pages need a color conversion. Writing uses chunky samples (no separate planes), one page per [Save](cref:Meziantou.Framework.Imaging.Image.Save) (use [ImageCollection](cref:Meziantou.Framework.Imaging.ImageCollection) for documents) and a seekable destination. |
| ICO / CUR | At most 256 pixels per side; 32-bit DIB or PNG representations are written. |
| ANI | One representation per frame; always loops; needs a seekable destination. |
| AVIF | Gain maps, depth maps and thumbnails are only read by `AuxiliaryImageCollection`; gain maps are written next to still images only. Sequences need a seekable destination; the encoder is single-threaded except for grid tiles. |
| HEIC | Extended precision processing (an option of the 16-bit profiles), multilayer and 3D extensions are not read; bursts and live-photo videos are ignored, depth maps, thumbnails and gain maps are only read by `AuxiliaryImageCollection` (Apple's gain map parameters, in maker notes, are not read). Sequences need a seekable destination. |
| JPEG XL | Premultiplied alpha and integer samples of more than 24 bits are not read; lossy decoding is close to, not identical with, the reference decoder. Writing has no layers or blending. |
| JPEG 2000 | More than 16 bits and Part 2 extensions are not read; a region decode reads the whole compressed data of each tile it meets. Writing has no precincts and no tile-parts beyond one per tile; HTJ2K output has one quality layer and no HT refinement passes. |
| OpenEXR | HTJ2K and LJ2K compressions and chroma (RY, BY) channels are not read; deep data are flattened, never read as samples; a multi-part file, deep data, other channels than R, G, B, A and Y, and an overscan, need a part, the flattening, a layer or a window mode ([OpenExrDecodingOptions](cref:Meziantou.Framework.Imaging.OpenExrDecodingOptions)). Writing uses lossless scan-line compression, but alpha premultiplication and sample conversion can change source pixels. |

## Drawing and text

- Text lines are broken greedily.
- Hinting and subpixel (LCD) rendering are opt-in. Subpixel rendering applies to a layout filled at a point with a
  solid color on an 8- or 16-bit RGB image; other text is drawn with grayscale antialiasing.
- A ruby annotation is set over its base only, without overhang over the text around it.
- Installed fonts are used only when you ask for them ([Font.LoadInstalled](cref:Meziantou.Framework.Imaging.Typography.Font.LoadInstalled), [FontStack.WithInstalledFallback](cref:Meziantou.Framework.Imaging.Typography.FontStack.WithInstalledFallback)).
- No SVG glyphs, Type 1 fonts or fonts without outlines.
