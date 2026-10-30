---
title: Handling errors
description: The exceptions the library throws, what each one means, and which ones to catch.
---

The library reports each kind of failure with its own exception, so your code can tell a file that is not an image
from a corrupted one, a feature the library does not support from a limit your configuration set.

## Catch the content errors

When you load files you do not control, catch the exceptions that describe the input:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_ErrorHandling;format=md-fenced-code-block;dedent=true -->
```csharp
public static Image? TryLoad(string path, ImageConfiguration configuration)
{
    try
    {
        return Image.Load(path, new ImageDecodeOptions { Configuration = configuration });
    }
    catch (UnknownImageFormatException)
    {
        return null; // not an image
    }
    catch (UnsupportedImageFeatureException ex)
    {
        Console.Error.WriteLine($"Unsupported {ex.Format} feature: {ex.Feature}");
        return null;
    }
    catch (ImageResourceLimitException ex)
    {
        Console.Error.WriteLine($"Limit {ex.Kind} exceeded ({ex.Requested} > {ex.Limit})");
        return null;
    }
    catch (InvalidImageContentException)
    {
        return null; // corrupted or truncated file
    }
}
```
<!-- endref -->

These exceptions derive from `ImageException`, which you can catch to handle them all at once:

| Exception | Meaning |
| --- | --- |
| `UnknownImageFormatException` | The content is not recognized as an image format. |
| `InvalidImageContentException` | The format is recognized, but the data is malformed, inconsistent or truncated. Also thrown when you save a malformed EXIF, ICC or XMP payload. `Format` tells the format. |
| `UnsupportedImageFeatureException` | The data uses a valid feature that the library does not support (a hierarchical JPEG, for example), or an operation would lose information without an explicit setting: an animation, transparency, precision, metadata, a cursor hotspot, a poster frame, a color profile, a duration or a play count the output cannot store. `Format` and `Feature` tell what. |
| `ImageResourceLimitException` | A [resource limit](07-ResourceLimits.md) was exceeded. `Kind`, `Limit` and `Requested` tell which one and by how much. |

A malformed or truncated input always throws: it is never reported as the clean end of an animation, and never
returns a partial image.

## Accept the files browsers accept

Some files in the wild break their specification in ways that browsers and the reference decoders tolerate: a GIF
without its trailer, for example. By default the library rejects them with an `InvalidImageContentException`. To load
them, read them leniently:

<!-- ref:../../../samples/Meziantou.Framework.Imaging.Samples/Examples.cs;region=Imaging_LenientDecoding;format=md-fenced-code-block;dedent=true -->
```csharp
public static Image LoadLeniently(string path)
{
    var configuration = new ImageConfiguration { DecodingStrictness = DecodingStrictness.Lenient };
    return Image.Load(path, new ImageDecodeOptions { Configuration = configuration });
}
```
<!-- endref -->

The lenient mode accepts a fixed list of deviations, given on the page of each [format](../10-Formats/01-Overview.md),
and repairs them the way the reference decoders do. Everything else stays the same:

- The [resource limits](07-ResourceLimits.md) apply exactly as they do in the strict mode.
- A file that could only be repaired by guessing pixels is still rejected.
- It applies to `Identify`, `Load`, the readers and the collections, so they agree on which files they accept.

## The other exceptions

The other failures use the standard .NET exceptions:

| Exception | Meaning |
| --- | --- |
| `ArgumentException` and derived types | An invalid argument: a file extension `Save` does not know, frames of different sizes, a stream that cannot be read or written, … |
| `NotSupportedException` | A pixel type that is not one of the [pixel formats](../04-Concepts/05-PixelFormats.md), thrown before any I/O |
| `InvalidOperationException` | An invalid state: a conflicting [lease](../04-Concepts/04-OwnershipAndLifetimes.md#leases), overlapping calls to a reader or a writer, a call to a reader or a writer that failed before |
| `ObjectDisposedException` | A disposed image, or a frame that was removed or whose image was disposed |
| `OperationCanceledException` | The operation was canceled |
| `IOException` | The file or the stream failed, passed on unchanged |

These usually point at a bug in the calling code or at the environment, not at the input: let them propagate.

## What to catch where

- **Loading user uploads**: catch `UnknownImageFormatException`, `InvalidImageContentException` and
  `UnsupportedImageFeatureException` as invalid input, and `ImageResourceLimitException` as input that is too large.
  See [Process untrusted uploads](../03-Guides/07-ProcessUntrustedUploads.md).
- **Saving**: an `UnsupportedImageFeatureException` means the format cannot store the image as it is. Choose the
  conversion on the encoder rather than catching it: see [Saving images](04-SavingImages.md). The file is not written,
  and an existing file is left untouched.
- **Processing**: an `ImageResourceLimitException` from a geometry change leaves the image unchanged. See
  [Thread safety and atomicity](../04-Concepts/06-ThreadSafetyAndAtomicity.md).

The messages of the exceptions are meant for people and may change between versions: test the type and the
properties, not the message.
