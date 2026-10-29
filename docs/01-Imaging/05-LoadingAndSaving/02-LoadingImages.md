---
title: Loading images
description: Load an image from a file, a stream or bytes, asynchronously or not, in its own pixel format or in yours.
---

`Image.Load` decodes an image from a file, a stream or bytes in memory. The format is detected from the content: the
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

- A path is opened, read and closed before `Load` returns.
- A stream is read from its current position, the bytes read are consumed (the stream is not rewound), and the stream
  is left open: dispose it yourself. Streams that cannot seek, such as a network stream, are supported.
- Bytes are copied as needed while decoding; you can reuse the buffer as soon as `Load` returns.

TIFF, ICO, CUR and ANI files are read at the offsets their structure points at. On a seekable stream, only those bytes
are read, and the position of the stream afterward is unspecified. A stream that cannot seek, or an asynchronous load,
reads the whole file into memory first, up to the `MaxEncodedBytes` [limit](06-ResourceLimits.md).

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
`OperationCanceledException` and returns no image. Invalid arguments, such as a stream that cannot be read, throw
before the task is returned. To protect a service from hostile uploads, combine the load with
[resource limits](06-ResourceLimits.md) and see [Process untrusted uploads](../03-Guides/07-ProcessUntrustedUploads.md).

## Load in a specific pixel format

`Image.Load` returns an `Image` in the smallest [pixel format](../04-Concepts/05-PixelFormats.md) that holds the file
without loss: `Rgba32` for most color images with alpha, `Rgb24` for a JPEG, `Gray16` for a 16-bit gray PNG. When your
code expects one pixel type, request it with `Image.Load<TPixel>`, which converts while decoding:

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

The conversion follows the rules of `CloneAs`: 8 to 16 bits is exact, 16 to 8 bits rounds to the nearest value. A load
that would drop information, such as the alpha of a transparent image loaded as `Rgb24`, throws an
`UnsupportedImageFeatureException` unless `ImageDecodeOptions.Conversion` says how to convert. A pixel type that is not
supported throws a `NotSupportedException` before anything is read.

## Decode options

`ImageDecodeOptions` configures a load:

| Option | Default | Effect |
| --- | --- | --- |
| `Configuration` | `ImageConfiguration.Default` | The [resource limits](06-ResourceLimits.md) and the parallelism. The loaded image keeps it for its later operations. |
| `FrameLimit` | `null` (every frame) | Decode only the first frames of an animation |
| `Conversion` | `PixelConversionOptions.Default` | How a typed load converts the pixels: background color for the alpha, floating-point samples out of range, color profile |
| `CmykConversion` | `null` (rejected) | Convert a CMYK image (a JPEG, a TIFF page or a JPEG 2000 file) to RGB: see [Color management](../09-Color/03-ColorManagement.md) |

The options are immutable: create them once and share them.

## Decode the first frames only

`FrameLimit` stops decoding after that many frames. The rest of the input is neither examined nor validated, which
makes a preview of a long animation cheap:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/PerformanceExamples.cs;region=Imaging_FirstFramePreview;format=md-fenced-code-block;dedent=true -->
```csharp
public static void SavePreview(string animationPath, string previewPath)
{
    // Decoding stops after the first displayed frame: the rest of the file is not read
    using var image = Image.Load(animationPath, new ImageDecodeOptions { FrameLimit = 1 });

    // The image keeps its animation settings: export the frame as a still image
    using var preview = image.CloneFrame(0);
    preview.Resize(new ResizeOptions(128, 128) { AllowUpscaling = false });
    preview.Save(previewPath, new PngEncoder());
}
```
<!-- endref -->

`FrameLimit` is a choice you make, unlike `ImageResourceLimits.MaxFrames`, which is a safety bound: decoding more frames
than `MaxFrames` throws, while reaching `FrameLimit` ends the load normally. An APNG poster frame is always kept and is
not counted by `FrameLimit`.

## What a load does not do

- It does not apply the EXIF orientation: a photo taken in portrait can load sideways. Call `AutoOrient()`, see
  [Metadata and orientation](../09-Color/02-MetadataAndOrientation.md) and
  [Handle phone photos](../03-Guides/05-HandlePhonePhotos.md).
- It does not convert colors: an ICC profile is kept as a label of the pixels. See
  [Color management](../09-Color/03-ColorManagement.md).
- It gives one image for a multi-page TIFF document (the first page) or an icon (its largest size). Use
  [`ImageCollection`](../04-Concepts/03-PagesAndRepresentations.md) to reach the other entries.
