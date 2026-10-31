---
title: Loading images
description: Load an image from a file, a stream or bytes, asynchronously or not, in its own pixel format or in yours.
---

[Image.Load](cref:Meziantou.Framework.Imaging.Image.Load) decodes an image from a file, a stream or bytes in memory. The format is detected from the content: the
file name and its extension are never used, so a PNG file named `photo.jpg` loads as a PNG.

## Load from a file, a stream or bytes

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/LoadingExamples.cs;region=Imaging_LoadFromEverySource;format=md-fenced-code-block;dedent=true -->
```csharp
public static Size[] LoadFromEverySource(string path)
{
    // From a file: Load opens the file, reads it and closes it before returning
    using var fromFile = Image.Load(path);

    // From a stream: reading starts at the current position, and the stream is left open
    using var stream = File.OpenRead(path);
    using var fromStream = Image.Load(stream);

    // From bytes in memory: a byte[] converts to a ReadOnlySpan<byte>, which is not retained
    var bytes = File.ReadAllBytes(path);
    using var fromBytes = Image.Load(bytes);

    return [fromFile.Size, fromStream.Size, fromBytes.Size];
}
```
<!-- endref -->

| Source | Synchronous | Asynchronous |
| --- | --- | --- |
| File path | `Image.Load(path)` | `Image.LoadAsync(path)` |
| Stream | `Image.Load(stream)` | `Image.LoadAsync(stream)` |
| Bytes (`ReadOnlySpan<byte>`, `byte[]`) | `Image.Load(data)` | None: the data is already in memory |

Every overload decodes every frame of an animation, and gives the same image and the same errors. The image is yours:
dispose it. Nothing of the input is kept:

- A path is opened, read and closed before [Load](cref:Meziantou.Framework.Imaging.Image.Load) returns.
- A stream is read from its current position, the bytes read are consumed (the stream is not rewound), and the stream
  is left open: dispose it yourself. Streams that cannot seek, such as a network stream, are supported.
- Bytes are copied as needed while decoding; you can reuse the buffer as soon as [Load](cref:Meziantou.Framework.Imaging.Image.Load) returns.

TIFF, ICO, CUR and ANI files are read at the offsets their structure points at. On a seekable stream, only those bytes
are read, and the position of the stream afterward is unspecified. A stream that cannot seek, or an asynchronous load,
reads the whole file into memory first, up to the [MaxEncodedBytes](cref:Meziantou.Framework.Imaging.ImageResourceLimits.MaxEncodedBytes) [limit](07-ResourceLimits.md).

## Load asynchronously

In a web application, load an upload without blocking a thread on I/O:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/LoadingExamples.cs;region=Imaging_LoadAsync;format=md-fenced-code-block;dedent=true -->
```csharp
public static async Task<Image> LoadUploadAsync(Stream upload, CancellationToken cancellationToken)
{
    // The stream is read with asynchronous I/O; the caller keeps it and disposes it.
    // The returned image is owned by the caller, who disposes it too.
    return await Image.LoadAsync(upload, cancellationToken: cancellationToken);
}
```
<!-- endref -->

The asynchronous overloads read with asynchronous I/O and accept a `CancellationToken`. A canceled load throws an
`OperationCanceledException` and returns no image. The loads of data already in memory (`Image.Load(bytes, options,
cancellationToken)`) run synchronously and accept a token too: the decoder checks it between rows, strips, tiles and
frames. Invalid arguments, such as a stream that cannot be read, throw
before the task is returned. To protect a service from hostile uploads, combine the load with
[resource limits](07-ResourceLimits.md) and see [Process untrusted uploads](../03-Guides/07-ProcessUntrustedUploads.md).

## Load in a specific pixel format

