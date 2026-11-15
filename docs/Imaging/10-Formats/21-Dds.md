---
title: DDS
description: Read DirectDraw Surface (DDS) textures, block-compressed (BC1 to BC7) or uncompressed, with their mipmaps, cube maps, arrays and volume slices.
---

DDS files (`.dds`) store the textures of Direct3D and of most game engines: block-compressed (BC1 to BC7, the DXT
formats) or uncompressed pixels, with their mipmaps, the faces of a cube map, the elements of a texture array or the
depth slices of a volume texture. The library reads them; it does not write them.

[Image.Load](cref:Meziantou.Framework.Imaging.Image.Load) gives the full-size level of the first face or element (the first depth slice of a volume
texture). [ImageCollection.Load](cref:Meziantou.Framework.Imaging.ImageCollection.Load) reaches every surface.

## What is read

| | Reading |
| --- | --- |
| Block-compressed | BC1 (DXT1, with 1-bit alpha), BC2 (DXT3), BC3 (DXT5), BC4 and BC5 unsigned and signed (`ATI1`, `ATI2`, `BC4U`, `BC4S`, `BC5U`, `BC5S`), BC6H unsigned and signed, BC7 (the eight modes); their `_SRGB` and `_TYPELESS` forms |
| Uncompressed | R8G8B8A8 and B8G8R8A8 (UNORM and SRGB), B8G8R8X8, B5G6R5, B5G5R5A1, B4G4R4A4, R10G10B10A2, R16G16B16A16 (UNORM and FLOAT), R32G32B32A32_FLOAT and the formats of fewer channels, R8, R8G8, R16, R16G16, A8, R11G11B10_FLOAT, R9G9B9E5_SHAREDEXP |
| Legacy headers | Every bit-mask layout of 8 to 32 bits (RGB, luminance, alpha), the DXT FourCCs and the numeric codes of the Direct3D 9 floating-point formats |
| Structure | Mipmaps, cube maps (and the partial cube maps of Direct3D 9), texture arrays, arrays of cube maps, volume textures, 1D textures |

The image of a format is the lossless form of what it stores:

- 8-bit formats and BC1, BC2, BC3 and BC7 decode to 8-bit pixels; formats with a channel of more than 8 bits (R16,
  R10G10B10A2, R16G16B16A16_UNORM) to 16-bit pixels. Channels of 4, 5 or 6 bits are expanded to the nearest 8-bit value.
- One channel (R8, R16, BC4, a luminance layout) is gray; two channels (R8G8, BC5) are red and green with a blue of 0;
  A8 is an alpha mask. A format without alpha is opaque.
- Floating-point formats, BC6H, R11G11B10_FLOAT and R9G9B9E5_SHAREDEXP decode to [RgbaHalf](cref:Meziantou.Framework.Imaging.RgbaHalf) or
  [RgbaFloat](cref:Meziantou.Framework.Imaging.RgbaFloat) in linear light, values outside [0, 1] included; the formats of one channel
  (R16_FLOAT, R32_FLOAT) decode to [GrayHalf](cref:Meziantou.Framework.Imaging.GrayHalf) or [GrayFloat](cref:Meziantou.Framework.Imaging.GrayFloat). The signed BC4 and BC5 formats decode to
  [GrayFloat](cref:Meziantou.Framework.Imaging.GrayFloat) and [RgbaFloat](cref:Meziantou.Framework.Imaging.RgbaFloat) values in [-1, 1] (normal maps).
- The `_SRGB` formats are labeled with the CICP description of sRGB; the other integer formats are untagged. The colors
  are never converted.

BC7 and BC6H decode to the exact values of their specification; BC1 to BC5 are rounded once from the real numbers the
specification gives to the precision of the format.

