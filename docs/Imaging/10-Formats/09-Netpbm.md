---
title: Netpbm
description: Read and write PBM, PGM, PPM and PAM files, binary or plain, without loss.
---

The Netpbm formats are minimal uncompressed formats for tools and test fixtures: PBM (portable bitmap, black and white),
PGM (graymap), PPM (pixmap, RGB) and PAM (arbitrary map, here RGB with alpha). [PnmEncoder](cref:Meziantou.Framework.Imaging.Formats.PnmEncoder) always writes them without
loss: the variant follows the pixel format of the image, and 16-bit images keep their 16 bits.

## What is read and written

| | Reading | Writing |
| --- | --- | --- |
| Variants | `P1` to `P7`: PBM, PGM and PPM, plain and binary, and PAM | `P5` (PGM), `P6` (PPM) and `P7` (PAM), or `P2` and `P3` in plain form; PBM (`P4`, `P1`) on request |
| Samples | 8 and 16 bits (`MAXVAL` up to 65,535) | `MAXVAL` 255 or 65,535 |
| Tuple types | Black and white, gray, RGB, each with or without alpha | `RGB_ALPHA` for pixel formats with alpha |
| Metadata | None | None |

## Save a Netpbm file

With the default settings, gray pixel formats are written as PGM, pixel formats with alpha as PAM, and the others as PPM:

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

The plain forms write decimal samples, which a text editor and a diff can show. They have no alpha, and neither has a
portable bitmap, so pixels that are not opaque need [BackgroundColor](cref:Meziantou.Framework.Imaging.Formats.PnmEncoder.BackgroundColor):

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/FormatExamples.cs;region=Imaging_SavePlainNetpbm;format=md-fenced-code-block;dedent=true -->
```csharp
public static void SavePlainNetpbm(string inputPath, string outputPath)
{
    using var image = Image.Load(inputPath);

    // Netpbm stores one still image: save the first frame. Decimal samples that a text editor and a diff can show. The
    // variant still follows the pixel format: P2 (PGM) for gray pixels, P3 (PPM) otherwise. The plain forms have no
    // alpha, so pixels that are not opaque are composited over the background color.
    image.Frames[0].Save(outputPath, new PnmEncoder { Encoding = PnmEncoding.Plain, BackgroundColor = new Rgba32(255, 255, 255) });
}
```
<!-- endref -->

## Options

| Option | What it does | Default |
| --- | --- | --- |
| [Encoding](cref:Meziantou.Framework.Imaging.Formats.PnmEncoder.Encoding) | [Binary](cref:Meziantou.Framework.Imaging.Formats.PnmEncoding.Binary), or [Plain](cref:Meziantou.Framework.Imaging.Formats.PnmEncoding.Plain) (decimal samples, lines within 70 characters) | [Binary](cref:Meziantou.Framework.Imaging.Formats.PnmEncoding.Binary) |
| [PixelLayout](cref:Meziantou.Framework.Imaging.Formats.PnmEncoder.PixelLayout) | [Auto](cref:Meziantou.Framework.Imaging.Formats.PnmPixelLayout.Auto) follows the pixel format; [Bitmap](cref:Meziantou.Framework.Imaging.Formats.PnmPixelLayout.Bitmap) writes a portable bitmap | [Auto](cref:Meziantou.Framework.Imaging.Formats.PnmPixelLayout.Auto) |
| [BackgroundColor](cref:Meziantou.Framework.Imaging.Formats.PnmEncoder.BackgroundColor) | The opaque color pixels that are not opaque are composited over when the output has no alpha; `null` rejects them | `null` |
| [MetadataHandling](cref:Meziantou.Framework.Imaging.Formats.MetadataHandling) | What to do with metadata, which Netpbm cannot store | [Strict](cref:Meziantou.Framework.Imaging.Formats.MetadataHandling.Strict) |

## Write a portable bitmap

A portable bitmap stores one bit per pixel, and every pixel must be exactly black or exactly white: any other pixel throws
an [UnsupportedImageFeatureException](cref:Meziantou.Framework.Imaging.UnsupportedImageFeatureException), because the encoder never thresholds nor dithers. Binarize the image first:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_Threshold;format=md-fenced-code-block;dedent=true -->
```csharp
public static void SaveScanAsBilevel(string inputPath, string tiffPath, string pbmPath)
{
    using var page = Image.Load(inputPath);

    // Sauvola's threshold follows uneven lighting: each pixel is compared with the mean and the contrast of the
    // 31 x 31 pixels around it, so the text becomes black and the paper white, even in a shadow. The pixels are now
    // exactly black or white, in the same pixel format.
    page.Threshold(new ThresholdOptions { Method = ThresholdMethod.Sauvola, WindowSize = 31 });

    // A bilevel page with Group 4 compression, the usual format of scanned documents
    page.Save(tiffPath, new TiffEncoder { PixelLayout = TiffPixelLayout.Bilevel, Compression = TiffCompression.CcittGroup4 });

    // A portable bitmap: one bit per pixel
    page.Save(pbmPath, new PnmEncoder { PixelLayout = PnmPixelLayout.Bitmap });
}
```
<!-- endref -->

The thresholding methods are described in [Binarization](../06-Processing/09-Binarization.md). A portable bitmap loads
back as [Gray8](cref:Meziantou.Framework.Imaging.Gray8), with one bit per component.

## Pitfalls

- An encoder never looks at the file extension: `Save("page.pbm")` without an encoder writes a portable bitmap, but
  `Save("page.pbm", new PnmEncoder())` writes the variant of the pixel format. Set `PixelLayout = PnmPixelLayout.Bitmap`
  explicitly.
- Netpbm stores one frame: save one frame of an animation with `image.Frames[i].Save`. A file that holds several images is read
  as its first image.
- Netpbm stores no metadata: an ICC profile, EXIF, XMP, an orientation, a resolution or text entries follow
  [MetadataHandling](cref:Meziantou.Framework.Imaging.Formats.MetadataHandling), which rejects them by default.
- PAM tuple types other than the standard black and white, gray and RGB forms are rejected.
- A comment written right after a number of the header, without a space (`4#comment`), is rejected. The
  [lenient mode](../05-LoadingAndSaving/08-ErrorHandling.md#accept-the-files-browsers-accept) reads it as Netpbm does:
  the comment ends the number.
- The truncation of a plain file cannot always be detected: its prefix can be a valid file with a smaller last sample.
