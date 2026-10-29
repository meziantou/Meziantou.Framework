---
title: Concepts
description: The model behind the API, what an image holds, who owns it, how pixels are stored, what is safe to share.
---

The pages of this group explain the model behind the API. Once you know them, most behaviors of the library are
easy to predict: why a frame cannot be disposed, why saving a transparent image to JPEG throws, why a failed resize
leaves the image as it was.

In short:

- An `Image` is a list of full frames that share one size and one pixel format. A still image has one frame; an
  animation has several, each with an exact duration.
- The pages of a TIFF document and the sizes of an icon are not frames: they live in an `ImageCollection`, whose
  entries can each have their own size and pixel format.
- You own and dispose the images you create or load. Frames are views of their image.
- Pixels have a declared format (`Rgba32`, `Gray16`, `RgbaFloat`, …), and nothing converts them, or drops their alpha
  or their precision, without your say.
- An image does one thing at a time; distinct images can be processed in parallel.

## Pages of this group

- [Images, frames and animations](02-ImagesFramesAndAnimations.md): what a frame is, durations, play counts, poster
  frames and animated cursors. Read it before you build or edit an animation.
- [Pages and representations](03-PagesAndRepresentations.md): multi-page TIFF documents and the sizes of an icon or a
  cursor, with `ImageCollection`.
- [Ownership and lifetimes](04-OwnershipAndLifetimes.md): what to dispose, how long a frame stays valid, and how to read
  and write pixels.
- [Pixel formats](05-PixelFormats.md): the formats, which one a file loads as, how to convert, and how alpha, 16-bit
  precision and floating-point samples are kept.
- [Thread safety and atomicity](06-ThreadSafetyAndAtomicity.md): what can be shared between threads, and what state an
  image is left in when an operation fails or is canceled.