[Image.Load](cref:Meziantou.Framework.Imaging.Image.Load) returns an [Image](cref:Meziantou.Framework.Imaging.Image) in the smallest [pixel format](../04-Concepts/05-PixelFormats.md) that holds the file
without loss: [Rgba32](cref:Meziantou.Framework.Imaging.Rgba32) for most color images with alpha, [Rgb24](cref:Meziantou.Framework.Imaging.Rgb24) for a JPEG, [Gray16](cref:Meziantou.Framework.Imaging.Gray16) for a 16-bit gray PNG. When your
code expects one pixel type, request it with [Image.Load\<TPixel\>](cref:Meziantou.Framework.Imaging.Image.Load), which converts while decoding:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/LoadingExamples.cs;region=Imaging_LoadTyped;format=md-fenced-code-block;dedent=true -->
```csharp
public static Image<Rgb24> LoadOpaque(string path)
{
    // Rgb24 has no alpha: transparent pixels are composited over white instead of failing the load.
    // Without a background color, a source that is not opaque throws UnsupportedImageFeatureException
    return Image.Load<Rgb24>(path, new ImageDecodeOptions
    {
        Conversion = new PixelConversionOptions { BackgroundColor = new Rgba64(65535, 65535, 65535) },
    });
}
```
<!-- endref -->

The conversion follows the rules of [CloneAs](cref:Meziantou.Framework.Imaging.Image.CloneAs): 8 to 16 bits is exact, 16 to 8 bits rounds to the nearest value. A load
that would drop information, such as the alpha of a transparent image loaded as [Rgb24](cref:Meziantou.Framework.Imaging.Rgb24), throws an
[UnsupportedImageFeatureException](cref:Meziantou.Framework.Imaging.UnsupportedImageFeatureException) unless [ImageDecodeOptions.Conversion](cref:Meziantou.Framework.Imaging.ImageDecodeOptions.Conversion) says how to convert. A pixel type that is not
supported throws a `NotSupportedException` before anything is read.

## Decode options

[ImageDecodeOptions](cref:Meziantou.Framework.Imaging.ImageDecodeOptions) configures a load:

