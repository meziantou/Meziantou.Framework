---
title: OpenEXR
description: Read and write OpenEXR files of half or float samples, scan lines or tiles with mipmaps, lossless or lossy, the linear-light HDR format of rendering and visual effects.
---

OpenEXR stores scene-linear, high dynamic range images in 16-bit (half) or 32-bit floating-point samples, as rendering
and visual-effects tools produce them. Files load as [RgbaHalf](cref:Meziantou.Framework.Imaging.RgbaHalf) or [RgbaFloat](cref:Meziantou.Framework.Imaging.RgbaFloat) pixels (luminance files as their gray
formats), values above 1 included, and
[OpenExrEncoder](cref:Meziantou.Framework.Imaging.Formats.OpenExrEncoder) compresses the stored samples losslessly by default, as scan lines or as tiles with mipmap or ripmap levels;
the lossy compressions of OpenEXR (PXR24, B44, B44A, DWAA, DWAB) are used only when they are chosen. Conversion between
the library's straight colors and OpenEXR's premultiplied colors can round them; this is not a general bit-exact
source-pixel round-trip guarantee.

## What is read and written

| | Reading | Writing |
| --- | --- | --- |
| Files | Scan-line and tiled files (the full-resolution level, or another level of mipmaps and ripmaps); one part of a multi-part file; deep data, flattened | Single-part scan-line or tiled files: any tile size, one level, or mipmap and ripmap levels generated from the image (levels rounded down or up); increasing or decreasing line order (random order for tiles) |
| Samples | HALF, FLOAT and UINT | HALF or FLOAT |
| Channels | `R`, `G`, `B`, `A`, or `Y` (luminance) with or without `A`; or one layer of a file with other channels | `R`, `G`, `B` (and `A` for pixel formats with alpha), or `Y` for gray pixel formats |
| Windows | The display window; an overscan cropped to it, or the data window | Both windows are the image |
| Compression | NONE, RLE, ZIPS, ZIP, PIZ, PXR24, B44, B44A, DWAA, DWAB, ZSTD; HTJ2K256 and HTJ2K32 of HALF channels | NONE, RLE, ZIPS, ZIP (default) or PIZ, lossless; PXR24, B44, B44A, DWAA or DWAB, lossy, when chosen |
| Metadata | Chromaticities, the windows | Chromaticities; `dwaCompressionLevel` for DWAA and DWAB |

A file of half samples without an alpha channel loads as [RgbaHalf](cref:Meziantou.Framework.Imaging.RgbaHalf), any other as [RgbaFloat](cref:Meziantou.Framework.Imaging.RgbaFloat), in linear light.
A luminance file (a `Y` channel) loads in the gray format of the same precision: [GrayHalf](cref:Meziantou.Framework.Imaging.GrayHalf), [GrayFloat](cref:Meziantou.Framework.Imaging.GrayFloat), or
[LaFloat](cref:Meziantou.Framework.Imaging.LaFloat) with an alpha channel. A luminance file whose data window does not cover the image loads
with alpha, [LaHalf](cref:Meziantou.Framework.Imaging.LaHalf) or [LaFloat](cref:Meziantou.Framework.Imaging.LaFloat), since the pixels the data window leaves are transparent black.
The extra precision is used when dividing colors by alpha. Infinities and samples that are not numbers are kept,
subject to the alpha conversion. The lossy compressions (PXR24, B44, DWAA, DWAB) decode to the same samples as OpenEXR, bit
for bit: what they lose is lost when the file is written. HTJ2K256 and HTJ2K32 store each chunk as a high-throughput JPEG
2000 codestream, which the JPEG 2000 decoder of the library reads, losslessly.

