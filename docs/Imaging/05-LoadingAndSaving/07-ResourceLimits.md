---
title: Resource limits
description: Bound the size, frames and memory of what you load, and protect a service from hostile files.
---

A small file can describe a huge image: a few kilobytes of PNG can declare a canvas of billions of pixels, and a GIF can
hold thousands of frames. Resource limits bound what decoding, processing and encoding may cost, and fail with an
[ImageResourceLimitException](cref:Meziantou.Framework.Imaging.ImageResourceLimitException) before the library allocates the memory or reads the input. The defaults suit trusted
files; lower them for files that come from users.

## Set stricter limits

Put the limits in an [ImageConfiguration](cref:Meziantou.Framework.Imaging.ImageConfiguration) and pass it to the load:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/LoadingExamples.cs;region=Imaging_ConfigureLimits;format=md-fenced-code-block;dedent=true -->
```csharp
public static Image LoadAvatar(Stream upload)
{
    // Create the configuration once and share it: it is immutable and thread-safe
    var configuration = new ImageConfiguration
    {
        Limits = new ImageResourceLimits
        {
            MaxEncodedBytes = 10 * 1024 * 1024, // 10 MiB of input
            MaxWidth = 4096,
            MaxHeight = 4096,
            MaxFramePixels = 16_000_000,
            MaxFrames = 100,
        },
    };

    // An input over a limit throws ImageResourceLimitException before the memory is allocated.
    // The image keeps the configuration: its later operations are bound by the same limits
    return Image.Load(upload, new ImageDecodeOptions { Configuration = configuration });
}
```
<!-- endref -->

An image keeps the configuration it was created or loaded with, and its later operations use it: a resize of this
image to more than 4,096 pixels wide throws too. The configuration and the limits are immutable, so one instance can
be shared by every thread.

The configuration is accepted wherever an image or an input is created: [ImageDecodeOptions](cref:Meziantou.Framework.Imaging.ImageDecodeOptions), [ImageIdentifyOptions](cref:Meziantou.Framework.Imaging.ImageIdentifyOptions),
[ImageReaderOptions](cref:Meziantou.Framework.Imaging.ImageReaderOptions), [ImageWriterOptions](cref:Meziantou.Framework.Imaging.ImageWriterOptions), the constructors of [Image\<TPixel\>](cref:Meziantou.Framework.Imaging.Image{TPixel}), [ImportPixelData](cref:Meziantou.Framework.Imaging.Image.ImportPixelData), [ImportPixelBytes](cref:Meziantou.Framework.Imaging.Image.ImportPixelBytes),
and [ImageCollection.Create](cref:Meziantou.Framework.Imaging.ImageCollection.Create) and [Load](cref:Meziantou.Framework.Imaging.ImageCollection.Load). Without one, [ImageConfiguration.Default](cref:Meziantou.Framework.Imaging.ImageConfiguration.Default) applies.

## The limits

| Limit | Default | Bounds |
| --- | --- | --- |
| [MaxWidth](cref:Meziantou.Framework.Imaging.ImageResourceLimits.MaxWidth), [MaxHeight](cref:Meziantou.Framework.Imaging.ImageResourceLimits.MaxHeight) | 32,768 | The width and the height of the canvas, in pixels |
| [MaxFramePixels](cref:Meziantou.Framework.Imaging.ImageResourceLimits.MaxFramePixels) | 100,000,000 | The pixels of one frame |
| [MaxFrames](cref:Meziantou.Framework.Imaging.ImageResourceLimits.MaxFrames) | 1,000 | The frames of one input, a poster frame included, and of an image |
| [MaxTotalPixels](cref:Meziantou.Framework.Imaging.ImageResourceLimits.MaxTotalPixels) | 1,000,000,000 | The pixels of all the frames decoded from one input |
| [MaxEncodedBytes](cref:Meziantou.Framework.Imaging.ImageResourceLimits.MaxEncodedBytes) | 256 MiB | The bytes read from one input |
| [MaxMetadataBytes](cref:Meziantou.Framework.Imaging.ImageResourceLimits.MaxMetadataBytes) | 16 MiB | The metadata of one input (profiles, EXIF, XMP, text), once decompressed |
| [MaxLiveAllocationBytes](cref:Meziantou.Framework.Imaging.ImageResourceLimits.MaxLiveAllocationBytes) | 512 MiB | The memory of an image, or of a reader and the images it returns |

