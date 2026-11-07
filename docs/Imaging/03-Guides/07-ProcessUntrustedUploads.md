---
title: Process untrusted uploads
description: Check the format and the size of an uploaded file before decoding it, bound its resources, handle every error, and re-encode it.
---

An image uploaded by a user, or downloaded from a site you do not control, may be anything: a file that is not an image,
a format you do not want to serve, a truncated file, or a file crafted to exhaust the memory of your server, such as a
small file that declares a canvas of a billion pixels. This guide accepts such a file only when it is a reasonable image,
and stores a clean copy of it.

## An upload pipeline

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/GuideExamples.cs;region=Imaging_ProcessUpload;format=md-fenced-code-block;dedent=true -->
```csharp
public static string? SaveUpload(Stream upload, string outputPath)
{
    // Safety bounds for this input: a file that exceeds one is rejected before the memory is allocated
    var configuration = new ImageConfiguration
    {
        Limits = new ImageResourceLimits
        {
            MaxEncodedBytes = 20 * 1024 * 1024,
            MaxWidth = 8192,
            MaxHeight = 8192,
            MaxFramePixels = 40_000_000,
            MaxLiveAllocationBytes = 256 * 1024 * 1024,
        },
    };

    try
    {
        // Reads the headers only, without decoding any pixel. The bytes it reads are consumed: remember the position
        var start = upload.Position;
        var info = Image.Identify(upload, new ImageIdentifyOptions { Configuration = configuration });
        if (info.Format is not (ImageFormat.Jpeg or ImageFormat.Png or ImageFormat.Gif or ImageFormat.WebP or ImageFormat.Heic or ImageFormat.Avif))
            return $"The {info.Format} format is not accepted";

        if (info.Width < 16 || info.Height < 16)
            return "The image is too small";

        // Decodes from the start again; the first frame of an animation is enough
        upload.Position = start;
        using var image = Image.Load(upload, new ImageDecodeOptions { Configuration = configuration, FrameLimit = 1 });
        image.RemovePosterFrame();
        image.Animation = null;

        // Re-encoding keeps the pixels and nothing else: data hidden in the file, its metadata (once the orientation
        // and the colors are applied to the pixels) and any bytes after the image are gone
        image.AutoOrient();
        var options = new IccColorTransformOptions { ToneMapping = HdrToneMappingOptions.Default };
        image.ConvertColorProfile(PixelFormats.IsGrayscale(image.PixelFormat) ? IccProfile.SrgbGray : IccProfile.Srgb, options);
        image.Save(outputPath, new WebPEncoder
        {
            Compression = WebPCompression.Lossy,
            Quality = 80,
            AllowBitDepthReduction = true,
            MetadataHandling = MetadataHandling.Strip,
        });
        return null;
    }
    catch (UnknownImageFormatException)
    {
        return "The file is not an image";
    }
    catch (ImageResourceLimitException ex)
    {
        return $"The image is too large ({ex.Kind})";
    }
    catch (InvalidImageContentException)
    {
        return "The file is corrupted or truncated";
    }
    catch (UnsupportedImageFeatureException ex)
    {
        return $"The image uses an unsupported feature ({ex.Feature})";
    }
}
```
<!-- endref -->

The method returns `null` when the upload is saved, or the reason why it is rejected, which you can show to the user.
The sections below explain each step.

## Identify before decoding

[Image.Identify](cref:Meziantou.Framework.Imaging.Image.Identify) reads the headers of the file and returns an [ImageInfo](cref:Meziantou.Framework.Imaging.ImageInfo) without decoding the pixels: the format, the
size, the pixel format, the frame count when the header gives it. It is cheap, so check everything you can there:

- **The format.** It is detected from the content of the file, never from its name or from the content type that the
  client declares. Accept the formats you serve, and reject the others before decoding them.
- **The size.** Reject images that are too small or too large for your use, before any pixel is decoded.
- Values the header does not give are `null`, never guessed: a GIF header has no frame count, for example.

[ImageIdentifyMode.FullScan](cref:Meziantou.Framework.Imaging.ImageIdentifyMode.FullScan) also walks the whole structure of the file (chunks, markers, checksums) without decoding
pixels, which detects more corrupted files before the decoding, for the cost of reading the whole file. See
[Identifying images](../05-LoadingAndSaving/03-IdentifyingImages.md).

## Bound the resources

Identification is not enough: the header may be honest and the data still hostile. [ImageResourceLimits](cref:Meziantou.Framework.Imaging.ImageResourceLimits) bounds what a
load can read and allocate. Each limit is checked before the memory is allocated or the data consumed, and exceeding one
throws an [ImageResourceLimitException](cref:Meziantou.Framework.Imaging.ImageResourceLimitException), never a truncated image:

| Limit | Default | What it bounds |
| --- | --- | --- |
| [MaxEncodedBytes](cref:Meziantou.Framework.Imaging.ImageResourceLimits.MaxEncodedBytes) | 256 MiB | The bytes read from the input |
| [MaxWidth](cref:Meziantou.Framework.Imaging.ImageResourceLimits.MaxWidth), [MaxHeight](cref:Meziantou.Framework.Imaging.ImageResourceLimits.MaxHeight) | 32,768 | The size of the canvas |
| [MaxFramePixels](cref:Meziantou.Framework.Imaging.ImageResourceLimits.MaxFramePixels) | 100,000,000 | The pixels of one frame |
| [MaxFrames](cref:Meziantou.Framework.Imaging.ImageResourceLimits.MaxFrames) | 1,000 | The frames of an animation |
| [MaxTotalPixels](cref:Meziantou.Framework.Imaging.ImageResourceLimits.MaxTotalPixels) | 1,000,000,000 | The pixels of all the frames together |
| [MaxMetadataBytes](cref:Meziantou.Framework.Imaging.ImageResourceLimits.MaxMetadataBytes) | 16 MiB | EXIF, XMP, ICC profiles and text, decompressed |
| [MaxLiveAllocationBytes](cref:Meziantou.Framework.Imaging.ImageResourceLimits.MaxLiveAllocationBytes) | 512 MiB | The memory that an image and the operation working on it hold at a time |

The defaults suit trusted files; lower them for uploads. Pass the same [ImageConfiguration](cref:Meziantou.Framework.Imaging.ImageConfiguration) to [Identify](cref:Meziantou.Framework.Imaging.Image.Identify) and to [Load](cref:Meziantou.Framework.Imaging.Image.Load):
the loaded image keeps its configuration, so its later operations (a resize, a color conversion) stay within the same
limits. Every limit must be positive; to lift one, set it to the largest value of its type. See
[Resource limits](../05-LoadingAndSaving/07-ResourceLimits.md).

`FrameLimit = 1` is not a limit: it deliberately decodes the first frame of an animation and ignores the rest of the
file, where [MaxFrames](cref:Meziantou.Framework.Imaging.ImageResourceLimits.MaxFrames) rejects a file that has too many frames.

## Catch the errors

Each kind of problem throws its own exception, all derived from [ImageException](cref:Meziantou.Framework.Imaging.ImageException):

| Exception | Meaning | Typical answer |
| --- | --- | --- |
| [UnknownImageFormatException](cref:Meziantou.Framework.Imaging.UnknownImageFormatException) | Not an image, or a format the library does not read | "Not an image" |
| [InvalidImageContentException](cref:Meziantou.Framework.Imaging.InvalidImageContentException) | Corrupted, inconsistent or truncated data | "Corrupted file" |
| [UnsupportedImageFeatureException](cref:Meziantou.Framework.Imaging.UnsupportedImageFeatureException) | A valid file that uses a feature the library does not support ([Feature](cref:Meziantou.Framework.Imaging.UnsupportedImageFeatureException.Feature) says which) | "Unsupported image" |
| [ImageResourceLimitException](cref:Meziantou.Framework.Imaging.ImageResourceLimitException) | A limit is exceeded ([Kind](cref:Meziantou.Framework.Imaging.ImageResourceLimitException.Kind), [Limit](cref:Meziantou.Framework.Imaging.ImageResourceLimitException.Limit) and [Requested](cref:Meziantou.Framework.Imaging.ImageResourceLimitException.Requested) say which and by how much) | "Image too large" |

`IOException` (the stream failed) and `OperationCanceledException` are not problems of the file: let them propagate.
See [Handling errors](../05-LoadingAndSaving/08-ErrorHandling.md).

## Re-encode the image

Never store or serve the uploaded bytes as they are. Decoding and encoding again keeps the picture and drops the rest:
the data that the decoder skips (unknown chunks, bytes after the end of the image) is not written, and
[MetadataHandling.Strip](cref:Meziantou.Framework.Imaging.Formats.MetadataHandling.Strip) removes the metadata, the GPS position of a photo included. The orientation and the color
profile are applied to the pixels first, since [Strip](cref:Meziantou.Framework.Imaging.Formats.MetadataHandling.Strip) drops them too (see
[Handle photos from phones and cameras](05-HandlePhonePhotos.md)).

Re-encoding also gives you one output format and one quality for every upload, whatever the users send.

## Streams

- [Identify](cref:Meziantou.Framework.Imaging.Image.Identify) and [Load](cref:Meziantou.Framework.Imaging.Image.Load) start reading at the current position of the stream, and consume what they read: the example
  remembers the position and seeks back to it before decoding. This needs a seekable stream.
- For a stream that cannot seek, such as a request body read as it arrives, copy the upload to a `MemoryStream` or a
  temporary file first, with a size limit, or skip [Identify](cref:Meziantou.Framework.Imaging.Image.Identify) and rely on the limits of [Load](cref:Meziantou.Framework.Imaging.Image.Load) alone.
- The library never closes your stream, and does not keep it after the call.
- [IdentifyAsync](cref:Meziantou.Framework.Imaging.Image.IdentifyAsync) and [LoadAsync](cref:Meziantou.Framework.Imaging.Image.LoadAsync) read with asynchronous I/O and take a `CancellationToken`, which decoders check between
  rows and frames: cancel it to stop working on a file that takes too long.
