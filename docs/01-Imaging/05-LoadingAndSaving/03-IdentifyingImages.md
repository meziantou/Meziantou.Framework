---
title: Identifying images
description: Read the format, size and frame count of an image without decoding it, or detect its format.
---

Before decoding an image, you often only need to know what it is: to reject a file that is too large, to choose a
processing path, or to show its dimensions. `Image.Identify` reads that information without decoding the pixels, and
`Image.DetectFormat` recognizes a format from the first bytes of a file.

## Identify an image

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_Identify;format=md-fenced-code-block;dedent=true -->
```csharp
public static string Describe(Stream stream)
{
    var info = Image.Identify(stream, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan });
    var frames = info.FrameCount?.ToString(CultureInfo.InvariantCulture) ?? "unknown";
    return $"{info.Format} {info.Size} {info.PixelFormat}, frames: {frames}, orientation: {info.Metadata.Orientation}";
}
```
<!-- endref -->

`Identify` takes a path, a stream or bytes, like [`Load`](02-LoadingImages.md), and `IdentifyAsync` a path or a stream.
It returns an `ImageInfo`:

| Property | Value |
| --- | --- |
| `Format` | The format of the file (`ImageFormat.Png`, `ImageFormat.Jpeg`, …); an APNG file is `Png` |
| `Size`, `Width`, `Height` | The size of the canvas |
| `PixelFormat` | The pixel format that `Image.Load` gives for this file |
| `ColorModel`, `BitsPerComponent` | How the samples are encoded, for example gray at 16 bits |
| `FrameCount`, `IsAnimated`, `HasPosterFrame`, `Animation` | The animation, when the examined part of the file tells it |
| `MayHaveTransparency` | Whether the encoding can produce pixels that are not opaque (not the result of a scan of the pixels) |
| `CollectionEntryCount` | The number of pages of a TIFF document or of sizes of an icon |
| `Metadata` | The metadata found, such as the EXIF orientation and the ICC profile |

A value the library cannot know from what it read is `null`: it is never guessed.

## Header or full scan

`ImageIdentifyOptions.Mode` chooses how much of the file is read:

| Mode | Reads | Use it to |
| --- | --- | --- |
| `ImageIdentifyMode.Header` (default) | The start of the file, up to the first pixel data | Get the size and the format quickly |
| `ImageIdentifyMode.FullScan` | The whole structure of the file, without decoding the pixels | Get the frame count of a GIF, check that the structure is sound |

In `Header` mode, values that only appear later in the file are `null`: a GIF file, for example, does not declare its
frame count in its header. A full scan validates the structure of the file but does not decode the pixels, so a file
that identifies without error can still fail to load.

Identification reads the input under the same [resource limits](06-ResourceLimits.md) as a load
(`ImageIdentifyOptions.Configuration`), and throws the same [exceptions](07-ErrorHandling.md) for a file it does not
recognize or that is malformed.

## Detect the format from the first bytes

`Image.DetectFormat` only looks at the signature. It needs at most `Image.FormatDetectionPrefixLength` (18) bytes, and
returns `ImageFormat.Unknown` when nothing matches:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/LoadingExamples.cs;region=Imaging_DetectFormat;format=md-fenced-code-block;dedent=true -->
```csharp
public static ImageFormat DetectFormat(string path)
{
    // The first 18 bytes are enough for every format; the extension of the file is never used
    var prefix = new byte[Image.FormatDetectionPrefixLength];
    using var stream = File.OpenRead(path);
    var length = stream.ReadAtLeast(prefix, prefix.Length, throwOnEndOfStream: false);
    return Image.DetectFormat(prefix.AsSpan(0, length)); // ImageFormat.Unknown when nothing matches
}
```
<!-- endref -->

- PNG, GIF, JPEG and QOI are recognized from 8 bytes, WebP from 12. A prefix shorter than 8 bytes is always `Unknown`.
- TGA files have no signature: they are recognized from a plausible 18-byte header, after every other format.

`Image.Load` and `Image.Identify` detect the format the same way, so you do not need `DetectFormat` before them: use it
to sort files, or to check an upload before you store it.