## Load and save an OpenEXR file

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_OpenExr;format=md-fenced-code-block;dedent=true -->
```csharp
public static Image<Rgba32> ConvertOpenExr(string exrPath, string pizPath)
{
    // Half-only files without alpha load as RgbaHalf; other OpenEXR files load as RgbaFloat, in linear light
    using var hdr = Image.Load(exrPath);

    // PIZ compresses the stored samples losslessly, values above 1 included; alpha conversion can round colors
    hdr.Save(pizPath, new OpenExrEncoder { Compression = OpenExrCompression.Piz });

    // Integer pixels cannot hold the highlights: a typed load compresses them with a tone mapping
    return Image.Load<Rgba32>(exrPath, new ImageDecodeOptions
    {
        Conversion = new PixelConversionOptions { ToneMapping = new HdrToneMappingOptions() },
    });
}
```
<!-- endref -->

Integer pixels cannot hold values above 1: loading an OpenEXR file as [Rgba32](cref:Meziantou.Framework.Imaging.Rgba32) or [Rgba64](cref:Meziantou.Framework.Imaging.Rgba64) rejects them, unless the
conversion clips them or compresses the highlights with a tone mapping, as in the example. The same applies to saving
floating-point images to other formats, with [ImageEncoder.FloatingPointConversion](cref:Meziantou.Framework.Imaging.Formats.ImageEncoder.FloatingPointConversion). See
[Pixel formats](../04-Concepts/05-PixelFormats.md).

## Write tiles, mipmaps and lossy files

Renderers read textures by tiles, at the resolution a surface needs: a tiled file with mipmap levels lets them load only
what they use. Review copies and plates whose exact samples do not matter can be several times smaller with a lossy
compression:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_OpenExrTiledLossy;format=md-fenced-code-block;dedent=true -->
```csharp
public static void SaveOpenExrTextureAndPreview(Image hdr, string texturePath, string reviewPath)
{
    // Tiles of 64x64 pixels, with the mipmap levels a renderer reads: the encoder generates them by area average
    hdr.Save(texturePath, new OpenExrEncoder
    {
        Compression = OpenExrCompression.Zip,
        Tiling = new OpenExrTiling { TileWidth = 64, TileHeight = 64, LevelMode = OpenExrLevelMode.MipmapLevels },
    });

    // A lossy compression is only used when it is chosen: DWAB codes the colors as DCT blocks (alpha stays exact);
    // a larger level gives a smaller file and larger errors
    hdr.Save(reviewPath, new OpenExrEncoder { Compression = OpenExrCompression.Dwab, DwaCompressionLevel = 45 });
}
```
<!-- endref -->

[Tiling](cref:Meziantou.Framework.Imaging.Formats.OpenExrEncoder.Tiling) writes tiles instead of scan lines. Each tile is a chunk compressed on its own; the tiles at the
right and bottom edges are clipped to the image. With [MipmapLevels](cref:Meziantou.Framework.Imaging.Formats.OpenExrLevelMode.MipmapLevels), the file also stores the
image halved again and again down to one pixel; with [RipmapLevels](cref:Meziantou.Framework.Imaging.Formats.OpenExrLevelMode.RipmapLevels), every combination of
halved widths and halved heights. The encoder generates the levels:
each one is the area average of the samples the file stores for the previous one (colors premultiplied by alpha, so that
transparent pixels do not darken the edges), rounded to the sample type. [LevelRounding](cref:Meziantou.Framework.Imaging.Formats.OpenExrTiling.LevelRounding) says how odd sizes are halved: a level of 5 pixels is followed by 2 with
[Down](cref:Meziantou.Framework.Imaging.Formats.OpenExrLevelRounding.Down), by 3 with [Up](cref:Meziantou.Framework.Imaging.Formats.OpenExrLevelRounding.Up). The library reads the full-resolution level back; renderers and
OpenImageIO read the others.

