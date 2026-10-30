---
title: Introduction
description: A fully managed .NET library to read, write, process and draw images, with no native dependency.
---

`Meziantou.Framework.Imaging` reads, writes, processes and draws images in .NET. It is written entirely in C#: it has
no native dependency, so it runs the same way on Windows, Linux and macOS, on x64 and arm64, and in trimmed and NativeAOT
applications.

## What you can do with it

- **Read and write images** in PNG and animated PNG (APNG), GIF, JPEG, WebP, AVIF, HEIC, JPEG XL, JPEG 2000, TIFF,
  BMP, TGA, QOI, Netpbm, ICO and CUR cursors, animated cursors (ANI) and OpenEXR. The format is detected from the content
  of the file, never from its extension. See [Supported formats](10-Formats/01-Overview.md).
- **Work with animations**: GIF, APNG, WebP, AVIF, HEIC and JPEG XL animations are loaded as full frames with their
  exact durations, and every operation applies to all the frames. See
  [Images, frames and animations](04-Concepts/02-ImagesFramesAndAnimations.md).
- **Process images**: crop, resize, rotate, flip, straighten a photo, correct a perspective, blur, sharpen, detect
  edges, adjust the colors, equalize the histogram, reduce to a palette or to black and white. See
  [Processing](06-Processing/01-Overview.md).
- **Draw**: shapes and paths, gradients, images, patterns, shadows, masks, layers and filters, with antialiasing. See
  [Drawing](07-Drawing/01-Overview.md).
- **Draw text** from font files (TrueType, OpenType, WOFF and WOFF2): paragraphs, right-to-left and vertical text, text
  on a path, measuring. See [Text](08-Text/01-Overview.md).
- **Manage colors and metadata**: EXIF, XMP and ICC profiles are kept, orientation is applied on request, and colors are
  converted between ICC profiles explicitly. See [Color](09-Color/01-Overview.md).
- **Process large inputs with bounded memory**: read and write long animations one frame at a time, and protect your
  application from hostile files with resource limits. See
  [Streaming readers and writers](05-LoadingAndSaving/05-StreamingReadersAndWriters.md) and
  [Resource limits](05-LoadingAndSaving/07-ResourceLimits.md).

## Principles

The library favors predictable results over guesses:

- **Nothing is lost silently.** Saving an image to a format that cannot store its transparency, its 16-bit precision,
  its animation or its metadata throws an exception, unless you say how to convert it. A JPEG never quietly becomes the
  first frame of an animation.
- **An image is a list of full frames.** Every frame of an animation is the complete picture shown at that time, and
  durations are exact fractions of a second. You never deal with the partial rectangles and disposal methods that the
  files store.
- **You own what you create.** Images are disposable; frames are views of their image. Loading a file never keeps the
  file or the stream open.
- **Geometry changes are all or nothing.** A crop, a resize or a rotation applies to every frame or, when it fails,
  leaves the image unchanged.
- **Same input, same pixels.** Decoding, processing and drawing give the same pixels on every platform and runtime.
- **Errors are explicit.** An unknown format, a corrupted file, an unsupported feature and an exceeded limit each throw a
  distinct exception. See [Handling errors](05-LoadingAndSaving/08-ErrorHandling.md).

## Namespaces

| Namespace | Contains |
| --- | --- |
| `Meziantou.Framework.Imaging` | Images, frames, pixel formats, loading and saving, processing, configuration |
| `Meziantou.Framework.Imaging.Formats` | The encoders and their settings (`PngEncoder`, `JpegEncoder`, …) |
| `Meziantou.Framework.Imaging.Metadata` | EXIF, XMP, ICC profiles, orientation, color descriptions |
| `Meziantou.Framework.Imaging.Drawing` | The canvas, brushes, pens and paths |
| `Meziantou.Framework.Imaging.Typography` | Fonts and text layout |

## Next steps

- [Getting started](02-GettingStarted.md): install the package and write your first program.
- [Guides](03-Guides/01-Overview.md): complete solutions to common tasks, such as thumbnails and format conversion.
- [Limitations](12-Limitations.md): what the library does not do.
