---
title: Loading and saving
description: Load images from files, streams and memory, identify them, save them, and protect your application.
---

The pages of this group cover the input and output of images: loading, identifying a file without decoding it, saving,
processing long animations with bounded memory, limiting what a hostile file can cost, and handling errors.

A few rules hold for all of them:

- The format of an input is detected from its content, never from its file name.
- An image loads from a path, a stream or bytes in memory, synchronously or asynchronously, with the same results and
  the same errors.
- Loading never keeps your file or stream open, and never closes your stream.
- Saving checks that the output can store the image before it writes anything, and a file is published only once it is
  complete.

## Pages of this group

- [Loading images](02-LoadingImages.md): `Image.Load` from a path, a stream or bytes, synchronously or not, in the pixel
  format you want.
- [Identifying images](03-IdentifyingImages.md): read the format, the size and the frame count of a file without
  decoding its pixels.
- [Saving images](04-SavingImages.md): choose the format from the extension or with an encoder, handle what the format
  cannot store, save to a stream.
- [Streaming readers and writers](05-StreamingReadersAndWriters.md): process animations of any length one frame at a
  time.
- [Resource limits](06-ResourceLimits.md): bound the size, the frame count and the memory of what you load, especially
  from untrusted sources.
- [Handling errors](07-ErrorHandling.md): the exceptions the library throws and which ones to catch.
