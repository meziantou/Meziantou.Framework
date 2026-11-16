---
title: Limitations
description: What the library does not read, write or do, so you can plan around it.
---

The library never decodes or encodes a file approximately: a feature it does not support throws an
[UnsupportedImageFeatureException](cref:Meziantou.Framework.Imaging.UnsupportedImageFeatureException), and a conversion that would lose information needs an explicit setting. This page
lists the main gaps. Each [format page](10-Formats/01-Overview.md) gives the details of its format.

## General

- **Custom pixel types go through a built-in format.** An image always stores one of the built-in pixel formats. A
  pixel type of yours ([ICustomPixel\<TSelf\>](cref:Meziantou.Framework.Imaging.ICustomPixel{TSelf})) converts from and to one of them, to import, copy and edit
  pixels; [Image\<TPixel\>](cref:Meziantou.Framework.Imaging.Image{TPixel}), the codecs, the readers and writers and the processing operations take
  the built-in types only. See [Pixel formats](04-Concepts/05-PixelFormats.md#custom-pixel-types).
- **Floating-point pixels are linear light, RGBA or gray.** [RgbaFloat](cref:Meziantou.Framework.Imaging.RgbaFloat), [RgbaHalf](cref:Meziantou.Framework.Imaging.RgbaHalf), [GrayFloat](cref:Meziantou.Framework.Imaging.GrayFloat), [GrayHalf](cref:Meziantou.Framework.Imaging.GrayHalf), [LaFloat](cref:Meziantou.Framework.Imaging.LaFloat) and [LaHalf](cref:Meziantou.Framework.Imaging.LaHalf) are the
  floating-point formats. OpenEXR, TIFF and DPX read and write them, JPEG XL, PSD, DNG, DDS and KTX 2.0 read them; the
  other encoders need an explicit reduction to 16 bits. Netpbm has no PFM support.
- **Scaled and region decoding are limited to some formats.** JPEG and JPEG 2000 decode at a reduced size, lossy JPEG XL
  at 1/8, interlaced PNG at 1/2, 1/4 or 1/8, TIFF from the reduced-resolution images it stores, and JPEG,
  JPEG 2000, TIFF, AVIF and HEIC decode an area; the other formats are decoded whole: resize or crop the image afterward.
  A progressive JPEG file keeps the coefficients of its whole frame until its end, whatever the size or the area. See
  [Loading images](05-LoadingAndSaving/02-LoadingImages.md#decode-a-smaller-image).
- **Color profiles are never applied implicitly.** Colors change only when you convert them
  ([ConvertColorProfile](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.ConvertColorProfile)). See [Color management](09-Color/03-ColorManagement.md).
- **Color management covers ICC versions 2, 4 and 5** grayscale, RGB and CMYK profiles, device-link and abstract
  profiles in sequences of profiles, and named color profiles. iccMAX profiles need a colorimetric connection space:
  no spectral connection, custom viewing conditions, color appearance, emission or reflectance elements, material
  (BRDF) or multiplex connection; their named colors are read at full tint only. Gamut mapping is per-channel clipping
  or a chroma reduction, opt-in. See [Supported profiles](09-Color/03-ColorManagement.md#supported-profiles).

## Formats

| Format | Main limitations |
| --- | --- |
| JPEG | JPEG-LS mapping tables (palettes) are not read. Writing stores one frame, and CMYK only as 8-bit Huffman-coded baseline. |
| GIF | At most 256 colors per frame and one fully transparent color; plain-text extensions are rejected. |
| PNG / APNG | Sub-byte grayscale requires [PngOptimization.MinimumSize](cref:Meziantou.Framework.Imaging.Formats.PngOptimization.MinimumSize) or [PngOptimization.Layout](cref:Meziantou.Framework.Imaging.Formats.PngOptimization.Layout) (still images), and APNG delta rectangles [OptimizeFrames](cref:Meziantou.Framework.Imaging.Formats.PngEncoder.OptimizeFrames) or MinimumSize; palettes are selected by [MinimumSize](cref:Meziantou.Framework.Imaging.Formats.PngOptimization.MinimumSize), [Layout](cref:Meziantou.Framework.Imaging.Formats.PngOptimization.Layout) or [PngPixelLayout.Indexed](cref:Meziantou.Framework.Imaging.Formats.PngPixelLayout.Indexed). Sequential writers fix their layout and palette from the first image. |
| WebP | 8-bit RGB(A) only and lossy output in 4:2:0, both properties of the format ([what the format allows](10-Formats/05-WebP.md#what-the-format-cannot-store)); an animation written to a non-seekable stream is kept in memory until completion. |
| QOI | 8-bit RGB(A) only, one still image, no metadata. |
| BMP | No embedded JPEG/PNG payloads, no linked ICC profile; one still image is written. |
| TGA | No premultiplied alpha; true-color and grayscale output only, no metadata. |
| Netpbm | [Image.Load](cref:Meziantou.Framework.Imaging.Image.Load) reads the first image of a concatenation ([ImageCollection](cref:Meziantou.Framework.Imaging.ImageCollection) reads them all); no metadata. |
| TIFF | Subsampled YCbCr data in separate planes, ITU L\*a\*b\*, lightness-only L\*a\*b\* and some extra-sample layouts are not read; L\*a\*b\* pages need a color conversion. Writing uses chunky samples (no separate planes), one page per [Save](cref:Meziantou.Framework.Imaging.Image.Save) (use [ImageCollection](cref:Meziantou.Framework.Imaging.ImageCollection) for documents) and a seekable destination. |
| ICO / CUR | At most 256 pixels per side; 32-bit DIB or PNG representations are written. |
| ANI | One representation per frame; always loops; needs a seekable destination. |
| AVIF | Gain maps, depth maps and thumbnails are only read by `AuxiliaryImageCollection`; gain maps are written next to still images only. Sequences need a seekable destination; the encoder is single-threaded except for grid tiles. |
| HEIC | Extended precision processing (an option of the 16-bit profiles), multilayer and 3D extensions are not read; bursts and live-photo videos are ignored, depth maps, thumbnails and gain maps are only read by `AuxiliaryImageCollection` (Apple's gain map parameters, in maker notes, are not read). Sequences need a seekable destination. |
| JPEG XL | Premultiplied alpha and integer samples of more than 24 bits are not read; lossy decoding is close to, not identical with, the reference decoder. Layers are written as a still image ([JpegXlLayeredImage](cref:Meziantou.Framework.Imaging.JpegXlLayeredImage)), not as the layers of the frames of an animation; their reading covers files whose frames are blended with the layers below them. |
| JPEG 2000 | More than 16 bits and Part 2 extensions are not read; a region decode reads the whole compressed data of each tile it meets. Writing has code-blocks of 64x64 coefficients (fewer in small precincts), no SOP or EPH markers, no regions of interest and no progression order changes; the layers of an HTJ2K output hold whole code-blocks (one HT set each, without placeholder passes). |
| OpenEXR | The LJ2K compression, HTJ2K of FLOAT or UINT channels and chroma (RY, BY) channels are not read; deep data are flattened, never read as samples; the lower levels of mipmaps and the preview image are only read on request; a multi-part file, deep data, other channels than R, G, B, A and Y, and an overscan, need a part, the flattening, a layer or a window mode ([OpenExrDecodingOptions](cref:Meziantou.Framework.Imaging.OpenExrDecodingOptions)). Writing is lossless by default (scan lines or tiles, with mipmap or ripmap levels generated by the encoder, never given), but alpha premultiplication and sample conversion can change source pixels; the lossy compressions (PXR24, B44, B44A, DWAA, DWAB) are used only when chosen. The encoder writes no UINT samples, no preview image and no attributes other than the required ones, the chromaticities and the DWA level. |
| PSD | Only the composite image is read: layers, masks, adjustment layers and effects are not rendered, and a file saved without a composite is rejected. ZIP-compressed image data, multichannel documents, 32-bit CMYK, 16 and 32-bit Lab and 32-bit duotone are not read; duotone is read as gray; the thumbnail and IPTC data are not read. Writing produces flat documents (one layer only for transparency), raw or PackBits: no layers of your own, no ZIP, no bitmap, indexed, duotone or Lab documents; the colors of the composite of partly transparent pixels are matted over white, as Photoshop stores them. |
| DNG | Writing produces linear raw data: no color filter array (the mosaic of a [DngRawImage](cref:Meziantou.Framework.Imaging.DngRawImage) cannot be written back), no transparency, no preview, no EXIF. |
| Cineon | Writing produces big-endian files with packing 5, of 8, 10 or 16 bits, without alpha. |
| DDS | Volume textures, partial cube maps and the formats outside the encoder's list (packed 16-bit, 10-bit, shared-exponent and typeless formats) are not written. Premultiplied alpha (DXT2, DXT4), video, depth and stencil, integer and most signed formats, palettes, and the typeless formats without a single reading are not read. |
| KTX 2.0 | Not written: 3D textures, ASTC HDR blocks, Basis Universal (ETC1S, UASTC), Zstandard supercompression, packed formats. Not read: PVRTC blocks, the YCbCr formats, 3D ASTC blocks, the depth, stencil, integer and uncompressed SNORM formats, vendor supercompression and premultiplied alpha are not read; illegal ASTC blocks are rejected instead of decoded to magenta. |

## Drawing and text

- Lines are broken greedily by default. [Optimal line breaking](08-Text/04-TextLayout.md#optimal-line-breaking) is opt-in: it does not
  shrink spaces, has no fitness classes, does not choose the breaks to fit [MaxLines](cref:Meziantou.Framework.Imaging.Typography.TextLayoutOptions.MaxLines) or [MaxHeight](cref:Meziantou.Framework.Imaging.Typography.TextLayoutOptions.MaxHeight), and breaks a
  paragraph greedily from where comparing its breaks would take too much work.
- Hinting and subpixel (LCD) rendering are opt-in. Subpixel rendering applies to a layout filled at a point with a
  solid color on an 8- or 16-bit RGB image; other text is drawn with grayscale antialiasing.
- A ruby annotation is set over its base only, without overhang over the text around it.
- Installed fonts are used only when you ask for them ([Font.LoadInstalled](cref:Meziantou.Framework.Imaging.Typography.Font.LoadInstalled), [FontStack.WithInstalledFallback](cref:Meziantou.Framework.Imaging.Typography.FontStack.WithInstalledFallback)).
- PostScript Type 1 fonts are read without their hints; multiple master, CID-keyed and stroked Type 1 fonts, Type 3
  fonts, and the Type 1 charstrings and the `seac` form of `endchar` in an OpenType CFF table are not read.
- Bitmap fonts are read only from OpenType files (`EBDT`, `bdat`, `CBDT`, `sbix`): the BDF, PCF and Windows `.fon`
  formats are not read.