| Option | Default | Effect |
| --- | --- | --- |
| [Configuration](cref:Meziantou.Framework.Imaging.ImageDecodeOptions.Configuration) | [ImageConfiguration.Default](cref:Meziantou.Framework.Imaging.ImageConfiguration.Default) | The [resource limits](07-ResourceLimits.md) and the parallelism. The loaded image keeps it for its later operations. |
| [FrameLimit](cref:Meziantou.Framework.Imaging.ImageDecodeOptions.FrameLimit) | `null` (every frame) | Decode only the first frames of an animation |
| [Conversion](cref:Meziantou.Framework.Imaging.ImageDecodeOptions.Conversion) | [PixelConversionOptions.Default](cref:Meziantou.Framework.Imaging.PixelConversionOptions.Default) | How a typed load converts the pixels: background color for the alpha, floating-point samples out of range, color profile |
| [CmykConversion](cref:Meziantou.Framework.Imaging.ImageDecodeOptions.CmykConversion) | `null` (rejected) | Convert a CMYK image (a JPEG, a TIFF page or a JPEG 2000 file) to RGB: see [Color management](../09-Color/03-ColorManagement.md) |
| [TargetSize](cref:Meziantou.Framework.Imaging.ImageDecodeOptions.TargetSize) | `null` (full size) | Decode a JPEG or JPEG 2000 file at a reduced size that is still at least this size: see [Decode a smaller image](#decode-a-smaller-image) |
| [ScaleDenominator](cref:Meziantou.Framework.Imaging.ImageDecodeOptions.ScaleDenominator) | 1 (full size) | Decode a JPEG file at exactly 1/2, 1/4 or 1/8 of its size, or a JPEG 2000 file at 1/2, 1/4, ... down to its lowest resolution level |

The options are immutable: create them once and share them.

## Decode the first frames only

[FrameLimit](cref:Meziantou.Framework.Imaging.ImageDecodeOptions.FrameLimit) stops decoding after that many frames. The rest of the input is neither examined nor validated, which
makes a preview of a long animation cheap:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/PerformanceExamples.cs;region=Imaging_FirstFramePreview;format=md-fenced-code-block;dedent=true -->
```csharp
public static void SavePreview(string animationPath, string previewPath)
{
    // Decoding stops after the first displayed frame: the rest of the file is not read
    using var image = Image.Load(animationPath, new ImageDecodeOptions { FrameLimit = 1 });

    image.Resize(new ResizeOptions(128, 128) { AllowUpscaling = false });

    // The image keeps its animation settings: save its frame as a still image
    image.Frames[0].Save(previewPath, new PngEncoder());
}
```
<!-- endref -->

[FrameLimit](cref:Meziantou.Framework.Imaging.ImageDecodeOptions.FrameLimit) is a choice you make, unlike [ImageResourceLimits.MaxFrames](cref:Meziantou.Framework.Imaging.ImageResourceLimits.MaxFrames), which is a safety bound: decoding more frames
than [MaxFrames](cref:Meziantou.Framework.Imaging.ImageResourceLimits.MaxFrames) throws, while reaching [FrameLimit](cref:Meziantou.Framework.Imaging.ImageDecodeOptions.FrameLimit) ends the load normally. An APNG poster frame is always kept and is
not counted by [FrameLimit](cref:Meziantou.Framework.Imaging.ImageDecodeOptions.FrameLimit).

## Decode a smaller image

A JPEG file can be decoded at 1/2, 1/4 or 1/8 of its width and height: each block of 8x8 pixels gives 4x4, 2x2 or one
pixel, computed from its coefficients, without decoding the full image first. The pixels are nearly those of a resize
of the full image (the mean of the pixels each one replaces), for much less memory and time. A JPEG 2000 file is
decoded at a lower resolution level of its wavelet transformation, each halving discarding one level, down to the
number of levels of the file: see [JPEG 2000](../10-Formats/16-Jpeg2000.md#thumbnails-and-viewports). This is what a
thumbnail or a preview of a large photo needs:

- [TargetSize](cref:Meziantou.Framework.Imaging.ImageDecodeOptions.TargetSize) asks for an image at least that large in both directions: a JPEG or JPEG 2000 file is decoded at
  the smallest of these scales that keeps that size, and any other file at full size. Resize the result to the exact size you want, as
  [Create thumbnails](../03-Guides/02-CreateThumbnails.md) does.
- [ScaleDenominator](cref:Meziantou.Framework.Imaging.ImageDecodeOptions.ScaleDenominator) asks for exactly one scale: the image has `ceil(width / n)` by `ceil(height / n)` pixels. A file
  that cannot be decoded at that scale (another format, a lossless JPEG file, a scale beyond 1/8, or beyond the resolution levels of a JPEG 2000
  file) throws an
  [UnsupportedImageFeatureException](cref:Meziantou.Framework.Imaging.UnsupportedImageFeatureException).

The sizes are those of the stored pixels, before the orientation is applied. The metadata is the one of the file, as
after a resize: the EXIF orientation still applies, and saving the image writes its new dimensions.
[Image.Identify](cref:Meziantou.Framework.Imaging.Image.Identify) always reports the size of the file. A reader created with the same options of
[ImageReaderOptions](cref:Meziantou.Framework.Imaging.ImageReaderOptions) gives frames of the reduced size, and its `Info` reports that size.

The whole file is still read: a progressive JPEG file keeps the coefficients of its full image until its end, so its memory
drops less than the one of a baseline file.

## What a load does not do

- It does not apply the EXIF orientation: a photo taken in portrait can load sideways. Call [AutoOrient()](cref:Meziantou.Framework.Imaging.ImageProcessingExtensions.AutoOrient), see
  [Metadata and orientation](../09-Color/02-MetadataAndOrientation.md) and
  [Handle phone photos](../03-Guides/05-HandlePhonePhotos.md).
- It does not convert colors: an ICC profile is kept as a label of the pixels. See
  [Color management](../09-Color/03-ColorManagement.md).
- It gives one image for a multi-page TIFF document (the first page) or an icon (its largest size). Use
  [`ImageCollection`](../04-Concepts/03-PagesAndRepresentations.md) to reach the other entries.