The constants [ImageResourceLimits.DefaultMaxDimension](cref:Meziantou.Framework.Imaging.ImageResourceLimits.DefaultMaxDimension), [DefaultMaxFramePixels](cref:Meziantou.Framework.Imaging.ImageResourceLimits.DefaultMaxFramePixels), … hold the defaults. The other limits
of [ImageResourceLimits](cref:Meziantou.Framework.Imaging.ImageResourceLimits) ([MaxFontBytes](cref:Meziantou.Framework.Imaging.ImageResourceLimits.MaxFontBytes), [MaxFontCollectionFaces](cref:Meziantou.Framework.Imaging.ImageResourceLimits.MaxFontCollectionFaces), [MaxGlyphComplexity](cref:Meziantou.Framework.Imaging.ImageResourceLimits.MaxGlyphComplexity), [MaxTextLength](cref:Meziantou.Framework.Imaging.ImageResourceLimits.MaxTextLength), [MaxTextGlyphs](cref:Meziantou.Framework.Imaging.ImageResourceLimits.MaxTextGlyphs), [MaxTextLines](cref:Meziantou.Framework.Imaging.ImageResourceLimits.MaxTextLines)) bound
fonts and text layouts: see [Fonts](../08-Text/02-Fonts.md).

- A limit is inclusive: a value equal to the limit is accepted.
- Every limit must be positive; zero does not mean "unlimited". To lift a limit, set it to the largest value of its
  type (`int.MaxValue` or `long.MaxValue`).
- [MaxLiveAllocationBytes](cref:Meziantou.Framework.Imaging.ImageResourceLimits.MaxLiveAllocationBytes) bounds each owner separately. It is a safety bound, not a cap on the memory of the process:
  bound the number of images you process at once too.
- For forward-only parsing, [MaxEncodedBytes](cref:Meziantou.Framework.Imaging.ImageResourceLimits.MaxEncodedBytes) equal to the input length is enough; a header-only identification or
  `FrameLimit` may stop before that many bytes are examined.
- TIFF, ICO, CUR and ANI loads from a synchronous path or seekable stream charge the cumulative bytes read, including
  repeated reads of the same range. They can succeed with a limit below the input length or require more than that length.
  Span, non-seekable and asynchronous loads instead parse the whole input in memory and charge its length once, so the
  input length is exactly enough for this limit.

## When a limit is exceeded

The exception tells which limit failed:

| Property | Value |
| --- | --- |
| [Kind](cref:Meziantou.Framework.Imaging.ImageResourceLimitException.Kind) | The limit, such as [ImageResourceLimitKind.Width](cref:Meziantou.Framework.Imaging.ImageResourceLimitKind.Width) or [ImageResourceLimitKind.EncodedBytes](cref:Meziantou.Framework.Imaging.ImageResourceLimitKind.EncodedBytes) |
| [Limit](cref:Meziantou.Framework.Imaging.ImageResourceLimitException.Limit) | The configured value |
| [Requested](cref:Meziantou.Framework.Imaging.ImageResourceLimitException.Requested) | The value the input or the operation asked for, when it is known |

A limit failure is never turned into a partial result: the load returns no image, and a geometry change leaves the
image [unchanged](../04-Concepts/06-ThreadSafetyAndAtomicity.md). See [Handling errors](08-ErrorHandling.md).

## Limits are not a way to select frames

To decode only the first frames of an animation, use [ImageDecodeOptions.FrameLimit](cref:Meziantou.Framework.Imaging.ImageDecodeOptions.FrameLimit) (or
[ImageReaderOptions.FrameLimit](cref:Meziantou.Framework.Imaging.ImageReaderOptions.FrameLimit)), not [MaxFrames](cref:Meziantou.Framework.Imaging.ImageResourceLimits.MaxFrames). `FrameLimit` is a choice: decoding stops cleanly after that many frames, and the rest of the input is
neither examined nor validated. [MaxFrames](cref:Meziantou.Framework.Imaging.ImageResourceLimits.MaxFrames) is a safety bound: an input that needs more frames fails. See
[Loading images](02-LoadingImages.md#decode-the-first-frames-only).

## Untrusted input

For files that come from users:

- Lower the limits to what your application needs, especially [MaxEncodedBytes](cref:Meziantou.Framework.Imaging.ImageResourceLimits.MaxEncodedBytes), [MaxWidth](cref:Meziantou.Framework.Imaging.ImageResourceLimits.MaxWidth), [MaxHeight](cref:Meziantou.Framework.Imaging.ImageResourceLimits.MaxHeight),
  [MaxFramePixels](cref:Meziantou.Framework.Imaging.ImageResourceLimits.MaxFramePixels) and [MaxFrames](cref:Meziantou.Framework.Imaging.ImageResourceLimits.MaxFrames).
- Use [`Image.Identify`](03-IdentifyingImages.md) to check the format and the size before decoding, for example to
  accept only some formats.
- Catch the [exceptions](08-ErrorHandling.md) of malformed files and exceeded limits, and answer with a client error.
- Bound the number of images processed at once, since each one can use up to [MaxLiveAllocationBytes](cref:Meziantou.Framework.Imaging.ImageResourceLimits.MaxLiveAllocationBytes).

[Process untrusted uploads](../03-Guides/07-ProcessUntrustedUploads.md) puts these together.
