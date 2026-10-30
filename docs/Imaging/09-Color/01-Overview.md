---
title: Color
description: Metadata, orientation, ICC profiles, HDR and CMYK images, and single color values.
---

An image is more than its pixels: it says how it was taken (EXIF), how it is described (XMP), which way is up
(orientation) and what its samples mean (an ICC profile or a CICP color description). The library keeps all of it when
it loads an image and writes it back when it saves one, and it never changes your pixels on its own:

- **Metadata is preserved.** Decoders keep EXIF, XMP, ICC profiles and text entries byte for byte, and encoders write
  them. When the output format cannot store something, saving throws unless you choose to drop it.
- **Orientation is reported, not applied.** A photo taken with a turned camera is loaded as it is stored, with its
  EXIF orientation. [AutoOrient](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.AutoOrient) turns the pixels when you ask for it.
- **Color profiles are labels.** A profile says what the samples mean; it is never applied implicitly. Colors are only
  converted when you call [ConvertColorProfile](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.ConvertColorProfile), use an [IccColorTransform](cref:Meziantou.Framework.Imaging.IccColorTransform), or load a CMYK image with a conversion.
  Pixels without a profile are sRGB.

## Pages of this section

- [Metadata and orientation](02-MetadataAndOrientation.md): what an image carries, how to apply the EXIF orientation,
  how to edit metadata, and what happens to it when you save to another format.
- [Color management](03-ColorManagement.md): convert an image to sRGB, turn an HDR image into a standard one, load CMYK
  JPEG and TIFF files, convert sample buffers between ICC profiles, and choose a rendering intent.
- [Color values](04-ColorValues.md): single colors in CIELAB, Oklch, HSL and the other color spaces, the notations of
  CSS, and how different two colors look.