## Read every surface of a texture

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_DdsTexture;format=md-fenced-code-block;dedent=true -->
```csharp
public static IReadOnlyList<string> ExportCubeFaces(string ddsPath, string outputDirectory)
{
    // Image.Load gives the top level of the first face; a collection reaches every surface, decoded on demand
    using var texture = ImageCollection.Load(ddsPath);
    var written = new List<string>();
    foreach (var entry in texture.Entries)
    {
        // Each entry says where it is in the texture: keep the full-size level of every face
        if (entry.TextureSurface is not { MipLevel: 0, Face: { } face } surface)
            continue;

        var path = Path.Combine(outputDirectory, $"{surface.ArrayIndex}-{face}.png");
        using var image = entry.Decode();
        image.Save(path);
        written.Add(path);
    }

    return written;
}
```
<!-- endref -->

A DDS collection has the kind [ImageCollectionKind.TextureSurfaces](cref:Meziantou.Framework.Imaging.ImageCollectionKind.TextureSurfaces). Its entries are in file order: for each element of
the array, for each face of a cube map (+X, −X, +Y, −Y, +Z, −Z: the faces a partial cube map stores), for each mipmap
level from the largest, the surface of the level, and for a volume texture each depth slice of the level.
[TextureSurface](cref:Meziantou.Framework.Imaging.ImageCollectionEntry.TextureSurface) tells where an entry is:

| Property | Meaning |
| --- | --- |
| [ArrayIndex](cref:Meziantou.Framework.Imaging.TextureSurface.ArrayIndex) | The element of the array (of the cube map in an array of cube maps), 0 otherwise |
| [Face](cref:Meziantou.Framework.Imaging.TextureSurface.Face) | The [CubeMapFace](cref:Meziantou.Framework.Imaging.CubeMapFace) of a cube map, `null` otherwise |
| [MipLevel](cref:Meziantou.Framework.Imaging.TextureSurface.MipLevel) | The mipmap level: 0 is the full size, each level halves the dimensions (at least 1) |
| [DepthSlice](cref:Meziantou.Framework.Imaging.TextureSurface.DepthSlice) | The depth slice of a level of a volume texture, 0 otherwise |

The collection reads the headers only; an entry decodes its surface when you call `Decode`. The number of surfaces
counts against [MaxFrames](cref:Meziantou.Framework.Imaging.ImageResourceLimits.MaxFrames), and
[ImageInfo.CollectionEntryCount](cref:Meziantou.Framework.Imaging.ImageInfo.CollectionEntryCount) gives it without loading the collection. A texture collection is
read, never created or saved: decode its entries to save them in another format.

## Pitfalls

- DDS is read only: saving to a `.dds` path throws an `ArgumentException`.
- Premultiplied alpha (`DXT2`, `DXT4`, the alpha mode `DDS_ALPHA_MODE_PREMULTIPLIED`) is rejected with an
  [UnsupportedImageFeatureException](cref:Meziantou.Framework.Imaging.UnsupportedImageFeatureException): the library never divides the colors by their alpha. The alpha modes
  straight, opaque and custom keep the stored alpha.
- Video formats (YUY2, NV12...), depth and stencil formats, integer (`_UINT`, `_SINT`) and most signed formats, and
  typeless formats that have no single reading (`BC6H_TYPELESS`, `R32G32B32A32_TYPELESS`) are rejected.
- The masks of a legacy header are read as they are written. DirectXTex reads the 10:10:10:2 masks with red and blue
  swapped, because D3DX wrote them backwards: such files decode with these two channels exchanged, as their header says.
- Writers fill the `dwPitchOrLinearSize` field unreliably. A value that contradicts the size of the surface is rejected
  by default; [lenient decoding](../05-LoadingAndSaving/08-ErrorHandling.md) ignores it, as DirectXTex does.
- [Image.Load](cref:Meziantou.Framework.Imaging.Image.Load) reads the first surface only: a file whose later surfaces are cut loads, and its collection is rejected.
- Floating-point textures cannot be loaded as integer pixels without a conversion policy: see
  [Pixel formats](../04-Concepts/05-PixelFormats.md).