| Compression | Lines per chunk | What is lost |
| --- | --- | --- |
| [Pxr24](cref:Meziantou.Framework.Imaging.Formats.OpenExrCompression.Pxr24) | 16 | The 8 low bits of the mantissa of float samples (a relative error below 2<sup>-16</sup>); half samples are exact |
| [B44](cref:Meziantou.Framework.Imaging.Formats.OpenExrCompression.B44) | 32 | Half samples, alpha included, in blocks of 4×4 stored in 14 bytes: small errors for smooth blocks, larger ones for blocks of very different values (or whose values cross zero); infinities and values that are not numbers become 0. Float samples are stored as they are |
| [B44A](cref:Meziantou.Framework.Imaging.Formats.OpenExrCompression.B44A) | 32 | As B44, and a block of one value takes 3 bytes |
| [Dwaa](cref:Meziantou.Framework.Imaging.Formats.OpenExrCompression.Dwaa) | 32 | The colors, coded as 8×8 DCT blocks of half values after a perceptual curve, at the level of [DwaCompressionLevel](cref:Meziantou.Framework.Imaging.Formats.OpenExrEncoder.DwaCompressionLevel) (45 by default, as OpenEXR); float colors come back as half values. Alpha is stored without loss |
| [Dwab](cref:Meziantou.Framework.Imaging.Formats.OpenExrCompression.Dwab) | 256 | As DWAA, in larger chunks: smaller files |

OpenEXR decodes these files to the samples the library decodes, bit for bit. A chunk that a compression does not shrink
is stored uncompressed.

## Read a layer of a renderer output

Renderers write depth (`Z`), other outputs in layers (`diffuse.R`, `diffuse.G`...) and often an overscan: a data window
larger than the display window. The default options reject such a file, since those channels and pixels would be lost.
[ImageConfiguration.OpenExrDecoding](cref:Meziantou.Framework.Imaging.ImageConfiguration.OpenExrDecoding) selects what is read:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_OpenExrLayers;format=md-fenced-code-block;dedent=true -->
```csharp
public static (Image Image, Point Origin) LoadRenderLayer(string exrPath, string layer)
{
    // Renderers store RGBA next to depth (Z) and other layers (diffuse.R, diffuse.G, ...), often with an overscan
    // around the display window: the default options reject such a file, since those channels and pixels would be lost
    var configuration = new ImageConfiguration
    {
        OpenExrDecoding = new OpenExrDecodingOptions
        {
            // "" is the default layer (R, G, B, A or Y); "diffuse" selects diffuse.R, diffuse.G, diffuse.B, diffuse.A
            Layer = layer,

            // DataWindow keeps every stored pixel instead
            WindowMode = OpenExrWindowMode.CropToDisplayWindow,
        },
    };

    // Identify, readers and collections take the same configuration, and report the same image
    var image = Image.Load(exrPath, new ImageDecodeOptions { Configuration = configuration });

    // The windows of the file, in its pixel coordinates: the cropped image starts at the display window
    var windows = image.Metadata.OpenExrWindows!;
    return (image, windows.DisplayWindowMin);
}
```
<!-- endref -->

| Option | What it does | Default |
| --- | --- | --- |
| [Layer](cref:Meziantou.Framework.Imaging.OpenExrDecodingOptions.Layer) | The layer to decode: `""` for the default layer, `"diffuse"` for `diffuse.R`, `diffuse.G`, `diffuse.B` and `diffuse.A`; the other channels are ignored | `null`: every channel must be `R`, `G`, `B`, `A` or `Y` |
| [Part](cref:Meziantou.Framework.Imaging.OpenExrDecodingOptions.Part) | The name of the part to decode in a multi-part file; the other parts are ignored | `null`: a multi-part file is rejected |
| [DeepData](cref:Meziantou.Framework.Imaging.OpenExrDecodingOptions.DeepData) | [Flatten](cref:Meziantou.Framework.Imaging.OpenExrDeepDataMode.Flatten) composites the samples of each pixel of deep data | [Reject](cref:Meziantou.Framework.Imaging.OpenExrDeepDataMode.Reject) |
| [WindowMode](cref:Meziantou.Framework.Imaging.OpenExrDecodingOptions.WindowMode) | [DisplayWindow](cref:Meziantou.Framework.Imaging.OpenExrWindowMode.DisplayWindow), [CropToDisplayWindow](cref:Meziantou.Framework.Imaging.OpenExrWindowMode.CropToDisplayWindow) (the pixels outside the display window are discarded) or [DataWindow](cref:Meziantou.Framework.Imaging.OpenExrWindowMode.DataWindow) (every stored pixel is the image) | [DisplayWindow](cref:Meziantou.Framework.Imaging.OpenExrWindowMode.DisplayWindow): an overscan is rejected |

