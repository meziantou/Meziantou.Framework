---
title: Saving images
description: Save to a file or a stream, choose the format and its settings, handle what the format cannot store.
---

`Save` encodes an image to a file or a stream. This page shows how the format is chosen, what is checked before
anything is written, and how to tell the library what to do with what the format cannot store.

## Choose the format from the extension

`image.Save(path)` chooses the encoder from the extension of the file, case-insensitively, with default settings:

| Extension | Format |
| --- | --- |
| `.png` | PNG, or APNG when the image is animated |
| `.apng` | APNG, always |
| `.gif` | GIF |
| `.jpg`, `.jpeg` | JPEG |
| `.webp` | WebP, lossless |
| `.avif`, `.avifs` | AVIF, lossless |
| `.heic`, `.heif`, `.heics`, `.heifs` | HEIC, lossless |
| `.jxl` | JPEG XL, lossless |
| `.jp2` | JPEG 2000 file, lossless |
| `.j2k`, `.j2c`, `.jpc` | JPEG 2000 codestream, lossless |
| `.tif`, `.tiff` | TIFF |
| `.bmp`, `.dib` | BMP |
| `.tga`, `.icb`, `.vda`, `.vst` | TGA |
| `.qoi` | QOI |
| `.pnm`, `.pam`, `.ppm`, `.pgm` | Netpbm |
| `.pbm` | Netpbm portable bitmap, from black and white pixels only |
| `.ico` | Icon |
| `.cur` | Cursor |
| `.ani` | Animated cursor |
| `.exr` | OpenEXR, ZIP compression |

Any other extension throws an `ArgumentException`. The JPEG 2000 extensions `.jpx`, `.jpf`, `.jph` and `.jhc` are read,
not written.

## Choose the format and its settings

Pass an encoder to choose the format and its options. The encoder decides the format, whatever the extension:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/ReadmeSnippets.cs;region=Imaging_QuickStartWithEncoders;format=md-fenced-code-block;dedent=true -->
```csharp
using var image = Image.Load("animation.gif");          // every displayed frame, exact timing
image.Resize(new ResizeOptions(320, 240));               // all frames, atomically
image.Save("thumbnail.png", new PngEncoder());           // APNG, because the image is animated

image.Frames[0].Save("first.jpg",                         // JPEG stores one frame: export it explicitly
    new JpegEncoder { Quality = 85, BackgroundColor = new Rgba32(255, 255, 255) });
```
<!-- endref -->

The encoders are in the [Meziantou.Framework.Imaging.Formats](cref:Meziantou.Framework.Imaging.Formats) namespace. They are immutable: create one and reuse it
for every image, from any thread. Each format page lists the settings of its encoder: see
[Supported formats](../10-Formats/01-Overview.md).

## What is checked before writing

A save checks the whole image against the output before writing anything: the animation, the poster frame, the
durations, the alpha, the precision and the metadata. When the format cannot store the image as it is, the save throws
an `UnsupportedImageFeatureException` and writes nothing:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/LoadingExamples.cs;region=Imaging_SaveValidation;format=md-fenced-code-block;dedent=true -->
```csharp
public static bool TrySaveAsJpeg(Image image, string path)
{
    try
    {
        image.Save(path, new JpegEncoder { Quality = 90 });
        return true;
    }
    catch (UnsupportedImageFeatureException)
    {
        // JPEG cannot store this image as it is (an animation, transparency, 16-bit samples, metadata...).
        // Nothing was written: a file that already exists at path is left untouched
        return false;
    }
}
```
<!-- endref -->

The exception names the format and the feature (`Format`, `Feature`). The settings that allow a conversion are on the
encoder:

| The image has | The output cannot store it | Setting |
| --- | --- | --- |
| Several frames | JPEG, a static PNG, and the other still formats | None: save one frame with `image.Frames[i].Save(...)` |
| A poster frame | Every format but animated PNG | None: remove it with `RemovePosterFrame` |
| A duration the format cannot represent | GIF, APNG, ANI, … | `DurationRounding` |
| Transparency | JPEG, BMP layouts other than 32-bit, … | `BackgroundColor` |
| 16-bit samples | JPEG, WebP, QOI, BMP, TGA, … | `AllowBitDepthReduction` |
| Floating-point samples | Every format but OpenEXR and TIFF | `FloatingPointConversion` |
| Metadata | Depends on the format | `MetadataHandling` |

The complete list is in [Pixel formats](../04-Concepts/05-PixelFormats.md#nothing-is-lost-silently).

## Metadata

`ImageEncoder.MetadataHandling` decides what happens to metadata the output format cannot store, such as an ICC profile
in a GIF file, an EXIF orientation in a BMP file or a cursor hotspot in a PNG file:

| Value | Effect |
| --- | --- |
| `MetadataHandling.Strict` (default) | Throws an `UnsupportedImageFeatureException` |
| `MetadataHandling.DiscardUnsupported` | Drops what the format cannot store, writes the rest |
| `MetadataHandling.Strip` | Writes no optional metadata |

The animation settings and the frame durations are never affected. A malformed EXIF, ICC or XMP payload that you set
yourself is rejected when you save. Which format stores what is described in
[Metadata and orientation](../09-Color/02-MetadataAndOrientation.md).

## Floating-point images

`RgbaFloat` and `RgbaHalf` images are stored by the OpenEXR encoder with the
[sample type and alpha conversion](../10-Formats/17-OpenExr.md) of OpenEXR, and by the TIFF encoder as 32-bit
floats (with `TiffPixelLayout.Auto` and a compression other than JPEG). Every other encoder rejects them before writing,
unless `ImageEncoder.FloatingPointConversion` gives the options of their reduction to 16-bit integers: a tone mapping
for the highlights, or clipping. See
[Pixel formats](../04-Concepts/05-PixelFormats.md#floating-point-and-high-dynamic-range).

## Files are published atomically

A save to a path writes a temporary file in the destination directory, and renames it to the destination only once
the encoding succeeded. On a failure or a cancellation, an existing file at the destination is left untouched and the
temporary file is deleted, so a reader of the destination never sees a half-written image. The library does not flush
the file to the disk (`fsync`): the publication is atomic, but it is not guaranteed to survive a crash of the machine.

The same holds for `SaveAsync`, for `ImageCollection.Save` and for the [writers](05-StreamingReadersAndWriters.md) that
write to a path.

## Save to a stream

A stream has no extension, so `Save(stream, encoder)` requires an encoder:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/LoadingExamples.cs;region=Imaging_SaveToStream;format=md-fenced-code-block;dedent=true -->
```csharp
public static async Task<byte[]> EncodeAsPngAsync(Image image, CancellationToken cancellationToken)
{
    using var stream = new MemoryStream();

    // Writing starts at the current position of the stream, which is left open
    await image.SaveAsync(stream, new PngEncoder(), cancellationToken);
    return stream.ToArray();
}
```
<!-- endref -->

- Writing starts at the current position of the stream, and the stream is left open.
- A TIFF file needs a stream that can seek: the encoder writes the pixels first, then goes back to write the offsets
  of the header. A stream that cannot seek is rejected before anything is written.
- A stream is written as the encoding goes: after a failure, the bytes already written remain, and nothing is
  finalized. Write to a `MemoryStream` or a temporary file first when the destination must not receive a partial
  image.
- `SaveAsync` writes with asynchronous I/O and accepts a `CancellationToken`.
