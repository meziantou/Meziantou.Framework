---
title: Limitations
description: What the library does not read, write or do, so you can plan around it.
---

The library never decodes or encodes a file approximately: a feature it does not support throws an
[UnsupportedImageFeatureException](cref:Meziantou.Framework.Imaging.UnsupportedImageFeatureException), and a conversion that would lose information needs an explicit setting. This page
lists the main gaps. Each [format page](10-Formats/01-Overview.md) gives the details of its format.

## General

- **No extension points.** There is no public allocator, custom pixel type, codec plug-in or custom image processor.
- **No CMYK pixel format.** CMYK JPEG and TIFF files are converted to RGB when they are loaded, with an explicit
  `CmykConversion` option; CMYK cannot be written. See [Color management](09-Color/03-ColorManagement.md).
- **No floating-point grayscale.** [RgbaFloat](cref:Meziantou.Framework.Imaging.RgbaFloat) and [RgbaHalf](cref:Meziantou.Framework.Imaging.RgbaHalf) are the floating-point formats. OpenEXR and TIFF read and
  write them; the other encoders need an explicit reduction to 16 bits.
- **No scaled or region decoding.** An image is decoded at its full size; resize it afterward.
- **Color profiles are never applied implicitly.** Colors change only when you convert them
  ([ConvertColorProfile](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.ConvertColorProfile)). See [Color management](09-Color/03-ColorManagement.md).
- **Color management covers ICC versions 2 and 4** grayscale, RGB and CMYK profiles; no iccMAX, device-link, abstract
  or named-color profiles.

## Formats

| Format | Main limitations |
| --- | --- |
| JPEG | Hierarchical JPEG, JPEG-LS and lossless JPEG with arithmetic coding are not read. Writing is 8-bit baseline or progressive only (no 12-bit, lossless, arithmetic, CMYK or restart markers), one frame. |
| GIF | At most 256 colors per frame and one fully transparent color; plain-text extensions are rejected. |
| PNG / APNG | Sub-byte grayscale and APNG delta rectangles require [PngOptimization.MinimumSize](cref:Meziantou.Framework.Imaging.Formats.PngOptimization.MinimumSize); palettes are selected by [MinimumSize](cref:Meziantou.Framework.Imaging.Formats.PngOptimization.MinimumSize) or [PngPixelLayout.Indexed](cref:Meziantou.Framework.Imaging.Formats.PngPixelLayout.Indexed). Sequential writers fix their layout and palette from the first image. Unknown ancillary chunks are not kept. |
| WebP | 8-bit RGB(A) only; lossy output is 4:2:0; animations need a seekable destination. |
| QOI | 8-bit RGB(A) only, one still image, no metadata. |
| BMP | No embedded JPEG/PNG payloads, no embedded ICC profile; one still image is written. |
| TGA | No premultiplied alpha; true-color and grayscale output only, no metadata. |
| Netpbm | Only the first image of a concatenation is read; no metadata. |
| TIFF | Old-style JPEG compression, planar storage, L\*a\*b\* and some extra-sample layouts are not read. Writing uses strips (no tiles), one page per [Save](cref:Meziantou.Framework.Imaging.Image.Save) (use [ImageCollection](cref:Meziantou.Framework.Imaging.ImageCollection) for documents) and a seekable destination. |
| ICO / CUR | At most 256 pixels per side; 32-bit DIB or PNG representations are written. |
| ANI | One representation per frame; always loops; needs a seekable destination. |
| AVIF | Gain maps, depth maps and thumbnails are not read. Sequences need a seekable destination; the encoder is single-threaded except for grid tiles. |
| HEIC | Bit depths above 12, multilayer and 3D extensions are not read; bursts, depth maps and live-photo videos are ignored. Sequences need a seekable destination. |
| JPEG XL | Floating-point samples, more than 16 bits, premultiplied alpha and CMYK are not read; lossy decoding is close to, not identical with, the reference decoder. Writing has no layers or blending. |
| JPEG 2000 | Signed samples, more than 16 bits and Part 2 extensions are not read; no lower-resolution decoding. Writing has no precincts, no HTJ2K and no tile-parts beyond one per tile. |
| OpenEXR | Multi-part files, deep data, DWA/HTJ2K/ZSTD compressions and channels other than R, G, B, A and Y are not read. Writing uses lossless scan-line compression, but alpha premultiplication and sample conversion can change source pixels. |

## Processing

- Convolution matrices have odd dimensions and no divisor (divide the weights).
- Edge detection has no non-maximum suppression or thresholds (no Canny detector).
- Histograms, equalization and auto-levels work on integer pixel formats only.

## Drawing and text

- Drawing is sequential: it does not use several threads.
- No mesh gradients.
- Text lines are broken greedily; no word lists are provided for the scripts that need a dictionary to find word
  boundaries.
- No AAT (`morx`, `kerx`) shaping, no subpixel (LCD) rendering, no ruby in vertical text.
- Hinting is opt-in and horizontal only.
- Installed fonts are used only when you ask for them ([Font.LoadInstalled](cref:Meziantou.Framework.Imaging.Typography.Font.LoadInstalled), [FontStack.WithInstalledFallback](cref:Meziantou.Framework.Imaging.Typography.FontStack.WithInstalledFallback)).
- No SVG glyphs, Type 1 fonts or fonts without outlines.