## Read a part or deep data

A multi-part file stores several images, such as the two views of a stereo pair or the passes of a renderer. Deep data
store a list of samples per pixel, each at its own depth, so that renders can be merged by depth. The default options
reject both, since parts and depths would be lost:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_OpenExrPartsAndDeepData;format=md-fenced-code-block;dedent=true -->
```csharp
public static (Image RightEye, Image Flattened) LoadStereoAndDeep(string stereoPath, string deepPath)
{
    // A multi-part file stores several images (the views of a stereo pair, the passes of a renderer): the default
    // options reject it, since the other parts would be lost. Part selects one by its name
    var stereo = new ImageConfiguration { OpenExrDecoding = new OpenExrDecodingOptions { Part = "right" } };
    var rightEye = Image.Load(stereoPath, new ImageDecodeOptions { Configuration = stereo });

    // Deep data store a list of samples per pixel, each at its depth (Z): Flatten composites them front to back, as
    // OpenEXR does, into one RGBA color per pixel; the depths are lost
    var deep = new ImageConfiguration { OpenExrDecoding = new OpenExrDecodingOptions { DeepData = OpenExrDeepDataMode.Flatten } };
    var flattened = Image.Load(deepPath, new ImageDecodeOptions { Configuration = deep });
    return (rightEye, flattened);
}
```
<!-- endref -->

[Part](cref:Meziantou.Framework.Imaging.OpenExrDecodingOptions.Part) names the part to decode; the message of the
[UnsupportedImageFeatureException](cref:Meziantou.Framework.Imaging.UnsupportedImageFeatureException) of a multi-part file lists the names of its parts. The other options apply to that part.
[DeepData](cref:Meziantou.Framework.Imaging.OpenExrDecodingOptions.DeepData) set to [Flatten](cref:Meziantou.Framework.Imaging.OpenExrDeepDataMode.Flatten) composites the samples of each pixel front to back with the
*over* operation, as OpenEXR flattens deep data: the samples are taken in the order the file stores them, until the
accumulated alpha reaches 1. Deep data need a `Z` and an `A` channel, and load as [RgbaFloat](cref:Meziantou.Framework.Imaging.RgbaFloat) pixels.

The options apply to identification, eager loads, readers and collections alike.
[ImageMetadata.OpenExrWindows](cref:Meziantou.Framework.Imaging.Metadata.ImageMetadata.OpenExrWindows) gives the two windows of the file; it is informational, and the encoder
writes both windows from (0, 0). The decoder never falls back to another layer: a layer without color channel, a layer
with RGB and luminance channels, a `Y` channel next to an `X` channel (a vector such as `N.X`, `N.Y`, `N.Z`, or CIE XYZ
colors) and the chroma channels `RY` and `BY` are rejected with an [UnsupportedImageFeatureException](cref:Meziantou.Framework.Imaging.UnsupportedImageFeatureException).

## Options

