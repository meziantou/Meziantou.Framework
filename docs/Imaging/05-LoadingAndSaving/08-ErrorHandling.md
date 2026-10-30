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

These exceptions derive from [ImageException](cref:Meziantou.Framework.Imaging.ImageException), which you can catch to handle them all at once:

| Exception | Meaning |
| --- | --- |
| [UnknownImageFormatException](cref:Meziantou.Framework.Imaging.UnknownImageFormatException) | The content is not recognized as an image format. |
| [InvalidImageContentException](cref:Meziantou.Framework.Imaging.InvalidImageContentException) | The format is recognized, but the data is malformed, inconsistent or truncated. Also thrown when you save a malformed EXIF, ICC or XMP payload. [Format](cref:Meziantou.Framework.Imaging.InvalidImageContentException.Format) tells the format. |
| [UnsupportedImageFeatureException](cref:Meziantou.Framework.Imaging.UnsupportedImageFeatureException) | The data uses a valid feature that the library does not support (a hierarchical JPEG, for example), or an operation would lose information without an explicit setting: an animation, transparency, precision, metadata, a cursor hotspot, a poster frame, a color profile, a duration or a play count the output cannot store. [Format](cref:Meziantou.Framework.Imaging.UnsupportedImageFeatureException.Format) and [Feature](cref:Meziantou.Framework.Imaging.UnsupportedImageFeatureException.Feature) tell what. |
| [ImageResourceLimitException](cref:Meziantou.Framework.Imaging.ImageResourceLimitException) | A [resource limit](07-ResourceLimits.md) was exceeded. [Kind](cref:Meziantou.Framework.Imaging.ImageResourceLimitException.Kind), [Limit](cref:Meziantou.Framework.Imaging.ImageResourceLimitException.Limit) and [Requested](cref:Meziantou.Framework.Imaging.ImageResourceLimitException.Requested) tell which one and by how much. |

A malformed or truncated input always throws: it is never reported as the clean end of an animation, and never
returns a partial image.

## The other exceptions

The other failures use the standard .NET exceptions:

| Exception | Meaning |
| --- | --- |
| `ArgumentException` and derived types | An invalid argument: a file extension [Save](cref:Meziantou.Framework.Imaging.Image.Save) does not know, frames of different sizes, a stream that cannot be read or written, … |
| `NotSupportedException` | A pixel type that is not one of the [pixel formats](../04-Concepts/05-PixelFormats.md), thrown before any I/O |
| `InvalidOperationException` | An invalid state: a conflicting [lease](../04-Concepts/04-OwnershipAndLifetimes.md#leases), overlapping calls to a reader or a writer, a call to a reader or a writer that failed before |
| `ObjectDisposedException` | A disposed image, or a frame that was removed or whose image was disposed |
| `OperationCanceledException` | The operation was canceled |
| `IOException` | The file or the stream failed, passed on unchanged |

These usually point at a bug in the calling code or at the environment, not at the input: let them propagate.

## What to catch where

- **Loading user uploads**: catch [UnknownImageFormatException](cref:Meziantou.Framework.Imaging.UnknownImageFormatException), [InvalidImageContentException](cref:Meziantou.Framework.Imaging.InvalidImageContentException) and
  [UnsupportedImageFeatureException](cref:Meziantou.Framework.Imaging.UnsupportedImageFeatureException) as invalid input, and [ImageResourceLimitException](cref:Meziantou.Framework.Imaging.ImageResourceLimitException) as input that is too large.
  See [Process untrusted uploads](../03-Guides/07-ProcessUntrustedUploads.md).
- **Saving**: an [UnsupportedImageFeatureException](cref:Meziantou.Framework.Imaging.UnsupportedImageFeatureException) means the format cannot store the image as it is. Choose the
  conversion on the encoder rather than catching it: see [Saving images](04-SavingImages.md). The file is not written,
  and an existing file is left untouched.
- **Processing**: an [ImageResourceLimitException](cref:Meziantou.Framework.Imaging.ImageResourceLimitException) from a geometry change leaves the image unchanged. See
  [Thread safety and atomicity](../04-Concepts/06-ThreadSafetyAndAtomicity.md).

The messages of the exceptions are meant for people and may change between versions: test the type and the
properties, not the message.
