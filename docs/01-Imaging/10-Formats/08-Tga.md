---
title: TGA
description: Read and write Truevision TGA files, uncompressed or run-length, and detect a format without a signature.
---

TGA (Truevision TGA) is a simple format of asset pipelines and game tools. `TgaEncoder` writes true-color and grayscale
images, uncompressed or run-length encoded, without loss for 8-bit pixels.

## What is read and written

| | Reading | Writing |
| --- | --- | --- |
| Image types | True-color, grayscale and color-mapped, uncompressed or run-length | True-color and grayscale, uncompressed or run-length |
| Depths | 15/16, 24 and 32-bit color, 8-bit gray, gray with an attribute byte, 8 and 16-bit color-map indexes | 8-bit gray, 24-bit BGR, 32-bit BGRA |
| Alpha | Straight alpha, as the image descriptor declares it | Straight alpha for pixel formats with alpha |
| Metadata | None | None |

Gray pixel formats are written as 8-bit grayscale, pixel formats with alpha as 32-bit BGRA and the others as 24-bit
BGR. Color-mapped and 15/16-bit files are read, never written.

## Save a TGA file

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/FormatExamples.cs;region=Imaging_SaveTga;format=md-fenced-code-block;dedent=true -->
```csharp
public static ImageFormat SaveTga(string inputPath, string tgaPath)
{
    using var image = Image.Load(inputPath);

    // TGA stores one still image: save the first frame. Run-length packets are lossless and usually smaller on flat
    // colors; uncompressed rows (the default) are the most widely readable. 16-bit images are rounded to 8 bits only
    // with consent.
    image.Frames[0].Save(tgaPath, new TgaEncoder { Compression = TgaCompression.RunLength, AllowBitDepthReduction = true });

    // TGA has no signature: DetectFormat recognizes it from its whole 18-byte header
    var header = new byte[Image.FormatDetectionPrefixLength];
    using var stream = File.OpenRead(tgaPath);
    stream.ReadExactly(header);
    return Image.DetectFormat(header);
}
```
<!-- endref -->

## Options

| Option | What it does | Default |
| --- | --- | --- |
| `Compression` | `RunLength` writes run-length packets, which never cross a row; `None` writes uncompressed rows, the most widely readable output | `None` |
| `AllowBitDepthReduction` | Lets 16-bit pixel formats be rounded to 8 bits | `false` |
| `MetadataHandling` | What to do with metadata, which TGA cannot store | `Strict` |

The `.tga`, `.icb`, `.vda` and `.vst` extensions select `TgaEncoder` with its defaults.

## TGA has no signature

Every other format starts with a signature; a TGA file is recognized from a plausible 18-byte header, and only after
every other format. `Image.DetectFormat` therefore needs `Image.FormatDetectionPrefixLength` (18) bytes, as in the
example above.

## Pitfalls

- TGA stores one frame: save one frame of an animation with `image.Frames[i].Save`.
- TGA stores no metadata: an ICC profile, EXIF, XMP, an orientation, a resolution or text entries follow
  `MetadataHandling`, which rejects them by default.
- A side is at most 65,535 pixels.
- The optional TGA 2.0 footer is at the end of the file, so the decoder reads a stream to its end: nothing can follow a
  TGA file on the same stream.
- A file without a footer is a TGA 1.0 file, whose trailing bytes mean nothing: a file cut after its pixel data is a
  valid file, so truncation is only detected inside the pixel data.
- Premultiplied alpha, which the extension area can declare, is rejected with an `UnsupportedImageFeatureException`.
- A 16-bit grayscale file is read as an 8-bit gray sample and an attribute byte (gray and alpha when the descriptor
  declares 8 alpha bits), never as a 16-bit luminance.