| Option | What it does | Default |
| --- | --- | --- |
| [Compression](cref:Meziantou.Framework.Imaging.Formats.OpenExrEncoder.Compression) | Lossless: [None](cref:Meziantou.Framework.Imaging.Formats.OpenExrCompression.None), [Rle](cref:Meziantou.Framework.Imaging.Formats.OpenExrCompression.Rle), [Zips](cref:Meziantou.Framework.Imaging.Formats.OpenExrCompression.Zips), [Zip](cref:Meziantou.Framework.Imaging.Formats.OpenExrCompression.Zip) or [Piz](cref:Meziantou.Framework.Imaging.Formats.OpenExrCompression.Piz) (PIZ is usually the smallest for photos and noisy images); lossy: [Pxr24](cref:Meziantou.Framework.Imaging.Formats.OpenExrCompression.Pxr24), [B44](cref:Meziantou.Framework.Imaging.Formats.OpenExrCompression.B44), [B44A](cref:Meziantou.Framework.Imaging.Formats.OpenExrCompression.B44A), [Dwaa](cref:Meziantou.Framework.Imaging.Formats.OpenExrCompression.Dwaa) or [Dwab](cref:Meziantou.Framework.Imaging.Formats.OpenExrCompression.Dwab) | [Zip](cref:Meziantou.Framework.Imaging.Formats.OpenExrCompression.Zip) |
| [DwaCompressionLevel](cref:Meziantou.Framework.Imaging.Formats.OpenExrEncoder.DwaCompressionLevel) | The level of DWAA and DWAB: larger is smaller and less exact; 0 keeps every coefficient a half value stores | 45 |
| [Tiling](cref:Meziantou.Framework.Imaging.Formats.OpenExrEncoder.Tiling) | Tiles of [TileWidth](cref:Meziantou.Framework.Imaging.Formats.OpenExrTiling.TileWidth)×[TileHeight](cref:Meziantou.Framework.Imaging.Formats.OpenExrTiling.TileHeight) pixels (64×64 by default) and the [LevelMode](cref:Meziantou.Framework.Imaging.Formats.OpenExrTiling.LevelMode): [OneLevel](cref:Meziantou.Framework.Imaging.Formats.OpenExrLevelMode.OneLevel), [MipmapLevels](cref:Meziantou.Framework.Imaging.Formats.OpenExrLevelMode.MipmapLevels) or [RipmapLevels](cref:Meziantou.Framework.Imaging.Formats.OpenExrLevelMode.RipmapLevels) | `null`: scan lines |
| [LineOrder](cref:Meziantou.Framework.Imaging.Formats.OpenExrEncoder.LineOrder) | The order of the chunks in the file: [IncreasingY](cref:Meziantou.Framework.Imaging.Formats.OpenExrLineOrder.IncreasingY), [DecreasingY](cref:Meziantou.Framework.Imaging.Formats.OpenExrLineOrder.DecreasingY), or [RandomY](cref:Meziantou.Framework.Imaging.Formats.OpenExrLineOrder.RandomY) for tiles only | [IncreasingY](cref:Meziantou.Framework.Imaging.Formats.OpenExrLineOrder.IncreasingY) |
| [PixelType](cref:Meziantou.Framework.Imaging.Formats.OpenExrEncoder.PixelType) | [Half](cref:Meziantou.Framework.Imaging.Formats.OpenExrPixelType.Half) or [Float](cref:Meziantou.Framework.Imaging.Formats.OpenExrPixelType.Float); `null` chooses half for [RgbaHalf](cref:Meziantou.Framework.Imaging.RgbaHalf) and 8-bit pixels, float otherwise | `null` |
| [AllowBitDepthReduction](cref:Meziantou.Framework.Imaging.Formats.OpenExrEncoder.AllowBitDepthReduction) | Lets [RgbaFloat](cref:Meziantou.Framework.Imaging.RgbaFloat) and 16-bit pixels be rounded to half samples when [PixelType](cref:Meziantou.Framework.Imaging.Formats.OpenExrEncoder.PixelType) is [Half](cref:Meziantou.Framework.Imaging.Formats.OpenExrPixelType.Half) | `false` |
| [MetadataHandling](cref:Meziantou.Framework.Imaging.Formats.MetadataHandling) | What to do with metadata OpenEXR cannot store | [Strict](cref:Meziantou.Framework.Imaging.Formats.MetadataHandling.Strict) |

By default, [RgbaHalf](cref:Meziantou.Framework.Imaging.RgbaHalf) and 8-bit pixels are written as half samples, and [RgbaFloat](cref:Meziantou.Framework.Imaging.RgbaFloat) and 16-bit pixels as float samples.
The gray formats ([Gray8](cref:Meziantou.Framework.Imaging.Gray8), [Gray16](cref:Meziantou.Framework.Imaging.Gray16), [GrayHalf](cref:Meziantou.Framework.Imaging.GrayHalf), [GrayFloat](cref:Meziantou.Framework.Imaging.GrayFloat)) are written as a luminance channel `Y`,
and [LaHalf](cref:Meziantou.Framework.Imaging.LaHalf) and [LaFloat](cref:Meziantou.Framework.Imaging.LaFloat) as `Y` and `A`; the half-precision gray formats are written as half samples.
Integer pixels are written as their value divided by the largest sample (`v / max`).

Nonzero alpha multiplies the colors on encoding and divides them on decoding. Rounding, underflow or overflow can
change the colors even with the default sample type. A zero alpha keeps its colors without multiplication or division.
Lossless compression preserves the resulting stored samples, independently of integer normalization, half-sample
rounding and alpha conversion.

## Pitfalls

- OpenEXR samples are linear light. An image labeled sRGB, which is the label of integer pixel formats, or with an ICC
  profile follows [MetadataHandling](cref:Meziantou.Framework.Imaging.Formats.MetadataHandling), which rejects it by default: saving an 8-bit PNG as OpenEXR throws. A CICP
  description with linear transfer characteristics and BT.709, BT.2020, DCI-P3 or Display P3 primaries is written as
  chromaticities.
- Compressed output needs a seekable destination (the offset table precedes the chunks); [None](cref:Meziantou.Framework.Imaging.Formats.OpenExrCompression.None) does not, tiled or not.
- A lossy compression changes the stored samples: B44 also changes alpha (and so the colors divided by it), and DWA
  stores float colors as half values. Keep a lossless master.
- [RandomY](cref:Meziantou.Framework.Imaging.Formats.OpenExrLineOrder.RandomY) is only valid for tiles: scan-line output with it throws an [ArgumentException](https://learn.microsoft.com/dotnet/api/system.argumentexception).
- The levels of a mipmap or a ripmap are always generated from the image; they cannot be given.
- Half samples are limited to ±65,504: a larger finite value is rejected when it is written as half.
- EXIF, XMP, an orientation, a resolution and text entries cannot be stored, and the attributes of a file other than its
  windows and chromaticities are not read. The encoder writes no UINT samples and no preview image.
- The LJ2K compression, the HTJ2K256 and HTJ2K32 compressions of FLOAT or UINT channels, the chroma channels `RY` and
  `BY`, and, unless a layer is selected,
  channels other than `R`, `G`, `B`, `A` and `Y` (layers, depth) are rejected with an
  [UnsupportedImageFeatureException](cref:Meziantou.Framework.Imaging.UnsupportedImageFeatureException). So are multi-part files without
  [Part](cref:Meziantou.Framework.Imaging.OpenExrDecodingOptions.Part), and deep data without [DeepData](cref:Meziantou.Framework.Imaging.OpenExrDecodingOptions.DeepData).
- Deep data are only flattened: the samples and their depths are not available.
- The decoder reads the full-resolution level of mipmaps and ripmaps. [LevelX](cref:Meziantou.Framework.Imaging.OpenExrDecodingOptions.LevelX) and
  [LevelY](cref:Meziantou.Framework.Imaging.OpenExrDecodingOptions.LevelY) decode another level, the image of its data window;
  [AuxiliaryImageCollection](cref:Meziantou.Framework.Imaging.AuxiliaryImageCollection) lists the levels of a file and its preview image (8-bit
  RGBA, for display), and decodes them.
- A file whose writer stopped before patching its offset table (zeros in the table) is rejected by default; the
  [lenient decoding](../05-LoadingAndSaving/08-ErrorHandling.md#accept-the-files-browsers-accept) rebuilds the table
  from the chunks, as OpenEXR does, when they are all there.
